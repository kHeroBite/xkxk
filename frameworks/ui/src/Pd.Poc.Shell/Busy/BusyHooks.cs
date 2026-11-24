// 진행 표시 연결부 — BusyService 상태를 오버레이·커서·포인터 추적 스피너에 잇고, 창 첫 열림·SDI 전환에서 쓰는 시작/끝 도우미를 둔다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Busy;

/// <summary>
/// 작업은 UI 스레드에서 돌기 때문에 동기 구간 동안에는 스피너가 그려지지 않는다. 그래서 끝 표시를 레이아웃 뒤(UiDeferred)로 미룬다.
/// 오래 걸린 동기 작업은 그 뒤 최소 표시 시간만큼 보이고, 비동기 작업은 BusyService.RunAsync 로 걸면 진행 내내 보인다.
/// </summary>
public static partial class BusyHooks
{
    /// <summary>메인 창 스코프 키.</summary>
    public const string MainScope = "main";

    private const int OpenTimeoutMs = 5000; // 뷰를 만드는 중인 문서가 끝을 알리지 못해도 스코프를 붙잡지 않게 하는 안전장치
    private const int UnrealizedGraceMs = 1000; // 이때까지 오버레이가 화면에 없는 문서(가려진 탭·숨긴 패널·열자마자 닫힘)는 볼 사람이 없어 끝낸다
    private const int FrameWaitMs = 100;

    private static readonly Dictionary<string, (BusyScope Scope, IDisposable Timeout)> s_opening = new(StringComparer.Ordinal);
    private static BusyService? s_service;
    private static BusyCursorDriver? s_cursor;
    private static readonly HashSet<string> s_afterFrame = new(StringComparer.Ordinal);
    private static PointerFollower? s_follower;

    /// <summary>공용 서비스. 처음 접근할 때 연결한다.</summary>
    public static BusyService Service
    {
        get
        {
            if (s_service is null)
            {
                s_service = new BusyService(new DispatcherBusyClock());
                s_cursor = new BusyCursorDriver(TopLevelOf);
                s_service.VisibilityChanged += OnVisibilityChanged;
                s_service.Armed += OnArmed;
            }

            return s_service;
        }
    }

    /// <summary>진단 전용(busy.hold): 끝 표시를 이만큼 더 미룬다. 평소 0 이다.</summary>
    internal static int DiagHoldMs { get; set; }

#if PD_DIAGNOSTICS
    private static readonly List<string> s_history = new();

    /// <summary>끝 처리까지 온 창 열림·전환 기록("스코프:사유", 앞 64건). 진단 전용.</summary>
    internal static IReadOnlyList<string> History => s_history;
#endif

    /// <summary>진단 전용(busy.slowWork): 양보 직후 UI 스레드를 이만큼 막는다. 평소 0 이다.</summary>
    internal static int DiagSlowMs { get; set; }

    /// <summary>메인 화면이 붙었는가. 그 전(기동 중 첫 테마 적용 등)은 첫 렌더 전에 끝나야 해서 미루지 않는다.</summary>
    internal static bool IsMainAttached => s_follower is not null;

    internal static BusyCursorDriver? Cursor => s_cursor;

    internal static PointerFollower? Follower => s_follower;

    internal static IReadOnlyCollection<string> OpeningScopes => s_opening.Keys;

    /// <summary>메인 화면이 로드될 때 부른다. 포인터 추적 스피너를 잇고 진단 명령을 등록한다.</summary>
    public static void AttachMain(PointerFollower follower)
    {
        _ = Service;
        s_follower = follower;
#if PD_DIAGNOSTICS
        Pd.Poc.Shell.Diagnostics.Remote.RemoteBusyCommands.Register(Pd.Poc.Shell.Diagnostics.Remote.RemoteCommandRouter.Default);
#endif
    }

    /// <summary>스코프를 시작하고 지금 작업의 레이아웃이 끝나면 저절로 끝낸다.</summary>
    public static void BeginUntilLaid(string scope, string reason) => EndAfterLayout(Service.Begin(scope, reason));

    /// <summary>
    /// UI 스레드를 막을 작업용 진입점. 표시를 걸고 프레임이 한 번 그려지기를 기다린 뒤 work 를 돌린다.
    /// 그래야 오버레이가 렌더 스레드에 먼저 올라가 work 가 막는 동안에도 보인다. 끝 표시는 레이아웃 뒤로 미룬다.
    /// </summary>
    public static async Task<T> RunLongAsync<T>(string scope, string reason, Func<T> work)
    {
        var token = Service.Begin(scope, reason, preArm: true);
        try
        {
            await YieldForRenderAsync(BusyOverlay.OverlaysFor(scope).FirstOrDefault());
            return work();
        }
        finally
        {
            EndAfterLayout(token);
        }
    }

    /// <summary>
    /// 동기 호출부용: 표시를 걸고 프레임 뒤에 work 를 돌린 다음 곧바로 돌아온다. 호출부는 work 결과를 그 자리에서 쓸 수 없다.
    /// 웹은 렌더 스레드가 없어 work 동안 스피너가 멈추지만, 이 양보 덕에 그려진 상태로는 남는다. dedupe=true 면 같은 스코프·사유의 연타를 무시한다.
    /// </summary>
    public static void RunAfterFrame(string scope, string reason, Action work, bool dedupe = true)
    {
        var key = scope + ":" + reason;
        if (dedupe && !s_afterFrame.Add(key))
        {
            return;
        }

        var token = Service.Begin(scope, reason, preArm: true);
        Pd.Poc.Shell.Platform.UiDeferred.Post(async () =>
        {
            try
            {
                await YieldForRenderAsync(BusyOverlay.OverlaysFor(scope).FirstOrDefault());
                work();
            }
            finally
            {
                if (dedupe)
                {
                    s_afterFrame.Remove(key);
                }

                EndAfterLayout(token);
            }
        });
    }

    /// <summary>anchor 가 든 창의 다음 프레임이 그려진 뒤 action 을 돌린다. 문서 뷰 생성을 오버레이 뒤로 미루는 데 쓴다.</summary>
    public static void AfterRender(Control anchor, Action action) => Pd.Poc.Shell.Platform.UiDeferred.Post(async () =>
    {
        await YieldForRenderAsync(anchor);
        action();
    });

    /// <summary>프레임 1회를 기다린다. 최소화·화면 밖이면 프레임이 오지 않을 수 있어 FrameWaitMs 뒤에는 그냥 넘어간다.</summary>
    private static async Task YieldForRenderAsync(Control? anchor)
    {
        if (anchor is not null && TopLevel.GetTopLevel(anchor) is { } top)
        {
            var frame = new TaskCompletionSource();
            top.RequestAnimationFrame(_ => frame.TrySetResult());
            await Task.WhenAny(frame.Task, Task.Delay(FrameWaitMs));
        }

        await Dispatcher.UIThread.InvokeAsync(() => { }, Pd.Poc.Shell.Platform.UiDeferred.Priority);
#if PD_DIAGNOSTICS
        if (DiagSlowMs > 0 && !OperatingSystem.IsBrowser())
        {
            System.Threading.Thread.Sleep(DiagSlowMs); // 시험: 실제 호출 지점의 작업이 오래 걸리는 상황을 흉내 낸다
        }
#endif
    }

    /// <summary>문서 첫 열림 오버레이·커서를 시작한다. 문서 단위 진입점은 Docking.WindowLoadState 하나라 이 메서드는 그것만 부른다(23 §2.1).</summary>
    internal static void TrackOpen(string documentId, BusyService service)
    {
        if (s_opening.ContainsKey(documentId))
        {
            return;
        }

        var scope = service.Begin(documentId, "open", preArm: true);
        var timeout = DispatcherTimer.RunOnce(() => OnOpenGrace(documentId), TimeSpan.FromMilliseconds(UnrealizedGraceMs), Pd.Poc.Shell.Platform.UiDeferred.Priority);
        s_opening[documentId] = (scope, timeout);
    }

    /// <summary>유예 시점 판정. 오버레이가 없으면 바로 끝내고, 있으면 뷰를 만드는 중이므로 남은 시간만큼 안전장치를 건다.</summary>
    private static void OnOpenGrace(string documentId)
    {
        if (!s_opening.TryGetValue(documentId, out var opening))
        {
            return;
        }

        if (BusyOverlay.OverlaysFor(documentId).Count == 0)
        {
            EndOpenDisplay(documentId); // 오버레이 표시만 끝낸다 — 창 아이콘의 불러오는 중은 콘텐츠 완료까지 남는다(23 §3)
            return;
        }

        var timeout = DispatcherTimer.RunOnce(() => EndOpenDisplay(documentId), TimeSpan.FromMilliseconds(OpenTimeoutMs - UnrealizedGraceMs));
        s_opening[documentId] = (opening.Scope, timeout);
    }

    /// <summary>문서 내용이 화면 트리에 붙었다. 오버레이를 끝내고, 보고 계약이 없는 콘텐츠면 열기 완료로 알린다. 두 번째 호출부터는 할 일이 없다.</summary>
    public static void DocumentRealized(string documentId)
    {
        EndOpenDisplay(documentId);
        Pd.Poc.Shell.Docking.WindowLoadState.Default.ViewRealized(documentId);
    }

    private static void EndAfterLayout(BusyScope scope) => Pd.Poc.Shell.Platform.UiDeferred.Post(() =>
    {
#if PD_DIAGNOSTICS
        if (s_history.Count < 64)
        {
            s_history.Add(scope.Scope + ":" + scope.Reason); // 표시 지연 안에 끝난 첫 열림도 시험에서 확인할 수 있게 남긴다
        }
#endif
        if (DiagHoldMs > 0)
        {
            DispatcherTimer.RunOnce(scope.Dispose, TimeSpan.FromMilliseconds(DiagHoldMs));
        }
        else
        {
            scope.Dispose();
        }
    });

    /// <summary>사전 걸기: 렌더 스레드가 지연을 세어 나타내므로 UI 스레드가 막혀도 보인다. 표시 전에 끝나면 취소된다.</summary>
    private static void OnArmed(string scope, bool armed)
    {
        if (armed)
        {
            BusyOverlay.SetScopeActive(scope, true, BusyService.CurrentShowDelayMs);
        }
        else if (BusyOverlay.IsScopeActive(scope))
        {
            BusyOverlay.SetScopeActive(scope, false);
        }
    }

    private static void OnVisibilityChanged(string scope, bool visible)
    {
        if (!visible || !BusyOverlay.IsScopeActive(scope)) // 걸어 둔 오버레이는 이미 켜져 있어 다시 켜지 않는다(스피너 재시작 방지)
        {
            BusyOverlay.SetScopeActive(scope, visible);
        }

        s_cursor?.SetScope(scope, visible);
        if (BusyCursorDriver.UsesFollower && s_follower is not null)
        {
            s_follower.IsActive = s_service!.Snapshot().Any(s => s.Visible);
        }
    }

    /// <summary>스코프의 오버레이가 속한 창. 오버레이가 화면에 없으면(가려진 탭) 커서를 바꾸지 않는다.</summary>
    private static TopLevel? TopLevelOf(string scope) =>
        BusyOverlay.OverlaysFor(scope).Select(TopLevel.GetTopLevel).FirstOrDefault(t => t is not null);
}

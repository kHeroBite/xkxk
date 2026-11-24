// 창 활성화·MRU 전환·SDI 분리/재결합·핀 — PERF-UI 하위 계측(ui.activate.*, ui.sdi.*) 포함(옛 WindowCommands.Activation, 공유 상태는 WindowSession)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>WindowCommands 파사드가 위임하는 활성화·SDI 전환. 생성자는 상태만 저장한다(구독 없음).</summary>
[WindowCommandsPart]
internal sealed class WindowActivation
{
    private readonly WindowSession _s;

    internal WindowActivation(WindowSession s) => _s = s;

    // 옛 WindowCommands 필드·도우미 이름 — 아래 본문을 옮기기만 하려고 같은 이름으로 잇는다.
    private PocDockFactory _factory => _s.Factory;
    private Action<string> _log => _s.Log;
    private List<PocDocument> _mru => _s.Mru;
    private PocDocument? _restoreTarget { set => _s.RestoreTarget = value; }
    private IDocumentDock Docs => _s.Docs;
    private ObservableCollection<PocDocument> OpenDocuments => _s.Owner.OpenDocuments;
    private IDockWindow? FindWindow(PocDocument doc) => _s.FindWindow(doc);
    private IEnumerable<PocDocument> MdiDocs() => _s.MdiDocs();
    private void SetActive(PocDocument? doc) => _s.Owner.SetActive(doc);
    private bool IsMinimized(PocDocument doc) => _s.Owner.StateOps.IsMinimized(doc);
    private void SetState(PocDocument document, MdiWindowState state) => _s.Owner.StateOps.SetState(document, state);

    public void Activate(PocDocument document)
    {
        _restoreTarget = null; // 명시적 활성화가 오면 복원 보호를 즉시 해제
        ActivateCore(document, restoreMinimized: true);
    }

    /// <summary>지금 진행 중인 활성화의 출처. ActiveChanged 구독자가 이 값으로 전환 효과를 고른다(그 밖에는 Program).</summary>
    internal ActivationOrigin CurrentOrigin { get; private set; }

    /// <summary>출처를 붙인 활성화가 끝난 뒤 (문서, 출처)로 발생한다.</summary>
    internal event Action<PocDocument, ActivationOrigin>? ActivatedFrom;

    private readonly ActivationOriginTracker _pending = new(() => Environment.TickCount64); // 600ms — 누름 → 놓음 선택까지 포함

    /// <summary>출처를 붙여 활성화한다(화면은 출처만 넘긴다 — 21 설계 §3). act 는 Activate·ActivateNext 등 실제 동작.</summary>
    internal void WithOrigin(ActivationOrigin origin, Action act)
    {
        var previous = CurrentOrigin;
        CurrentOrigin = origin;
        try
        {
            act();
        }
        finally
        {
            CurrentOrigin = previous;
        }
    }

    /// <summary>입력 순간 표식(DOCK 문서 탭·본문 누름·제목 탭 키 선택·Ctrl+Tab). 600ms 안의 다음 활성 변경(Dock 이 직접 바꾸는 것 포함)이 가져간다.</summary>
    internal void MarkOrigin(ActivationOrigin origin) => _pending.Mark(origin);

    /// <summary>남은 출처 표식을 지운다. 창 열기·닫기·복원 경로가 불러 사용자 전환이 아닌 활성 변경이 출처를 가져가지 않게 한다.</summary>
    internal void ClearOrigin() => _pending.Clear();

    /// <summary>활성 문서 통지. 진행 중 출처가 없으면 남은 표식을 쓰고, 바뀐 활성에 출처가 있으면 통지 뒤 ActivatedFrom 을 낸다.</summary>
    internal void NotifyActive(PocDocument? doc, bool changed, Action raise)
    {
        var origin = CurrentOrigin != ActivationOrigin.Program ? CurrentOrigin : _pending.Take(changed);

        WithOrigin(origin, raise);
        if (changed && doc is not null && origin != ActivationOrigin.Program)
        {
            ActivatedFrom?.Invoke(doc, origin);
        }
    }

    /// <summary>활성화. restoreMinimized=false 는 레이아웃 복원용(저장된 최소화 상태 유지, MDI-10).</summary>
    internal void ActivateCore(PocDocument document, bool restoreMinimized)
    {
        using (PerfProbe.Current.TimeOperation("ui.activate.dock")) // Dock 활성·포커스 지정(Dock 재배치 포함)
        {
            _factory.SetActiveDockable(document);
            if (MdiTabGroup.GroupOf(document) is { } group)
            {
                _factory.SetActiveDockable(group); // Dock 은 문서의 Owner(묶음) 활성만 바꾼다 — 묶음을 MDI 칸의 활성 창으로도 세워 이전 창 활성 표시가 남지 않게
            }

            if (document.Owner is IDock owner)
            {
                _factory.SetFocusedDockable(owner, document);
            }
        }

        if (restoreMinimized && IsMinimized(document))
        {
            SetState(document, MdiWindowState.Normal);
        }

        FindWindow(document)?.SetActive();
#if PD_DIAGNOSTICS
        // SetActive 의 끝은 SetForegroundWindow 인데 Avalonia 가 반환값을 버린다(WindowImpl.Activate). 활성화 실패와 색 갱신 실패를 가르려고 실제 결과를 남긴다
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (FindWindow(document)?.Host is Avalonia.Controls.Window host)
            {
                _log($"[activate] doc={document.Id} hostActive={host.IsActive} state={host.WindowState} bg={xkxk.frameworks.ui.shell.Platform.SystemDialogHost.BackgroundMode}");
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
#endif
        using (PerfProbe.Current.TimeOperation("ui.activate.notify")) // 탭 선택·MRU·ActiveChanged 구독자(Z 재매김은 Background)
        {
            SetActive(document);
        }
    }

    public void ActivateNext()
    {
        // MRU [A,B,C,D] → D 활성 → [D,A,B,C]: 반복하면 모든 창을 순환한다.
        if (_mru.Count > 1)
        {
            using var op = PerfProbe.Current.TimeOperation("ui.activate.next");
            Activate(_mru[^1]);
        }
    }

    public void ActivatePrevious()
    {
        // MRU [A,B,C,D] → B 활성 후 A 를 맨 뒤로 → [B,C,D,A]: ActivateNext 의 역순환.
        if (_mru.Count > 1)
        {
            using var op = PerfProbe.Current.TimeOperation("ui.activate.prev");
            var current = _mru[0];
            Activate(_mru[1]);
            _mru.Remove(current);
            _mru.Add(current);
        }
    }

    public void Float(PocDocument document)
    {
        using var op = PerfProbe.Current.TimeOperation("ui.sdi.float");
        ClearOrigin(); // 독립 창 전환의 활성 변경은 사용자 창 전환이 아니다
        Busy.BusyHooks.BeginUntilLaid(document.Id, "float"); // 전환 동안 진행 표시(레이아웃 뒤 저절로 끝난다)
        document.PreFloatBounds ??= MdiTabGroup.WindowOf(document)!.MdiBounds; // 재결합 시 원위치(otest RETEST: 위치 순서 보존, 묶음 안이면 묶음 창 자리)
        var screen = SdiScreenPlacement.MdiScreenRect(document); // 전환 직전 MDI 창의 화면상 보이는 사각형 — SDI 가 같은 자리·크기로 보이게(C2)
        SdiScreenPlacement.RestoreStrip(document); // 접어 둔 기능 버튼 줄은 분리 전에 되돌린다(F08)
#if PD_DIAGNOSTICS
        SdiDiag("float.before", document);
#endif
        using (PerfProbe.Current.TimeOperation("ui.sdi.float.dock")) // Dock 모델 이동 + 호스트 창 생성·표시(호스트 분해는 odev-4b)
        {
            _factory.FloatDockable(document);
        }

        if (screen is { } target)
        {
            SdiScreenPlacement.PlaceSdi(FindWindow(document), target, _log);
        }
        else
        {
            SdiScreenPlacement.PlaceOnMainMonitor(FindWindow(document), _log);
        }
#if PD_DIAGNOSTICS
        SdiDiag("float.after", document);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SdiDiag("float.laid", document), Avalonia.Threading.DispatcherPriority.Background);
#endif

        _log(Loc.Tr("log.window.floated", document.Title));
    }

    /// <summary>
    /// SDI 재결합(MDI-09). DockAsDocument 는 이 구성에서 플로팅 창을 비우지 못해(otest 역라우팅 #2)
    /// 직접 처리한다: 플로팅 dock 에서 제거 → DocumentDock 에 추가 → 빈 창은 창 목록에서 빼고 호스트 창 닫기.
    /// </summary>
    public void Redock(PocDocument document)
    {
        var window = FindWindow(document);
        if (window is null)
        {
            return;
        }

        using var op = PerfProbe.Current.TimeOperation("ui.sdi.redock");
        ClearOrigin(); // 재결합의 활성 변경은 사용자 창 전환이 아니다
        Busy.BusyHooks.BeginUntilLaid(document.Id, "redock"); // 전환 동안 진행 표시(레이아웃 뒤 저절로 끝난다)
        var frame = SdiScreenPlacement.SdiScreenRect(window); // SDI 의 마지막 보이는 프레임 — 재결합 MDI 창을 그 화면 자리에(C2, 못 재면 기존 원위치)
#if PD_DIAGNOSTICS
        SdiDiag("redock.before", document);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SdiDiag("redock.laid", document), Avalonia.Threading.DispatcherPriority.Background);
#endif
        using (PerfProbe.Current.TimeOperation("ui.sdi.redock.remove"))
        {
            _factory.RemoveDockable(document, false);
        }

        using (PerfProbe.Current.TimeOperation("ui.sdi.redock.add")) // MDI 재템플릿·뷰 재부착
        {
            document.MdiBounds = WindowMinSize.AtLeast((frame is { } f ? SdiScreenPlacement.EstimateMdiBounds(Docs, f) : null)
                ?? document.PreFloatBounds ?? PocDockFactory.NextMdiBounds(MdiDocs().Select(d => MdiTabGroup.WindowOf(d)!).Distinct().Select(w => w.MdiBounds))); // 창 단위(묶음 1개 = 자리 1개). 최소 크기 미만으로 재결합하지 않는다(D-26)
            document.PreFloatBounds = null;
            SdiScreenPlacement.KeepPlaced(document, Docs, frame); // 요건 4 — 50% 이상 보이면 첫 배치부터 영역 안으로 당기지 않는다
            _factory.AddDockable(Docs, document);
        }

        using (PerfProbe.Current.TimeOperation("ui.sdi.redock.exit")) // 빈 플로팅 창 정리 + 호스트(OS 창·관리형 창) 닫기
        {
            if (window.Layout is null || !OpenDocuments.Any(d => LayoutSerializer.Contains(window.Layout, d)))
            {
                _factory.RemoveWindow(window);
                window.Exit();
            }
        }

        using (PerfProbe.Current.TimeOperation("ui.sdi.redock.activate"))
        {
            Activate(document);
        }

        if (frame is { } last)
        {
            SdiScreenPlacement.PlaceMdi(document, last, _log); // 배치 뒤 실측으로 바로잡기(추정 실패·DPI·여백 차이)
        }

        _log(Loc.Tr(FindWindow(document) is null ? "log.window.redocked" : "log.window.redockFailed", document.Title));
    }

#if PD_DIAGNOSTICS
    /// <summary>
    /// ③ SDI 캐시 미스 판별 로그(진단 빌드 전용, 동작 변경 없음 — 읽기만 한다).
    /// cached = DockHost 캐시의 이 문서 항목(hash), parent = 그 항목의 시각 부모/논리 부모 형식,
    /// others = DockHost 밖 DockControl(독립 창)의 ControlRecycling 이 DockHost 것과 같은지(same/diff/null).
    /// float.after 에서 cached hash 가 바뀌면 A(떼어 내기 실패 → 대체 생성), 그대로인데 화면 뷰가 다르면 B(다른 캐시).
    /// </summary>
    private void SdiDiag(string stage, PocDocument document)
    {
        var controls = _factory.DockControls.OfType<Avalonia.Controls.Control>().ToList();
        var host = controls.FirstOrDefault(c => c.Name == "DockHost") ?? controls.FirstOrDefault();
        var hostRec = host is null ? null : Avalonia.Controls.Recycling.ControlRecyclingDataTemplate.GetControlRecycling(host);
        object? cached = null;
        var hit = (hostRec as Avalonia.Controls.Recycling.ControlRecycling)?.TryGetValue(document, out cached) ?? false;
        var view = cached as Avalonia.Controls.Control;
        var parent = view is null ? "-" : (Avalonia.VisualTree.VisualExtensions.GetVisualParent(view)?.GetType().Name ?? "null") + "/" + (view.Parent?.GetType().Name ?? "null");
        var others = string.Join(",", controls.Where(c => !ReferenceEquals(c, host)).Select(c =>
        {
            var r = Avalonia.Controls.Recycling.ControlRecyclingDataTemplate.GetControlRecycling(c);
            return r is null ? "null" : ReferenceEquals(r, hostRec) ? "same" : "diff:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r);
        }));
        static string H(object? o) => o is null ? "null" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o).ToString();
        _log($"[sdi] {stage} doc={document.Id} dockControls={controls.Count} hostRec={H(hostRec)} hit={hit} cached={H(view)}({view?.GetType().Name}) parent={parent} others=[{others}]");
    }
#endif

    public void ToggleSdi(PocDocument document)
    {
        if (FindWindow(document) is null)
        {
            Float(document);
        }
        else
        {
            Redock(document);
        }
    }

    public bool TogglePin(PocDocument document)
    {
        var window = FindWindow(document);
        if (window is not null)
        {
            using var op = PerfProbe.Current.TimeOperation("ui.pin");
            LayoutSerializer.SetTopmost(window, !window.Topmost);
            _log(Loc.Tr(window.Topmost ? "log.window.pinned" : "log.window.unpinned", document.Title));
        }

        return window?.Topmost ?? false;
    }
}

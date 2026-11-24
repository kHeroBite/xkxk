// MDI 창 끌기 중 가벼운 크롬 옵션 한 곳 — 켬/끔·조용 시간(ms)·이동 창/정지 창 캐시·정지 창 가벼운 크롬·영역 dirty 추적(시작 옵션). Views 의 MdiMoveLightener 와 원격 토글(chrome.moveLight)이 함께 읽는다(화면 조작 없음)
using System;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 끌기 중 가벼운 크롬(옵션, 초기 제시값). 창 위치가 바뀌는 동안 그림자 같은 비싼 크롬을 잠시 덜어 이동 프레임 비용을 줄인다.
/// Enabled = 경량화 사용 여부(기본 켬). QuietMs = 마지막 위치 변경 뒤 이 시간(ms) 동안 변경이 없으면 이동이 끝난 것으로 보고 원래 크롬으로 되돌린다(기본 300 — 끌다가 잠깐 멈출 때 그림자·모서리가 깜박이며 되살아나는 반복을 막는다, team-lead 결정 2026-11-10).
/// Shell 은 Views 를 참조하지 않는다. 적용은 Views 의 MdiMoveLightener 가 이 값을 읽고 Changed 를 구독해서 한다.
/// </summary>
public static partial class MdiMoveLight
{
    /// <summary>QuietMs 기본값(ms). 01 화면설계 공통원칙 §4.2 와 같다.</summary>
    public const int DefaultQuietMs = 300;

    /// <summary>QuietMs 허용 하한(ms). 너무 짧으면 이동 중 켬/끔이 반복돼 오히려 비싸다.</summary>
    public const int MinQuietMs = 50;

    /// <summary>QuietMs 허용 상한(ms). 이동이 끝난 뒤 크롬이 너무 늦게 돌아오지 않게 한다.</summary>
    public const int MaxQuietMs = 1000;

    private static bool _enabled = true;
    private static int _quietMs = DefaultQuietMs;

    /// <summary>값이 바뀌면 알린다(Views 가 진행 중인 경량화를 즉시 풀거나 다시 판단하게).</summary>
    public static event Action? Changed;

    /// <summary>끌기 중 가벼운 크롬 사용 여부. 기본 켬.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) { return; }
            _enabled = value;
            Changed?.Invoke();
        }
    }

    /// <summary>마지막 위치 변경 뒤 원래 크롬으로 돌아가기까지 기다리는 시간(ms). MinQuietMs~MaxQuietMs 로 자른다. 기본 300.</summary>
    public static int QuietMs
    {
        get => _quietMs;
        set
        {
            var v = Math.Clamp(value, MinQuietMs, MaxQuietMs);
            if (_quietMs == v) { return; }
            _quietMs = v;
            Changed?.Invoke();
        }
    }

    /// <summary>이동 중 비트맵 캐시 기본값(끔). 처음 근거(c7)는 경량화가 걸리지 않은 빌드라 무효이고, c9 에서는 창 5개 p95 차이가 잡음 범위였다 — 추가 측정 뒤 다시 정한다. 규칙 정본 01_화면설계_공통원칙.md §4.2.</summary>
    public const bool DefaultBitmapCache = false;

    private static bool _bitmapCache = DefaultBitmapCache;

    /// <summary>이동 중 창 안쪽(PdClip)을 BitmapCache 로 그릴지(Enabled 의 하위 옵션, 기본 끔). Enabled 가 꺼져 있으면 의미 없다.</summary>
    public static bool BitmapCache
    {
        get => _bitmapCache;
        set
        {
            if (_bitmapCache == value) { return; }
            _bitmapCache = value;
            Changed?.Invoke();
        }
    }

    /// <summary>끌기 중 정지 아래 창 캐시 기본값(끔 — 기본값은 측정 뒤 정한다, 01 §4.2).</summary>
    public const bool DefaultStaticCache = false;

    private static bool _staticCache = DefaultStaticCache;

    /// <summary>
    /// 한 창이 움직이는 동안 같은 MDI 영역의 움직이지 않는 다른 보통 상태 창에 BitmapCache 를 걸지(Enabled 와 별개 옵션).
    /// 근거: Avalonia 12.1.4 는 캐시가 걸린 요소의 내용이 바뀌지 않으면 위로 지나가는 형제 때문에 다시 그리지 않고 저장 비트맵만 그린다(odev-10 디컴파일 확인).
    /// </summary>
    public static bool StaticCache
    {
        get => _staticCache;
        set
        {
            if (_staticCache == value) { return; }
            _staticCache = value;
            Changed?.Invoke();
        }
    }

    /// <summary>끌기 중 겹친 정지 창 가벼운 크롬 기본값(켬 — c12 판정으로 끔에서 변경, 끄는 동안만의 일시 변화, 01 §4.2 N2).</summary>
    public const bool DefaultSiblings = true;

    private static bool _siblings = DefaultSiblings;

    /// <summary>한 창이 움직이는 동안 같은 MDI 영역의 움직이지 않는 다른 보통 상태 창에도 가벼운 크롬(그림자 없음·모서리 0)을 걸지(Enabled 와 별개 옵션, 기본 켬).</summary>
    public static bool Siblings
    {
        get => _siblings;
        set
        {
            if (_siblings == value) { return; }
            _siblings = value;
            Changed?.Invoke();
        }
    }

    /// <summary>끌기 중 모서리 반경 0 기본값(끔 — 사용자 지시 2026-11-11: 끌기 중에도 정지 상태와 같은 둥근 모서리, 01 §4.2).</summary>
    public const bool DefaultSquareCorners = false;

    private static bool _squareCorners = DefaultSquareCorners;

    /// <summary>끌기 중(끄는 창·겹친 정지 창)에 바깥 모서리·잘라냄 반경을 0 으로 할지(예전 동작, 렌더 비용 절감). 기본 끔. Enabled·Siblings 가 걸리는 창에만 의미 있다.</summary>
    public static bool SquareCorners
    {
        get => _squareCorners;
        set
        {
            if (_squareCorners == value) { return; }
            _squareCorners = value;
            Changed?.Invoke();
        }
    }

    /// <summary>끌기 중 끄는 창 내용 히트 테스트 끔 기본값(켬 — 화면 차이 없음, 01a K3 2026-11-14).</summary>
    public const bool DefaultHitTestOff = true;

    private static bool _hitTestOff = DefaultHitTestOff;

    /// <summary>끄는 창이 움직이는 동안 그 창 내용 영역을 입력 히트 테스트에서 뺄지(Views 의 MdiMoveHitTest, 원복은 QuietMs 원복과 같은 시점). 기본 켬.</summary>
    public static bool HitTestOff
    {
        get => _hitTestOff;
        set
        {
            if (_hitTestOff == value) { return; }
            _hitTestOff = value;
            Changed?.Invoke();
        }
    }

    /// <summary>끌기 중 포인터 이동 합치기 기본값(끔 — 지금 동작 그대로, 01a K5 2026-11-14, 기본값은 측정 뒤 리드가 정한다).</summary>
    public const bool DefaultCoalescePointer = false;

    private static bool _coalescePointer = DefaultCoalescePointer;

    /// <summary>MDI 창이 포인터를 잡은 동안의 이동을 렌더 프레임당 마지막 좌표 1회로 합쳐 Dock 에 보낼지(MdiMovePointerCoalesce, 놓기·키 직전 반영). 기본 끔.</summary>
    public static bool CoalescePointer
    {
        get => _coalescePointer;
        set
        {
            if (_coalescePointer == value) { return; }
            _coalescePointer = value;
            Changed?.Invoke();
        }
    }

    /// <summary>놓기 반영을 다음 프레임에(01a K6 2026-11-14) 기본값 — 끔(지금 동작 그대로, 기본값은 측정 뒤 리드가 정한다).</summary>
    public const bool DefaultReleaseFlushFrame = false;

    /// <summary>MDI 제목줄 끌기를 놓은 뒤 렌더 우선순위 이상 디스패처 작업을 바로 실행해 놓기 반영을 다음 프레임에 그릴지(MdiSnapDrag, 01a K6). 다음 놓기부터 반영.</summary>
    public static bool ReleaseFlushFrame { get; set; } = DefaultReleaseFlushFrame;

    /// <summary>K6: 이번 놓기가 MDI 제목줄 끌기의 끝이라 TopLevel 처리기에서 디스패처 작업을 바로 실행할 차례인지(MdiSnapDrag 가 세운다).</summary>
    public static bool ReleaseFlushPending { get; set; }

    private static readonly System.Collections.Generic.HashSet<Avalonia.Controls.TopLevel> s_flushHooked = new();

    /// <summary>K6: TopLevel 인스턴스 놓기 처리기를 1회 붙인다. 인스턴스 처리기는 같은 요소의 클래스 처리기와 창(Dock 끌기 종료·캡처 해제) 처리기가 모두 끝난 뒤에 돈다.</summary>
    public static void HookReleaseFlush(Avalonia.Controls.TopLevel? top)
    {
        if (top is null || !s_flushHooked.Add(top)) { return; }
        top.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, _) =>
        {
            if (!ReleaseFlushPending) { return; }
            ReleaseFlushPending = false;
            // 놓기 후속 작업(스냅 적용·탭 합치기 Post)과 UI 쪽 렌더(레이아웃·합성 커밋)를 다음 rAF 전에 실행한다 — 브라우저는 이산 입력 직후 setTimeout 이 다음 프레임 뒤로 밀린다(01a K6)
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(Avalonia.Threading.DispatcherPriority.Render);
#if PD_DIAGNOSTICS
            ReleaseFlushed++;
            Diagnostics.Remote.MdiReleaseTrace.Mark("k6.flush");
#endif
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>끌기 시작 계산 미리 하기(01a K7 2026-11-14) 기본값 — 끔.</summary>
    public const bool DefaultStaticLightEarly = false;

    /// <summary>정지 창 찾기를 MDI 배치 패널에서 멈추고, 제목줄 누름 때 목록을 미리 계산해 첫 위치 변화 때 쓸지(MdiStaticLight·MdiStaticCache, 01a K7). 다음 누름부터 반영.</summary>
    public static bool StaticLightEarly { get; set; } = DefaultStaticLightEarly;

#if PD_DIAGNOSTICS
    /// <summary>진단 카운터(K6): 놓기 뒤 디스패처 작업을 바로 실행한 수 누계.</summary>
    public static int ReleaseFlushed { get; set; }

    /// <summary>진단 카운터(K7): 미리 계산한 정지 창 목록을 쓴 수·미리 계산한 수·패널에서 멈춘 찾기 수 누계.</summary>
    public static int EarlyUsed { get; set; }

    public static int EarlyComputed { get; set; }

    public static int SiblingsBounded { get; set; }
#endif

    /// <summary>합친 마지막 좌표를 다시 보내는 중이면 true(그 이동 이벤트 처리 동안만). 측정 코드가 다시 보낸 이동을 입력으로 세지 않게 거를 때 쓴다(01a K5).</summary>
    public static bool CoalesceReplaying { get; internal set; }

#if PD_DIAGNOSTICS
    /// <summary>진단 카운터(K5): 합쳐서 Dock 에 보내지 않은 이동 수 누계.</summary>
    public static int CoalesceHeld { get; private set; }

    /// <summary>진단 카운터(K5): 렌더 직전에 마지막 좌표를 다시 보낸 수 누계.</summary>
    public static int CoalesceReplayed { get; private set; }

    /// <summary>진단 카운터(K5): 놓기·키 입력 직전에 남은 좌표를 먼저 반영한 수 누계.</summary>
    public static int CoalesceFlushed { get; private set; }

    /// <summary>진단 카운터(K5): 창이 포인터를 놓쳐 남은 좌표를 버린 수 누계.</summary>
    public static int CoalesceDropped { get; private set; }

    /// <summary>MdiMovePointerCoalesce 가 부른다. 화면 동작에는 쓰지 않는다.</summary>
    public static void NoteCoalesce(int held = 0, int replayed = 0, int flushed = 0, int dropped = 0)
    {
        CoalesceHeld += held;
        CoalesceReplayed += replayed;
        CoalesceFlushed += flushed;
        CoalesceDropped += dropped;
    }

    /// <summary>K5 카운터를 0 으로(원격 coalesceReset=true).</summary>
    public static void ResetCoalesce() => (CoalesceHeld, CoalesceReplayed, CoalesceFlushed, CoalesceDropped) = (0, 0, 0, 0);
#endif

#if PD_DIAGNOSTICS
    /// <summary>진단 카운터(K3): 시작 뒤 내용 히트 테스트를 끈 창 수 누계(Views 의 MdiMoveHitTest 가 갱신, 원격 chrome.moveLight 응답).</summary>
    public static int HitTestOffAppliedTotal { get; private set; }

    /// <summary>진단 카운터(K3): 지금 내용 히트 테스트가 꺼져 있는 창 수(이동이 끝나면 0).</summary>
    public static int HitTestOffActive { get; private set; }

    /// <summary>Views 가 히트 테스트를 끄거나 되돌린 뒤 부른다(applied = 이번에 새로 끈 수, active = 지금 꺼진 수). 화면 동작에는 쓰지 않는다.</summary>
    public static void NoteHitTest(int applied, int active)
    {
        HitTestOffAppliedTotal += applied;
        HitTestOffActive = active;
    }
#endif

    /// <summary>진단 카운터: 시작 뒤 정지 창 캐시를 건 창 수 누계(Views 의 MdiStaticCache 가 갱신, 원격 chrome.moveLight 응답). 끌기 1회에 아래 창 수만큼 늘면 실제로 걸린 것이다.</summary>
    public static int StaticCacheAppliedTotal { get; private set; }

    /// <summary>진단 카운터: 지금 정지 창 캐시가 걸려 있는 창 수(이동이 끝나면 0).</summary>
    public static int StaticCacheActive { get; private set; }

    /// <summary>Views 가 정지 창 캐시를 걸거나 뗀 뒤 부른다(applied = 이번에 새로 건 수, active = 지금 걸린 수). 화면 동작에는 쓰지 않는다.</summary>
    public static void NoteStaticCache(int applied, int active)
    {
        StaticCacheAppliedTotal += applied;
        StaticCacheActive = active;
    }

    /// <summary>영역 dirty 추적 기본값(끔 — 기본값은 측정 뒤 정한다, 01 §4.2).</summary>
    public const bool DefaultRenderDirtyRegions = false;

    /// <summary>settings.json 키(UiSettingsDto 의 같은 이름 속성). 앱 시작 때만 읽힌다.</summary>
    public const string RenderDirtyRegionsSettingsKey = "RenderDirtyRegions";

    /// <summary>
    /// 이번 실행에 적용된 영역 dirty 추적 여부(Avalonia CompositionOptions.UseRegionDirtyRectClipping). 앱 시작 전에만 정할 수 있어 호스트가 기동 때 1회 기록한다.
    /// 실행 중 설정 변경은 저장만 되고 다음 시작부터 반영된다.
    /// </summary>
    public static bool RenderDirtyRegions { get; set; } = DefaultRenderDirtyRegions;

    /// <summary>settings.json 텍스트에서 RenderDirtyRegions 만 읽는다(UiSettings 전체 역직렬화 전). 없거나 손상·bool 이 아니면 null.</summary>
    public static bool? RenderDirtyRegionsFromSettingsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty(RenderDirtyRegionsSettingsKey, out var p)
                && p.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                ? p.GetBoolean()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // 손상된 설정은 기본값으로 — 기동을 막지 않는다
        }
    }
}

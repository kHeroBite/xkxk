// MDI 창 자석(UI-12, MDI-14) — Dock 기본 배치를 그대로 쓰고 끌기·크기 조절 경계만 실시간 흡착으로 보정하는 레이아웃 관리자
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Dock.Avalonia.Mdi;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Settings;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 연결: 테마(MdiChrome.axaml, odev-7c)가 MdiDocumentControl.LayoutManager 에 <see cref="Instance"/> 를 넣는다.
/// 규칙(UI-12 확정 사양): 끄는 중 실시간 흡착(미리보기 없음, 놓은 위치 = 보인 위치), 크기 조절은 끄는 가장자리만,
/// 기준 = MDI 영역 경계 + 다른 창 가장자리(간격 0), 최소·최대화 창은 기준에서 제외, 위치 고정 창은 기준으로만 쓰고 자신은 움직이지 않는다.
/// 켜기·범위는 UiSettings.MagnetEnabled·MagnetRange(0 = 꺼짐). 최소화·최대화 배치와 Z 순서는 Dock 기본 관리자에 맡긴다.
/// 경계: Dock 기본은 창 전체를 영역 안에 가둔다. 이 관리자는 제목 줄의 끌 수 있는 구간만 영역에 남긴다(MdiDragLimit).
/// </summary>
public sealed class MagnetMdiLayoutManager : IMdiLayoutManager
{
    private static readonly IMdiLayoutManager Base = ClassicMdiLayoutManager.Instance;

    /// <summary>최소화 창을 놓는 화면 밖 자리(U7 — 하단 최소화 줄 없음). 크기 0 이라도 최소 크기만큼 그려질 수 있어 영역 밖 먼 곳에 둔다.</summary>
    private static readonly Rect Offscreen = new(-100000, -100000, 0, 0);

    /// <summary>크기 조절에서 Dock 의 영역 제한을 피하는 계산용 원점. 실제 창 좌표보다 충분히 크면 된다.</summary>
    private const double FarOrigin = 1_000_000;

    // UI 스레드 전용 재사용 버퍼(프레임마다 할당하지 않음)
    private readonly List<Rect> _others = new();
    private readonly List<MdiLayoutEntry> _shown = new();

    private MagnetMdiLayoutManager()
    {
    }

    public static MagnetMdiLayoutManager Instance { get; } = new();

    /// <summary>
    /// U7(사용자 11-01): 하단 최소화 표시줄을 없앤다. 최소화 창은 화면 밖에 두고, 나머지 창만 Dock 기본 관리자에 넘긴다 —
    /// 기본 관리자는 최소화 창이 있으면 아래 32px 를 최소화 줄로 비워 두므로(디컴파일 ClassicMdiLayoutManager.Arrange) 넘기지 않아야 영역 전체를 쓴다.
    /// 최소화 창은 상단 창 제목 탭 목록에서 상태색으로 보이고 탭 클릭으로 복원된다(MainView TaskbarTabs).
    /// </summary>
    public void Arrange(IReadOnlyList<MdiLayoutEntry> entries, Size finalSize)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Document.MdiState == MdiWindowState.Minimized)
            {
                entries[i].Control.Arrange(Offscreen); MdiStripFold.Sync(entries[i].Control, Offscreen, finalSize, false);
            }
        }

        var shown = Shown(entries);
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            // D-C3-12·요건 4: 끌어 놓은 창은 놓은 자리 그대로. 밀어 둔 창도 제목 줄을 잡을 수 있으면 그대로 둔다.
            // 잡을 수 없는 창만 Dock 기본 배치가 영역 안으로 당겨 그리고, MdiVisibilityGuard 가 기본 위치로 옮긴다.
            if (shown[i].Document.MdiBounds is { Width: > 0, Height: > 0 } b && shown[i].Document.MdiState == MdiWindowState.Normal)
            {
                var r = new Rect(b.X, b.Y, b.Width, b.Height);
                if (CenterDropTitleAlign.IsDropPlaced(shown[i].Document) || (!new Rect(finalSize).Contains(r) && MdiDragLimit.IsGrabbable(r, finalSize, ArrangeZoneOf(shown[i].Control))))
                {
                    shown[i].Control.Arrange(r); MdiStripFold.Sync(shown[i].Control, r, finalSize, true);
                    shown.RemoveAt(i);
                }
            }
        }
        Base.Arrange(shown, finalSize);
        foreach (var e in shown) { MdiStripFold.Sync(e.Control, e.Control.Bounds, finalSize, e.Document.MdiState == MdiWindowState.Normal); } // 배치 경로와 무관하게 Normal 창은 배치 뒤 실제 위치로 접기를 다시 판정(복구 포함), 최대화는 원복
    }

    public void UpdateZOrder(IReadOnlyList<IMdiDocument> documents, IMdiDocument? activeDocument) =>
        Base.UpdateZOrder(documents, activeDocument);

    public Rect GetDragBounds(IMdiDocument document, Rect startBounds, Vector delta, Size finalSize, IReadOnlyList<MdiLayoutEntry> entries)
    {
        // R2(끌기 프레임 문서 수 비례 비용): 고정 여부·끌기 구간·다른 창 사각형은 끌기 시작 때 1회만 잰다(DragSnapshotOf).
        // 프레임마다는 이동·제한·가장 가까운 기준선 조회만 한다. 끄는 동안 다른 창은 움직이지 않으므로 결과는 같다.
        if (DragSnapshotOf(document, startBounds, entries))
        {
            return startBounds; // 위치 고정 창은 움직이지 않는다(odev-7c 입력 차단과 이중 방어)
        }

        var zone = _dragZone;
        var moved = MdiDragLimit.ClampMove(startBounds.Translate(delta), finalSize, zone);
        var range = XkxkUi.Current.Settings.EffectiveMagnetRange;
        if (range <= 0)
        {
            return Fold(document, moved, finalSize, entries);
        }

        SkipDockMoveClamp();
        var t0 = Stopwatch.GetTimestamp();
        var snapped = MagnetSnap.SnapMove(moved, range, finalSize, _dragOthers);
        var result = snapped == moved ? moved : MdiDragLimit.ClampMove(snapped, finalSize, zone); // 영역 제한 재적용
        Record("ui.magnet.drag", t0);
        return Fold(document, result, finalSize, entries);
    }

    // K2(01a 2026-11-14): 끄는 창 컨트롤은 끌기 스냅샷(DragSnapshotOf)에서 찾아 둔 것을 쓴다 — 이동마다 항목을 다시 훑지 않는다
    private Rect Fold(IMdiDocument d, Rect r, Size a, IReadOnlyList<MdiLayoutEntry> es) { if (ReferenceEquals(d, _dragZoneDoc) && _dragControl is { } c) { MdiStripFold.Sync(c, r, a, true); return r; } foreach (var e in es) { if (ReferenceEquals(e.Document, d)) { MdiStripFold.Sync(e.Control, r, a, true); break; } } return r; }

    /// <summary>
    /// Dock MdiDocumentWindow.DragMove 는 이 관리자의 결과를 받은 뒤 EnableWindowMagnetism 이 켜져 있으면 다시 창 전체를 영역 안에 가둔다(12.1.0.12 디컴파일).
    /// 그 설정은 SDI(OS 창) 자석에 필요해 끌 수 없으므로, 이 호출이 돌아간 직후의 판단에서만 끄고 바로 되돌린다. MDI 흡착은 위 MagnetSnap 이 이미 했다.
    /// </summary>
    private static void SkipDockMoveClamp()
    {
        DockSettings.EnableWindowMagnetism = false;
        if (s_restorePending)
        {
            return; // R2: 되돌리기 작업이 이미 대기 중이면 프레임마다 새로 Post 하지 않는다(그 작업이 이 판단 뒤에 돈다)
        }

        s_restorePending = true;
        Dispatcher.UIThread.Post(static () =>
        {
            s_restorePending = false;
            DockSettings.EnableWindowMagnetism = XkxkUi.Current.Settings.EffectiveMagnetRange > 0;
        }, DispatcherPriority.Send);
    }

    private static bool s_restorePending;

    /// <summary>끄는 동안 재사용하는 스냅샷 — 같은 문서·같은 시작 경계(한 번의 끌기 동안 고정)이고 직전 호출 뒤 DragZoneReuseMs 안이면 다시 재지 않는다.</summary>
    private const double DragZoneReuseMs = 500;

    private IMdiDocument? _dragZoneDoc;
    private Rect _dragZoneStart;
    private long _dragZoneAt;
    private MdiGrabZone _dragZone;
    private bool _dragLocked;
    private Avalonia.Controls.Control? _dragControl; // 끄는 창 항목 컨트롤(스냅샷과 함께 잰다, K2)
    private readonly List<Rect> _dragOthers = new();

    /// <summary>
    /// W4·R2(웹 끌기 프레임 간격): Dock 은 끌기 이동마다 GetDragBounds 를 부르므로(MdiDocumentWindow.DragMove), 창 시각 트리를 훑는 구간 읽기(MdiGrabZoneReader)·
    /// 고정 여부 조회·다른 창 사각형 수집(문서 수 N 비례)을 끌기 시작 때 1회로 줄인다. 끄는 동안 이 창 크기와 다른 창 위치는 바뀌지 않아 값도 같다.
    /// 새 끌기는 문서·시작 경계가 달라지거나 시간 간격이 벌어지거나 InvalidateDragSnapshot 으로 다시 잰다. 돌려주는 값 = 위치 고정 여부.
    /// </summary>
    private bool DragSnapshotOf(IMdiDocument document, Rect startBounds, IReadOnlyList<MdiLayoutEntry> entries)
    {
        var now = Stopwatch.GetTimestamp();
        if (!ReferenceEquals(_dragZoneDoc, document) || _dragZoneStart != startBounds
            || Stopwatch.GetElapsedTime(_dragZoneAt, now).TotalMilliseconds > DragZoneReuseMs)
        {
            _dragLocked = IsLocked(document);
            _dragZone = ZoneOf(document, entries, out _dragControl);
            _dragOthers.Clear();
            _dragOthers.AddRange(Others(document, entries));
            _dragZoneDoc = document;
            _dragZoneStart = startBounds;
        }

        _dragZoneAt = now;
        return _dragLocked;
    }

    /// <summary>끌기 스냅샷을 버린다 — 끌기가 아닌 경로(크기 조절·진단 구동)로 창 배치가 바뀔 수 있을 때 다음 끌기가 반드시 다시 재게 한다.</summary>
    public void InvalidateDragSnapshot()
    {
        _dragZoneDoc = null;
        _dragControl = null;
        MdiGrabZoneCache.InvalidateAll(); // 구간 캐시도 전부 버린다(크기 조절·진단 구동 뒤 첫 판정은 다시 잰다)
    }

    /// <summary>Arrange 의 끌 수 있는 구간 읽기. 끌기 여부와 무관하게 MdiGrabZoneCache 를 쓴다 — 위치만 바뀐 창은 다시 재지 않는다(키: 폭·접힘·템플릿 세대).</summary>
    private static MdiGrabZone ArrangeZoneOf(Avalonia.Controls.Control control) => MdiGrabZoneCache.Get(control);

    /// <summary>문서의 끌 수 있는 구간. 항목을 못 찾으면 제목 줄 전체로 본다.</summary>
    private static MdiGrabZone ZoneOf(IMdiDocument document, IReadOnlyList<MdiLayoutEntry> entries) => ZoneOf(document, entries, out _);

    private static MdiGrabZone ZoneOf(IMdiDocument document, IReadOnlyList<MdiLayoutEntry> entries, out Avalonia.Controls.Control? control)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (ReferenceEquals(entries[i].Document, document))
            {
                control = entries[i].Control;
                return MdiGrabZoneReader.Read(control);
            }
        }

        control = null;
        return MdiGrabZone.Whole;
    }

    /// <summary>Dock 의 최소·최대 크기 규칙만 쓰고(영역 제한이 닿지 않는 먼 좌표에서 계산), 경계는 MdiDragLimit 로 다시 잡는다.</summary>
    private Rect ResizeWithin(IMdiDocument document, Rect start, Vector delta, MdiResizeDirection direction, Size finalSize, MdiGrabZone zone, IReadOnlyList<MdiLayoutEntry> entries)
    {
        var far = new Vector(FarOrigin, FarOrigin);
        var sized = Base.GetResizeBounds(document, start.Translate(far), delta, direction, new Size(FarOrigin * 4, FarOrigin * 4), Shown(entries)).Translate(-far);
        return MdiDragLimit.ClampResize(sized, finalSize, zone, direction.HasFlag(MdiResizeDirection.Left), direction.HasFlag(MdiResizeDirection.Top), direction.HasFlag(MdiResizeDirection.Right));
    }

    public Rect GetResizeBounds(IMdiDocument document, Rect startBounds, Vector delta, MdiResizeDirection direction, Size finalSize,
        IReadOnlyList<MdiLayoutEntry> entries)
    {
        InvalidateDragSnapshot(); // 크기 조절은 창 배치를 바꾸므로 다음 끌기가 다시 재게 한다
        if (IsLocked(document))
        {
            return startBounds;
        }

        var zone = ZoneOf(document, entries);
        var resized = ResizeWithin(document, startBounds, delta, direction, finalSize, zone, entries);
        var range = XkxkUi.Current.Settings.EffectiveMagnetRange;
        if (range <= 0)
        {
            return resized;
        }

        var t0 = Stopwatch.GetTimestamp();
        bool left = direction.HasFlag(MdiResizeDirection.Left), right = direction.HasFlag(MdiResizeDirection.Right);
        bool top = direction.HasFlag(MdiResizeDirection.Top), bottom = direction.HasFlag(MdiResizeDirection.Bottom);
        var snapped = MagnetSnap.SnapResize(resized, range, finalSize, Others(document, entries), left, top, right, bottom);
        var result = resized;
        if (snapped != resized)
        {
            // 붙인 가장자리까지의 이동량으로 다시 계산해 최소·최대 크기와 영역 제한을 Dock 규칙대로 적용한다.
            var dx = left ? snapped.Left - startBounds.Left : right ? snapped.Right - startBounds.Right : delta.X;
            var dy = top ? snapped.Top - startBounds.Top : bottom ? snapped.Bottom - startBounds.Bottom : delta.Y;
            result = ResizeWithin(document, startBounds, new Vector(dx, dy), direction, finalSize, zone, entries);
        }

        Record("ui.magnet.resize", t0);
        return result;
    }

    private static bool IsLocked(IMdiDocument document) =>
        WindowCommands.FindDocument(document) is { } doc && WindowCommands.ChromeOf(doc).IsLocked;

    /// <summary>최소화 창을 뺀 항목(U7 — 최소화 줄이 없으므로 기본 관리자의 영역 계산·제한에서 최소화 창을 뺀다). 재사용 버퍼를 돌려준다.</summary>
    private List<MdiLayoutEntry> Shown(IReadOnlyList<MdiLayoutEntry> entries)
    {
        _shown.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Document.MdiState != MdiWindowState.Minimized)
            {
                _shown.Add(entries[i]);
            }
        }

        return _shown;
    }

    /// <summary>기준이 되는 다른 창(보통 상태, 위치 고정 창 포함 — 기준으로만 쓰인다).</summary>
    private List<Rect> Others(IMdiDocument self, IReadOnlyList<MdiLayoutEntry> entries)
    {
        _others.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            var d = entries[i].Document;
            if (ReferenceEquals(d, self) || d.MdiState != MdiWindowState.Normal)
            {
                continue;
            }

            var b = d.MdiBounds;
            if (b.Width > 0 && b.Height > 0)
            {
                _others.Add(new Rect(b.X, b.Y, b.Width, b.Height));
            }
        }

        return _others;
    }

    /// <summary>보정 비용 기록(진단 빌드만 — 목표 프레임당 1ms 미만, 창 20개).</summary>
    private static void Record(string name, long t0)
    {
        if (!PerfProbe.IsEnabled)
        {
            return;
        }

        var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        PerfProbe.Current.RecordOperation(name, ms, ms);
    }
}

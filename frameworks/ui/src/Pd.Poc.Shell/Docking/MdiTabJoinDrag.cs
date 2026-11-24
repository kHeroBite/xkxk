// MDI 창 제목줄 끌기로 다른 MDI 창의 합치기 아이콘·탭 줄 위에 250ms 머문 뒤 놓으면 탭으로 묶는 처리기(정본 01 §4.4 창 탭 합류 규칙 1~8, MDI→MDI)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// PanelJoinDrag 와 같은 방식(MdiDocumentWindow 누름·이동·놓기 클래스 처리기, handledEventsToo)으로 따로 엿본다 — Dock 의 MDI 이동은 그대로 둔다.
/// 겹침만으로는 아무것도 하지 않는다. 표적(다른 창 제목줄 왼쪽 합치기 아이콘, 묶음 창의 탭 줄) 위에 TabJoinRules.DwellMs 이상 머문 뒤 놓을 때만 MdiTabGroup.Join.
/// 표적 목록·가림 판정용 창 사각형은 끌기 시작 때 1회만 모은다(끌기 중 트리 훑기 없음 — W4·B2 끌기 비용 회귀 방지). 좌표는 MDI 문서 칸(host) DIP.
/// 표시 클래스: 다른 MDI 창 .pdTabJoinArmed(아이콘 보임), 머무는 표적 창 .pdTabJoinTarget + 종류 .pdTabJoinOnIcon|.pdTabJoinOnStrip, 끄는 창 .pdTabJoinSource(표적 위일 때 반투명 — 아래 표적이 보이게).
/// 설치: 테마(MdiDocumentWindowTheme)가 Enabled=True 를 거는 첫 창에서 클래스 처리기를 1회 등록한다(등록 파일 배정 밖이라 첨부 속성으로 붙인다).
/// </summary>
public static partial class MdiTabJoinDrag
{
    public const string ArmedClass = "pdTabJoinArmed";
    public const string TargetClass = "pdTabJoinTarget";
    public const string OnIconClass = "pdTabJoinOnIcon";
    public const string OnStripClass = "pdTabJoinOnStrip";
    public const string SourceClass = "pdTabJoinSource";

    /// <summary>테마의 합치기 아이콘 요소 표식 클래스(템플릿 부품 이름 리터럴을 Shell 에 두지 않으려고 클래스로 찾는다).</summary>
    public const string IconMarkClass = "pdTabJoinIcon";

    /// <summary>테마가 켜는 첨부 속성. 처음 True 가 되면 처리기를 등록한다.</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<MdiDocumentWindow, bool>("Enabled", typeof(MdiTabJoinDrag));

    private static readonly TabJoinDwellGate s_gate = new();
    private static readonly Dictionary<string, (MdiDocumentWindow Window, IMdiDocument Doc, Rect Rect, int Z)> s_windows = new(StringComparer.Ordinal);
    private static List<TabJoinTarget> s_targets = new();
    private static bool s_installed;
    private static MdiDocumentWindow? s_sourceWindow;
    private static IMdiDocument? s_source;
    private static IDocumentDock? s_mdi;
    private static Visual? s_host;
    private static Point s_press;
    private static bool s_dragging;
    private static bool s_captureLost;
    private static MdiDocumentWindow? s_hot;

    static MdiTabJoinDrag() => EnabledProperty.Changed.AddClassHandler<MdiDocumentWindow>((_, e) => { if (e.NewValue is true) { Install(); } });

    public static bool GetEnabled(MdiDocumentWindow w) => w.GetValue(EnabledProperty);
    public static void SetEnabled(MdiDocumentWindow w, bool value) => w.SetValue(EnabledProperty, value);

    /// <summary>합류했다(원천 창 키, 대상 창 키) — 로그·시험 관측용.</summary>
    public static event Action<string, string>? Joined;

    /// <summary>마지막 놓기 판정 한 줄(진단·시험 관측용).</summary>
    public static string LastOutcome { get; private set; } = "none";

    /// <summary>지금 끄는 중이고 합류 표적 위인가(읽기 전용 — MdiSnapDrag 우선순위 ①).</summary>
    public static bool IsOverTarget => s_dragging && s_gate.Current is not null;

    /// <summary>클래스 처리기를 1회 등록한다(멱등).</summary>
    public static void Install()
    {
        if (s_installed) { return; }
        s_installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiDocumentWindow>(OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<MdiDocumentWindow>((w, e) => OnReleasedDiag(e, "window"), RoutingStrategies.Bubble, handledEventsToo: true);
        // 창이 캡처를 먼저 잃으면 놓기는 포인터 아래 요소로 간다(PanelJoinDrag i0215 실측과 같은 경로) — TopLevel 에서 받아 판정한다
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, e) => { if (s_captureLost) { OnReleasedDiag(e, "toplevel"); } }, RoutingStrategies.Bubble, handledEventsToo: true);
        // 하위 요소의 캡처 잃음(누를 때 암묵 캡처 → BeginDrag 가 창으로 옮김)은 취소가 아니다 — 끄는 창 자신이 잃을 때만
        InputElement.PointerCaptureLostEvent.AddClassHandler<MdiDocumentWindow>((w, e) => { if (ReferenceEquals(e.Source, w) && ReferenceEquals(w, s_sourceWindow)) { OnCaptureLost(); } },
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>source 창 기준 합류 표적 목록(MDI 문서 칸 DIP, 끄는 창 제외). 시험·진단(window.tabJoinTargets)용 — 끌기 상태를 바꾸지 않는다. 숨은 아이콘도 자리 기준 사각형을 낸다.</summary>
    public static IReadOnlyList<TabJoinTarget> CollectTargets(MdiDocumentWindow source) =>
        source.DataContext is IMdiDocument { Owner: IDocumentDock mdi } doc && FindHost(source, mdi) is { } host
            ? Collect(host, mdi, new(StringComparer.Ordinal)).Where(t => t.WindowKey != doc.Id).ToList()
            : Array.Empty<TabJoinTarget>();

    private static void OnPressed(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        Reset();
        if (window.DataContext is IMdiDocument doc and (PocDocument or MdiTabGroupDock) && doc.Owner is IDocumentDock mdi
            && doc.MdiState == MdiWindowState.Normal && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed
            && PanelJoinTargets.IsTitlePress(e.Source as Visual, window) && FindHost(window, mdi) is { } host)
        {
            (s_sourceWindow, s_source, s_mdi, s_host, s_press) = (window, doc, mdi, host, e.GetPosition(host));
        }
    }

    private static void OnMoved(MdiDocumentWindow window, PointerEventArgs e)
    {
        if (!ReferenceEquals(window, s_sourceWindow) || s_host is not { } host || s_mdi is not { } mdi) { return; }
        var point = e.GetPosition(host);
        if (!s_dragging)
        {
            if (!TabJoinRules.IsDrag(s_press, point)) { return; }
            s_dragging = true;
#if PD_DIAGNOSTICS
            using var diagBegin = Diagnostics.MdiDragFrameDiag.Time(Diagnostics.MdiDragFrameDiag.Stage.TabJoinBegin); // 01a P1: 끌기 시작(표적 수집·아이콘) 시간
#endif
            s_targets = Collect(host, mdi, s_windows); // 끌기 시작 때 1회만
            if (!BeginNearArm(window)) // 옵션 A 끔(기본): 다른 창 전부 아이콘 켬(종전 그대로). 켬: 근접 창만(Near.cs)
            {
                foreach (var (w, _, _, _) in s_windows.Values.Where(x => !ReferenceEquals(x.Window, window)))
                {
                    w.Classes.Add(ArmedClass);
                }
            }
        }

        if (s_gate.Update(PickVisible(point))) { ShowHover(); }
        if (s_nearMode) { UpdateNearArm(point); }
    }

    private static void OnReleased(PointerReleasedEventArgs e, string via)
    {
        if (s_source is not { } source || s_mdi is not { } mdi || s_host is not { } host) { return; }
        var dragging = s_dragging;
        var current = s_gate.Current;
        var armed = s_gate.IsArmed;
        var atRelease = dragging ? PickVisible(e.GetPosition(host)) : null;
        var accepted = dragging && armed && current is not null && atRelease?.Id == current.Id
            && s_windows.TryGetValue(current.WindowKey, out var hit) && !ReferenceEquals(hit.Doc, source);
        var target = accepted ? s_windows[current!.WindowKey].Doc : null;
        LastOutcome = $"{via}:source={source.Id}|dragging={dragging}|current={current?.Id ?? "none"}|release={atRelease?.Id ?? "none"}|armed={armed}|accepted={accepted}";
        ResetOnRelease();
        if (target is null || mdi is not IDock { Factory: { } factory }) { return; }
        // Dock 의 놓기 처리(EndDrag)가 끝난 뒤 트리를 바꾼다(PanelJoinDrag.Join 과 같은 이유). 그새 닫혔으면 하지 않는다.
        Dispatcher.UIThread.Post(() =>
        {
            if (mdi.VisibleDockables is not { } children || !children.Contains(source) || !children.Contains(target)) { LastOutcome += "|join=skipped"; return; }
            MdiTabGroup.Join(factory, mdi, target, source);
            LastOutcome += "|join=done";
            Joined?.Invoke(source.Id, target.Id);
        });
    }

    /// <summary>창 자신이 캡처를 잃음. 놓기가 TopLevel 로 올 수 있어 500ms 기다린 뒤에만 취소한다(PanelJoinDrag 와 같은 규칙).</summary>
    private static void OnCaptureLost()
    {
        s_captureLost = true;
        var source = s_source;
        DispatcherTimer.RunOnce(() =>
        {
            if (s_captureLost && source is not null && ReferenceEquals(s_source, source)) { LastOutcome = $"capture-lost-no-release:source={source.Id}"; Reset(); }
        }, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>표적 고르기 + 가림 판정 — 표적보다 Z 가 높은 다른 창(끄는 창 제외)이 그 점을 덮고 있으면 보이지 않는 표적이라 고르지 않는다.</summary>
    private static TabJoinTarget? PickVisible(Point point)
    {
        var sourceKey = s_source?.Id;
        var pick = TabJoinRules.Pick(point, s_targets, sourceKey);
        return pick is null || s_windows.Any(kv => kv.Key != sourceKey && kv.Key != pick.WindowKey && kv.Value.Z > pick.Z && kv.Value.Rect.Contains(point)) ? null : pick;
    }

    private static void ShowHover()
    {
        ClearHot();
        if (s_gate.Current is { } t && s_windows.TryGetValue(t.WindowKey, out var w))
        {
            s_hot = w.Window;
            s_hot.Classes.Add(TargetClass);
            s_hot.Classes.Add(t.Kind == TabJoinKind.Icon ? OnIconClass : OnStripClass);
            s_sourceWindow?.Classes.Add(SourceClass);
        }
    }

    private static void ClearHot()
    {
        s_hot?.Classes.RemoveAll(new[] { TargetClass, OnIconClass, OnStripClass });
        s_hot = null;
        s_sourceWindow?.Classes.Remove(SourceClass);
    }

    private static void Reset()
    {
        ClearHot();
        if (s_nearMode)
        {
            ResetNear(); // 옵션 A 켬으로 시작한 끌기: 켜져 있던 창만 원복
        }
        else
        {
            foreach (var (w, _, _, _) in s_windows.Values)
            {
                w.Classes.Remove(ArmedClass);
            }
        }

        s_windows.Clear();
        s_targets = new List<TabJoinTarget>();
        s_gate.Reset();
        (s_sourceWindow, s_source, s_mdi, s_host) = (null, null, null, null);
        s_dragging = s_captureLost = false;
    }

    /// <summary>MDI 문서 칸 — 창 위쪽 조상 중 DataContext 가 그 MDI dock 인 첫 요소(문서 칸 안 창들의 공통 조상).</summary>
    internal static Visual? FindHost(MdiDocumentWindow window, IDocumentDock mdi)
    {
        var depth = 0;
        for (var v = window.GetVisualParent(); v is not null && v is not TopLevel && depth < 12; v = v.GetVisualParent(), depth++)
        {
            if (v is StyledElement { DataContext: var dc } && ReferenceEquals(dc, mdi))
            {
                return v;
            }
        }

        return window.GetVisualParent();
    }

    /// <summary>host 아래 같은 MDI dock 의 보이는 창(최소화 제외)을 모아 창 사각형과 표적(아이콘 칸, 묶음이면 탭 줄)을 만든다. 창 안쪽(문서 내용)으로는 내려가지 않는다.</summary>
    private static List<TabJoinTarget> Collect(Visual host, IDocumentDock mdi, Dictionary<string, (MdiDocumentWindow Window, IMdiDocument Doc, Rect Rect, int Z)> windows)
    {
        windows.Clear();
        var targets = new List<TabJoinTarget>();
        var stack = new Stack<Visual>(host.GetVisualChildren());
        while (stack.Count > 0)
        {
            var v = stack.Pop();
            if (v is not MdiDocumentWindow w)
            {
                foreach (var c in v.GetVisualChildren()) { stack.Push(c); }
                continue;
            }

            if (!w.IsVisible || w.DataContext is not IMdiDocument doc || !ReferenceEquals(doc.Owner, mdi)
                || doc is not (PocDocument or MdiTabGroupDock) || doc.MdiState == MdiWindowState.Minimized || RectIn(w, host) is not { } rect) { continue; }
            var z = doc.MdiState == MdiWindowState.Maximized ? int.MaxValue : doc.MdiZIndex;
            windows[doc.Id] = (w, doc, rect, z);
            if (IconRect(w, host) is { } icon) { targets.Add(new TabJoinTarget(doc.Id + "#icon", TabJoinKind.Icon, icon, z, doc.Id)); }
            if (doc is MdiTabGroupDock && TabSlotMarker.Descendants(w).OfType<DocumentTabStrip>().FirstOrDefault() is { } strip && RectIn(strip, host) is { } stripRect)
            {
                targets.Add(new TabJoinTarget(doc.Id + "#strip", TabJoinKind.TabStrip, stripRect, z, doc.Id));
            }
        }

        return targets;
    }

    /// <summary>
    /// 합치기 아이콘 사각형 — 아이콘은 끌기 전엔 숨어 있어(크기 0) 늘 보이는 부모 칸(테마의 아이콘 자리) 가운데에 아이콘 크기로 계산한다.
    /// 2 DIP 여유를 둔다(20×20 → 24×24). 표식 요소가 없으면 null.
    /// </summary>
    private static Rect? IconRect(MdiDocumentWindow w, Visual host)
    {
        var header = TabSlotMarker.Descendants(w).OfType<Control>().FirstOrDefault(c => c.Name == TemplateParts.Header);
        var icon = header?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Classes.Contains(IconMarkClass));
        if (icon?.GetVisualParent() is not Visual slot || RectIn(slot, host) is not { } s || double.IsNaN(icon.Width) || double.IsNaN(icon.Height))
        {
            return null;
        }

        return new Rect(s.Center.X - (icon.Width / 2), s.Center.Y - (icon.Height / 2), icon.Width, icon.Height).Inflate(2);
    }

    internal static Rect? RectIn(Visual v, Visual host) =>
        v.TranslatePoint(default, host) is { } p && v.Bounds.Width > 0 && v.Bounds.Height > 0 ? new Rect(p, v.Bounds.Size) : null;
}

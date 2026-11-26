// 창 메뉴 '배치 ▸'·창 배치 단축키가 부르는 칸 배치 동작 — 대상 창의 기준 영역(MDI 영역·웹 관리형 창 레이어·모니터 작업 영역)에서 칸 사각형(SnapZoneContext.CellTarget)을 적용한다(11_창배치_스냅레이아웃.md §6·§3.6)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Platform;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 화면 층(창 메뉴 '배치' 하위 메뉴)과 단축키(SnapKeyboardCommands)는 대상(WindowMenuTarget)과 칸(layoutId·cell)만 넘긴다.
/// 기준 영역: MDI 문서 창 = 그 창의 MDI 배치 패널, 웹 관리형 창 = 관리형 창 레이어, OS 창(데스크톱 SDI·메인) = 창이 있는 모니터의 작업 영역.
/// 옵션은 끌기와 같은 UiSettings.CreateSnapContext 로 반영한다(스냅 끔·칸 종류 끔·최소 크기 미달이면 적용 불가). 위치 고정 창은 대상이 아니다.
/// 'max' 칸은 사각형을 채우는 대신 최대화 상태로 바꾼다(창 메뉴 최대화와 같은 경로, MdiSnapDrag 와 같은 처리).
/// 스냅 전 크기는 메모리에만 둔다(SnapRestoreMemory, 저장 형식 무변경). RestoreSnap 이 되돌린다(단축키 '복원').
/// </summary>
public static partial class WindowMenuOps
{
    /// <summary>'배치' 하위 메뉴에 보일 칸 종류(옵션 SnapLayouts·SnapEnabled 반영, 목록 순서).</summary>
    public static IReadOnlyList<SnapLayout> PlacementLayouts => UiSettings.Current.EffectiveSnapLayouts;

    /// <summary>대상에 그 칸을 적용할 수 있는가(메뉴 항목 활성). 칸이 최소 크기보다 작거나 스냅이 꺼졌으면 false.</summary>
    public static bool CanApplyCell(WindowMenuTarget target, string layoutId, int cell) =>
        Placement(target) is { } p && p.Context.CellTarget(layoutId, cell) is { Width: > 0, Height: > 0 }
        && (!IsMax(layoutId) || Facts(target).Maximize);

    /// <summary>
    /// 대상 창을 칸에 배치한다. 최소화·최대화 창은 먼저 보통 상태로 돌린다. 적용했으면 true.
    /// assist 가 있으면(끌기 막대 칸·최대화 단추 팝업 칸) 배치 성공 뒤 다음 프레임에 Snap Assist 를 연다(11 문서 §3.9). 창 메뉴·단축키·원격·세션 Pick 은 null 이라 띄우지 않는다.
    /// </summary>
    public static bool ApplyCell(WindowMenuTarget target, string layoutId, int cell, SnapAssistSource? assist = null)
    {
        if (Placement(target) is not { } p || p.Context.CellTarget(layoutId, cell) is not { Width: > 0, Height: > 0 } rect)
        {
            PdLog.Info("window-menu", $"{target.Kind} snap {layoutId}#{cell} doc={target.Document?.Id} done=False(not-applicable)");
            return false;
        }

        bool done;
        if (IsMax(layoutId))
        {
            done = Run(target, WindowMenuAction.Maximize);
        }
        else
        {
            if (Facts(target).Restore)
            {
                Run(target, WindowMenuAction.Restore);
            }

            SnapRestoreMemory.Remember(p.Key, p.Current(), rect, p.Tolerance);
            p.Set(rect);
            done = true;
            Finish(target);
        }

        PdLog.Info("window-menu", $"{target.Kind} snap {layoutId}#{cell} doc={target.Document?.Id} rect={rect} done={done}");
        // max 제외는 SnapAssistSession.TryStart 가 판정해 LastSkipReason="max" 로 남긴다(계획 §5.1 끝 사유 계약).
        if (done && assist is { } source)
        {
            StartAssist(target, layoutId, cell, source);
        }

        return done;
    }

    /// <summary>
    /// 칸 배치 뒤 Snap Assist 진입(ApplyCell 의 assist 경로). 범위·anchor: OS 창 = Sdi(그 Window), 관리형 창 = Managed(레이어 배치 패널), MDI 창 = Mdi(MDI 배치 패널).
    /// 메인 창(OS 팝업 경로)과 SDI 스냅을 OS·끔으로 둔 경우는 띄우지 않는다. 나머지 제외 판정은 SnapAssistSession.TryStart 가 한다(LastSkipReason).
    /// 배치·활성화가 끝난 다음 프레임(Background)에 연다.
    /// </summary>
    private static void StartAssist(WindowMenuTarget target, string layoutId, int cell, SnapAssistSource source)
    {
        if (target.Kind == WindowMenuKind.Main)
        {
            return;
        }

        var window = target.Document is { } d && XkxkUi.Current.Shell is WindowCommands s ? s.SdiPlacement.WindowOf(d) : target.PanelWindow;
        SnapAssistScope scope;
        object? anchor;
        if (window is not null)
        {
            if (window.Host is Window host)
            {
                if (UiSettings.Current.SnapSdiMode is SnapSdiMode.Os or SnapSdiMode.Off)
                {
                    return;
                }

                (scope, anchor) = (SnapAssistScope.Sdi, host);
            }
            else
            {
                (scope, anchor) = (SnapAssistScope.Managed, ManagedWindowRect.LayerPanel());
            }
        }
        else if (target.Document is { } doc)
        {
            (scope, anchor) = (SnapAssistScope.Mdi, FindMdi(doc).Panel);
        }
        else
        {
            return;
        }

        Dispatcher.UIThread.Post(() => SnapAssistSession.TryStart(target, layoutId, cell, source, scope, anchor!), DispatcherPriority.Background);
    }

    /// <summary>MDI 끌기(MdiSnapDrag.Apply 의 apply=snapped) 뒤 Snap Assist 진입. 대상은 놓은 MDI 창, anchor 는 그 창의 MDI 배치 패널(zone 좌표 = MdiBounds 좌표).</summary>
    internal static void StartAssistFromMdiDrag(IMdiDocument mdi, SnapZone zone)
    {
        if (WindowCommands.FindDocument(mdi) is not { } doc)
        {
            return;
        }

        var target = new WindowMenuTarget(WindowMenuKind.Mdi, doc, null);
        Dispatcher.UIThread.Post(() => SnapAssistSession.TryStartFromZone(target, zone, SnapAssistScope.Mdi, FindMdi(doc).Panel!), DispatcherPriority.Background);
    }

    /// <summary>관리형 창 끌기(ManagedSnapDrag.Apply 의 apply=snapped) 뒤 Snap Assist 진입. 대상은 놓은 관리형 창(문서가 없으면 패널 창), anchor 는 레이어 배치 패널.</summary>
    internal static void StartAssistFromManagedDrag(ManagedDockWindowDocument managed, IDockWindow window, SnapZone zone)
    {
        var target = WindowCommands.FindDocument(managed) is { } doc
            ? new WindowMenuTarget(WindowMenuKind.Sdi, doc, null)
            : new WindowMenuTarget(WindowMenuKind.Sdi, null, null, window);
        Dispatcher.UIThread.Post(() => SnapAssistSession.TryStartFromZone(target, zone, SnapAssistScope.Managed, ManagedWindowRect.LayerPanel()!), DispatcherPriority.Background);
    }

    /// <summary>위치 고정 창인가(Snap Assist 후보 제외 기준 — SnapAssistCandidates.IsLocked 와 같은 기준). 문서가 없는 대상(메인·패널 창)은 false.</summary>
    internal static bool IsSnapLockedTarget(WindowMenuTarget target) => target.Document is { } doc && IsSnapLocked(doc);

    /// <summary>
    /// 단축키 '복원': 최대화·최소화 창은 보통 상태로, 스냅된 보통 창은 스냅 전 크기로(지금 위치 기준, 영역 안으로 당김). 할 것이 없으면 false.
    /// </summary>
    public static bool RestoreSnap(WindowMenuTarget target)
    {
        if (Facts(target).Restore)
        {
            return Run(target, WindowMenuAction.Restore);
        }

        if (Placement(target) is not { } p || !SnapRestoreMemory.TryTake(p.Key, p.Current(), out var before))
        {
            return false;
        }

        var now = p.Current();
        var area = p.Context.Area;
        var (x, y) = p.Context.Enabled
            ? (Math.Clamp(now.X, area.X, Math.Max(area.X, area.Right - before.Width)), Math.Clamp(now.Y, area.Y, Math.Max(area.Y, area.Bottom - before.Height)))
            : (now.X, now.Y);
        p.Set(new Rect(x, y, before.Width, before.Height));
        Finish(target);
        PdLog.Info("window-menu", $"{target.Kind} snap-restore doc={target.Document?.Id} size={before}");
        return true;
    }

    private static bool IsMax(string layoutId) => string.Equals(layoutId?.Trim(), SnapLayoutCatalog.Max, StringComparison.OrdinalIgnoreCase);

    /// <summary>적용 뒤 공통: 문서면 활성화(맨 앞), 끌기 없는 배치 변경이라 자동 저장 dirty.</summary>
    private static void Finish(WindowMenuTarget target)
    {
        if (target.Document is { } doc && XkxkUi.Current.Shell is WindowCommands shell)
        {
            shell.Activate(doc);
        }

        App.NotifyLayoutChanged();
    }
}

/// <summary>
/// 스냅 전 크기·스냅된 사각형 메모리(창 메뉴·단축키·관리형 창 끌기 공용, 저장 안 함 — 11 문서 §3.6). 키 = 창 객체(MDI 창, IDockWindow, OS Window).
/// 지금 사각형이 스냅된 사각형과 다르면(사용자가 옮기거나 크기를 바꿈) 기억을 버린다. 연달아 스냅하면 처음 크기를 유지한다.
/// </summary>
internal static class SnapRestoreMemory
{
    private sealed class Memo
    {
        public Size Before;
        public Rect Snapped;
        public double Tolerance;
    }

    private static readonly ConditionalWeakTable<object, Memo> s_memo = new();

    /// <summary>current = 적용 직전 사각형, snapped = 적용할 사각형.</summary>
    public static void Remember(object key, Rect current, Rect snapped, double tolerance = 0.5)
    {
        var before = s_memo.TryGetValue(key, out var m) && IsSame(current, m.Snapped, m.Tolerance) ? m.Before : current.Size;
        s_memo.AddOrUpdate(key, new Memo { Before = before, Snapped = snapped, Tolerance = tolerance });
    }

    /// <summary>current 가 기억한 스냅 사각형 그대로면 스냅 전 크기를 꺼낸다. 어느 경우든 기억은 지운다.</summary>
    public static bool TryTake(object key, Rect current, out Size before)
    {
        before = default;
        if (!s_memo.TryGetValue(key, out var m))
        {
            return false;
        }

        s_memo.Remove(key);
        if (!IsSame(current, m.Snapped, m.Tolerance))
        {
            return false;
        }

        before = m.Before;
        return true;
    }

    private static bool IsSame(Rect a, Rect b, double tol) =>
        Math.Abs(a.X - b.X) < tol && Math.Abs(a.Y - b.Y) < tol && Math.Abs(a.Width - b.Width) < tol && Math.Abs(a.Height - b.Height) < tol;
}

/// <summary>웹·Android 관리형 창 좌표 도우미(ManagedSnapDrag 끌기와 '배치' 메뉴 공용). 좌표 = 관리형 창 레이어 DIP.</summary>
internal static class ManagedWindowRect
{
    /// <summary>관리형 창의 지금 사각형. 그려지는 값(MdiBounds)이 우선이고, 없으면 모델 X/Y/Width/Height.</summary>
    public static Rect RectOf(ManagedDockWindowDocument? managed, IDockWindow window) =>
        managed?.MdiBounds is { Width: > 0, Height: > 0 } b ? new Rect(b.X, b.Y, b.Width, b.Height) : new Rect(window.X, window.Y, window.Width, window.Height);

    /// <summary>위치·크기 적용 — SdiPlacement·sdi.setBounds 와 같은 경로(모델 X/Y/W/H + Host.SetPosition/SetSize, ManagedHostWindow 가 MdiBounds 에 쓴다).</summary>
    public static void SetRect(IDockWindow window, Rect r)
    {
        (window.X, window.Y, window.Width, window.Height) = (r.X, r.Y, r.Width, r.Height);
        window.Host?.SetPosition(r.X, r.Y);
        window.Host?.SetSize(r.Width, r.Height);
    }

    /// <summary>IDockWindow 에 해당하는 관리형 창 래퍼(보이는 레이어에서 찾는다). 없으면 null.</summary>
    public static ManagedDockWindowDocument? FindManaged(IDockWindow window) =>
        Layer()?.GetVisualDescendants().OfType<MdiDocumentWindow>()
            .Select(w => w.DataContext as ManagedDockWindowDocument).FirstOrDefault(m => ReferenceEquals(m?.Window, window));

    /// <summary>레이어의 배치 영역(ManagedWindowHostCommands.LayerArea 와 같은 패널). 레이어를 못 찾으면 빈 사각형.</summary>
    public static Rect LayerArea() =>
        LayerPanel() is { Bounds: { Width: > 0, Height: > 0 } } panel
            ? new Rect(panel.Bounds.Size)
            : default;

    /// <summary>레이어의 배치 패널(LayerArea 와 같은 패널 — Snap Assist 의 Managed anchor). 레이어를 못 찾으면 null.</summary>
    public static MdiLayoutPanel? LayerPanel() => Layer()?.GetVisualDescendants().OfType<MdiLayoutPanel>().FirstOrDefault();

    private static ManagedWindowLayer? Layer()
    {
        var view = Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: { } v } ? v : null;
        return view is not null && TopLevel.GetTopLevel(view) is { } top
            ? top.GetVisualDescendants().OfType<ManagedWindowLayer>().FirstOrDefault(l => l.IsVisible)
            : null;
    }
}

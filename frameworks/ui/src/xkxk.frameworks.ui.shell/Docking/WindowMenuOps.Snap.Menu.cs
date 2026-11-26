// 창 메뉴 '배치'·단축키 칸 배치의 대상별 배치 수단 — 기준 영역(MDI 배치 패널·관리형 창 레이어·모니터 작업 영역) 판정과 사각형 읽기·쓰기(WindowMenuOps.Snap.cs 의 partial)
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

public static partial class WindowMenuOps
{
    /// <summary>대상 1개의 배치 수단: 판정 문맥(기준 영역·옵션), 메모리 키, 지금 사각형 읽기, 사각형 쓰기, 같은 사각형 판정 허용 오차(DIP).</summary>
    private sealed record SnapPlacement(SnapZoneContext Context, object Key, Func<Rect> Current, Action<Rect> Set, double Tolerance);

    private static SnapPlacement? Placement(WindowMenuTarget target)
    {
        if (target.Kind == WindowMenuKind.Main)
        {
            return target.Main is { } main ? OsPlacement(main, new Size(Math.Max(0, main.MinWidth), Math.Max(0, main.MinHeight))) : null;
        }

        if (target.Document is { } locked && IsSnapLocked(locked))
        {
            return null; // 위치 고정 창은 대상이 아니다(11 문서 §2)
        }

        var window = target.Document is { } d && XkxkUi.Current.Shell is WindowCommands s ? s.SdiPlacement.WindowOf(d) : target.PanelWindow;
        if (window is not null)
        {
            return window.Host is Window host ? OsPlacement(host, WindowMinSize.Current) : ManagedPlacement(window);
        }

        if (target.Document is not { } doc || PocDockFactory.PanelOf(doc) is not null || MdiTabGroup.WindowOf(doc) is not { } win
            || ((IDockable)win).Owner is not IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi })
        {
            return null;
        }

        var (panel, _) = FindMdi(doc);
        if (panel is not { Bounds: { Width: > 0, Height: > 0 } })
        {
            return null;
        }

        return new SnapPlacement(XkxkUi.Current.Settings.CreateSnapContext(new Rect(panel.Bounds.Size), WindowMinSize.Current), win,
            () => new Rect(win.MdiBounds.X, win.MdiBounds.Y, win.MdiBounds.Width, win.MdiBounds.Height),
            r =>
            {
                win.MdiBounds = new DockRect(r.X, r.Y, r.Width, r.Height);
                MagnetMdiLayoutManager.Instance.InvalidateDragSnapshot(); // 끌기가 아닌 경로로 배치가 바뀌었다
            }, 0.5);
    }

    /// <summary>웹·Android 관리형 창: 기준 영역 = 관리형 창 레이어(ManagedSnapDrag 와 같은 영역·같은 쓰기 경로).</summary>
    private static SnapPlacement? ManagedPlacement(IDockWindow window)
    {
        var area = ManagedWindowRect.LayerArea();
        if (!(area.Width > 0 && area.Height > 0))
        {
            return null;
        }

        var managed = ManagedWindowRect.FindManaged(window);
        return new SnapPlacement(XkxkUi.Current.Settings.CreateSnapContext(area, WindowMinSize.Current), window,
            () => ManagedWindowRect.RectOf(managed, window), r => ManagedWindowRect.SetRect(window, r), 0.5);
    }

    /// <summary>
    /// OS 창(데스크톱 SDI·메인): 기준 영역 = 창이 있는 모니터의 작업 영역을 그 모니터 배율로 나눈 DIP. 칸 사각형은 '보이는 프레임'(App.VisibleFrameOf — 보이지 않는 크기 조절 테두리 제외)에 맞춘다.
    /// 비전경 시험(--bg-pos)은 화면 밖 자리를 지키므로 적용하지 않는다(SdiScreenPlacement 와 같은 규칙).
    /// </summary>
    private static SnapPlacement? OsPlacement(Window w, Size minSize)
    {
        if (PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode) || (w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary) is not { } screen)
        {
            return null;
        }

        var s = screen.Scaling > 0 ? screen.Scaling : 1;
        var wa = screen.WorkingArea;
        var area = new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
        return new SnapPlacement(XkxkUi.Current.Settings.CreateSnapContext(area, minSize), w, () => VisibleDip(w, s), r => PlaceOs(w, r, s), 1.5);
    }

    private static PixelRect VisibleFrame(Window w) =>
        App.VisibleFrameOf?.Invoke(w) ?? new PixelRect(w.Position, PixelSize.FromSize(w.FrameSize ?? w.ClientSize, w.RenderScaling));

    private static Rect VisibleDip(Window w, double s)
    {
        var f = VisibleFrame(w);
        return new Rect(f.X / s, f.Y / s, f.Width / s, f.Height / s);
    }

    /// <summary>보이는 프레임이 r(작업 영역 DIP)이 되게 위치(px)·클라이언트 크기(DIP)를 정한다. 보이지 않는 테두리·제목 틀 차이는 지금 값으로 잰다.</summary>
    private static void PlaceOs(Window w, Rect r, double s)
    {
        w.WindowState = WindowState.Normal;
        var vis = VisibleFrame(w);
        var frame = w.FrameSize ?? w.ClientSize;
        var invisibleW = Math.Max(0, frame.Width - vis.Width / s);
        var invisibleH = Math.Max(0, frame.Height - vis.Height / s);
        w.Position = new PixelPoint((int)Math.Round(r.X * s) - (vis.X - w.Position.X), (int)Math.Round(r.Y * s) - (vis.Y - w.Position.Y));
        w.Width = Math.Max(0, r.Width + invisibleW - (frame.Width - w.ClientSize.Width));
        w.Height = Math.Max(0, r.Height + invisibleH - (frame.Height - w.ClientSize.Height));
    }

    /// <summary>위치 고정 창인가. 묶음은 안 문서 중 하나라도 잠기면 잠긴 창이다(Facts 와 같은 기준, 고정 여부 자체로 판단 — LockBlocksMinMax 와 무관).</summary>
    private static bool IsSnapLocked(PocDocument doc)
    {
        if (MdiTabGroup.GroupOf(doc) is { } group)
        {
            foreach (var d in group.Documents)
            {
                if (WindowCommands.ChromeOf(d).IsLocked) { return true; }
            }

            return false;
        }

        return WindowCommands.ChromeOf(doc).IsLocked;
    }
}

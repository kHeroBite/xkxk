// 창 메뉴 '기본 위치로 이동'의 데스크톱 실행 — 메인 창은 소속 모니터 가운데로, SDI 창은 그 창 하나만 ⑦ 공식 자리로 옮긴다(크기 유지, 넘치는 메인만 줄임)
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 자동 복구(MonitorChangeWatcher)와 ④ 초기화(ResetMain)는 주 모니터를 기준으로 하지만, 이 명령은 사용자가 지금 쓰는 모니터에서 창을 찾으려는 것이라 메인 창의 소속 모니터를 기준으로 한다.
/// 비전경 시험(--bg-pos)인데 --primary-override 가 없으면 기준 영역이 실제 모니터가 되므로 옮기지 않는다(시험 창이 사용자 화면에 나오지 않게).
/// </summary>
internal static class WindowDefaultMove
{
    public static bool Main(Window main)
    {
        try
        {
            if (IsBlocked())
            {
                HostLog.Write("window-menu move main skip: background without primary override");
                return false;
            }

            WindowPosTrace.Begin("move-default");
            MaxTrackHook.Release();
            WindowFrameMetrics.RestoreIfArranged(main);
            main.WindowState = WindowState.Normal; // 복원한 뒤의 사각형으로 소속 모니터를 고른다(최소화 좌표는 쓸 수 없다)
            var work = AreasWithOwnerFirst(main)[0];
            var scale = main.DesktopScaling;
            var (fw, fh) = WindowFrameMetrics.FrameExtra(main);
            var w = Math.Min(main.ClientSize.Width, work.Width / scale - fw);
            var h = Math.Min(main.ClientSize.Height, work.Height / scale - fh);
            main.Width = w;
            main.Height = h;
            var pxW = (int)((w + fw) * scale);
            var pxH = (int)((h + fh) * scale);
            main.Position = new PixelPoint((int)work.X + ((int)work.Width - pxW) / 2, (int)work.Y + ((int)work.Height - pxH) / 2);
            OffscreenGuardHook.PinBegin(main.Position); // ResetMain 과 같은 순서: 이어지는 크기 변경의 화면 안 보정을 되돌린다
            main.UpdateLayout();
            var sizeFix = WindowFrameMetrics.EnsureClientSize(main, w, h);
            OffscreenGuardHook.PinEnd();
            HostLog.Write($"window-menu move main work={work} size={w:F0}x{h:F0}dip pos=({main.Position.X},{main.Position.Y}){sizeFix}");
            return true;
        }
        catch (Exception ex)
        {
            HostLog.Write($"window-menu move main failed: {ex.Message}");
            return false;
        }
    }

    public static bool Sdi(Window main, IDockWindow window)
    {
        try
        {
            if (window.Host is not SdiWindowHost host || IsBlocked())
            {
                HostLog.Write("window-menu move sdi skip");
                return false;
            }

            var wins = DesktopWindowHost.OpenSdi.Where(w => w.IsVisible).ToArray();
            var inputs = wins.Select(w =>
            {
                var size = w.FrameSize ?? w.ClientSize;
                return new SdiResetWindow(new Point(w.Position.X, w.Position.Y), new Size(size.Width * w.RenderScaling, size.Height * w.RenderScaling), !ReferenceEquals(w, host));
            }).ToArray();
            var areas = AreasWithOwnerFirst(main);
            var at = Array.IndexOf(wins, host);
            if (at < 0 || SdiResetLayout.Compute(areas, WindowCommands.SdiDefaultPosition(areas[0]), inputs)[at] is not { } p)
            {
                return false;
            }

            host.RecoverTo(new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y)));
            HostLog.Write($"window-menu move sdi '{host.Title}' area={areas[0]} pos=({host.Position.X},{host.Position.Y})");
            return true;
        }
        catch (Exception ex)
        {
            HostLog.Write($"window-menu move sdi failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsBlocked() => SdiWindowHost.Placement is not null && DesktopWindowHost.PrimaryOverride is null;

    /// <summary>작업 영역 목록. 메인 창 소속 모니터가 맨 앞이다. 진단 --primary-override(가상 모니터 아님)면 그 사각형 하나만 쓴다.</summary>
    private static List<Rect> AreasWithOwnerFirst(Window main)
    {
        if (DesktopWindowHost.PrimaryOverride is { } po && !DesktopMonitorProvider.IsVirtual)
        {
            return new List<Rect> { new(po.X, po.Y, po.Width, po.Height) };
        }

        var monitors = MonitorFitService.Monitors.GetMonitors();
        var size = main.FrameSize ?? main.ClientSize;
        var frame = new PixelRect(main.Position.X, main.Position.Y, (int)(size.Width * main.DesktopScaling), (int)(size.Height * main.DesktopScaling));
        var owner = MonitorSelection.LargestOverlap(monitors, frame);
        var ordered = monitors.OrderBy(m => ReferenceEquals(m, owner) ? 0 : 1).Select(m => new Rect(m.WorkingArea.X, m.WorkingArea.Y, m.WorkingArea.Width, m.WorkingArea.Height)).ToList();
        if (ordered.Count == 0)
        {
            var wa = (main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary ?? main.Screens.All[0]).WorkingArea;
            ordered.Add(new Rect(wa.X, wa.Y, wa.Width, wa.Height));
        }

        return ordered;
    }
}

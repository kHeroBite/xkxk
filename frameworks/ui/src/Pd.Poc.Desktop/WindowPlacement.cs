// MDI-12 창 화면 안 정상화 — 어느 모니터 작업영역에서도 보이지 않는 창을 가장 가까운 모니터 안으로 옮긴다
using Avalonia;
using Avalonia.Controls;

namespace Pd.Poc.Desktop;

/// <summary>MDI-12 — 창이 어느 모니터 작업영역에서도 보이지 않으면 가장 가까운 모니터 안으로 옮긴다.</summary>
public static class WindowPlacement
{
    // 제목줄 기준 최소 노출 크기(px). 이만큼 보이면 사용자가 잡아 끌 수 있다고 본다.
    private const int MinVisibleWidth = 120;
    private const int TitleStrip = 32;

    // 새 분리 창 최소 클라이언트(dip). 문서 창 기본 640×420 의 약 2/3 로, 머리줄과 내용이 함께 보이는 값으로 정했다(판단값, Mars 값 아님).
    private const double MinNewClientWidth = 400;
    private const double MinNewClientHeight = 300;

    public static bool IsVisibleOnAnyScreen(Window window, PixelPoint? origin = null)
    {
        var screens = window.Screens?.All;
        if (screens is null || screens.Count == 0)
        {
            return true;
        }

        var strip = TitleRect(window, origin ?? window.Position);
        return screens.Any(s =>
        {
            var hit = s.WorkingArea.Intersect(strip);
            return hit.Width >= Math.Min(MinVisibleWidth, strip.Width) && hit.Height >= Math.Min(16, strip.Height);
        });
    }

    /// <summary>
    /// 보이지 않으면 옮기고 true. 로그: "[host] MDI-12 normalize ...".
    /// requested 는 저장 레이아웃이 요구한 좌표다. 표시 시점(Show)에 OS·플랫폼이 화면 밖 좌표를 주 모니터 쪽으로
    /// 먼저 보정하므로(실측: (50000,-30000) → OnOpened 시 (1904,0)), 판정·목표 모니터는 보정 전 요구 좌표로 계산한다.
    /// </summary>
    public static bool EnsureVisible(Window window, string tag, PixelPoint? requested = null)
    {
        var before = requested ?? window.Position;
        var visible = IsVisibleOnAnyScreen(window, before);
        Console.WriteLine(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] MDI-12 check {tag} pos=({window.Position.X},{window.Position.Y}) " +
            $"requested=({before.X},{before.Y}) " +
            $"frame={FrameSize(window).Width}x{FrameSize(window).Height} screens={window.Screens?.All.Count ?? -1} visible={visible}");
        if (visible)
        {
            return false;
        }

        var screens = window.Screens!.All;
        var size = FrameSize(window);
        var center = new PixelPoint(before.X + size.Width / 2, before.Y + size.Height / 2);
        var target = screens.OrderBy(s => Distance(s.WorkingArea, center)).First().WorkingArea;

        var w = Math.Min(size.Width, target.Width);
        var h = Math.Min(size.Height, target.Height);
        var x = Math.Clamp(before.X, target.X, target.Right - w);
        var y = Math.Clamp(before.Y, target.Y, target.Bottom - h);
        if (w < size.Width || h < size.Height)
        {
            window.Width = w / window.DesktopScaling;
            window.Height = h / window.DesktopScaling;
        }

        window.Position = new PixelPoint(x, y);
        Console.WriteLine(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] MDI-12 normalize {tag} " +
            $"({before.X},{before.Y})->({x},{y}) size={w}x{h} screen={target.X},{target.Y},{target.Width}x{target.Height}");
        return true;
    }

    /// <summary>
    /// 새 분리 창을 쓸 수 있게 맞춘다(D-SDI-RECT): 클라이언트 최소 크기를 보장하고 작업 영역보다 크면 줄이며, move 면 창 전체를 작업 영역 안으로 옮긴다.
    /// 분리 크기는 패널 크기를 이어받아 납작하거나 작업 영역을 넘을 수 있다. 복원 창은 이 뒤 ApplyWindow 가 저장값을 다시 준다.
    /// </summary>
    public static void FitNewWindow(Window window, string tag, bool move)
    {
        var screens = window.Screens?.All;
        if (screens is null || screens.Count == 0)
        {
            return;
        }

        var scale = window.DesktopScaling;
        var before = FrameSize(window);
        var center = new PixelPoint(window.Position.X + before.Width / 2, window.Position.Y + before.Height / 2);
        var area = screens.OrderBy(s => Distance(s.WorkingArea, center)).First().WorkingArea;
        var client = window.ClientSize;
        var extraW = Math.Max(0, before.Width / scale - client.Width);
        var extraH = Math.Max(0, before.Height / scale - client.Height);
        var w = Math.Min(Math.Max(client.Width, MinNewClientWidth), area.Width / scale - extraW);
        var h = Math.Min(Math.Max(client.Height, MinNewClientHeight), area.Height / scale - extraH);
        if (Math.Abs(w - client.Width) >= 1 || Math.Abs(h - client.Height) >= 1)
        {
            window.Width = w;
            window.Height = h;
        }

        var pxW = (int)Math.Ceiling((w + extraW) * scale);
        var pxH = (int)Math.Ceiling((h + extraH) * scale);
        if (move)
        {
            window.Position = new PixelPoint(Math.Clamp(window.Position.X, area.X, area.Right - pxW), Math.Clamp(window.Position.Y, area.Y, area.Bottom - pxH));
        }

        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] sdi fit {tag} client {client.Width:F0}x{client.Height:F0}->{w:F0}x{h:F0}dip pos=({window.Position.X},{window.Position.Y}) area={area.X},{area.Y},{area.Width}x{area.Height} move={move}");
    }

    private static PixelSize FrameSize(Window window)
    {
        var scale = window.DesktopScaling;
        var size = window.FrameSize ?? window.ClientSize;
        return new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale)));
    }

    private static PixelRect TitleRect(Window window, PixelPoint origin)
    {
        var size = FrameSize(window);
        return new PixelRect(origin, new PixelSize(size.Width, Math.Min(TitleStrip, size.Height)));
    }

    private static double Distance(PixelRect area, PixelPoint p)
    {
        var dx = Math.Max(0, Math.Max(area.X - p.X, p.X - area.Right));
        var dy = Math.Max(0, Math.Max(area.Y - p.Y, p.Y - area.Bottom));
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}

// 모니터 목록 컨트롤 계약(U20) — 플랫폼 호스트가 이름·해상도·주 모니터·작업 영역을 알려 주고, 셸 메뉴는 목록 표시·선택 저장만 맡는다
using System;
using System.Collections.Generic;
using Avalonia;

namespace Pd.Poc.Shell.Docking;

/// <summary>모니터 1대의 정보. 좌표·크기는 물리 px(가상 화면 좌표계).</summary>
/// <param name="Id">저장용 식별자(OS 장치 이름, 예 \\.\DISPLAY1). 모니터 분리·재연결 뒤에도 같은 장치면 같은 값.</param>
/// <param name="Name">메뉴에 보일 이름(장치 이름에서 \\.\ 접두를 뺀 값).</param>
/// <param name="Bounds">모니터 전체 영역.</param>
/// <param name="WorkingArea">작업표시줄을 뺀 영역(최대화 창이 차지하는 영역).</param>
/// <param name="IsPrimary">주 모니터 여부.</param>
public sealed record MonitorInfo(string Id, string Name, PixelRect Bounds, PixelRect WorkingArea, bool IsPrimary);

/// <summary>
/// 데스크톱 호스트가 구현해 MonitorMenuViewModel.Provider 에 넣는다(DesktopMonitorProvider). 웹·모바일은 단일 표면이라 구현이 없다(null → 메뉴 비활성).
/// 모든 멤버는 UI 스레드에서 불린다. 예외를 밖으로 던지지 않는다.
/// </summary>
public interface IMonitorProvider
{
    /// <summary>현재 연결된 모니터 목록(주 모니터 먼저, 그다음 왼쪽→오른쪽·위→아래). 메인 창이 아직 없으면 빈 목록.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>모니터 연결·분리·해상도·배치가 바뀌었을 때(메뉴 목록 갱신용).</summary>
    event Action? MonitorsChanged;
}

/// <summary>선택 모니터 해석 규칙(셸·호스트 공용, 플랫폼 무관 순수 함수).</summary>
public static class MonitorSelection
{
    /// <summary>
    /// 저장된 선택 id 에 해당하는 모니터. 없으면(미선택·분리·이름 변경) 주 모니터, 주 모니터도 없으면 첫 모니터, 목록이 비면 null.
    /// fallback=true 는 저장값이 있었는데 찾지 못해 대체했다는 뜻이다(로그·판정용).
    /// </summary>
    public static MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, string? selectedId, out bool fallback)
    {
        fallback = false;
        if (monitors.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(selectedId))
        {
            foreach (var m in monitors)
            {
                if (string.Equals(m.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }

            fallback = true;
        }

        foreach (var m in monitors)
        {
            if (m.IsPrimary)
            {
                return m;
            }
        }

        return monitors[0];
    }

    /// <summary>
    /// 창이 속한 모니터: 전체 영역과 겹치는 면적이 가장 큰 모니터. Windows MonitorFromWindow(MONITOR_DEFAULTTONEAREST)와 같은 규칙이다.
    /// 면적이 같으면 목록 앞쪽(주 모니터 먼저)이 이긴다(실측: 50:50 걸침의 표준 Win32 창은 주 모니터로 최대화). 겹치는 모니터가 없으면 가장 가까운 모니터, 목록이 비면 null.
    /// </summary>
    public static MonitorInfo? LargestOverlap(IReadOnlyList<MonitorInfo> monitors, PixelRect window)
    {
        MonitorInfo? best = null;
        long bestArea = 0;
        foreach (var m in monitors)
        {
            var hit = m.Bounds.Intersect(window);
            var area = (long)hit.Width * hit.Height;
            if (area > bestArea)
            {
                (best, bestArea) = (m, area);
            }
        }

        return best ?? Nearest(monitors, window);
    }

    /// <summary>창 사각형을 from 안의 상대 위치 그대로 to 로 옮기고, to 보다 크면 줄이고 밖이면 안으로 넣는다.</summary>
    public static PixelRect Relocate(PixelRect rect, PixelRect from, PixelRect to)
    {
        var w = Math.Min(rect.Width, to.Width);
        var h = Math.Min(rect.Height, to.Height);
        var x = Math.Clamp(to.X + (rect.X - from.X), to.X, to.Right - w);
        var y = Math.Clamp(to.Y + (rect.Y - from.Y), to.Y, to.Bottom - h);
        return new PixelRect(x, y, w, h);
    }

    private static MonitorInfo? Nearest(IReadOnlyList<MonitorInfo> monitors, PixelRect window)
    {
        MonitorInfo? best = null;
        var bestGap = long.MaxValue;
        foreach (var m in monitors)
        {
            long dx = Math.Max(0, Math.Max(m.Bounds.X - window.Right, window.X - m.Bounds.Right));
            long dy = Math.Max(0, Math.Max(m.Bounds.Y - window.Bottom, window.Y - m.Bounds.Bottom));
            var gap = dx * dx + dy * dy;
            if (gap < bestGap)
            {
                (best, bestGap) = (m, gap);
            }
        }

        return best;
    }
}

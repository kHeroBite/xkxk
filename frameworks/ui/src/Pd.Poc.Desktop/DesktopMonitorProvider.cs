// 데스크톱 모니터 목록 제공자(U20, IMonitorProvider) — Avalonia Screens.All 에서 이름·해상도·주 모니터·작업 영역을 읽고, 진단 빌드에서는 가상 모니터를 주입한다
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Desktop;

/// <summary>
/// 메인 창의 Screens 로 모니터를 조회한다(메인 창이 붙기 전에는 빈 목록). 모니터 연결·분리·해상도 변경은 Screens.Changed 로 알린다.
/// 진단 빌드 + --primary-override + --virtual-monitors N: 실제 모니터 대신 override 사각형을 가로로 N 등분한 가상 모니터를 돌려준다
/// (단일 모니터 PC 에서 다중 모니터 분기를 사용자 화면 밖에서 판정 — 첫 번째가 주 모니터, 작업 영역 = 아래 VirtualTaskbar px 를 뺀 영역).
/// </summary>
public sealed class DesktopMonitorProvider : IMonitorProvider
{
    private const int VirtualTaskbar = 48; // 가상 모니터의 작업표시줄 높이(px) — 작업 영역 ≠ 전체 영역을 판정에서 구별하려는 값(판단값)

    private Window? _main;

    public static DesktopMonitorProvider Instance { get; } = new();

    public event Action? MonitorsChanged;

    /// <summary>진단 --virtual-monitors N(2 이상, --primary-override 필요). 실행 중 monitors.setVirtual 은 1 도 줄 수 있다. 0 = 실제 모니터(제품·기본).</summary>
    public static int VirtualCount { get; set; }

    /// <summary>가상 모니터를 쓰는 중인가(MonitorFitService 가 OS 최대화 대신 맞춤으로 흉내 낼지 결정).</summary>
    public static bool IsVirtual => VirtualCount >= 1 && DesktopWindowHost.PrimaryOverride is not null;

#if PD_DIAGNOSTICS
    /// <summary>
    /// 진단 monitors.setVirtual: 실행 중 가상 모니터 수를 바꾼다(요건 5 시험). 모니터 1대의 폭을 유지하므로 2→1 은 오른쪽 모니터가 사라진 것과 같다.
    /// --primary-override 로 기동한 앱만 받는다 — 그래야 화면 밖 가드(OffscreenGuardHook)가 설치돼 있어 시험 창이 사용자 화면으로 끌려오지 않는다. 오류 문구를 돌려주고, 성공하면 null.
    /// </summary>
    public string? SetVirtual(int count)
    {
        if (DesktopWindowHost.PrimaryOverride is not { } area)
        {
            return "requires --primary-override at launch";
        }

        if (count < 1)
        {
            return "count must be 1 or more";
        }

        var old = Math.Max(1, VirtualCount);
        DesktopWindowHost.PrimaryOverride = new PixelRect(area.X, area.Y, area.Width / old * count, area.Height);
        VirtualCount = count;
        Program.Log($"virtual-monitors set {old}->{count} override={DesktopWindowHost.PrimaryOverride}");
        MonitorsChanged?.Invoke();
        return null;
    }
#endif

    /// <summary>가상 모니터 맞춤 대상(진단 전용, IsVirtual 일 때만 부른다). union=true 면 가상 모니터 Bounds 합집합(⑥ 멀티), false 면 주 가상 모니터 Bounds(⑤ 싱글).</summary>
    public static PixelRect VirtualFitTarget(bool union)
    {
        var list = Virtual(DesktopWindowHost.PrimaryOverride!.Value, VirtualCount);
        return union ? list.Select(m => m.Bounds).Aggregate((a, b) => a.Union(b)) : list.First(m => m.IsPrimary).Bounds;
    }

    /// <summary>MonitorFitService.Attach 에서 1회. 이후 모니터 변경 알림을 메뉴로 넘긴다.</summary>
    public void Attach(Window main)
    {
        var first = !ReferenceEquals(_main, main);
        _main = main;
        if (first && main.Screens is { } screens) // Opened 때 다시 불려도 구독은 1번만
        {
            screens.Changed += (_, _) => MonitorsChanged?.Invoke();
        }

        var list = GetMonitors();
        Program.Log($"monitor-list count={list.Count} virtual={IsVirtual} real={main.Screens?.All.Count ?? -1} [{string.Join(" | ", list.Select(m => $"{m.Id} primary={m.IsPrimary} bounds={m.Bounds} work={m.WorkingArea}"))}]");
        MonitorsChanged?.Invoke();
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        try
        {
            if (IsVirtual)
            {
                return Virtual(DesktopWindowHost.PrimaryOverride!.Value, VirtualCount);
            }

            var all = _main?.Screens?.All;
            if (all is null || all.Count == 0)
            {
                return Array.Empty<MonitorInfo>();
            }

            return all
                .Select(s =>
                {
                    // 이름 순서: Windows 장치 이름(HMONITOR → GetMonitorInfo, 예 \\.\DISPLAY1) → Avalonia DisplayName → 좌표(둘 다 없을 때)
                    var id = DeviceName(s) ?? (string.IsNullOrWhiteSpace(s.DisplayName) ? $"{s.Bounds.X},{s.Bounds.Y}" : s.DisplayName!);
                    return new MonitorInfo(id, id.TrimStart('\\', '.'), s.Bounds, s.WorkingArea, s.IsPrimary);
                })
                .OrderBy(m => m.IsPrimary ? 0 : 1)
                .ThenBy(m => m.Bounds.X)
                .ThenBy(m => m.Bounds.Y)
                .ToArray();
        }
        catch (Exception ex)
        {
            Program.Log($"monitor-list 예외: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<MonitorInfo>();
        }
    }

    // Windows 장치 이름. Avalonia 12 Screen.DisplayName 은 이 PC 에서 빈 값이었다(c2dev_n 실측 01:50) — 핸들(HMONITOR)로 OS 에 직접 묻는다. 실패하면 null.
    private static string? DeviceName(Avalonia.Platform.Screen screen)
    {
        if (!OperatingSystem.IsWindows() || screen.TryGetPlatformHandle()?.Handle is not { } hmon || hmon == IntPtr.Zero)
        {
            return null;
        }

        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfo(hmon, ref info) && !string.IsNullOrWhiteSpace(info.Device) ? info.Device : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public int ML, MT, MR, MB; // rcMonitor
        public int WL, WT, WR, WB; // rcWork
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);

    // override 사각형을 가로로 count 등분(마지막 칸이 나머지 px 를 가짐)
    private static MonitorInfo[] Virtual(PixelRect area, int count)
    {
        var list = new MonitorInfo[count];
        var w = area.Width / count;
        for (var i = 0; i < count; i++)
        {
            var width = i == count - 1 ? area.Width - w * i : w;
            var bounds = new PixelRect(area.X + w * i, area.Y, width, area.Height);
            var work = new PixelRect(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height - VirtualTaskbar));
            list[i] = new MonitorInfo($"VIRTUAL{i + 1}", $"VIRTUAL{i + 1}", bounds, work, i == 0);
        }

        return list;
    }
}

// 설정 창 앱 모달의 Win32 OS 수준 차단 — ModalScope.PlatformBlock 을 EnableWindow 로 구현하고, 표시 중 다시 켜지면 되막고, 모달이 끝나면 이 훅이 막은 창을 빠짐없이 되살린다
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// ModalScope(셸)가 "이 창을 막아라/풀어라"를 부르면 그 창 HWND 에 EnableWindow 를 건다. 막힌 창은 OS 제목줄·캡션 단추·Alt+F4·클릭이 모두 거부된다.
/// 되막기(c27 결함 2): 모달 중 새로 연 창은 ModalScope 가 WindowOpenedEvent 때 막는데, Avalonia 12.1.4 Window.ShowCore 는 이 이벤트를
/// PlatformImpl.Show 앞에서 낸다(SysWindowBgPinHook 주석과 같은 근거). 그 뒤 표시 경로에서 창이 다시 켜져(osEnabled=true 실측) 막힘이 풀렸다.
/// 그래서 ① 막은 창마다 WndProc 훅으로 WM_ENABLE(켜짐)을 감시해 모달 중이면 곧바로 되막고 처음 1회 호출 스택을 로그에 남기며(원인 기록),
/// ② 막은 직후 Background 우선순위로 한 번 더 확인하고, ③ 모달 동안 300ms 주기로 IsWindowEnabled 를 확인해 훅이 놓친 경우도 되막는다.
/// 복구 보장: 이 훅이 막은 HWND 만 집합으로 기억하고, ModalScope.Changed 에서 모달이 끝났으면(IsActive=false) 남은 HWND 를 전부 다시 켠다.
/// Windows 외 데스크톱은 설치하지 않는다(앱 관문만). 출처: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enablewindow
/// </summary>
internal static class Win32ModalBlock
{
    private const uint WmEnable = 0x000A;

    private static readonly Dictionary<nint, TopLevel> s_blocked = new();
    private static readonly Dictionary<nint, long> s_blockedAt = new(); // 막은 시각(ms) — 되막기 로그의 경과 시간
    private static readonly HashSet<TopLevel> s_hooked = new();
    private static readonly HashSet<nint> s_stackLogged = new();
    private static DispatcherTimer? s_timer;

    /// <summary>창을 만들기 전에 한 번 부른다(Program.ConfigureApp).</summary>
    public static void Install()
    {
        if (!OperatingSystem.IsWindows())
        {
            Program.Log("modal-block skip: not windows (app gate only)");
            return;
        }

        ModalScope.PlatformBlock = Block;
        ModalScope.Changed += (_, _) =>
        {
            if (!ModalScope.IsActive)
            {
                ReleaseAll("scope-ended");
            }
        };
#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.shell.Diagnostics.Remote.RemoteModalCommands.OsEnabledProbe = IsOsEnabled; // modal.state osEnabled
#endif
        Program.Log("modal-block installed (EnableWindow + WM_ENABLE reassert)");
    }

    /// <summary>blocked=true 면 막고 false 면 푼다. HWND 가 없으면(닫힌 창) 아무것도 하지 않는다.</summary>
    private static void Block(TopLevel top, bool blocked)
    {
        if (HandleOf(top) is not { } hwnd)
        {
            Program.Log($"modal-block {(blocked ? "block" : "release")} skip no-handle title='{(top as Window)?.Title}'"); // 해제 누락 원인 확정용(c27 '프로그램 정보')
            return;
        }

        try
        {
            if (blocked)
            {
                s_blocked[hwnd] = top;
                s_blockedAt[hwnd] = Environment.TickCount64;
                Hook(top);
                EnableWindow(hwnd, false);
                Dispatcher.UIThread.Post(() => Reassert(hwnd, "post-show"), DispatcherPriority.Background); // 표시(PlatformImpl.Show) 뒤 한 번 더
                EnsureTimer();
            }
            else
            {
                s_blocked.Remove(hwnd); // 먼저 집합에서 빼야 WM_ENABLE 감시가 정상 해제를 되막지 않는다
                s_blockedAt.Remove(hwnd);
                EnableWindow(hwnd, true);
            }

            Program.Log($"modal-block {(blocked ? "disable" : "enable")} hwnd=0x{hwnd:X} title='{(top as Window)?.Title}' os={IsWindowEnabled(hwnd)}");
        }
        catch (Exception ex)
        {
            Program.Log($"modal-block failed hwnd=0x{hwnd:X}: {ex.Message}"); // 차단 실패는 앱 관문이 대신한다 — 창 열기를 막지 않는다
        }
    }

    /// <summary>막아 둔 창이 켜져 있으면 다시 막는다(모달 중일 때만).</summary>
    private static void Reassert(nint hwnd, string reason)
    {
        if (!ModalScope.IsActive || !s_blocked.TryGetValue(hwnd, out var top) || !IsWindow(hwnd) || !IsWindowEnabled(hwnd))
        {
            return;
        }

        EnableWindow(hwnd, false);
        var ms = s_blockedAt.TryGetValue(hwnd, out var at) ? Environment.TickCount64 - at : -1;
        Program.Log($"modal-block reassert hwnd=0x{hwnd:X} title='{(top as Window)?.Title}' reason={reason} after={ms}ms os={IsWindowEnabled(hwnd)}");
    }

    /// <summary>WM_ENABLE(wParam≠0 = 켜짐) 감시. EnableWindow 는 호출 스레드에서 동기로 WM_ENABLE 을 보내므로 이 시점 스택이 다시 켠 호출자다.</summary>
    private static void Hook(TopLevel top)
    {
        if (!s_hooked.Add(top))
        {
            return;
        }

        Win32Properties.AddWndProcHookCallback(top, (IntPtr h, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WmEnable && wParam != IntPtr.Zero && ModalScope.IsActive && s_blocked.ContainsKey(h))
            {
                if (s_stackLogged.Add(h))
                {
                    var frames = Environment.StackTrace.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("Avalonia") || l.Contains("xkxk") || l.Contains("Dock")).Take(12);
                    Program.Log($"modal-block WM_ENABLE(on) while blocked hwnd=0x{h:X} stack: {string.Join(" | ", frames)}");
                }

                Dispatcher.UIThread.Post(() => Reassert(h, "wm_enable"), DispatcherPriority.Send);
            }

            return IntPtr.Zero;
        });
        if (top is Window w)
        {
            w.Closed += (_, _) => s_hooked.Remove(top);
        }
    }

    private static void EnsureTimer()
    {
        s_timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
        {
            if (s_blocked.Count == 0 || !ModalScope.IsActive)
            {
                s_timer?.Stop();
                return;
            }

            foreach (var hwnd in s_blocked.Keys.ToArray())
            {
                Reassert(hwnd, "poll");
            }
        });
        s_timer.Start();
    }

    /// <summary>이 훅이 막아 둔 HWND 를 전부 다시 켠다(이미 파괴된 HWND 는 건너뛴다).</summary>
    private static void ReleaseAll(string reason)
    {
        s_timer?.Stop();
        if (s_blocked.Count == 0)
        {
            return;
        }

        var all = s_blocked.Keys.ToArray();
        s_blocked.Clear(); // 먼저 비워 WM_ENABLE 감시가 복구를 되막지 않게
        s_blockedAt.Clear();
        foreach (var hwnd in all)
        {
            try
            {
                if (IsWindow(hwnd))
                {
                    EnableWindow(hwnd, true);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"modal-block release failed hwnd=0x{hwnd:X}: {ex.Message}");
            }
        }

        Program.Log($"modal-block release-all count={all.Length} reason={reason}");
    }

    private static nint? HandleOf(TopLevel top) =>
        top.TryGetPlatformHandle()?.Handle is { } h && h != 0 ? h : null;

#if PD_DIAGNOSTICS
    /// <summary>Win32 IsWindowEnabled — HWND 가 없으면 null(모름).</summary>
    private static bool? IsOsEnabled(TopLevel top) => HandleOf(top) is { } hwnd ? IsWindowEnabled(hwnd) : null;
#endif

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);
}

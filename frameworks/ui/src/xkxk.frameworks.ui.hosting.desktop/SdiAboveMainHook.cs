// 설정 '항상 메인 창 위' — 메인 창 WM_WINDOWPOSCHANGING 에서 메인을 보이는 SDI·그 그림자 전부의 바로 밑에 끼운다(Win32 전용, owner 없이 앱이 Z 를 유지)
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// SDI 는 owner 가 없다(Program: Dock NeverOwned). 그래서 메인을 활성화하면 메인이 SDI 위로 올라온다.
/// 설정 켬이면 메인의 Z 변경 요청(SWP_NOZORDER 없음)을 가로채 hwndInsertAfter 를
/// 집합 S = {보이는·최소화 아닌·최상위(WS_EX_TOPMOST) 아닌 SDI} ∪ {이 프로세스의 보이는·비최상위 PdSdiShadow 창} 중 Z 최하위 hwnd 로 바꾼다.
/// 그림자까지 포함해야 메인이 SDI 와 그 그림자 사이에 끼지 않는다(SdiShadowWindow.PlaceBehind·SdiShadowStack.RestackAll 과 반복 재배치 방지).
/// 요청이 이미 그 hwnd 밑이면 손대지 않고, 메인이 이미 그 바로 밑인데 위로 올리려는 요청이면 SWP_NOZORDER 를 붙여 불필요한 Z 변경을 없앤다.
/// 설정 끔·S 비어 있음·메인이 항상 위면 개입하지 않는다. SDI 의 Z·상태는 건드리지 않는다(메인 최소화·복원에도 SDI 무반응).
/// 위치·크기 필드는 바꾸지 않으므로 bg pin(MainBgPinHook)·최대 추적(MaxTrackHook)·Win+화살표 스냅과 겹치지 않는다.
/// </summary>
internal static class SdiAboveMainHook
{
    private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private const uint GwHwndNext = 2;
    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;
    private const string ShadowClass = "PdSdiShadow"; // SdiShadowSurface.ClassName 과 같은 값
    private static readonly IntPtr HwndTop = IntPtr.Zero, HwndBottom = new(1), HwndTopmost = new(-1), HwndNoTopmost = new(-2);

    private static readonly HashSet<IntPtr> s_sdi = new();
    private static IntPtr s_main;
    private static bool s_busy;

    /// <summary>SdiWindowHost.ConfigureMain 에서 1회 — 메인 창 훅 설치 + 설정 켬 전환 시 즉시 재배치.</summary>
    public static void Install(Window main)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Win32Properties.AddWndProcHookCallback(main, Hook);
        main.Opened += (_, _) => s_main = main.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        XkxkUi.Current.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiSettings.SdiAboveMain) && s_main != IntPtr.Zero && !IsIconic(s_main))
            {
                // 켬: 메인 Z 를 맨 위로 요청하면 아래 훅이 S 최하위 밑으로 바꾼다(이미 제자리면 NOZORDER).
                // 끔: 훅이 개입하지 않으므로 메인이 SDI 위로 올라온다. 메인이 이미 전경 활성이면 이후 Activate 가 Z 를 바꾸지 않아
                //     (최소화→복원 뒤 켬 상태로 SDI 밑에 낀 채 활성인 경우) 끄는 즉시 올려야 한다. 활성화·이동 없음.
                SetWindowPos(s_main, HwndTop, 0, 0, 0, 0, SdiZOrderRaiser.SwpNoMove | SdiZOrderRaiser.SwpNoSize | SwpNoActivate);
            }
        };
#if PD_DIAGNOSTICS
        // 원격 window.main.activate 가 실제 클릭처럼 메인 Z 를 요청하게 한다(켬이면 아래 훅이 SDI 밑으로 보정)
        xkxk.frameworks.ui.shell.Diagnostics.Remote.RemoteUiModeCommands.MainRaiseZ = _ =>
        {
            if (s_main != IntPtr.Zero && !IsIconic(s_main))
            {
                SetWindowPos(s_main, HwndTop, 0, 0, 0, 0, SdiZOrderRaiser.SwpNoMove | SdiZOrderRaiser.SwpNoSize | SwpNoActivate);
            }
        };
#endif
        HostLog.Write($"sdi-above-main hook installed setting={XkxkUi.Current.Settings.SdiAboveMain}");
    }

    /// <summary>SdiWindowHost.OnOpened 에서 — 대상 SDI 목록 등록(닫히면 제거).</summary>
    public static void Register(Window sdi)
    {
        if (!OperatingSystem.IsWindows() || sdi.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero || !s_sdi.Add(hwnd))
        {
            return;
        }

        sdi.Closed += (_, _) => s_sdi.Remove(hwnd);
    }

    private static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != SdiBgPinHook.WmWindowPosChanging || lParam == IntPtr.Zero || s_busy || s_sdi.Count == 0 || !XkxkUi.Current.Settings.SdiAboveMain)
        {
            return IntPtr.Zero;
        }

        var wp = Marshal.PtrToStructure<SdiBgPinHook.WindowPos>(lParam);
        if ((wp.Flags & SwpNoZOrder) != 0 || IsTopmost(hWnd))
        {
            return IntPtr.Zero; // 이동·크기만(끌기 포함)이거나 메인이 항상 위면 개입 안 함
        }

        s_busy = true;
        try
        {
            if (Adjust(hWnd, ref wp))
            {
                Marshal.StructureToPtr(wp, lParam, false);
            }
        }
        finally
        {
            s_busy = false;
        }

        return IntPtr.Zero;
    }

    // Z 순회(위→아래)로 S 최하위 hwnd 와 요청 기준 hwnd 의 순번을 찾아, 메인이 S 위로 가는 요청이면 고친다.
    private static bool Adjust(IntPtr main, ref SdiBgPinHook.WindowPos wp)
    {
        var pid = Environment.ProcessId;
        IntPtr lowest = IntPtr.Zero;
        int lowestIdx = -1, afterIdx = -1, mainIdx = -1, idx = 0;
        for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, GwHwndNext), idx++)
        {
            if (h == main)
            {
                mainIdx = idx;
            }
            else if (IsInSet(h, pid))
            {
                lowest = h;
                lowestIdx = idx;
            }

            if (h == wp.InsertAfter)
            {
                afterIdx = idx;
            }
        }

        if (lowest == IntPtr.Zero || wp.InsertAfter == HwndBottom)
        {
            return false;
        }

        // 요청 결과가 S 최하위보다 위인가: HWND_TOP·TOPMOST·NOTOPMOST(비최상위 맨 위) 또는 S 최하위보다 위(순번 작음)의 창 바로 밑
        var special = wp.InsertAfter == HwndTop || wp.InsertAfter == HwndTopmost || wp.InsertAfter == HwndNoTopmost;
        var above = special || (afterIdx >= 0 && afterIdx < lowestIdx);
        if (!above)
        {
            return false;
        }

        if (mainIdx == lowestIdx + 1)
        {
            wp.Flags |= SwpNoZOrder; // 이미 S 최하위 바로 밑 — Z 변경을 없앤다
        }
        else
        {
            wp.InsertAfter = lowest;
        }

        return true;
    }

    // S 원소: 등록된 SDI 또는 이 프로세스의 PdSdiShadow — 보이고·최소화 아니고·최상위 아님
    private static bool IsInSet(IntPtr h, int pid)
    {
        if (!IsWindowVisible(h) || IsIconic(h) || IsTopmost(h))
        {
            return false;
        }

        if (s_sdi.Contains(h))
        {
            return true;
        }

        GetWindowThreadProcessId(h, out var owner);
        if (owner != (uint)pid)
        {
            return false;
        }

        var sb = new StringBuilder(32);
        return GetClassName(h, sb, sb.Capacity) > 0 && sb.ToString() == ShadowClass;
    }

    private static bool IsTopmost(IntPtr h) => (GetWindowLongPtr(h, GwlExStyle).ToInt64() & WsExTopmost) != 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}

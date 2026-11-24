// SDI 그림자 창 z 재정렬 — 주 창·다른 SDI 의 z 변경으로 그림자가 자기 SDI 바로 뒤에서 밀려나면 모든 그림자를 다시 건다(Win32 전용, B6)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Desktop;

/// <summary>
/// 원인(odev-shadow r3 실측): SDI 는 주 창이 소유한 창이라, 한 SDI 를 최소화·복원하면 OS 가 소유자(주 창)를 마지막 소유 창 바로 뒤로 옮긴다.
/// 이때 다른 SDI 는 WM_WINDOWPOSCHANGED 를 받지 않으므로 그 SDI 의 그림자가 주 창 뒤로 밀렸다(SDI_B z83 → 주 창 z84 → 그림자 z85).
/// 대응: 살아 있는 그림자 목록을 두고, 아무 SDI 또는 주 창의 WM_WINDOWPOSCHANGED 때 모든 그림자를 자기 SDI 바로 뒤에 다시 건다(z 만, 이동·크기 없음).
/// </summary>
internal static class SdiShadowStack
{
    private const uint WmWindowPosChanged = 0x0047;
    private static readonly List<SdiShadowWindow> s_all = new();
    private static Window? s_hookedMain;
    private static bool s_restacking;
    private static bool s_subscribed;

    public static void Register(SdiShadowWindow shadow)
    {
        if (!s_all.Contains(shadow))
        {
            s_all.Add(shadow);
        }

        HookMain();
        if (!s_subscribed)
        {
            s_subscribed = true;
            WindowCommands.ChromeApplied += SyncAll; // 크롬 스타일 전환(그림자 켜짐·반경·모양 리소스 변경) 때 모든 그림자를 다시 맞춘다(UI 스레드)
        }
    }

    /// <summary>모든 그림자를 현재 상태로 다시 맞춘다(UI 스레드). 소유 SDI 가 이미 없는 항목은 Sync/RestackZ 안에서 IsWindow 로 걸러져 Destroy→Unregister 된다.</summary>
    public static void SyncAll()
    {
        foreach (var shadow in s_all.ToArray())
        {
            shadow.Sync();
        }
    }

    public static void Unregister(SdiShadowWindow shadow) => s_all.Remove(shadow);

    /// <summary>모든 그림자를 자기 SDI 바로 뒤로(재진입 방지 — 그림자 자체 SetWindowPos 는 SDI·주 창에 메시지를 보내지 않지만 안전하게 막는다).</summary>
    public static void RestackAll(SdiShadowWindow? except = null)
    {
        if (s_restacking)
        {
            return;
        }

        s_restacking = true;
        try
        {
            foreach (var shadow in s_all.ToArray())
            {
                if (!ReferenceEquals(shadow, except))
                {
                    shadow.RestackZ();
                }
            }
        }
        finally
        {
            s_restacking = false;
        }
    }

    private static void HookMain()
    {
        var main = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (main is null || ReferenceEquals(main, s_hookedMain))
        {
            return;
        }

        s_hookedMain = main;
        Win32Properties.AddWndProcHookCallback(main, MainHook);
    }

    private static IntPtr MainHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmWindowPosChanged)
        {
            RestackAll();
        }

        return IntPtr.Zero;
    }
}

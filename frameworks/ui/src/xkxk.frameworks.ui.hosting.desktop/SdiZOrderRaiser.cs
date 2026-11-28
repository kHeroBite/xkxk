// SDI 호스트 창 OS Z순서 올리기 — 활성화 없이 맨 위로(SetWindowPos HWND_TOP) + HostWindow 기본 SetActive 호출(인터페이스 재구현 뒤)
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// Z순서 계약(나중에 표시된 창이 위, 비전경 모드의 SetActive 는 전경 획득 없이 Z만 올림)의 OS 호출부. 계약 판단은 SdiWindowHost 가 한다.
/// SwpNoSize·SwpNoMove 는 bg pin 훅(SdiBgPinHook·MainBgPinHook)도 같이 쓴다.
/// </summary>
internal static class SdiZOrderRaiser
{
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    internal const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;

    // HostWindow 의 IHostWindow.SetActive 구현(공개·명시 구현 모두)을 인터페이스 매핑으로 찾는다(재구현 후 기본 동작 호출용).
    private static readonly MethodInfo? s_baseSetActive = FindBaseSetActive();

    /// <summary>같은 그룹(일반/항상 위) 안에서 이 창을 맨 위로 올린다. 활성화·이동·크기 변경 없음. Windows 외에는 무시.</summary>
    public static void RaiseZ(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
        {
            return;
        }

        // HWND_TOP 은 항상 위(Topmost) 속성을 바꾸지 않으므로 핀 창은 항상 위 그룹 안에서 맨 위가 된다.
        SetWindowPos(hwnd, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    /// <summary>HostWindow 의 기본 SetActive 를 부른다. 찾지 못했으면 false(호출자가 Activate 로 대신한다).</summary>
    public static bool TryBaseSetActive(HostWindow host)
    {
        if (s_baseSetActive is null)
        {
            return false;
        }

        s_baseSetActive.Invoke(host, null);
        return true;
    }

    private static MethodInfo? FindBaseSetActive()
    {
        var map = typeof(HostWindow).GetInterfaceMap(typeof(IHostWindow));
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            // 기본 인터페이스 메서드(구현 형식이 인터페이스)면 호출 시 이 재구현으로 되돌아와 무한 재귀가 되므로 쓰지 않는다.
            if (map.InterfaceMethods[i].Name == nameof(IHostWindow.SetActive) && map.TargetMethods[i].DeclaringType is { IsInterface: false })
            {
                return map.TargetMethods[i];
            }
        }

        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}

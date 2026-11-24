// SDI 창의 실제 OS Z 순서를 읽어 문서 Id 순서로 돌려주는 진단 훅 — DesktopWindowHost 에서 분리
#if PD_DIAGNOSTICS // 진단 빌드 전용(원격 훅 RemoteCommands.SdiZOrderProbe 만 읽는다)
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Desktop;

/// <summary>Q12 원격 훅(RemoteCommands.SdiZOrderProbe)용 Z 순서 조회. 창 목록은 호출부가 인자로 넘긴다.</summary>
internal static class SdiZOrderProbe
{
    private const uint GwHwndNext = 2; // GetWindow: Z 순서상 바로 아래 창

    /// <summary>
    /// SDI 호스트 창이 담은 문서 Id 를 실제 OS Z 순서 아래→위로 돌려준다.
    /// GetTopWindow(NULL) → GetWindow(GW_HWNDNEXT) 로 최상위 창을 위(항상 위 창 포함)에서 아래로 훑고 뒤집는다. 메인 창은 넣지 않는다.
    /// 한 창의 문서 여러 개는 그 창의 레이아웃 순서대로 같은 자리에 연달아 넣는다. Windows 가 아니거나 SDI 가 없으면 null(셸이 visual·model 로 대신).
    /// </summary>
    public static IReadOnlyList<string>? Read(IReadOnlyCollection<Window> sdi)
    {
        if (!OperatingSystem.IsWindows() || sdi.Count == 0)
        {
            return null;
        }

        var byHandle = new Dictionary<IntPtr, Window>();
        foreach (var w in sdi)
        {
            if (w.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero)
            {
                byHandle[h] = w;
            }
        }

        var topDown = new List<Window>();
        var guard = 0;
        for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && guard < 20000 && topDown.Count < byHandle.Count; h = GetWindow(h, GwHwndNext), guard++)
        {
            if (byHandle.TryGetValue(h, out var w))
            {
                topDown.Add(w);
            }
        }

        var ids = new List<string>();
        for (var i = topDown.Count - 1; i >= 0; i--) // 아래→위
        {
            if (topDown[i] is Dock.Model.Core.IHostWindow { Window.Layout: { } root })
            {
                CollectDocumentIds(root, ids);
            }
        }

        Program.Log($"sdi-z os bottom→top count={ids.Count} [{string.Join(" ", ids)}]");
        return ids.Count > 0 ? ids : null;
    }

    private static void CollectDocumentIds(Dock.Model.Core.IDockable dockable, List<string> ids)
    {
        if (dockable is PocDocument doc)
        {
            ids.Add(doc.Id);
            return;
        }

        if (dockable is Dock.Model.Core.IDock { VisibleDockables: { } children })
        {
            foreach (var child in children)
            {
                CollectDocumentIds(child, ids);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
}
#endif

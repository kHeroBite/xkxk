// 진단 전용 — 메인 창 맞춤·초기화(④⑤⑥) 직후 1초간 위치·DPI·디스플레이 창 메시지를 로그로 남기는 계측(Q6 #5 간헐 y=5 원인 확정용, 동작 변경 없음). 제품 빌드에서는 호출째 빠진다(Conditional)
using Avalonia.Controls;
#if PD_DIAGNOSTICS
using System.Diagnostics;
using System.Runtime.InteropServices;
#endif

namespace Pd.Poc.Desktop;

/// <summary>
/// 배경(team-lead·odev-dnd 분석): fit-single pos=(-7,0) 185ms 뒤 실제 위치 (-7,5), 이어진 초기화의 위치 설정도 먹지 않음 —
/// 맞춤 창(-7..2569)이 DISPLAY3(x&lt;0)와 7px 겹쳐 OS 모니터/DPI 재배치가 의심된다(추정). 이 계측은 그 1초 동안
/// WM_WINDOWPOSCHANGING·WM_WINDOWPOSCHANGED·WM_DPICHANGED·WM_DISPLAYCHANGE 의 시각·flags·x,y,cx,cy·관리 코드 호출 스택 요약을 남긴다.
/// 관리 스택에 우리 코드(ResetMain·FitTo·EnsureClientSize)나 Avalonia Resize 가 없으면 OS 가 보낸 메시지로 읽는다.
/// </summary>
internal static class WindowPosTrace
{
#if PD_DIAGNOSTICS
    private const uint WmWindowPosChanging = 0x0046, WmWindowPosChanged = 0x0047, WmDisplayChange = 0x007E, WmDpiChanged = 0x02E0;
    private static readonly TimeSpan TraceSpan = TimeSpan.FromSeconds(1);
    private static readonly Stopwatch s_clock = new();
    private static string s_tag = "";
    private static bool s_installed;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int L, T, R, B;
    }
#endif

    /// <summary>메인 창에 계측 훅을 건다(AttachMain 에서 1회). 훅은 Begin 뒤 1초 동안만 기록한다.</summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    public static void Install(Window main)
    {
#if PD_DIAGNOSTICS
        if (!OperatingSystem.IsWindows() || s_installed)
        {
            return;
        }

        s_installed = true;
        Win32Properties.AddWndProcHookCallback(main, Hook);
#endif
    }

    /// <summary>④⑤⑥ 시작 시 부른다 — 지금부터 1초 동안 기록한다.</summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    public static void Begin(string tag)
    {
#if PD_DIAGNOSTICS
        s_tag = tag;
        s_clock.Restart();
        Program.Log($"wpos-trace begin {tag}");
#endif
    }

#if PD_DIAGNOSTICS
    private static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!s_clock.IsRunning || s_clock.Elapsed > TraceSpan
            || (msg != WmWindowPosChanging && msg != WmWindowPosChanged && msg != WmDpiChanged && msg != WmDisplayChange))
        {
            return IntPtr.Zero;
        }

        var t = $"+{s_clock.ElapsedMilliseconds}ms";
        string body;
        if ((msg == WmWindowPosChanging || msg == WmWindowPosChanged) && lParam != IntPtr.Zero)
        {
            var wp = Marshal.PtrToStructure<WindowPos>(lParam);
            body = $"{(msg == WmWindowPosChanging ? "CHANGING" : "CHANGED")} flags=0x{wp.Flags:X} pos=({wp.X},{wp.Y}) size={wp.Cx}x{wp.Cy}";
        }
        else if (msg == WmDpiChanged && lParam != IntPtr.Zero)
        {
            var r = Marshal.PtrToStructure<Rect32>(lParam);
            body = $"DPICHANGED dpi={(long)wParam & 0xFFFF}x{((long)wParam >> 16) & 0xFFFF} suggested=({r.L},{r.T})-({r.R},{r.B})";
        }
        else
        {
            body = $"DISPLAYCHANGE bpp={(long)wParam} res={(long)lParam & 0xFFFF}x{((long)lParam >> 16) & 0xFFFF}";
        }

        Program.Log($"wpos-trace {s_tag} {t} {body} stack={StackSummary()}");
        return IntPtr.Zero; // 계측만 — handled 를 바꾸지 않는다
    }

    /// <summary>관리 코드 호출 스택 요약(훅 자신 제외, 최대 8 프레임 '형식.메서드').</summary>
    private static string StackSummary()
    {
        var frames = new StackTrace(2, false).GetFrames();
        return string.Join("<", frames.Take(8).Select(f => f.GetMethod() is { } m ? $"{m.DeclaringType?.Name}.{m.Name}" : "?"));
    }
#endif
}

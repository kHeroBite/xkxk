// SDI 그림자 창(SdiShadowWindow)이 쓰는 Win32 호출 모음 — 레이어드 창 생성·갱신·위치·DWM 프레임 조회(Windows 전용)
using System.Runtime.InteropServices;

namespace Pd.Poc.Desktop;

/// <summary>P/Invoke 선언만 둔다. 판단·상태는 SdiShadowWindow, 화소 계산은 SdiShadowBitmap 이 맡는다.</summary>
internal static class SdiShadowNative
{
    public const int WsPopup = unchecked((int)0x80000000);
    public const int WsExLayered = 0x00080000, WsExTransparent = 0x00000020, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;
    public const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoRedraw = 0x0008, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;
    public const int SwHide = 0;
    public const int GwlExStyle = -20, WsExTopmost = 0x00000008;
    public const int SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;
    public static readonly IntPtr HwndNoTopmost = new(-2);

    [DllImport("user32.dll")]
    public static extern int GetWindowLongW(IntPtr h, int index);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr h);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr BeginDeferWindowPos(int count);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr DeferWindowPos(IntPtr dwp, IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EndDeferWindowPos(IntPtr dwp);
    public const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point32
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Size32
    {
        public int Cx, Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Blend
    {
        public byte Op, Flags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public int CbSize;
        public uint Style;
        public IntPtr WndProc;
        public int ClsExtra, WndExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WndClassEx wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr h);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr h, int cmd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr h);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr h);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr h);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr h, out Rect32 r);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr h);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dstDc, ref Point32 dst, ref Size32 size, IntPtr srcDc, ref Point32 src, int key, ref Blend blend, int flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr h);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr h, IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect32 value, int size);
}

// SDI 그림자 창의 네이티브 표면 — 레이어드 창 생성·화소 버퍼(재사용)→DIB→UpdateLayeredWindow 그리기·SDI 뒤 z 배치·숨김·파괴(Win32 전용)
using System.Runtime.InteropServices;
using static xkxk.frameworks.ui.hosting.desktop.SdiShadowNative;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 그림자 창 핸들 하나를 소유하고 Win32 작업만 한다. 언제 그릴지·실패 처리는 SdiShadowWindow 가 맡는다.
/// W2: 화소 수는 long 으로 곱해 검사하고 상한(가상 화면 넓이×높이×2)을 넘으면 그리지 않는다. 화면·메모리 DC 를 못 얻으면 얻은 것만 정리하고 false.
/// 같은 크기면 관리 버퍼를 다시 쓴다(지우고 채움). GDI 객체(DC·DIB)는 호출마다 만들고 같은 호출 안에서 모두 돌려준다.
/// W7: 항상 위 해제 추종은 NOTOPMOST + SDI 뒤 끼우기를 DeferWindowPos 한 묶음으로, 실패하면 2안(첫 호출 SWP_NOREDRAW).
/// </summary>
internal sealed class SdiShadowSurface
{
    private const string ClassName = "PdSdiShadow";
    private const long FallbackPixelCap = 7680L * 4320L * 2; // 가상 화면 크기를 못 읽을 때의 상한(8K 두 장)
    private static bool s_registered;
#if PD_DIAGNOSTICS
    private static readonly bool s_forceFail = string.Equals(Environment.GetEnvironmentVariable("PD_SDI_SHADOW")?.Trim(), "fail", StringComparison.OrdinalIgnoreCase);
#endif

    private int[]? _buf;
    private int _bufW, _bufH;

    public IntPtr Hwnd { get; private set; }

    public bool Shown { get; private set; }

    /// <summary>실패 사유(로그용). Create·Draw 가 false 를 돌려줄 때만 의미가 있다.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>비소유 레이어드 창을 만든다(B6: 소유 창은 OS 가 SDI 앞에 둔다). 이미 있으면 true.</summary>
    public bool Create()
    {
        if (Hwnd != IntPtr.Zero)
        {
            return true;
        }

        var inst = GetModuleHandleW(null);
        if (!s_registered)
        {
            var wc = new WndClassEx { CbSize = Marshal.SizeOf<WndClassEx>(), WndProc = NativeLibrary.GetExport(NativeLibrary.Load("user32.dll"), "DefWindowProcW"), Instance = inst, ClassName = ClassName };
            s_registered = RegisterClassExW(ref wc) != 0 || Marshal.GetLastWin32Error() == 1410; // 1410 = 이미 등록됨
        }

        Hwnd = CreateWindowExW(WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate, ClassName, null, WsPopup, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        LastError = Hwnd == IntPtr.Zero ? $"창 생성 실패 err={Marshal.GetLastWin32Error()}" : "";
        return Hwnd != IntPtr.Zero;
    }

    /// <summary>(x,y,w,h) 에 그림자 모양으로 내용·위치·크기를 갱신한다. 실패하면 false(예외는 호출 측이 잡는다).</summary>
    public bool Draw(int x, int y, int w, int h, int ext, double radius, SdiShadowShape shape, double scale)
    {
#if PD_DIAGNOSTICS
        if (s_forceFail)
        {
            throw new InvalidOperationException("진단 PD_SDI_SHADOW=fail — 그림자 그리기 강제 실패");
        }
#endif
        var pixels = (long)w * h;
        var cap = PixelCap();
        if (w <= 0 || h <= 0 || pixels > cap || pixels > int.MaxValue)
        {
            LastError = $"화소 수 범위 밖 size={w}x{h} pixels={pixels} cap={cap}";
            return false;
        }

        var buf = Buffer(w, h);
        SdiShadowBitmap.Fill(buf, w, h, ext, radius * scale, shape, scale); // radius = 창 바깥 모서리 반경(96DPI px, SdiShadowShape.ReadOuterRadius — C3 정합)
        var screen = GetDC(IntPtr.Zero);
        var mem = screen == IntPtr.Zero ? IntPtr.Zero : CreateCompatibleDC(screen);
        if (mem == IntPtr.Zero)
        {
            if (screen != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screen);
            }

            LastError = screen == IntPtr.Zero ? "화면 DC 0" : "메모리 DC 0";
            return false;
        }

        try
        {
            var bmi = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            var bmp = CreateDIBSection(mem, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            if (bmp == IntPtr.Zero || bits == IntPtr.Zero)
            {
                LastError = $"CreateDIBSection 실패 size={w}x{h}";
                if (bmp != IntPtr.Zero)
                {
                    DeleteObject(bmp);
                }

                return false;
            }

            var old = SelectObject(mem, bmp);
            try
            {
                Marshal.Copy(buf, 0, bits, w * h);
                var dst = new Point32 { X = x, Y = y };
                var size = new Size32 { Cx = w, Cy = h };
                var src = new Point32();
                var blend = new Blend { SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_OVER, AC_SRC_ALPHA
                var ok = UpdateLayeredWindow(Hwnd, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2); // ULW_ALPHA
                LastError = ok ? "" : $"UpdateLayeredWindow 실패 err={Marshal.GetLastWin32Error()} size={w}x{h}";
                return ok;
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(bmp);
            }
        }
        finally
        {
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>
    /// owner 바로 뒤(z)에 건다. owner 가 항상 위가 아닌데 그림자만 항상 위로 남아 있으면(항상 위 해제 직후) 묶음으로 내린다 —
    /// 항상 위 SDI 뒤에 끼우면 OS 가 그림자도 항상 위로 만들기 때문이다. 해제 추종 때는 어느 경로(defer/noredraw)였는지 로그에 남긴다.
    /// </summary>
    public void PlaceBehind(IntPtr owner, int x, int y, uint flags, string? title)
    {
        flags |= SwpNoActivate;
        Shown |= (flags & SwpShowWindow) != 0;
        if ((GetWindowLongW(owner, GwlExStyle) & WsExTopmost) != 0 || (GetWindowLongW(Hwnd, GwlExStyle) & WsExTopmost) == 0)
        {
            SetWindowPos(Hwnd, owner, x, y, 0, 0, flags);
            return;
        }

        var path = DemoteBehind(owner, x, y, flags);
        HostLog.Write($"sdi-shadow '{title}' 항상 위 해제 추종 path={path}");
    }

    private string DemoteBehind(IntPtr owner, int x, int y, uint flags)
    {
        var dwp = BeginDeferWindowPos(2);
        if (dwp != IntPtr.Zero)
        {
            dwp = DeferWindowPos(dwp, Hwnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoRedraw);
        }

        if (dwp != IntPtr.Zero)
        {
            dwp = DeferWindowPos(dwp, Hwnd, owner, x, y, 0, 0, flags); // DeferWindowPos 가 실패하면 시스템이 묶음을 해제한다
        }

        if (dwp != IntPtr.Zero && EndDeferWindowPos(dwp) && (GetWindowLongW(Hwnd, GwlExStyle) & WsExTopmost) == 0)
        {
            return "defer";
        }

        var err = Marshal.GetLastWin32Error(); // 묶음 실패 사유(Begin/Defer/EndDeferWindowPos)
        SetWindowPos(Hwnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoRedraw);
        SetWindowPos(Hwnd, owner, x, y, 0, 0, flags);
        return $"noredraw err={err}";
    }

    public void Hide()
    {
        if (Hwnd != IntPtr.Zero && Shown)
        {
            ShowWindow(Hwnd, SwHide);
            Shown = false;
        }
    }

    /// <summary>창과 관리 버퍼를 놓는다. 여러 번 불려도 안전하다.</summary>
    public void Destroy()
    {
        if (Hwnd != IntPtr.Zero)
        {
            DestroyWindow(Hwnd);
            Hwnd = IntPtr.Zero;
        }

        Shown = false;
        _buf = null;
        _bufW = _bufH = 0;
    }

    /// <summary>owner 의 보이는 프레임(DWMWA_EXTENDED_FRAME_BOUNDS, 실패 시 GetWindowRect).</summary>
    public static bool TryFrame(IntPtr owner, out Rect32 frame)
    {
        if (DwmGetWindowAttribute(owner, DwmwaExtendedFrameBounds, out frame, Marshal.SizeOf<Rect32>()) == 0 && frame.Right > frame.Left)
        {
            return true;
        }

        return GetWindowRect(owner, out frame) && frame.Right > frame.Left && frame.Bottom > frame.Top;
    }

    // 같은 크기면 기존 버퍼를 0 으로 지워 다시 쓴다(Fill 은 0 으로 초기화된 배열을 전제로 프레임 안을 건너뛴다).
    private int[] Buffer(int w, int h)
    {
        if (_buf is not null && _bufW == w && _bufH == h)
        {
            Array.Clear(_buf);
            return _buf;
        }

        _buf = new int[w * h];
        (_bufW, _bufH) = (w, h);
        return _buf;
    }

    private static long PixelCap()
    {
        var vw = GetSystemMetrics(SmCxVirtualScreen);
        var vh = GetSystemMetrics(SmCyVirtualScreen);
        return vw > 0 && vh > 0 ? (long)vw * vh * 2 : FallbackPixelCap;
    }
}

// SDI 창 바깥 그림자(Q2 #16) — SDI 바로 뒤(z)에 붙는 클릭 통과 레이어드 창이 보이는 프레임 둘레에 활성·비활성별 그림자를 그린다(Win32 전용)
using Avalonia.Controls;
using static xkxk.frameworks.ui.hosting.desktop.SdiShadowNative;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 원인(odev-shadow 실측): SDI 는 BorderOnly + 확장 클라이언트라 DWM 이 활성 구분 없는 고정 약한 그림자만 그린다(D-C2-51).
/// 대응: 비소유 레이어드 창(SdiShadowSurface)을 보이는 프레임 + 사방 여백 크기로 겹쳐 프레임 밖에만 그리고, SDI 의 WM_WINDOWPOSCHANGED·WM_NCACTIVATE 마다 SDI 바로 뒤에 건다(B6).
/// 최대화·최소화·숨김·끔(SdiShadowShape.IsOn)이면 감춘다. 화소는 키(크기·활성·배율·활성/비활성 모양·켜짐)가 바뀔 때만 다시 만든다(W1).
/// 견고성(W2·W5·W6): 예외는 Sync 안에서 잡아 그림자만 숨기고 1회 로그·같은 크기 재시도 안 함, 재진입은 끝난 뒤 1회 재실행, SDI 핸들이 죽었으면 스스로 정리.
/// </summary>
internal sealed class SdiShadowWindow
{
    private const uint WmDestroy = 0x0002, WmWindowPosChanged = 0x0047, WmNcActivate = 0x0086;

    private readonly SdiShadowSurface _surface = new();
    private Window? _window;
    private IntPtr _owner;
    private bool _active, _syncing, _again, _failLogged, _dead; // _dead: 한 번 정리되면 다시 만들지 않는다(닫는 중 늦게 온 메시지 대비)
    private (int W, int H, bool Active, uint Dpi, SdiShadowShape ShapeA, SdiShadowShape ShapeI, double Radius, bool On) _drawn;
    private (int W, int H) _attempt, _failedSize;
#if PD_DIAGNOSTICS
    private readonly string? _force = Environment.GetEnvironmentVariable("PD_SDI_SHADOW")?.Trim().ToLowerInvariant(); // off | active | inactive | fail(그리기 강제 실패 — SdiShadowSurface)
    private bool Enabled => OperatingSystem.IsWindows() && _force != "off";
    private bool IsActiveLook() => _force is "active" or "inactive" ? _force == "active" : _active; // 비전경 개발 확인용 강제
#else
    private static bool Enabled => OperatingSystem.IsWindows();
    private bool IsActiveLook() => _active;
#endif

    /// <summary>SdiWindowHost 생성자에서 호출. Windows 가 아니면 아무것도 하지 않는다.</summary>
    public void Install(Window window)
    {
        if (Enabled)
        {
            _window = window;
            Win32Properties.AddWndProcHookCallback(window, Hook);
            SdiShadowStack.Register(this); // 처음부터 등록 — 꺼진 채 열린 SDI 도 켬(SyncAll) 때 바로 만든다(W1)
        }
    }

    private IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmWindowPosChanged:
                _owner = hWnd;
                Sync();
                SdiShadowStack.RestackAll(this); // 이 SDI 의 z 변경이 다른 SDI 와 그림자 사이에 끼어들 수 있다
                break;
            case WmNcActivate:
                _owner = hWnd;
                _active = wParam != IntPtr.Zero;
                Sync();
                break;
            case WmDestroy:
                Destroy();
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>현재 상태로 그림자를 맞춘다(훅·SdiShadowStack.SyncAll 에서, UI 스레드). 예외를 밖으로 내보내지 않는다.</summary>
    internal void Sync()
    {
        if (_syncing)
        {
            _again = true;
            return;
        }

        _syncing = true;
        try
        {
            SyncCore();
            if (_again)
            {
                SyncCore(); // 맞추는 중 다시 불렸다 — 끝난 뒤 1회만 다시 맞춘다
            }
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _syncing = _again = false;
        }
    }

    private void SyncCore()
    {
        if (_owner == IntPtr.Zero || _dead || !IsWindow(_owner))
        {
            if (_owner != IntPtr.Zero && !_dead)
            {
                Destroy(); // 주인 SDI 핸들이 이미 없다(W5)
            }

            return;
        }

        var on = SdiShadowShape.IsOn();
        if (!on || !IsWindowVisible(_owner) || IsIconic(_owner) || IsZoomed(_owner) || !SdiShadowSurface.TryFrame(_owner, out var frame))
        {
            _surface.Hide();
            _drawn = on ? _drawn : default; // 다시 켜면 같은 크기여도 새로 그린다
            return;
        }

        var active = IsActiveLook();
        var dpi = GetDpiForWindow(_owner);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        var (shapeA, shapeI) = (SdiShadowShape.ReadActive(), SdiShadowShape.ReadInactive());
        var shape = active ? shapeA : shapeI;
        var ext = (int)Math.Ceiling(Math.Max(shapeA.Extent, shape.Extent) * scale); // 창 크기는 상태와 무관하게 활성 여백(전환 때 크기 변경 없음)
        var (x, y) = (frame.Left - ext, frame.Top - ext);
        var (w, h) = (frame.Right - frame.Left + 2 * ext, frame.Bottom - frame.Top + 2 * ext);
        _attempt = (w, h);
        if (_failedSize == (w, h))
        {
            _surface.Hide(); // 같은 크기에서 이미 실패했다 — 크기가 바뀔 때까지 다시 시도하지 않는다
            return;
        }

        if (!_surface.Create())
        {
            Fail(_surface.LastError);
            return;
        }

        var radius = SdiShadowShape.ReadOuterRadius();
        var key = (w, h, active, dpi, shapeA, shapeI, radius, on);
        var moveFlag = SwpNoSize;
        if (_drawn != key)
        {
            if (!_surface.Draw(x, y, w, h, ext, radius, shape, scale))
            {
                Fail(_surface.LastError);
                return;
            }

            _drawn = key;
            moveFlag |= SwpNoMove; // 위치·크기는 UpdateLayeredWindow 가 이미 맞췄다
        }

        var wasShown = _surface.Shown;
        _surface.PlaceBehind(_owner, x, y, moveFlag | (wasShown ? 0 : SwpShowWindow), _window?.Title);
        if (!wasShown)
        {
            HostLog.Write($"sdi-shadow '{_window?.Title}' show active={active} frame=({frame.Left},{frame.Top},{frame.Right},{frame.Bottom}) ext={ext} dpi={dpi}");
        }
    }

    private void Fail(string reason) // 그림자만 숨기고 SDI 는 그대로 둔다. 로그는 창마다 1회, 같은 크기는 다시 시도하지 않는다
    {
        _failedSize = _attempt;
        _drawn = default;
        _surface.Hide();
        if (!_failLogged)
        {
            _failLogged = true;
            HostLog.Write($"sdi-shadow '{_window?.Title}' 그림자 실패 → 숨김(같은 크기 재시도 안 함) size={_attempt.W}x{_attempt.H} {reason}");
        }
    }

    /// <summary>보이는 그림자만 자기 SDI 바로 뒤로 다시 건다(z 만). SdiShadowStack 이 주 창·다른 SDI 의 z 변경 때 부른다.</summary>
    internal void RestackZ()
    {
        try
        {
            if (!_surface.Shown || _owner == IntPtr.Zero)
            {
                return;
            }

            if (!IsWindow(_owner))
            {
                Destroy();
                return;
            }

            _surface.PlaceBehind(_owner, 0, 0, SwpNoMove | SwpNoSize, _window?.Title);
        }
        catch (Exception ex)
        {
            Fail($"RestackZ {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>그림자 창을 없앤다. WM_DESTROY·SDI Closed(SdiWindowHost)·주인 핸들 소멸에서 불리며 여러 번 불려도 안전하다.</summary>
    internal void Destroy()
    {
        _dead = true;
        _surface.Destroy();
        SdiShadowStack.Unregister(this);
    }
}

// SDI 창 사용자 크기 조절 끝 처리 — 마지막 조절 뒤 잠시 멈추면 레이아웃 자동 저장을 알린다
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// SdiWindowHost 가 필드 초기화자로 창마다 1개 보유한다(K2). 타이머와 창 닫힘 구독은 첫 사용자 조절 때 만든다(옛 지연 생성과 같은 시점).
/// </summary>
internal sealed class SdiResizeDebounce
{
    // Win32 WM_EXITSIZEMOVE 를 Avalonia 가 공개하지 않으므로, 사용자 크기 조절(Reason=User)이 이 시간 동안 멈추면 끝으로 본다.
    private static readonly TimeSpan ResizeSettle = TimeSpan.FromMilliseconds(250);
    private DispatcherTimer? _resizeTimer;

    /// <summary>SdiWindowHost.OnResized(사용자 조절, 일반 모드)에서 호출 — 조절이 이어지는 동안 끝 판정을 미룬다(Stop→Start).</summary>
    public void Restart(Window window)
    {
        _resizeTimer ??= CreateResizeTimer(window);
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private static DispatcherTimer CreateResizeTimer(Window window)
    {
        var timer = new DispatcherTimer { Interval = ResizeSettle };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            App.NotifyLayoutChanged(); // 크기 조절은 셸이 받을 신호가 없어 호스트가 자동 저장(디바운스)을 알린다(odev-2 계약)
        };
        window.Closed += (_, _) => timer.Stop();
        return timer;
    }
}

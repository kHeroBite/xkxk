// macOS 에서 OS 가 그리는 신호등을 쓸 때 제목줄을 맞추는 공통 처리 — 신호등 자리 여백과 SDI 창 장식(Full) 전환(메인·SDI 제목줄이 호출)
using xkxk.frameworks.ui.shell;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// team-lead 결정 ①: OS 최상위 창(메인·SDI)은 macOS 에서 OS 신호등을 쓰고 앱이 그린 캡션 3버튼은 숨긴다(직접 그리는 것은 MDI 내부 창뿐).
/// 근거: Apple HIG "Avoid creating custom window UI"(https://developer.apple.com/design/human-interface-guidelines/windows).
/// </summary>
internal static class MacNativeCaption
{
    /// <summary>
    /// 네이티브 신호등을 쓰면 true. 제목줄 테두리(bar)의 왼쪽 안쪽 여백을 신호등 자리만큼 비우고, ownsDecorations 이면 창 장식도 맞춘다 —
    /// Avalonia 12.1.4 macOS 는 WindowDecorations=Full 일 때만 신호등을 보이므로(native/OSX/WindowImpl.mm) SDI 창은 로컬 값 Full 로 HostWindow 테마의
    /// BorderOnly 를 이기고, 네이티브가 아니면 로컬 값을 지워 테마로 돌아간다. window 가 null 이면(웹·Android·확장 안 함) 여백만 0 으로 되돌린다.
    /// </summary>
    public static bool Apply(Border bar, Window? window, bool ownsDecorations)
    {
        var native = window is not null && ChromePlatform.UseNativeMacCaption;
        bar.Padding = new Thickness(native ? ChromePlatform.NativeMacCaptionInset : 0, 0, 0, 0);
        if (ownsDecorations && window is not null)
        {
            if (native)
            {
                window.SetValue(Window.WindowDecorationsProperty, WindowDecorations.Full);
            }
            else
            {
                window.ClearValue(Window.WindowDecorationsProperty);
            }
        }

        return native;
    }
}

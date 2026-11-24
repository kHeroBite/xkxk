// 메인 창 확장 제목줄(Q14) 호스트 몫 — OS 제목줄 영역까지 클라이언트를 넓히고(Avalonia 12 ExtendClientAreaToDecorationsHint) 최대화·DPI 상태를 로그로 남긴다
using Avalonia.Controls;

namespace Pd.Poc.Desktop;

/// <summary>
/// 공식 방식(Avalonia 12, 설치 패키지 Avalonia.Controls.xml / Avalonia.Base.xml 문서 주석 근거):
/// - Window.ExtendClientAreaToDecorationsHint=true 로 클라이언트 영역을 제목줄까지 넓히고, ExtendClientAreaTitleBarHeightHint 로 제목줄 높이를 정한다.
/// - 셸(MainView)은 제목줄 요소에 WindowDecorationProperties.ElementRole 을 붙인다:
///   빈 공간 = TitleBar(끌기·더블클릭 최대화), 최소화 = MinimizeButton, 최대화 = MaximizeButton(Win32 HTMAXBUTTON → Win11 스냅 레이아웃),
///   닫기 = CloseButton, 메뉴·로고 등 누를 수 있는 요소 = User.
/// 호스트는 창 속성만 맡는다. 셸 쪽 제목줄이 준비되기 전에 켜면 메뉴가 캡션 영역과 겹치므로 Enabled 로 켠다(셸 계약, odev-2).
/// </summary>
public static class MainWindowChrome
{
    /// <summary>제목줄 높이(DIP). 셸 제목줄 행 높이와 같아야 한다.</summary>
    public const double TitleBarHeight = 32;

    /// <summary>확장 제목줄 사용 여부. 호스트 인자 --main-titlebar on|off 로 바꾼다(기본 on — Q14, 2026-10-29 전환).</summary>
    public static bool Enabled { get; set; }

    public static void Apply(Window main)
    {
        if (!Enabled)
        {
            return;
        }

        main.ExtendClientAreaToDecorationsHint = true;
        main.ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
        MainNcMenuHook.Install(main); // 메뉴 줄 빈칸·캡션 버튼 우클릭 = Pd 창 메뉴(OS 시스템 메뉴와 겹치지 않게)
        var overlayRemoved = MainWindowDecorations.RemoveOverlay(main); // Avalonia 자체 제목 글자·캡션 버튼이 셸 제목줄과 겹치지 않게(사용자 신고, otest-12 T2)
        main.Opened += (_, _) => Program.Log(
            $"main-titlebar extended={main.IsExtendedIntoWindowDecorations} height={TitleBarHeight} decorations={main.WindowDecorations} scaling={main.DesktopScaling} overlayRemoved={overlayRemoved}");
        main.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                // 최대화 시 상단 잘림 점검용: 최대화 상태의 위치·틀 크기를 남긴다(실측 판정은 화면 캡처로).
                Program.Log($"main-titlebar state={main.WindowState} pos=({main.Position.X},{main.Position.Y}) frame={main.FrameSize?.Width:F0}x{main.FrameSize?.Height:F0} client={main.ClientSize.Width:F0}x{main.ClientSize.Height:F0} scaling={main.DesktopScaling}");
            }
        };
    }
}

// 패널 기능(도킹 안내·핀) 켬/끔 모드와 적용 UI 형별 순수 판정 규칙
namespace Pd.Poc.Shell.Theming;

/// <summary>패널 기능 설정값. Auto 는 적용된 UI 형으로 해석한다(PC형 켬, 태블릿형·모바일형 끔).</summary>
public enum PanelFeatureMode
{
    Auto,
    On,
    Off,
}

/// <summary>패널 기능 켬/끔 판정. Avalonia·Dock 의존 없음.</summary>
public static class PanelFeatureRules
{
    /// <summary>
    /// 패널 기능(패널로의 도킹 놓기·핀)을 쓸 수 있는지. On/Off 는 형과 무관하고 Auto 는 적용 형이 PC형일 때만 true 다.
    /// applied 는 저장값이 아니라 지금 적용된 형(UiStyleViewModel.Current)이다. 아직 적용 전(Auto)이면 Auto 모드에서 false 다.
    /// </summary>
    public static bool IsEnabled(PanelFeatureMode mode, UiStyle applied) => mode switch
    {
        PanelFeatureMode.On => true,
        PanelFeatureMode.Off => false,
        _ => applied == UiStyle.Pc,
    };

    /// <summary>
    /// 패널 기능이 끔일 때도 패널 탭 문서가 패널 밖으로 나가는 놓기를 허용하는지. 문서 영역에 놓기(Fill)와 창으로 빼기(Window)만 true 이고
    /// 방향 분할(L/R/T/B)과 그 밖의 놓기는 false 다. 패널 탭 문서가 아닌 원본은 이 예외를 받지 않는다.
    /// </summary>
    public static bool AllowsPanelExit(bool sourceIsPanelDocument, bool isFill, bool isWindow) =>
        sourceIsPanelDocument && (isFill || isWindow);
}

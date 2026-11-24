// 템플릿 부품 이름 계약 — 제어층이 찾는 PART_* 이름의 정본. 실재 여부는 poc/ui/tools/check_layers.py 가 Pd.Ui.Views 템플릿과 Dock 패키지에서 대조한다
namespace Pd.Ui.Controls;

/// <summary>
/// 제어층(Pd.Poc.Shell)이 시각 트리·이름 범위에서 찾는 템플릿 부품 이름.
/// Shell 코드에 문자열을 직접 쓰지 않고 이 상수를 쓴다. <c>is</c>·속성 패턴에도 쓰이므로 const 여야 한다.
/// </summary>
public static class TemplateParts
{
    /// <summary>템플릿 부품 이름의 공통 접두. 개별 부품이 아니라 진단 덤프의 분류 기준이다.</summary>
    public const string Prefix = "PART_";

    // Pd.Ui.Views 의 MDI 문서 창 템플릿(MdiDocumentWindowTheme.axaml)이 정의한다
    public const string OuterBorder = "PART_OuterBorder";
    public const string Header = "PART_Header";
    public const string TitleGrid = "PART_Grid";
    public const string DragHandle = "PART_DragHandle";
    public const string ContentBorder = "PART_ContentBorder";
    public const string ToolPinButton = "PART_ToolPinButton";

    // Dock.Avalonia 패키지 템플릿이 정의한다
    public const string PinButton = "PART_PinButton";
    public const string ManagedWindowLayer = "PART_ManagedWindowLayer";
    public const string SelectorOverlay = "PART_SelectorOverlay";
}

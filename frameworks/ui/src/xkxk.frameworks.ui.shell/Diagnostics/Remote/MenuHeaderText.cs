// 메뉴 항목 Header(문자열·TextBlock·ContentControl)를 표시 문자열로 바꾸는 원격 진단 공용 규칙 — dock.menuHeaders·문맥 메뉴 검사가 함께 쓴다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using Avalonia.Controls;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>메뉴 Header 의 화면 문자열 추출 규칙 한 곳(예전 RemoteCommands.DockMenu·ContextMenuInspector 의 HeaderText 중복을 합침).</summary>
internal static class MenuHeaderText
{
    /// <summary>null → null, 문자열 → 그대로, TextBlock → Text, 문자열 Content 의 ContentControl → Content, 그 밖 → ToString().</summary>
    internal static string? Of(object? header) => header switch
    {
        null => null,
        string s => s,
        TextBlock tb => tb.Text,
        ContentControl { Content: string cs } => cs,
        _ => header.ToString(),
    };
}
#endif

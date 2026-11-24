// 테마 JSON 파일 주고받기 데이터 처리 — 파일 이름 규칙과 스트림 쓰기·읽기(화면 ThemeEditorView 에서 분리, D-LAYER)
using System.IO;
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.data.Theming;

/// <summary>파일 대화상자는 화면이 띄우고, 고른 파일의 스트림에 JSON 을 쓰고 읽는 일은 여기서 한다.</summary>
public static class ThemeFileExchange
{
    /// <summary>내보내기 기본 파일 이름. 테마 이름이 없으면 theme.</summary>
    public static string SuggestedFileName(string? themeName) => $"{themeName ?? "theme"}.pdtheme.json";

    /// <summary>테마 JSON 을 스트림에 쓴다.</summary>
    public static async Task WriteAsync(Stream stream, string json)
    {
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
    }

    /// <summary>스트림 전체를 테마 JSON 문자열로 읽는다.</summary>
    public static async Task<string> ReadAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}

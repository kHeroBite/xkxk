// 데스크톱 호스트의 파일 기반 레이아웃 저장소(ILayoutStore 구현)
using xkxk.frameworks.ui.data;
using xkxk.frameworks.ui.data.Layout;
using System.Text;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// 레이아웃 JSON 을 파일 1개에 저장·복원한다. 기본 경로는 %LOCALAPPDATA%\xkxk\frameworks\ui\layout.json,
/// 실행 인자 --layout-file 로 바꿀 수 있다. 쓰기는 임시 파일 → 교체로 원자 처리한다.
/// </summary>
public sealed class FileLayoutStore : ILayoutStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public FileLayoutStore(string? path = null)
    {
        FilePath = string.IsNullOrWhiteSpace(path) ? DefaultPath() : Path.GetFullPath(path);
    }

    public string FilePath { get; }

    public static string DefaultPath()
    {
        return Path.Combine(AppDataFolder.Root, "layout.json");
    }

    public async Task SaveAsync(string json)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, Utf8NoBom).ConfigureAwait(false);
        File.Move(tmp, FilePath, overwrite: true);
    }

    public async Task<string?> LoadAsync()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(FilePath, Utf8NoBom).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }
}

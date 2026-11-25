// Android 앱 전용 저장소의 레이아웃·설정 JSON 저장소 — 앱 내부 files 폴더에 원자적으로 쓰고 읽는다
using xkxk.frameworks.ui.data;
using xkxk.frameworks.ui.data.Layout;
using System.Text;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.android;

/// <summary>
/// ILayoutStore 구현. 경로 = Environment.SpecialFolder.LocalApplicationData(앱 전용, 다른 앱 접근 불가) / xkxk/frameworks/ui / 파일명.
/// 쓰기는 임시 파일 → 교체로 원자적이다(저장 중 강제 종료돼도 이전 본이 남는다).
/// 시험에서는 adb shell run-as xkxk.frameworks.ui ls/cat 으로 확인한다.
/// </summary>
public sealed class AndroidLayoutStore : ILayoutStore
{
    public AndroidLayoutStore(string fileName)
    {
        var dir = AppDataFolder.Root;
        FilePath = Path.Combine(dir, fileName);
    }

    public string FilePath { get; }

    public async Task SaveAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public async Task<string?> LoadAsync() =>
        File.Exists(FilePath) ? await File.ReadAllTextAsync(FilePath, Encoding.UTF8) : null;
}

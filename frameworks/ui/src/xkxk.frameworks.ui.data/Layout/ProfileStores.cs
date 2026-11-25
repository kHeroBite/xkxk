// 진단 인자 --profile-dir <경로> 의 저장소 — 레이아웃·설정·개인 레이아웃을 지정 폴더 파일로 저장해 실행 간 지속성은 두고 사용자 파일은 건드리지 않는다(시험 격리)
#if PD_DIAGNOSTICS // 진단 빌드 전용(제품 빌드 문자열 0건 — Q9)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.data.Layout;

/// <summary>
/// 사용: 데스크톱 `--profile-dir C:\tmp\pdprof1`(또는 `--profile-dir=...`), Android `--es pdargs "--profile-dir t1"`.
/// 상대 경로는 앱 데이터 폴더(LocalApplicationData/xkxk/frameworks/ui/profiles) 아래로 풀린다 — Android 는 앱 전용 폴더만 쓸 수 있어서다.
/// 폴더 안 파일: layout.json(자동 저장 슬롯), settings.json(설정), layouts/(개인 레이아웃·전체 백업). 브라우저는 파일 시스템이 없어 무시된다(웹 격리는 --layout-key).
/// --fresh 와 함께 주면 --fresh(메모리)가 우선한다(FreshGuardStores).
/// </summary>
public static class ProfileStores
{
    public const string ArgName = "--profile-dir";

    /// <summary>지금 적용 중인 프로필 폴더(절대 경로). 없으면 null.</summary>
    public static string? Directory { get; private set; }

    public static ILayoutStore? Layout { get; private set; }

    public static ILayoutStore? Settings { get; private set; }

    public static IKeyValueStore? Library { get; private set; }

    /// <summary>인자에서 --profile-dir 값을 꺼내(인자 목록에서는 뺀다) 저장소를 만든다. 브라우저·값 없음·이미 정해짐이면 저장소는 그대로다. 남은 인자를 돌려준다.</summary>
    public static IReadOnlyList<string> Take(IReadOnlyList<string> args)
    {
        var rest = new List<string>(args.Count);
        string? value = null;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith(ArgName + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = a[(ArgName.Length + 1)..];
            }
            else if (string.Equals(a, ArgName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                value = args[++i];
            }
            else
            {
                rest.Add(a);
            }
        }

        if (!string.IsNullOrWhiteSpace(value) && !OperatingSystem.IsBrowser() && Directory is null)
        {
            var dir = Path.IsPathRooted(value)
                ? value
                : Path.Combine(AppDataFolder.Root, "profiles", value);
            Directory = Path.GetFullPath(dir);
            Layout = new FileTextStore(Path.Combine(Directory, "layout.json"));
            Settings = new FileTextStore(Path.Combine(Directory, "settings.json"));
            Library = new FileKeyValueStore(Path.Combine(Directory, "layouts"));
            Console.WriteLine($"[shell] profile-dir={Directory}"); // i18n-ok 진단 출력(호스트 로그)
        }

        return rest;
    }

    /// <summary>파일 1개 텍스트 저장소(임시 파일에 쓴 뒤 교체 — 원자 쓰기).</summary>
    private sealed class FileTextStore : ILayoutStore
    {
        private readonly string _path;

        public FileTextStore(string path) => _path = path;

        public async Task SaveAsync(string json)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(tmp, _path, overwrite: true);
        }

        public async Task<string?> LoadAsync() =>
            File.Exists(_path) ? await File.ReadAllTextAsync(_path, Encoding.UTF8).ConfigureAwait(false) : null;
    }
}
#endif

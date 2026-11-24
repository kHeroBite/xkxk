// 데스크톱·Android 용 로컬 폴더 blob 저장소 — 앱 로컬 데이터 backgrounds/<테넌트>/<사용자>/ 아래에 해시 이름 파일만 둔다(웹은 호스트의 IndexedDB 구현). UI 비의존
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Pd.Client.Data.Theming;

/// <summary>
/// 이름은 BackgroundImageStore.NamePattern 에 맞아야 하고(경로 구분자 불가), 테넌트·사용자 폴더 이름도 영숫자·밑줄·하이픈만 받는다.
/// PoC 는 로그인이 없어 테넌트·사용자 = "local"·"local" 이다. 로그아웃 때 폴더 삭제는 제품 단계 과제다.
/// </summary>
public sealed class LocalBlobStore : IBlobStore
{
    public const string LocalAccount = "local";

    private static readonly Regex SegmentPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    public LocalBlobStore(string appDataRoot, string tenant = LocalAccount, string user = LocalAccount)
    {
        if (string.IsNullOrWhiteSpace(appDataRoot)) { throw new ArgumentException("앱 데이터 폴더가 비었다", nameof(appDataRoot)); }
        if (!SegmentPattern.IsMatch(tenant ?? string.Empty) || !SegmentPattern.IsMatch(user ?? string.Empty))
        {
            throw new ArgumentException("테넌트·사용자 폴더 이름은 영숫자·밑줄·하이픈 1~64자여야 한다");
        }

        Folder = Path.Combine(appDataRoot, "backgrounds", tenant!, user!);
    }

    /// <summary>이 저장소가 쓰는 폴더(없으면 첫 쓰기 때 만든다).</summary>
    public string Folder { get; }

    public async Task<byte[]?> ReadAsync(string name)
    {
        var path = PathOf(name);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path).ConfigureAwait(false) : null;
    }

    public async Task WriteAsync(string name, byte[] data)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Folder);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, data).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true); // 쓰다 끊겨도 반쯤 쓴 파일이 해시 이름으로 남지 않게 한다
    }

    public Task DeleteAsync(string name)
    {
        var path = PathOf(name);
        if (File.Exists(path)) { File.Delete(path); }
        return Task.CompletedTask;
    }

    private string PathOf(string name)
    {
        if (!BackgroundImageStore.IsValidName(name)) { throw new ArgumentException("저장 이름 규칙에 맞지 않는다", nameof(name)); }
        return Path.Combine(Folder, name);
    }
}

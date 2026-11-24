// 폴더 기반 키-값 저장소 — 키 하나를 <폴더>/<키>.json 파일 하나로 저장(임시 파일에 쓴 뒤 교체하는 원자 쓰기), 데스크톱·Android 공용
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Pd.Client.Data.Layout;

public sealed class FileKeyValueStore : IKeyValueStore
{
    private readonly string _folder;

    public FileKeyValueStore(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("저장 폴더가 비었습니다.", nameof(folder));
        }

        _folder = folder;
    }

    public async Task<string?> GetAsync(string key)
    {
        var path = PathOf(key);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false) : null;
    }

    public async Task SetAsync(string key, string value)
    {
        Directory.CreateDirectory(_folder);
        var path = PathOf(key);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, value, new UTF8Encoding(false)).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true); // 쓰는 도중 종료돼도 기존 파일은 온전하다
    }

    public Task DeleteAsync(string key)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <summary>키는 영문·숫자·'.'·'-'·'_' 만 허용(경로 탈출·예약 문자 차단).</summary>
    private string PathOf(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"잘못된 키: {key}", nameof(key));
        }

        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                throw new ArgumentException($"잘못된 키: {key}", nameof(key));
            }
        }

        return Path.Combine(_folder, key + ".json");
    }
}

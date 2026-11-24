// 브라우저 호스트의 localStorage 기반 레이아웃 저장소(ILayoutStore 구현)
using Pd.Client.Data.Layout;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Browser;

/// <summary>
/// 레이아웃 JSON 을 window.localStorage 의 키 1개에 저장·복원한다.
/// 같은 출처(127.0.0.1:포트)에서만 유지되며, 시험 간 격리는 키 접미사(--layout-key)로 한다.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserLayoutStore : ILayoutStore
{
    public const string DefaultKey = "pd-poc-layout";

    public BrowserLayoutStore(string? key = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
    }

    public string Key { get; }

    public Task SaveAsync(string json)
    {
        SetItem(Key, json);
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync()
    {
        var json = GetItem(Key);
        return Task.FromResult(string.IsNullOrWhiteSpace(json) ? null : json);
    }

    // main.js 가 정의한 화살표 함수 래퍼(localStorage 메서드를 떼어 호출하면 this 바인딩 오류가 난다)
    [JSImport("globalThis.__pdStorage.set")]
    private static partial void SetItem(string key, string value);

    [JSImport("globalThis.__pdStorage.get")]
    private static partial string? GetItem(string key);
}

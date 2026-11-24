// 브라우저 호스트의 localStorage 기반 키-값 저장소(IKeyValueStore 구현) — 개인 레이아웃 목록·내용(UI-11)
using Pd.Client.Data.Layout;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Browser;

/// <summary>키 하나를 localStorage 항목 "<접두사>:<키>" 하나로 저장한다. 같은 출처(호스트·포트)에서만 유지된다.</summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserKeyValueStore : IKeyValueStore
{
    private readonly string _prefix;

    public BrowserKeyValueStore(string prefix)
    {
        _prefix = prefix;
    }

    public Task<string?> GetAsync(string key)
    {
        var value = GetItem(Full(key));
        return Task.FromResult(string.IsNullOrEmpty(value) ? null : value);
    }

    public Task SetAsync(string key, string value)
    {
        SetItem(Full(key), value); // 용량 초과 등은 JS 예외 → 호출자(LayoutLibrary)가 Failed 로 알린다
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key)
    {
        RemoveItem(Full(key));
        return Task.CompletedTask;
    }

    private string Full(string key) => _prefix + ":" + key;

    // main.js 의 globalThis.__pdStorage 래퍼(localStorage 메서드를 떼어 호출하면 this 바인딩 오류가 난다)
    [JSImport("globalThis.__pdStorage.get")]
    private static partial string? GetItem(string key);

    [JSImport("globalThis.__pdStorage.set")]
    private static partial void SetItem(string key, string value);

    [JSImport("globalThis.__pdStorage.remove")]
    private static partial void RemoveItem(string key);
}

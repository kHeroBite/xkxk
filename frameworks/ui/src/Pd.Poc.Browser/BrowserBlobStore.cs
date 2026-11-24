// 웹 배경 이미지 저장소(IBlobStore 구현) — IndexedDB 모듈(wwwroot/pd-blobstore.js)에 해시 이름별 바이트를 둔다. 할당량 초과는 JSException(QuotaExceededError)으로 올라간다(09/21 §6.2)
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Pd.Client.Data.Theming;

namespace Pd.Poc.Browser;

/// <summary>키는 "&lt;접두사&gt;/&lt;해시 이름&gt;". 접두사는 레이아웃 키 규칙을 따라 시험 간 격리한다. 같은 출처(호스트·포트)에서만 유지된다.</summary>
[SupportedOSPlatform("browser")]
public sealed partial class BrowserBlobStore : IBlobStore
{
    private const string ModuleName = "pd-blobstore";
    private const string ModuleFile = "pd-blobstore.js";

    private readonly string _prefix;

    public BrowserBlobStore(string prefix)
    {
        _prefix = prefix;
    }

    /// <summary>모듈을 한 번 불러온다. 주소는 문서 기준 주소(document.baseURI)에 붙여 하위 경로 배포에서도 맞게 한다.</summary>
    public static Task ImportModuleAsync()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document");
        var baseUri = document?.GetPropertyAsString("baseURI") ?? "./";
        return JSHost.ImportAsync(ModuleName, new Uri(new Uri(baseUri), ModuleFile).ToString());
    }

    public async Task<byte[]?> ReadAsync(string name)
    {
        var b64 = await Read(Key(name)).ConfigureAwait(false);
        return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64);
    }

    public Task WriteAsync(string name, byte[] data) => Write(Key(name), Convert.ToBase64String(data));

    public Task DeleteAsync(string name) => Remove(Key(name));

    private string Key(string name) => _prefix + "/" + name;

    [JSImport("read", ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string?> Read(string key);

    [JSImport("write", ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task Write(string key, string base64);

    [JSImport("remove", ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task Remove(string key);
}

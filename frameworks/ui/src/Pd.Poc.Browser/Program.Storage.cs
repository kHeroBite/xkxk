// 브라우저 호스트의 배경 이미지 저장소 연결(09/21 §6.2) — IndexedDB 모듈을 불러와 DesignEffectsApplier.BlobStore 에 넣는다. 실패하면 저장소 없음(이미지 = none·알림 1회)
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Browser;

internal sealed partial class Program
{
    /// <summary>레이아웃 키 접미사 규칙(자동 저장 슬롯 키 + "-bg")으로 시험 간 격리하고, 테넌트·사용자는 PoC 고정값 local/local 이다.</summary>
    private static async Task InstallBlobStoreAsync(string? layoutKey)
    {
        try
        {
            await BrowserBlobStore.ImportModuleAsync();
            DesignEffectsApplier.BlobStore = new BrowserBlobStore((layoutKey ?? BrowserLayoutStore.DefaultKey) + "-bg/local/local");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[host] background blob store unavailable: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

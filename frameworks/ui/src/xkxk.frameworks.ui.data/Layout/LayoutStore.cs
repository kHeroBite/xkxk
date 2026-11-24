// 레이아웃 JSON 저장소 계약과 메모리 구현 — ILayoutStore.cs 에서 저장소 형식만 분리했다
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.data.Layout;

/// <summary>레이아웃 JSON 을 저장·복원하는 저장소. 데스크톱=파일, 브라우저=localStorage(호스트가 구현).</summary>
public interface ILayoutStore
{
    Task SaveAsync(string json);

    /// <summary>저장된 JSON. 없으면 null.</summary>
    Task<string?> LoadAsync();
}

/// <summary>저장소 미주입 시 쓰는 메모리 저장소(프로세스 종료 시 소멸).</summary>
public sealed class MemoryLayoutStore : ILayoutStore
{
    private string? _json;

    public Task SaveAsync(string json)
    {
        _json = json;
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync() => Task.FromResult(_json);
}

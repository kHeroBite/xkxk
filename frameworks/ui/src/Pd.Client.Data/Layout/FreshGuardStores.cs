// 실행 격리 저장소 — --fresh 면 실행 중 메모리, (진단) --profile-dir 면 그 폴더 파일로 레이아웃·설정·개인 레이아웃을 돌려 사용자 파일을 읽지도 쓰지도 않는다
using System;
using System.Threading.Tasks;

namespace Pd.Client.Data.Layout;

/// <summary>
/// 호스트가 준 저장소를 감싸 대상을 고른다. 우선순위: isolated()(--fresh) → 메모리, profile()(진단 --profile-dir) → 그 폴더 파일, 그 밖 → 호스트 저장소.
/// · --fresh: 같은 실행 안의 저장→불러오기(시험 시나리오)는 동작하고 프로세스가 끝나면 사라진다.
/// · --profile-dir: 실행 간 지속성은 유지하되 사용자 파일 대신 지정 폴더를 쓴다(재기동 유지 시험용).
/// 판정을 호출 때마다 하므로 시작 인자가 늦게 들어오는 Android(pdargs)에서도 그 뒤 접근부터 적용된다.
/// 원인 사례: 성능 구간 5(모든 기동 --fresh)에서 perf-ui 의 layout.save(AutomationBridge.PerfUi.cs:72) → WindowCommands.SaveLayoutAsync(State.cs:167)
/// 가 사용자 layout.json 을 덮었다(odev-4b 03:12:27). 쓰기 경로를 하나하나 막는 대신 저장소 한 곳에서 돌려 새 경로가 생겨도 새지 않게 한다.
/// </summary>
public sealed class FreshGuardLayoutStore : ILayoutStore
{
    private readonly ILayoutStore _inner;
    private readonly Func<bool> _isolated;
    private readonly Func<ILayoutStore?> _profile;
    private readonly MemoryLayoutStore _memory = new();

    public FreshGuardLayoutStore(ILayoutStore inner, Func<bool> isolated, Func<ILayoutStore?>? profile = null)
    {
        _inner = inner;
        _isolated = isolated;
        _profile = profile ?? (() => null);
    }

    private ILayoutStore Target => _isolated() ? _memory : _profile() ?? _inner;

    public Task SaveAsync(string json) => Target.SaveAsync(json);

    public Task<string?> LoadAsync() => Target.LoadAsync();
}

/// <summary>개인 레이아웃·전체 백업 저장소용 같은 격리(FreshGuardLayoutStore 설명 참조).</summary>
public sealed class FreshGuardKeyValueStore : IKeyValueStore
{
    private readonly IKeyValueStore _inner;
    private readonly Func<bool> _isolated;
    private readonly Func<IKeyValueStore?> _profile;
    private readonly MemoryKeyValueStore _memory = new();

    public FreshGuardKeyValueStore(IKeyValueStore inner, Func<bool> isolated, Func<IKeyValueStore?>? profile = null)
    {
        _inner = inner;
        _isolated = isolated;
        _profile = profile ?? (() => null);
    }

    private IKeyValueStore Target => _isolated() ? _memory : _profile() ?? _inner;

    public Task<string?> GetAsync(string key) => Target.GetAsync(key);

    public Task SetAsync(string key, string value) => Target.SetAsync(key, value);

    public Task DeleteAsync(string key) => Target.DeleteAsync(key);
}

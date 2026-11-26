// 아이콘 레지스트리 내부 저장소(UiIconStore)와 등록 주체별 보기 — 해석 순서·등록 규칙 진단 XKUI0201~0207·정책 P1~P4(UADR-0013)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>앱 컨텍스트가 1개 소유하는 아이콘 저장소. 앱·모듈 보기는 같은 저장소를 등록 주체(Owner)·진단 싱크만 바꿔 본다.</summary>
/// <remarks>
/// 해석 순서 ① 명시 등록 ② 공급자(새 것이 앞, 기본 생성 공급자 맨 끝). 잠금 1개, 공급자 호출·Changed·진단 보고는 잠금 밖.
/// 프레임워크는 아이콘을 명시 등록하지 않고 공급자로만 더한다(T-RESERVED).
/// </remarks>
internal sealed class UiIconStore
{
    private readonly object _gate = new();
    private readonly UiDiagnostics _runtime;
    private readonly Dictionary<string, UiIconDefinition> _explicit = new(StringComparer.Ordinal);
    private readonly List<ProviderEntry> _providers = new();
    private readonly HashSet<string> _renderFailed = new(StringComparer.Ordinal);

    internal UiIconStore(UiDiagnostics runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        // 기본 생성 공급자는 내부 경로로 넣는다(예약 검사 대상 아님). 이후 AddProvider 는 앞에 끼우므로 늘 맨 끝에 남는다.
        _providers.Add(new ProviderEntry(new GeneratedIconProvider(), UiRegistryOwners.Generated));
        Runtime = new View(this, UiRegistryOwners.App, runtime, null);
    }

    /// <summary>앱 수준 보기(Owner <see cref="UiRegistryOwners.App"/>, 진단은 앱 Diagnostics).</summary>
    internal IUiIconRegistry Runtime { get; }

    private event EventHandler<UiRegistryChangedEventArgs>? Changed;

    /// <summary>모듈용 보기. Build 가 끝난 뒤(session.IsCompleted)에는 진단을 앱 Diagnostics 로 보낸다(T-POLICY P4).</summary>
    internal IUiIconRegistry ViewFor(string owner, UiDiagnostics builderSink, UiBuildSession session)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(builderSink);
        ArgumentNullException.ThrowIfNull(session);
        return new View(this, owner, builderSink, session);
    }

    /// <summary>키를 해석할 수 있으면 true(진단 없음). 명령 아이콘 검사용.</summary>
    internal bool CanResolve(string key) => !string.IsNullOrWhiteSpace(key) && Resolve(key, out _);

    /// <summary>해석 시 그리기 실패를 키당 1회 XKUI0204(Warning)로 앱 Diagnostics 에 남긴다.</summary>
    internal void ReportRenderFailure(string key, string reason)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(key) || !_renderFailed.Add(key))
            {
                return;
            }
        }

        _runtime.Report(new UiDiagnostic(UiDiagnosticIds.IconRenderFailed, UiDiagnosticSeverity.Warning,
            $"아이콘 '{key}' 를 그리지 못했다: {reason}", key)); // i18n-ok 개발자 진단
    }

    private bool Add(string owner, UiDiagnostics sink, string key, Action<UiIconOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(configure);
        if (string.IsNullOrWhiteSpace(key))
        {
            return Fail(sink, UiDiagnosticIds.IconKeyEmpty, "아이콘 키가 비어 있어 건너뛴다.", owner); // i18n-ok 개발자 진단
        }

        var options = new UiIconOptions();
        configure(options);
        var hasPath = !string.IsNullOrWhiteSpace(options.PathData);
        if (hasPath == !string.IsNullOrWhiteSpace(options.ResourceKey))
        {
            return Fail(sink, UiDiagnosticIds.IconSourceInvalid, $"아이콘 '{key}' 는 PathData·ResourceKey 중 정확히 하나가 있어야 한다.", owner); // i18n-ok 개발자 진단
        }

        var def = hasPath ? UiIconDefinition.FromPathData(key, options.PathData!) : UiIconDefinition.FromResource(key, options.ResourceKey!);
        def = (options.Description is null ? def : def.WithDescription(options.Description)).WithOwner(owner);
        return Apply(owner, sink, key, () =>
        {
            var exists = _explicit.ContainsKey(key);
            if (exists && !options.Replace)
            {
                return new Outcome(null, UiDiagnosticIds.IconDuplicate, $"아이콘 '{key}' 가 이미 명시 등록돼 있다. 첫 등록을 유지한다."); // i18n-ok 개발자 진단
            }

            // P1: Replace 인데 기존 명시 등록이 없으면 새로 등록(Added). 교체는 주체와 무관하게 허용하고 Owner 를 옮긴다(P2).
            _explicit[key] = def;
            return new Outcome(exists ? UiRegistryChangeKind.Replaced : UiRegistryChangeKind.Added);
        });
    }

    private bool Remove(string owner, UiDiagnostics sink, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Apply(owner, sink, key, () =>
        {
            if (!_explicit.TryGetValue(key, out var current))
            {
                return default; // 명시 등록만 지운다. 공급자 키만 있으면 false(진단 없음).
            }

            if (!string.Equals(current.Owner, owner, StringComparison.Ordinal))
            {
                return new Outcome(null, UiDiagnosticIds.IconRemoveDenied, $"아이콘 '{key}' 는 '{current.Owner}' 가 등록해 지울 수 없다."); // i18n-ok 개발자 진단
            }

            _explicit.Remove(key);
            return new Outcome(UiRegistryChangeKind.Removed);
        });
    }

    private bool AddProvider(string owner, UiDiagnostics sink, UiIconProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var id = provider.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            return Fail(sink, UiDiagnosticIds.IconProviderRejected, "아이콘 공급자 Id 가 비어 있어 등록하지 않는다.", owner); // i18n-ok 개발자 진단
        }

        if (!UiRegistryOwners.IsFramework(owner) && id.StartsWith(UiRegistryOwners.ReservedPrefix, StringComparison.Ordinal))
        {
            return Fail(sink, UiDiagnosticIds.IconProviderIdReserved, $"아이콘 공급자 Id '{id}' 는 프레임워크 예약 접두어({UiRegistryOwners.ReservedPrefix})를 쓴다.", owner); // i18n-ok 개발자 진단
        }

        return Apply(owner, sink, id, () =>
        {
            if (IndexOf(id) >= 0)
            {
                return new Outcome(null, UiDiagnosticIds.IconProviderRejected, $"아이콘 공급자 Id '{id}' 가 이미 등록돼 있다."); // i18n-ok 개발자 진단
            }

            _providers.Insert(0, new ProviderEntry(provider, owner));
            return new Outcome(UiRegistryChangeKind.ProviderAdded);
        });
    }

    private bool RemoveProvider(string owner, UiDiagnostics sink, string providerId)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        return Apply(owner, sink, providerId, () =>
        {
            var index = IndexOf(providerId);
            if (index < 0)
            {
                return default;
            }

            var holder = _providers[index].Owner;
            if (!string.Equals(holder, owner, StringComparison.Ordinal))
            {
                return new Outcome(null, UiDiagnosticIds.IconRemoveDenied, $"아이콘 공급자 '{providerId}' 는 '{holder}' 가 등록해 지울 수 없다."); // i18n-ok 개발자 진단
            }

            _providers.RemoveAt(index);
            return new Outcome(UiRegistryChangeKind.ProviderRemoved);
        });
    }

    // 잠금 안에서 변경을 시도한다. 거부면 잠금 밖에서 진단 + false, 변경이면 잠금 밖에서 Changed + true, 대상 없음이면 false.
    private bool Apply(string owner, UiDiagnostics sink, string id, Func<Outcome> attempt)
    {
        Outcome result;
        lock (_gate)
        {
            result = attempt();
        }

        if (result.Code is not null)
        {
            return Fail(sink, result.Code, result.Message ?? result.Code, owner);
        }

        if (result.Kind is not { } kind)
        {
            return false;
        }

        Changed?.Invoke(Runtime, new UiRegistryChangedEventArgs(kind, id)); // 구독자 예외는 삼키지 않는다.
        return true;
    }

    private bool Resolve(string key, [NotNullWhen(true)] out UiIconDefinition? icon)
    {
        ProviderEntry[] providers;
        lock (_gate)
        {
            if (_explicit.TryGetValue(key, out icon))
            {
                return true;
            }

            providers = _providers.ToArray();
        }

        foreach (var entry in providers)
        {
            try
            {
                if (entry.Provider.TryGetIcon(key, out var found) && found is not null)
                {
                    icon = found.WithOwner(entry.Provider.Id);
                    return true;
                }
            }
            catch (Exception ex)
            {
                // 공급자 예외는 해석 실패로 남기고 다음 공급자로 넘어간다.
                ReportRenderFailure(key, $"공급자 '{entry.Provider.Id}' 예외 {ex.GetType().Name}"); // i18n-ok 개발자 진단
            }
        }

        icon = null;
        return false;
    }

    private IReadOnlyList<string> SnapshotKeys()
    {
        ProviderEntry[] providers;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            keys.UnionWith(_explicit.Keys);
            providers = _providers.ToArray();
        }

        foreach (var entry in providers)
        {
            try
            {
                keys.UnionWith(entry.Provider.Keys);
            }
            catch (Exception ex)
            {
                ReportRenderFailure(entry.Provider.Id, $"공급자 키 목록 예외 {ex.GetType().Name}"); // i18n-ok 개발자 진단
            }
        }

        var result = new string[keys.Count];
        keys.CopyTo(result);
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    private UiIconProvider[] SnapshotProviders()
    {
        lock (_gate)
        {
            return _providers.ConvertAll(e => e.Provider).ToArray();
        }
    }

    private int IndexOf(string providerId) => _providers.FindIndex(e => string.Equals(e.Provider.Id, providerId, StringComparison.Ordinal));

    private static bool Fail(UiDiagnostics sink, string id, string message, string owner)
    {
        sink.Report(new UiDiagnostic(id, UiDiagnosticSeverity.Error, message, owner));
        return false;
    }

    private readonly record struct ProviderEntry(UiIconProvider Provider, string Owner);

    // Kind 가 있으면 변경, Code 가 있으면 거부(진단), 둘 다 없으면(default) 대상 없음.
    private readonly record struct Outcome(UiRegistryChangeKind? Kind, string? Code = null, string? Message = null);

    /// <summary>등록 주체·진단 싱크를 고정한 보기. 같은 저장소를 공유한다.</summary>
    private sealed class View(UiIconStore store, string owner, UiDiagnostics builderSink, UiBuildSession? session) : IUiIconRegistry
    {
        public IReadOnlyList<string> Keys => store.SnapshotKeys();

        public IReadOnlyList<UiIconProvider> Providers => store.SnapshotProviders();

        public event EventHandler<UiRegistryChangedEventArgs>? Changed
        {
            add => store.Changed += value;
            remove => store.Changed -= value;
        }

        // P4: 실행 중(앱 보기) 또는 Build 완료 뒤에는 앱 Diagnostics, Build 도중에는 빌더 Diagnostics.
        private UiDiagnostics Sink => session is null || session.IsCompleted ? store._runtime : builderSink;

        public bool Add(string key, Action<UiIconOptions> configure) => store.Add(owner, Sink, key, configure);

        public bool AddGeometry(string key, string pathData) => store.Add(owner, Sink, key, o => o.PathData = pathData);

        public bool Remove(string key) => store.Remove(owner, Sink, key);

        public bool Contains(string key) => store.Resolve(key ?? throw new ArgumentNullException(nameof(key)), out _);

        public bool TryGet(string key, [NotNullWhen(true)] out UiIconDefinition? icon) =>
            store.Resolve(key ?? throw new ArgumentNullException(nameof(key)), out icon);

        public bool AddProvider(UiIconProvider provider) => store.AddProvider(owner, Sink, provider);

        public bool RemoveProvider(string providerId) => store.RemoveProvider(owner, Sink, providerId);
    }
}

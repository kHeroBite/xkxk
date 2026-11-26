// 서비스 등록부 — 형식 키로 인스턴스·팩토리를 등록하고 리플렉션 없이 조회한다(UADR-0009 원칙 3, AOT·WASM 안전)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 서비스 공급자 등록부. 같은 형식의 중복 Add 는 XKUI0101(Warning) 진단을 남기고 첫 등록을 유지한다.
/// </summary>
/// <remarks>
/// 팩토리 등록은 첫 <c>Get</c>/<c>TryGet</c> 때 1회만 호출한다(스레드 안전). 리플렉션·동적 코드를 쓰지 않는다.
/// 확장 규칙: 뒤 batch 의 키 조회·여러 개 등록은 새 메서드로 추가한다(기존 서명 불변).
/// </remarks>
public sealed class UiServiceRegistry : IUiServiceProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, Entry> _entries = new();
    private readonly UiDiagnostics _diagnostics;

    /// <summary>자체 진단 모음으로 등록부를 만든다.</summary>
    public UiServiceRegistry()
        : this(new UiDiagnostics())
    {
    }

    /// <summary>진단을 지정한 모음에 보고하는 등록부를 만든다.</summary>
    /// <param name="diagnostics">진단 모음. null 이면 <see cref="ArgumentNullException"/>.</param>
    public UiServiceRegistry(UiDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics;
    }

    /// <summary>등록된 서비스 형식 수.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>인스턴스를 등록한다. 이미 있으면 XKUI0101 Warning, 첫 등록 유지.</summary>
    public UiServiceRegistry Add<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        return AddCore(typeof(TService), new Entry(instance, null));
    }

    /// <summary>팩토리를 등록한다(첫 조회 때 1회 호출). 이미 있으면 XKUI0101 Warning, 첫 등록 유지.</summary>
    public UiServiceRegistry Add<TService>(Func<IUiServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return AddCore(typeof(TService), new Entry(null, factory));
    }

    /// <summary>인스턴스로 등록을 바꾼다(없으면 추가).</summary>
    public UiServiceRegistry Replace<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        lock (_gate)
        {
            _entries[typeof(TService)] = new Entry(instance, null);
        }

        return this;
    }

    /// <summary>팩토리로 등록을 바꾼다(없으면 추가).</summary>
    public UiServiceRegistry Replace<TService>(Func<IUiServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_gate)
        {
            _entries[typeof(TService)] = new Entry(null, factory);
        }

        return this;
    }

    /// <summary>등록을 지운다. 지웠으면 true.</summary>
    public bool Remove<TService>() where TService : class
    {
        lock (_gate)
        {
            return _entries.Remove(typeof(TService));
        }
    }

    /// <inheritdoc />
    public bool Contains<TService>() where TService : class
    {
        lock (_gate)
        {
            return _entries.ContainsKey(typeof(TService));
        }
    }

    /// <inheritdoc />
    public bool TryGet<TService>([MaybeNullWhen(false)] out TService service) where TService : class
    {
        Entry? entry;
        lock (_gate)
        {
            _entries.TryGetValue(typeof(TService), out entry);
        }

        if (entry is null)
        {
            service = null;
            return false;
        }

        service = (TService)entry.Resolve(this, typeof(TService));
        return true;
    }

    /// <inheritdoc />
    public TService Get<TService>() where TService : class
    {
        if (TryGet<TService>(out var service))
        {
            return service;
        }

        throw new InvalidOperationException($"서비스가 등록되지 않았습니다: {typeof(TService).FullName}");
    }

    private UiServiceRegistry AddCore(Type key, Entry entry)
    {
        bool duplicate;
        lock (_gate)
        {
            duplicate = !_entries.TryAdd(key, entry);
        }

        if (duplicate)
        {
            _diagnostics.Report(new UiDiagnostic(
                UiDiagnosticIds.ServiceDuplicate,
                UiDiagnosticSeverity.Warning,
                $"서비스가 이미 등록되어 첫 등록을 유지합니다: {key.FullName}"));
        }

        return this;
    }

    /// <summary>등록 1건 — 인스턴스 또는 지연 팩토리.</summary>
    private sealed class Entry
    {
        private readonly object _resolveGate = new();
        private readonly Delegate? _factory;
        private object? _instance;

        internal Entry(object? instance, Delegate? factory)
        {
            _instance = instance;
            _factory = factory;
        }

        internal object Resolve(IUiServiceProvider provider, Type serviceType)
        {
            lock (_resolveGate)
            {
                if (_instance is not null)
                {
                    return _instance;
                }

                var created = ((Func<IUiServiceProvider, object?>)_factory!).Invoke(provider);
                _instance = created ?? throw new InvalidOperationException(
                    $"서비스 팩토리가 null 을 돌려주었습니다: {serviceType.FullName}");
                return _instance;
            }
        }
    }
}

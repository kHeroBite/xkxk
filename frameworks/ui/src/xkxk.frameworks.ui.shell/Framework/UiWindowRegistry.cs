// 창 레지스트리 내부 저장소(UiWindowStore)·등록 주체별 보기·열기(Open) — 진단 XKUI0501~0506·0508·정책 P1~P4(UADR-0013 과 같음, a10). 규칙 검사는 .Rules.cs
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 앱 컨텍스트가 1개 소유하는 창 종류 저장소. 앱(<see cref="Runtime"/>)과 모듈(<see cref="ViewFor"/>)은 같은 저장소를
/// 등록 주체(Owner)·진단 싱크만 바꿔 본다. 생성 때 셸 기본 창(<see cref="ShellDefaultWindows"/>)을 등록한다.
/// </summary>
/// <remarks>
/// <para>잠금은 1개이며 구성 대리자 호출·Changed 발생·진단 보고·셸 호출은 잠금 밖에서 한다.
/// 열기 진단(XKUI0505·0506)은 늘 앱 Diagnostics 로 간다.</para>
/// <para>ID 비교는 Ordinal, 별칭은 OrdinalIgnoreCase 다. <see cref="Resolve"/> 는 ID → 별칭 → 옛 이름·숫자
/// (<c>LegacyWindowKinds.ToId</c> 가 준 ID 로 재탐색) 순서로 본다. 옛 이름·숫자 해석은 정의 Aliases 와 무관한 영구 별칭이다(a10 §2·§3).</para>
/// </remarks>
internal sealed partial class UiWindowStore
{
    private readonly object _gate = new();
    private readonly IUiAppContext _app;
    private readonly UiDiagnostics _runtime;
    private readonly List<UiWindowDefinition> _items = new();

    internal UiWindowStore(IUiAppContext app, UiDiagnostics runtime)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Runtime = new View(this, UiRegistryOwners.App, runtime, null);
        // 셸 기본 창은 프레임워크 주체(xkxk.shell)·실행 중 진단으로 등록한다. 다른 저장소를 부르지 않는다(재진입 없음).
        ShellDefaultWindows.Register(new View(this, UiRegistryOwners.Shell, runtime, null));
    }

    /// <summary>앱 수준 보기(Owner <see cref="UiRegistryOwners.App"/>, 진단은 앱 Diagnostics).</summary>
    internal IUiWindowRegistry Runtime { get; }

    private event EventHandler<UiRegistryChangedEventArgs>? Changed;

    /// <summary>모듈용 보기. Build 가 끝난 뒤(session.IsCompleted)에는 실행 중 등록과 같다(T-POLICY P4).</summary>
    internal IUiWindowRegistry ViewFor(string owner, UiDiagnostics builderSink, UiBuildSession session) =>
        new View(this, owner ?? throw new ArgumentNullException(nameof(owner)),
            builderSink ?? throw new ArgumentNullException(nameof(builderSink)), session ?? throw new ArgumentNullException(nameof(session)));

    /// <summary>ID(Ordinal)로만 찾는다. 별칭은 보지 않는다.</summary>
    internal UiWindowDefinition? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate) { var i = IndexOf(id); return i < 0 ? null : _items[i]; }
    }

    /// <summary>
    /// ID(Ordinal) → 별칭(OrdinalIgnoreCase) → 옛 이름·숫자 순서로 찾는다. 마지막 단계는 <c>LegacyWindowKinds.ToId</c> 가 준
    /// ID(Ordinal)로 다시 찾으므로, 셸 기본 창을 별칭 없이 교체해도 Grid·Gantt·Form·"0"·"1"·"2" 가 풀린다.
    /// </summary>
    internal UiWindowDefinition? Resolve(string idOrAlias)
    {
        ArgumentNullException.ThrowIfNull(idOrAlias);
        lock (_gate)
        {
            var i = IndexOf(idOrAlias);
            if (i >= 0)
            {
                return _items[i];
            }

            if (_items.Find(w => HasAlias(w, idOrAlias)) is { } byAlias)
            {
                return byAlias;
            }

            // 옛 이름·숫자는 예약어(Rules.cs)라 셸 밖 창이 위 두 단계에서 가로채지 못한다.
            var legacy = LegacyWindowKinds.ToId(idOrAlias);
            i = legacy is null ? -1 : IndexOf(legacy);
            return i < 0 ? null : _items[i];
        }
    }

    /// <summary>
    /// 창을 연다. 못 찾으면 false+XKUI0505(Warning), 셸이 없으면 false+XKUI0506(Warning).
    /// 찾으면 정규 ID 와 옵션으로 <c>IShellCommands.OpenDocument(string, UiWindowOpenOptions)</c> 를 부르고 true.
    /// 배치·중복 허용의 최종 판정(옵션 ?? 정의·현재 설정)은 셸이 한다(I8).
    /// </summary>
    private bool Open(string owner, string idOrAlias, Action<UiWindowOpenOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(idOrAlias);
        var options = new UiWindowOpenOptions();
        configure?.Invoke(options);
        if (Resolve(idOrAlias) is not { } window)
        {
            return Warn(UiDiagnosticIds.WindowNotRegistered, $"등록되지 않은 창 '{idOrAlias}' 을 열려 했다.", owner); // i18n-ok 개발자 진단
        }

        if (_app.Shell is not { } shell)
        {
            return Warn(UiDiagnosticIds.WindowShellUnavailable, $"셸이 없어 창 '{window.Id}' 을 열 수 없다.", owner); // i18n-ok 개발자 진단
        }

        shell.OpenDocument(window.Id, options);
        return true;
    }

    private int IndexOf(string id) =>
        _items.FindIndex(w => string.Equals(w.Id, id, StringComparison.Ordinal));

    private IReadOnlyList<UiWindowDefinition> Snapshot()
    {
        lock (_gate) { return _items.ToArray(); }
    }

    private bool Warn(string id, string message, string owner)
    {
        _runtime.Report(new UiDiagnostic(id, UiDiagnosticSeverity.Warning, message, owner));
        return false;
    }

    private void Raise(UiRegistryChangeKind kind, string id) =>
        Changed?.Invoke(Runtime, new UiRegistryChangedEventArgs(kind, id)); // 잠금 밖. 구독자 예외는 삼키지 않는다.

    /// <summary>등록 주체·진단 싱크를 고정한 보기. 같은 저장소를 공유한다.</summary>
    private sealed class View : IUiWindowRegistry
    {
        private readonly UiWindowStore _store;
        private readonly string _owner;
        private readonly UiDiagnostics _builderSink;
        private readonly UiBuildSession? _session;

        internal View(UiWindowStore store, string owner, UiDiagnostics builderSink, UiBuildSession? session)
        {
            _store = store;
            _owner = owner;
            _builderSink = builderSink;
            _session = session;
        }

        public IReadOnlyList<UiWindowDefinition> Items => _store.Snapshot();

        public event EventHandler<UiRegistryChangedEventArgs>? Changed
        {
            add => _store.Changed += value;
            remove => _store.Changed -= value;
        }

        // P4: 실행 중(세션 없음) 또는 Build 완료 뒤에는 앱 Diagnostics, Build 도중에는 빌더 Diagnostics.
        private UiDiagnostics Sink => _session is null || _session.IsCompleted ? _store._runtime : _builderSink;

        public bool Add(string id, Action<UiWindowOptions> configure) => _store.Add(_owner, Sink, id, configure);

        public bool Remove(string id) => _store.Remove(_owner, Sink, id);

        public bool Contains(string id) => _store.Find(id) is not null;

        public bool TryGet(string id, [NotNullWhen(true)] out UiWindowDefinition? window)
        {
            window = _store.Find(id);
            return window is not null;
        }

        public bool TryResolve(string idOrAlias, [NotNullWhen(true)] out UiWindowDefinition? window)
        {
            window = _store.Resolve(idOrAlias);
            return window is not null;
        }

        public bool Open(string id) => _store.Open(_owner, id, null);

        public bool Open(string id, Action<UiWindowOpenOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            return _store.Open(_owner, id, configure);
        }
    }
}

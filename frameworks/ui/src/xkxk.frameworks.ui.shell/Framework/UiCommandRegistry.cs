// 명령 레지스트리 내부 저장소(UiCommandStore)·등록 주체별 보기·빌드 세션(UiBuildSession) — 진단 XKUI0301~0309(단축키 0305·0310 은 .Gesture.cs)·정책 P1~P4(UADR-0013)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 앱 컨텍스트가 1개 소유하는 명령 저장소. 앱(<see cref="Runtime"/>)과 모듈(<see cref="ViewFor"/>)은 같은 저장소를
/// 등록 주체(Owner)·진단 싱크만 바꿔 본다. 생성 때 셸 기본 명령(<see cref="ShellDefaultCommands"/>)을 등록한다.
/// </summary>
/// <remarks>
/// 잠금은 1개이며 대리자 호출·Changed 발생·진단 보고는 잠금 밖에서 한다. 실행 진단(XKUI0306·0307)은 늘 앱 Diagnostics 로 간다.
/// </remarks>
internal sealed partial class UiCommandStore
{
    private readonly object _gate = new();
    private readonly IUiAppContext _app;
    private readonly UiIconStore _icons;
    private readonly UiDiagnostics _runtime;
    private readonly List<Entry> _entries = new();

    internal UiCommandStore(IUiAppContext app, UiIconStore icons, UiDiagnostics runtime)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Runtime = new View(this, UiRegistryOwners.App, runtime, null);
        // 셸 기본 명령은 프레임워크 주체(xkxk.shell)·실행 중 진단으로 등록한다. 아이콘은 전달받은 저장소로 즉시 검사(재진입 없음).
        ShellDefaultCommands.Register(new View(this, UiRegistryOwners.Shell, runtime, null));
    }

    /// <summary>앱 수준 보기(Owner <see cref="UiRegistryOwners.App"/>, 진단은 앱 Diagnostics).</summary>
    internal IUiCommandRegistry Runtime { get; }

    private event EventHandler<UiRegistryChangedEventArgs>? Changed;

    /// <summary>모듈용 보기. Build 가 끝난 뒤(session.IsCompleted)에는 실행 중 등록과 같다(T-POLICY P4).</summary>
    internal IUiCommandRegistry ViewFor(string owner, UiDiagnostics builderSink, UiBuildSession session) =>
        new View(this, owner ?? throw new ArgumentNullException(nameof(owner)),
            builderSink ?? throw new ArgumentNullException(nameof(builderSink)), session ?? throw new ArgumentNullException(nameof(session)));

    private bool Add(string owner, UiDiagnostics sink, UiBuildSession? session, string id, Action<UiCommandOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(configure);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Fail(sink, UiDiagnosticIds.CommandIdEmpty, "명령 ID 가 비어 있어 건너뛴다.", owner); // i18n-ok 개발자 진단
        }

        var o = new UiCommandOptions();
        configure(o);
        if (o.Execute is null)
        {
            return Fail(sink, UiDiagnosticIds.CommandExecuteMissing, $"명령 '{id}' 에 Execute 가 없어 등록하지 않는다.", owner); // i18n-ok 개발자 진단
        }

        var def = new UiCommandDefinition(id, o, UiGestureText.IsValid(o.Gesture) ? o.Gesture : null, owner);
        var entry = new Entry(def, o.Execute, o.CanExecute);
        var reserved = !UiRegistryOwners.IsFramework(owner) && id.StartsWith(UiRegistryOwners.ReservedPrefix, StringComparison.Ordinal);
        int index;
        UiCommandDefinition? conflict;
        lock (_gate)
        {
            // 중복(Replace 아님)·예약(T-RESERVED ①: 없는 xkxk.* ID 를 새로 만듦, Replace 무관)은 등록하지 않는다.
            index = IndexOf(id);
            if (index >= 0 && o.Replace)
            {
                _entries[index] = entry; // P3 제자리 교체, P2 교체는 주체 무관·Owner 이동.
            }
            else if (index < 0 && !reserved)
            {
                _entries.Add(entry); // P1 Replace 인데 없으면 새로 등록(Added).
            }

            conflict = FindGestureConflict(def); // 등록을 거부하면 아래에서 먼저 돌아가 쓰지 않는다.
        }

        if (index >= 0 && !o.Replace)
        {
            return Fail(sink, UiDiagnosticIds.CommandDuplicate, $"명령 '{id}' 가 이미 등록돼 있다. 첫 등록을 유지한다.", owner); // i18n-ok 개발자 진단
        }

        if (index < 0 && reserved)
        {
            return Fail(sink, UiDiagnosticIds.CommandIdReserved, $"명령 ID '{id}' 는 프레임워크 예약 접두어({UiRegistryOwners.ReservedPrefix})를 쓴다.", owner); // i18n-ok 개발자 진단
        }

        ReportGesture(sink, def, o.Gesture, conflict);

        if (o.Icon is not null && (session is null || !session.Defer(id, o.Icon, owner)) && !_icons.CanResolve(o.Icon))
        {
            ReportIconMissing(sink, id, o.Icon, owner);
        }

        Raise(index >= 0 ? UiRegistryChangeKind.Replaced : UiRegistryChangeKind.Added, id);
        return true;
    }

    private bool Remove(string owner, UiDiagnostics sink, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        string? holder;
        lock (_gate)
        {
            var index = IndexOf(id);
            holder = index < 0 ? null : _entries[index].Definition.Owner;
            if (holder == owner)
            {
                _entries.RemoveAt(index); // P2: 등록한 주체만 지운다(Ordinal).
            }
        }

        if (holder is null)
        {
            return false;
        }

        if (holder != owner)
        {
            return Fail(sink, UiDiagnosticIds.CommandRemoveDenied, $"명령 '{id}' 는 '{holder}' 가 등록해 지울 수 없다.", owner); // i18n-ok 개발자 진단
        }

        Raise(UiRegistryChangeKind.Removed, id);
        return true;
    }

    private bool CanExecute(string id, object? parameter) =>
        Find(id) is { } entry && Allowed(entry, new UiCommandContext(_app, id, parameter));

    private bool Execute(string owner, string id, object? parameter)
    {
        if (Find(id) is not { } entry)
        {
            _runtime.Report(new UiDiagnostic(UiDiagnosticIds.CommandNotFound, UiDiagnosticSeverity.Warning,
                $"없는 명령 '{id}' 을 실행하려 했다.", owner)); // i18n-ok 개발자 진단
            return false;
        }

        var ctx = new UiCommandContext(_app, id, parameter);
        try
        {
            var run = entry.CanExecute is null || entry.CanExecute(ctx); // false 면 진단 없이 false.
            if (run)
            {
                entry.Execute(ctx);
            }

            return run;
        }
        catch (Exception ex)
        {
            return ReportFailed(entry, ex);
        }
    }

    private bool Allowed(Entry entry, UiCommandContext ctx)
    {
        try
        {
            return entry.CanExecute is null || entry.CanExecute(ctx);
        }
        catch (Exception ex)
        {
            return ReportFailed(entry, ex);
        }
    }

    private bool ReportFailed(Entry entry, Exception ex) =>
        Fail(_runtime, UiDiagnosticIds.CommandFailed,
            $"명령 '{entry.Definition.Id}' 실행 중 예외 {ex.GetType().FullName}: {ex.Message}", entry.Definition.Owner); // i18n-ok 개발자 진단

    private Entry? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate) { var i = IndexOf(id); return i < 0 ? null : _entries[i]; }
    }

    private int IndexOf(string id) =>
        _entries.FindIndex(e => string.Equals(e.Definition.Id, id, StringComparison.Ordinal));

    private IReadOnlyList<UiCommandDefinition> Snapshot()
    {
        lock (_gate) { return _entries.ConvertAll(e => e.Definition).ToArray(); }
    }

    internal static void ReportIconMissing(UiDiagnostics sink, string id, string icon, string owner) =>
        Fail(sink, UiDiagnosticIds.CommandIconMissing, $"명령 '{id}' 의 아이콘 '{icon}' 를 해석하지 못했다. 아이콘 없이 표시한다.", owner); // i18n-ok 개발자 진단

    private static bool Fail(UiDiagnostics sink, string id, string message, string owner)
    {
        sink.Report(new UiDiagnostic(id, UiDiagnosticSeverity.Error, message, owner)); return false;
    }

    private void Raise(UiRegistryChangeKind kind, string id) =>
        Changed?.Invoke(Runtime, new UiRegistryChangedEventArgs(kind, id)); // 잠금 밖. 구독자 예외는 삼키지 않는다.

    private sealed record Entry(UiCommandDefinition Definition, Action<UiCommandContext> Execute, Func<UiCommandContext, bool>? CanExecute);

    /// <summary>등록 주체·진단 싱크를 고정한 보기. 같은 저장소를 공유한다.</summary>
    private sealed class View : IUiCommandRegistry
    {
        private readonly UiCommandStore _store;
        private readonly string _owner;
        private readonly UiDiagnostics _builderSink;
        private readonly UiBuildSession? _session;

        internal View(UiCommandStore store, string owner, UiDiagnostics builderSink, UiBuildSession? session)
        {
            _store = store;
            _owner = owner;
            _builderSink = builderSink;
            _session = session;
        }

        public IReadOnlyList<UiCommandDefinition> Commands => _store.Snapshot();

        public event EventHandler<UiRegistryChangedEventArgs>? Changed
        {
            add => _store.Changed += value;
            remove => _store.Changed -= value;
        }

        // P4: 실행 중(세션 없음) 또는 Build 완료 뒤에는 앱 Diagnostics·즉시 검사, Build 도중에는 빌더 Diagnostics·지연 검사.
        private UiBuildSession? LiveSession => _session is null || _session.IsCompleted ? null : _session;

        private UiDiagnostics Sink => LiveSession is null ? _store._runtime : _builderSink;

        public bool Add(string id, Action<UiCommandOptions> configure) => _store.Add(_owner, Sink, LiveSession, id, configure);

        public bool Remove(string id) => _store.Remove(_owner, Sink, id);

        public bool Contains(string id) => _store.Find(id) is not null;

        public bool TryGet(string id, [NotNullWhen(true)] out UiCommandDefinition? command)
        {
            command = _store.Find(id)?.Definition;
            return command is not null;
        }

        public bool CanExecute(string id) => _store.CanExecute(id, null);

        public bool CanExecute(string id, object? parameter) => _store.CanExecute(id, parameter);

        public bool Execute(string id) => _store.Execute(_owner, id, null);

        public bool Execute(string id, object? parameter) => _store.Execute(_owner, id, parameter);
    }
}

/// <summary>빌더 1회분 세션. Build 도중 모듈 명령의 아이콘 검사를 모았다가 Build 끝에 한 번에 한다(모듈 순서 무관).</summary>
internal sealed class UiBuildSession
{
    private readonly object _gate = new();
    private readonly List<(string Id, string Icon, string Owner)> _pending = new();
    private bool _completed;

    /// <summary>Build 가 끝났으면 true. 이후 등록은 실행 중 등록과 같다(T-POLICY P4).</summary>
    internal bool IsCompleted
    {
        get { lock (_gate) { return _completed; } }
    }

    /// <summary>아이콘 검사를 미룬다. 이미 완료된 세션이면 false(호출자가 즉시 검사).</summary>
    internal bool Defer(string commandId, string icon, string owner)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _pending.Add((commandId, icon, owner));
            }

            return !_completed;
        }
    }

    /// <summary>미뤄 둔 아이콘을 검사해 해석 못 한 것마다 XKUI0304 를 sink 에 남기고 세션을 완료한다.</summary>
    internal void Complete(UiIconStore icons, UiDiagnostics sink)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(sink);
        (string Id, string Icon, string Owner)[] pending;
        lock (_gate)
        {
            pending = _pending.ToArray();
            _pending.Clear();
            _completed = true;
        }

        foreach (var p in Array.FindAll(pending, x => !icons.CanResolve(x.Icon)))
        {
            UiCommandStore.ReportIconMissing(sink, p.Id, p.Icon, p.Owner);
        }
    }
}

// 메뉴 레지스트리 내부 저장소(UiMenuStore)·등록 주체별 보기 — 등록·제거·이동·숨김, 진단 XKUI0401~0411·정책 P1~P4(UADR-0014)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 앱 컨텍스트가 1개 소유하는 메뉴 저장소. 앱(<see cref="Runtime"/>)과 모듈(<see cref="ViewFor"/>)은 같은 저장소를
/// 등록 주체(Owner)·진단 싱크만 바꿔 본다. 생성 때 셸 기본 메뉴(<see cref="ShellDefaultMenus"/>)를 등록한다.
/// </summary>
/// <remarks>
/// 항목은 평면 목록 1개에 부모 ID 와 함께 둔다. 형제 순서는 목록 안 순서다. 잠금은 1개이며 옵션 대리자·조건 대리자 호출,
/// Changed 발생, 진단 보고, 명령 레지스트리 조회는 잠금 밖에서 한다. 조건 대리자 진단(XKUI0411)은 늘 앱 Diagnostics 로 간다.
/// </remarks>
internal sealed partial class UiMenuStore
{
    private readonly object _gate = new();
    private readonly IUiAppContext _app;
    private readonly UiDiagnostics _runtime;
    private readonly List<UiMenuItemDefinition> _items = new();
    private readonly HashSet<string> _conditionReported = new(StringComparer.Ordinal);
    private readonly Dictionary<UiBuildSession, List<(string Id, string CommandId, string Owner)>> _pending = new();

    internal UiMenuStore(IUiAppContext app, UiDiagnostics runtime)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Runtime = new View(this, UiRegistryOwners.App, runtime, null);
        // 셸 기본 메뉴는 프레임워크 주체(xkxk.shell)·실행 중 진단으로 등록한다.
        ShellDefaultMenus.Register(new View(this, UiRegistryOwners.Shell, runtime, null), app);
    }

    /// <summary>앱 수준 보기(Owner <see cref="UiRegistryOwners.App"/>, 진단은 앱 Diagnostics).</summary>
    internal IUiMenuRegistry Runtime { get; }

    private event EventHandler<UiRegistryChangedEventArgs>? Changed;

    /// <summary>모듈용 보기. Build 가 끝난 뒤(session.IsCompleted)에는 실행 중 등록과 같다(P4).</summary>
    internal IUiMenuRegistry ViewFor(string owner, UiDiagnostics builderSink, UiBuildSession session) =>
        new View(this, owner ?? throw new ArgumentNullException(nameof(owner)),
            builderSink ?? throw new ArgumentNullException(nameof(builderSink)), session ?? throw new ArgumentNullException(nameof(session)));

    private bool Add(string owner, UiDiagnostics sink, UiBuildSession? session, string id, Action<UiMenuItemOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(configure);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Fail(sink, UiDiagnosticIds.MenuIdEmpty, "메뉴 ID 가 비어 있어 건너뛴다.", owner); // i18n-ok 개발자 진단
        }

        var o = new UiMenuItemOptions();
        configure(o);
        if (KindError(o) is { } kindError)
        {
            return Fail(sink, UiDiagnosticIds.MenuKindInvalid, $"메뉴 '{id}' 의 종류({o.Kind})와 속성이 맞지 않는다: {kindError}", owner); // i18n-ok 개발자 진단
        }

        var def = new UiMenuItemDefinition(id, o, owner);
        var reserved = !UiRegistryOwners.IsFramework(owner) && UiRegistryOwners.IsReserved(id);
        Outcome outcome;
        lock (_gate)
        {
            outcome = AddLocked(def, o, reserved);
        }

        if (outcome.Code is not null)
        {
            return Fail(sink, outcome.Code, $"메뉴 '{id}': {outcome.Message}", owner); // i18n-ok 개발자 진단
        }

        if (outcome.AnchorMissing)
        {
            Warn(sink, UiDiagnosticIds.MenuAnchorMissing, $"메뉴 '{id}' 의 기준 항목 '{o.InsertAfter ?? o.InsertBefore}' 가 같은 부모 아래 없어 맨 뒤에 붙인다.", owner); // i18n-ok 개발자 진단
        }

        if (def.Kind == UiMenuItemKind.Command && def.CommandId is { } cmd && !DeferCommand(session, id, cmd, owner) && !_app.Commands.Contains(cmd))
        {
            ReportCommandMissing(sink, id, cmd, owner);
        }

        Raise(outcome.Replaced ? UiRegistryChangeKind.Replaced : UiRegistryChangeKind.Added, id);
        return true;
    }

    private Outcome AddLocked(UiMenuItemDefinition def, UiMenuItemOptions o, bool reserved)
    {
        var index = IndexOf(def.Id);
        if (index >= 0 && !o.Replace)
        {
            return Outcome.Error(UiDiagnosticIds.MenuDuplicate, "이미 등록돼 있다. 첫 등록을 유지한다."); // i18n-ok 개발자 진단
        }

        if (index < 0 && reserved)
        {
            return Outcome.Error(UiDiagnosticIds.MenuIdReserved, $"프레임워크 예약 접두어({UiRegistryOwners.ReservedPrefix})를 쓰는 새 ID 다."); // i18n-ok 개발자 진단
        }

        if (def.Parent is { } parent && !ValidParent(parent, def.Id))
        {
            return Outcome.Error(UiDiagnosticIds.MenuParentMissing, $"부모 '{parent}' 가 없거나 하위 메뉴가 아니다."); // i18n-ok 개발자 진단
        }

        if (index >= 0 && def.Kind != UiMenuItemKind.Submenu && HasChildren(def.Id))
        {
            return Outcome.Error(UiDiagnosticIds.MenuKindInvalid, "자식이 있는 하위 메뉴를 다른 종류로 바꿀 수 없다."); // i18n-ok 개발자 진단
        }

        if (index >= 0)
        {
            ForgetConditionReports(def.Id);
            if (string.Equals(_items[index].Parent, def.Parent, StringComparison.Ordinal))
            {
                _items[index] = def; // P3 제자리 교체, 주체 무관·Owner 이동.
                return new Outcome(true, false);
            }

            _items.RemoveAt(index); // 부모가 바뀐 교체는 새 부모 아래 지정 자리로 옮긴다.
        }

        return new Outcome(index >= 0, !Place(def, o.InsertAfter, o.InsertBefore));
    }

    private bool Remove(string owner, UiDiagnostics sink, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        string? denied = null;
        lock (_gate)
        {
            if (IndexOf(id) < 0)
            {
                return false;
            }

            var subtree = Subtree(id);
            denied = subtree.Find(d => !string.Equals(d.Owner, owner, StringComparison.Ordinal))?.Owner;
            if (denied is null)
            {
                foreach (var d in subtree)
                {
                    _items.RemoveAt(IndexOf(d.Id)); // P2: 항목과 자식 전원이 같은 주체일 때만 자식째 지운다.
                    ForgetConditionReports(d.Id);
                }
            }
        }

        if (denied is not null)
        {
            return Fail(sink, UiDiagnosticIds.MenuRemoveDenied, $"메뉴 '{id}'(또는 그 자식)는 '{denied}' 가 등록해 지울 수 없다.", owner); // i18n-ok 개발자 진단
        }

        Raise(UiRegistryChangeKind.Removed, id);
        return true;
    }

    private bool Move(string owner, UiDiagnostics sink, string id, Action<UiMenuPlacementOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(configure);
        var p = new UiMenuPlacementOptions();
        configure(p);
        string? error = null;
        var anchorMissing = false;
        lock (_gate)
        {
            var index = IndexOf(id);
            var current = index < 0 ? null : _items[index];
            var parent = p.ToRoot ? null : p.Parent ?? current?.Parent;
            if (current is null)
            {
                error = "대상이 없다."; // i18n-ok 개발자 진단
            }
            else if (parent is not null && !ValidParent(parent, id))
            {
                error = $"새 부모 '{parent}' 가 없거나 하위 메뉴가 아니거나 자기 자신·자손이다."; // i18n-ok 개발자 진단
            }
            else
            {
                _items.RemoveAt(index); // Move 는 비파괴라 주체와 무관하게 허용, Owner 는 그대로다.
                anchorMissing = !Place(current.WithParent(parent), p.InsertAfter, p.InsertBefore);
            }
        }

        if (error is not null)
        {
            return Fail(sink, UiDiagnosticIds.MenuMoveInvalid, $"메뉴 '{id}' 를 옮기지 않는다: {error}", owner); // i18n-ok 개발자 진단
        }

        if (anchorMissing)
        {
            Warn(sink, UiDiagnosticIds.MenuAnchorMissing, $"메뉴 '{id}' 이동 기준 항목 '{p.InsertAfter ?? p.InsertBefore}' 가 없어 맨 뒤에 붙인다.", owner); // i18n-ok 개발자 진단
        }

        Raise(UiRegistryChangeKind.Moved, id);
        return true;
    }

    private bool SetHidden(string id, bool hidden)
    {
        ArgumentNullException.ThrowIfNull(id);
        bool changed;
        lock (_gate)
        {
            var index = IndexOf(id);
            if (index < 0)
            {
                return false;
            }

            changed = _items[index].IsHidden != hidden;
            _items[index] = _items[index].WithHidden(hidden); // 비파괴라 주체와 무관, Owner 는 그대로다.
        }

        if (changed)
        {
            Raise(UiRegistryChangeKind.VisibilityChanged, id);
        }

        return true;
    }

    private void Raise(UiRegistryChangeKind kind, string id) =>
        Changed?.Invoke(Runtime, new UiRegistryChangedEventArgs(kind, id)); // 잠금 밖. 구독자 예외는 삼키지 않는다.

    /// <summary>등록 주체·진단 싱크를 고정한 보기. 같은 저장소를 공유한다.</summary>
    private sealed class View : IUiMenuRegistry
    {
        private readonly UiMenuStore _store;
        private readonly string _owner;
        private readonly UiDiagnostics _builderSink;
        private readonly UiBuildSession? _session;

        internal View(UiMenuStore store, string owner, UiDiagnostics builderSink, UiBuildSession? session)
        {
            _store = store;
            _owner = owner;
            _builderSink = builderSink;
            _session = session;
        }

        public IReadOnlyList<UiMenuItemDefinition> Items => _store.Snapshot();

        public event EventHandler<UiRegistryChangedEventArgs>? Changed
        {
            add => _store.Changed += value;
            remove => _store.Changed -= value;
        }

        // P4: 실행 중(세션 없음) 또는 Build 완료 뒤에는 앱 Diagnostics·즉시 검사, Build 도중에는 빌더 Diagnostics·Build 끝 검사.
        private UiBuildSession? LiveSession => _session is null || _session.IsCompleted ? null : _session;

        private UiDiagnostics Sink => LiveSession is null ? _store._runtime : _builderSink;

        public bool Add(string id, Action<UiMenuItemOptions> configure) => _store.Add(_owner, Sink, LiveSession, id, configure);

        public bool Remove(string id) => _store.Remove(_owner, Sink, id);

        public bool Contains(string id) => _store.Find(id) is not null;

        public bool TryGet(string id, [NotNullWhen(true)] out UiMenuItemDefinition? item)
        {
            item = _store.Find(id);
            return item is not null;
        }

        public IReadOnlyList<UiMenuItemDefinition> GetChildren(string? parentId) => _store.Children(parentId);

        public bool Move(string id, Action<UiMenuPlacementOptions> configure) => _store.Move(_owner, Sink, id, configure);

        public bool SetHidden(string id, bool hidden) => _store.SetHidden(id, hidden);

        public UiMenuItemState GetState(string id, UiMenuSurface surface) => _store.GetState(id, surface);
    }
}

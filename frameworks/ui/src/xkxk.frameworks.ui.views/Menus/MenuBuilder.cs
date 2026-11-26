// 메뉴 레지스트리 → 제목줄 최상위 MenuItem 생성기 — 최상위를 만들 때 자식·섹션 칸까지 즉시 전부 만들고(D4), 열 때는 상태만 다시 맞춘다(UADR-0014)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>
/// 레지스트리의 최상위 정의마다 MenuItem 을 만든다. 자식(하위 메뉴·명령·구분선)과 섹션 칸은 최상위와 같은 때 모두 만든다 —
/// 원격 menu.list·UIA 시험이 메뉴를 열지 않고 논리 트리에서 자식 AutomationId 를 읽기 때문이다(지연 생성 금지).
/// 메뉴가 열릴 때(SubmenuOpened)는 컨트롤을 새로 만들지 않고 GetState 로 보임·활성·제목·툴팁·단축키·아이콘만 맞춘다.
/// 레지스트리가 바뀌면(Changed) 그 항목이 속한 최상위만 다시 만들고 <see cref="TopsChanged"/> 로 알린다.
/// </summary>
internal sealed class MenuBuilder
{
    private readonly IUiMenuRegistry _menus;
    private readonly List<MenuItem> _tops = new();
    private readonly Dictionary<string, MenuItem> _topById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rootOf = new(StringComparer.Ordinal); // 메뉴 ID → 만들 때의 최상위 ID
    private readonly Dictionary<Control, string> _ids = new();                        // 만든 컨트롤 → 메뉴 ID(섹션이 만든 칸은 없음)
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private bool _all;
    private bool _syncQueued;

    internal MenuBuilder(IUiMenuRegistry menus)
    {
        ArgumentNullException.ThrowIfNull(menus);
        _menus = menus;
        Rebuild(null);
    }

    /// <summary>지금 최상위 메뉴(레지스트리 순서). 보이지 않는 최상위도 들어 있다.</summary>
    internal IReadOnlyList<MenuItem> Tops => _tops;

    /// <summary>최상위 목록이나 최상위 MenuItem 이 바뀌었다. 받는 쪽은 <see cref="Tops"/> 로 제목줄을 다시 맞춘다.</summary>
    internal event Action? TopsChanged;

    /// <summary>레지스트리 변경 구독을 켠다. 붙어 있지 않은 동안의 변경을 놓치지 않게 켤 때 전체를 다시 맞춘다.</summary>
    internal void Attach()
    {
        _menus.Changed -= OnRegistryChanged; // 두 번 붙어도 한 번만 구독한다.
        _menus.Changed += OnRegistryChanged;
        _all = true;
        QueueSync();
    }

    internal void Detach() => _menus.Changed -= OnRegistryChanged;

    /// <summary>모든 최상위의 상태를 다시 맞춘다(언어 변경·화면에 붙은 직후).</summary>
    internal void RefreshAll()
    {
        foreach (var top in _tops)
        {
            Refresh(top, true);
        }
    }

    private void OnRegistryChanged(object? sender, UiRegistryChangedEventArgs e)
    {
        if (e.Kind is not (UiRegistryChangeKind.Added or UiRegistryChangeKind.Removed or UiRegistryChangeKind.Replaced
            or UiRegistryChangeKind.Moved or UiRegistryChangeKind.VisibilityChanged))
        {
            _all = true; // 모르는 종류는 전체가 바뀐 것으로 본다.
        }
        else
        {
            // 바뀌기 전 최상위(옮기기·제거)와 바뀐 뒤 최상위(추가·옮기기)를 모두 다시 만든다.
            _dirty.UnionWith(new[] { _rootOf.GetValueOrDefault(e.Id), RootOf(e.Id) }.OfType<string>());
        }

        QueueSync();
    }

    // 같은 프레임의 여러 변경(모듈이 한꺼번에 등록)을 1회 재구성으로 합친다. 다른 스레드에서 와도 UI 스레드에서 처리한다.
    private void QueueSync()
    {
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncQueued = false;
            var dirty = _all ? null : _dirty.ToArray();
            _all = false;
            _dirty.Clear();
            Rebuild(dirty);
            TopsChanged?.Invoke();
        });
    }

    // dirty=null 이면 전부, 아니면 그 최상위만 새로 만들고 나머지는 기존 MenuItem 을 그대로 쓴다(열린 메뉴·더보기 상태 보존).
    private void Rebuild(IReadOnlyCollection<string>? dirty)
    {
        var old = new Dictionary<string, MenuItem>(_topById, StringComparer.Ordinal);
        _tops.Clear();
        _topById.Clear();
        foreach (var def in _menus.GetChildren(null))
        {
            if (def.Kind is not (UiMenuItemKind.Submenu or UiMenuItemKind.Command))
            {
                continue; // 메뉴 막대에는 구분선·섹션·모르는 종류를 그리지 않는다.
            }

            if (!old.Remove(def.Id, out var item) || dirty is null || dirty.Contains(def.Id))
            {
                if (item is not null)
                {
                    Forget(item, def.Id); // 새로 만들기 전에 옛 기록을 지운다.
                }

                item = BuildTop(def);
            }

            _tops.Add(item);
            _topById[def.Id] = item;
        }

        foreach (var (id, gone) in old)
        {
            Forget(gone, id); // 레지스트리에서 사라진 최상위.
        }
    }

    private MenuItem BuildTop(UiMenuItemDefinition def)
    {
        var top = (MenuItem)Create(def, def.Id, null)!; // 하위 메뉴면 Create 가 SubmenuOpened 를 구독한다.
        Refresh(top, true);
        return top;
    }

    // 정의 1개 → 컨트롤 1개(하위 메뉴는 자식까지 즉시). 섹션·모르는 종류는 null(섹션 칸은 부모가 끼워 넣는다).
    // rootItem=null 이면 최상위다. 최상위의 SubmenuOpened 를 구독하고, 열림 처리기가 GetState 반영 뒤 ShellMenuSections.RefreshOnOpen 을 부른다.
    private Control? Create(UiMenuItemDefinition def, string root, MenuItem? rootItem)
    {
        Control control;
        switch (def.Kind)
        {
            case UiMenuItemKind.Separator:
                control = new Separator();
                break;
            case UiMenuItemKind.Command:
                control = new MenuItem { Command = def.CommandId is { } cmd ? new RegistryMenuCommand(cmd, def.CommandParameter) : null };
                break;
            case UiMenuItemKind.Submenu:
                var sub = new MenuItem();
                if (rootItem is null)
                {
                    rootItem = sub;
                    sub.AddHandler(MenuItem.SubmenuOpenedEvent, OnSubmenuOpened);
                }

                foreach (var child in _menus.GetChildren(def.Id))
                {
                    if (child.Kind == UiMenuItemKind.Section)
                    {
                        AddSection(sub, rootItem, child, root);
                    }
                    else if (Create(child, root, rootItem) is { } c)
                    {
                        sub.Items.Add(c);
                    }
                }

                control = sub;
                break;
            default:
                return null; // 모르는 종류는 하위까지 그리지 않는다.
        }

        if (control is MenuItem mi)
        {
            AutomationProperties.SetAutomationId(mi, def.AutomationId);
            ToolTip.SetShowOnDisabled(mi, def.HasToolTip); // 사유 툴팁은 비활성일 때도 보인다(옛 axaml 의 ShowOnDisabled).
        }

        _ids[control] = def.Id;
        _rootOf[def.Id] = root;
        return control;
    }

    // 섹션 행에는 MenuItem 을 만들지 않고 섹션이 만든 칸을 그 자리에 끼운다. 칸의 상태는 섹션 소유라 정의 ID 와 잇지 않는다(_ids 에 넣지 않음 → GetState 로 덮지 않음).
    // 모르는 키는 섹션이 진단(XKUI0409)하고 빈 목록을 준다.
    private void AddSection(MenuItem host, MenuItem rootItem, UiMenuItemDefinition def, string root)
    {
        _rootOf[def.Id] = root;
        foreach (var c in ShellMenuSections.Build(def.Section, host, rootItem))
        {
            host.Items.Add(c);
        }
    }

    // 버린 최상위의 기록을 지운다. 다른 최상위로 옮겨 이미 새로 기록된 항목(최상위가 다름)은 남긴다.
    private void Forget(MenuItem top, string rootId)
    {
        top.RemoveHandler(MenuItem.SubmenuOpenedEvent, OnSubmenuOpened);
        foreach (var c in Walk(top).ToList())
        {
            if (_ids.Remove(c, out var id) && _rootOf.TryGetValue(id, out var r) && r == rootId)
            {
                _rootOf.Remove(id);
            }
        }
    }

    // 열린 메뉴(최상위 또는 하위 메뉴) 아래만 다시 맞춘다. 최상위가 열리면 창 메뉴 뷰모델의 열린 창 목록·사유 툴팁도 새로 계산한다(옛 WindowMenu 와 같다).
    private void OnSubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not MenuItem opened || !_ids.ContainsKey(opened))
        {
            return;
        }

        var isTop = ReferenceEquals(opened, sender);
        if (isTop && XkxkUi.Current.MainViewModel is { } vm)
        {
            vm.WindowMenu.OpenWindows.Refresh();
            vm.WindowMenu.RefreshTips();
        }

        Refresh(opened, isTop);
        if (isTop)
        {
            ShellMenuSections.RefreshOnOpen(opened); // 정의 상태를 맞춘 뒤 섹션이 마지막에 덮는다(스냅 0개면 하위 메뉴 숨김).
        }
    }

    // 정의에서 만든 컨트롤만 맞춘다(섹션 칸은 이 뒤 ShellMenuSections.RefreshOnOpen 이 맞춘다).
    private void Refresh(MenuItem root, bool includeSelf)
    {
        foreach (var c in Walk(root).Where(c => includeSelf || !ReferenceEquals(c, root)).ToList())
        {
            if (_ids.TryGetValue(c, out var id))
            {
                Apply(c, id);
            }
        }
    }

    private void Apply(Control c, string id)
    {
        var state = _menus.GetState(id, UiMenuSurface.Desktop);
        c.IsVisible = state.IsVisible;
        if (c is not MenuItem mi)
        {
            return;
        }

        var command = mi.Command as RegistryMenuCommand;
        mi.IsEnabled = state.IsEnabled;
        mi.Header = state.Title;
        MenuIcon.SetKey(mi, state.Icon);
        mi.InputGesture = RegistryMenuCommand.DisplayGesture(state.Gesture, command?.CommandId);
        if (ToolTip.GetShowOnDisabled(mi))
        {
            ToolTip.SetTip(mi, state.ToolTip); // 툴팁 대리자가 있는 항목만(만들 때 표시해 둠).
        }

        command?.RaiseCanExecuteChanged();
    }

    // 레지스트리 최상위까지 부모를 따라간다. 없는 ID 면 null.
    private string? RootOf(string id)
    {
        var guard = 0;
        UiMenuItemDefinition? def = null;
        while (guard++ < 64 && _menus.TryGet(id, out def) && def.Parent is { } parent)
        {
            id = parent;
        }

        return def is { Parent: null } ? def.Id : null;
    }

    // 자신과 그 아래 컨트롤 전부(전위 순서). 섹션이 만든 칸도 들어 있다.
    private static IEnumerable<Control> Walk(MenuItem item) =>
        new Control[] { item }.Concat(item.Items.OfType<Control>().SelectMany(c => c is MenuItem m ? Walk(m) : new[] { c }));
}

// 메뉴 섹션 — 열린 창 목록 1~9·창 더 보기와 창 루트 열기 훅(옛 WindowMenu 코드 비하인드 OnAttachedToVisualTree·OnSubmenuOpened 이전)
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views.Menus;

internal static partial class ShellMenuSections
{
    // 최상위 MenuItem ↔ 그 안 섹션 부품과 열기 훅(최상위를 다시 만들면 새 키로 새로 붙는다)
    private static readonly ConditionalWeakTable<MenuItem, RootHooks> s_roots = new();

    private static RootHooks HooksOf(MenuItem root) => s_roots.GetValue(root, r => new RootHooks(r));

    /// <summary>
    /// 최상위가 열릴 때 섹션을 다시 맞춘다(열린 창 목록·사유 툴팁·스냅 칸 활성·✓). MenuBuilder 의 상태 맞춤(GetState) 뒤에 돌아야 한다 —
    /// 켜진 배치가 0개면 스냅 하위 메뉴를 숨기는 값이 GetState 의 표시 값을 덮어야 하기 때문이다. 섹션이 없는 최상위는 아무것도 안 한다.
    /// </summary>
    /// <param name="root">열린 최상위 MenuItem.</param>
    public static void RefreshOnOpen(MenuItem root)
    {
        if (s_roots.TryGetValue(root, out var hooks) && root.DataContext is MainViewModel vm)
        {
            hooks.Refresh(vm);
        }
    }

    // ── 열린 창: 최상위 xkxk.window 끝의 조건부 구분선 + 칸 9개 + 창 더 보기 ─────────────────

    private static IReadOnlyList<Control> BuildOpenWindows(MenuItem host, MenuItem root)
    {
        var separator = new Separator { IsVisible = false };
        var slots = new MenuItem[OpenWindowListViewModel.MaxSlots];
        var result = new List<Control>(slots.Length + 2) { separator };
        for (var i = 0; i < slots.Length; i++)
        {
            // 아이콘 칸 = 그 문서 아이콘, 활성 창 ✓ = 토글 칸(ToggleType CheckBox — 아이콘 칸과 별개라 16px 아이콘과 겹치지 않는다)
            var item = NewItem("shell.mnuWinList" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsVisible = false;
            item.Icon = new WindowIcon { Width = 16, Height = 16 };
            slots[i] = item;
            result.Add(item);
        }

        // 10개 이상이면 '창 더 보기…' — 전체 목록 대화상자(Views/MoreWindowsView, MainTitleBar 의 Popup)
        var more = NewItem("shell.mnuWinListMore");
        BindTitle(more, "menu.window.more");
        MenuIcon.SetKey(more, CmdIconKeys.WinListMore);
        more.IsVisible = false;
        result.Add(more);

        var part = new OpenListPart(separator, slots, more);
        HooksOf(root).OpenLists.Add(part);
        // 뷰모델 연결은 논리 트리에 붙어 있고 DataContext(상속)가 있을 때만 — 다시 만든 메뉴의 옛 칸이 구독을 남기지 않게
        root.AttachedToLogicalTree += (_, _) => part.Attach(root.DataContext as MainViewModel);
        root.DetachedFromLogicalTree += (_, _) => part.Attach(null);
        root.DataContextChanged += (_, _) =>
        {
            if (((ILogical)root).IsAttachedToLogicalTree)
            {
                part.Attach(root.DataContext as MainViewModel);
            }
        };
        part.Attach(root.DataContext as MainViewModel);
        return result;
    }

    // 열린 창 칸 묶음 — 슬롯 뷰모델 값을 칸 MenuItem 에 옮긴다(리플렉션 바인딩 없이, 트리밍 안전)
    private sealed class OpenListPart
    {
        private readonly Separator _separator;
        private readonly MenuItem[] _items;
        private readonly MenuItem _more;
        private readonly PropertyChangedEventHandler _listChanged;
        private readonly PropertyChangedEventHandler _slotChanged;
        private OpenWindowListViewModel? _list;

        public OpenListPart(Separator separator, MenuItem[] items, MenuItem more)
        {
            _separator = separator;
            _items = items;
            _more = more;
            _listChanged = (_, _) => ApplyList();
            _slotChanged = (s, _) => ApplySlot(s as OpenWindowSlot);
        }

        public void Attach(MainViewModel? vm)
        {
            var list = vm?.WindowMenu.OpenWindows;
            if (ReferenceEquals(list, _list))
            {
                return;
            }

            if (_list is { } old)
            {
                old.PropertyChanged -= _listChanged;
                foreach (var slot in old.Slots)
                {
                    slot.PropertyChanged -= _slotChanged;
                }
            }

            _list = list;
            _more.Command = list?.OpenMoreCommand;
            for (var i = 0; i < _items.Length; i++)
            {
                var slot = list is not null && i < list.Slots.Count ? list.Slots[i] : null;
                _items[i].Command = list?.SelectCommand;
                _items[i].CommandParameter = slot;
                if (slot is not null)
                {
                    slot.PropertyChanged += _slotChanged;
                }
            }

            if (list is not null)
            {
                list.PropertyChanged += _listChanged;
            }

            ApplyList();
            foreach (var item in _items)
            {
                ApplyItem(item);
            }
        }

        private void ApplyList()
        {
            _separator.IsVisible = _list?.HasAny == true;
            _more.IsVisible = _list?.HasMore == true;
        }

        // 칸 1개를 슬롯 값으로 맞춘다. 슬롯이 없으면(뷰모델 미연결) 숨긴다
        private static void ApplyItem(MenuItem item)
        {
            if (item.CommandParameter is not OpenWindowSlot slot)
            {
                item.IsVisible = false;
                return;
            }

            item.Header = slot.Header;
            item.IsVisible = slot.IsVisible;
            item.SetCurrentValue(MenuItem.IsCheckedProperty, slot.IsActive);
            if (item.Icon is WindowIcon icon)
            {
                icon.Document = slot.Document;
            }
        }

        private void ApplySlot(OpenWindowSlot? slot)
        {
            foreach (var item in _items)
            {
                if (slot is not null && ReferenceEquals(item.CommandParameter, slot))
                {
                    ApplyItem(item);
                }
            }
        }

        // 열린 창 칸의 ✓(토글 칸)를 실제 활성 값으로 맞춘다. 메뉴는 CheckBox 항목을 누르면 IsChecked 를 SetCurrentValue 로 뒤집는데,
        // 이미 활성인 창을 다시 고르면 IsActive 가 안 바뀌어 알림이 오지 않는다
        public void SyncActiveChecks()
        {
            foreach (var item in _items)
            {
                if (item.CommandParameter is OpenWindowSlot slot && item.IsChecked != slot.IsActive)
                {
                    item.SetCurrentValue(MenuItem.IsCheckedProperty, slot.IsActive);
                }
            }
        }
    }

    // 최상위 1개의 섹션 부품과 훅 — 화면에 붙은 뒤 사유 툴팁 재계산, 열 때 섹션 갱신
    private sealed class RootHooks
    {
        public RootHooks(MenuItem root)
        {
            // 화면에 붙은 뒤(배치 끝, 데스크톱은 메인 창 등록 뒤) 사유 툴팁을 다시 계산한다 — 붙기 전 '실행 불가'로 읽힌 값이 남지 않게
            root.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(
                () => (root.DataContext as MainViewModel)?.WindowMenu.RefreshTips(), DispatcherPriority.Background);
            Root = root;
        }

        public MenuItem Root { get; }

        public List<(MenuItem Host, List<MenuItem> Layouts)> Snaps { get; } = new();

        public List<OpenListPart> OpenLists { get; } = new();

        // 열 때마다 열린 창 목록·사유 툴팁을 다시 채운다 — 이름 변경·호스트 상태 변화처럼 알림이 없는 변화도 반영
        public void Refresh(MainViewModel vm)
        {
            vm.WindowMenu.OpenWindows.Refresh();
            vm.WindowMenu.RefreshTips();
            foreach (var (host, layouts) in Snaps)
            {
                RefreshSnap(host, layouts, Root, vm);
            }

            foreach (var part in OpenLists)
            {
                part.SyncActiveChecks();
            }
        }
    }
}

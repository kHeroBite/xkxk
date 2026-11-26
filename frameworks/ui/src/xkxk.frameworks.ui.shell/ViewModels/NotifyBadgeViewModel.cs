// 상태 표시줄 알림 배지 뷰모델 — 알림 패널이 집중 숨김·닫힘·비활성일 때 새 알림 건수를 모으고, 누르면 패널을 표시하며 지운다
using System;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>배지 상태. 알림 판정은 기존 알림 도구(NotificationPanelViewModel.Items)를 그대로 쓰고, 패널 표시 여부는 도킹 트리에서 읽는다.</summary>
public sealed class NotifyBadgeViewModel : ObservableObject
{
    private readonly PocDockFactory _factory;
    private readonly WindowCommands _commands;
    private readonly CompactModeViewModel _compact;
    private int _count;
    private bool _languageBurst;

    public NotifyBadgeViewModel(PocDockFactory factory, WindowCommands commands, CompactModeViewModel compact)
    {
        _factory = factory;
        _commands = commands;
        _compact = compact;
        compact.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CompactModeViewModel.IsCompact) or nameof(CompactModeViewModel.IsNotifyOpen))
            {
                Reevaluate();
            }
        };
        ShowCommand = new RelayCommand(Show);
        factory.Notifications.Items.CollectionChanged += OnItemsChanged;
        factory.PanelTabsChanged += _ => Reevaluate();
        factory.SplitChanged += Reevaluate;
        factory.ActiveDockableChanged += (_, _) => Reevaluate();
        XkxkUi.Current.Localization.LanguageChanged += () => { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Tip)); };
    }

    public IRelayCommand ShowCommand { get; }

    /// <summary>모인 새 알림 건수.</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(Tip));
            }
        }
    }

    /// <summary>배지 표시 여부. 건수가 있을 때만 보인다(패널이 보이면 건수가 0이다).</summary>
    public bool IsVisible => _count > 0;

    public string Name => Loc.Tr("notify.badge.name");

    public string Tip => Loc.Tr("notify.badge.tip", _count);

    /// <summary>알림 도구가 지금 화면에 보이는가. 집중 숨김·숨긴 도구·접힌/닫힌 패널·가려진 탭이면 false 이다.</summary>
    public bool IsPanelVisible()
    {
        if (_compact.IsCompact)
        {
            return _compact.IsNotifyOpen; // 좁은 화면은 Dock 을 숨기고 모델만 남긴다 — 보이는 곳은 알림 오버레이뿐이다
        }

        var tool = _factory.Notifications;
        if (_factory.SidePanelsHidden || _factory.IsToolHidden(NotificationPanelViewModel.ToolId))
        {
            return false;
        }

        // 가려진 탭(같은 패널의 다른 탭이 활성)이면 안 보인다
        if (tool.Owner is IDock tabs && tabs.ActiveDockable is { } active && !ReferenceEquals(active, tool))
        {
            return false;
        }

        // 접히거나 닫힌 패널은 부모의 보이는 목록에서 빠진다 — 루트까지 모두 보이는 목록에 있어야 한다
        IDockable current = tool;
        while (current.Owner is IDock owner)
        {
            if (owner.VisibleDockables?.Contains(current) != true)
            {
                return false;
            }

            current = owner;
        }

        return true;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // 언어 변경 때 알림 목록이 비워졌다 다시 채워진다 — 새 알림이 아니므로 같은 처리 순서 안의 추가는 세지 않는다
            _languageBurst = true;
            Dispatcher.UIThread.Post(() => _languageBurst = false);
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add || _languageBurst)
        {
            return;
        }

        if (!IsPanelVisible())
        {
            Count += e.NewItems?.Count ?? 1;
        }
    }

    /// <summary>패널이 보이게 된 것을 감지하면 배지를 지운다.</summary>
    private void Reevaluate()
    {
        if (_count > 0 && IsPanelVisible())
        {
            Count = 0;
        }
    }

    private void Show()
    {
        if (_compact.IsCompact)
        {
            _compact.IsNotifyOpen = true;
        }
        else
        {
            _commands.ShowTool(NotificationPanelViewModel.ToolId);
        }

        Count = 0;
    }
}

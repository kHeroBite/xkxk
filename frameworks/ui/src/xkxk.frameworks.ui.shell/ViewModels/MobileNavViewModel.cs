// 모바일형 하단 탭·삼선 서랍 컨트롤 레이어 — 카탈로그 항목을 기존 메인 뷰모델 명령에 연결하고 비활성 항목에 사유를 붙인다
using xkxk.frameworks.ui.data.Navigation;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.ViewModels.Settings;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>하단 탭 1칸.</summary>
public sealed class MobileNavTab : ObservableObject
{
    private bool _isActive;

    public MobileNavTab(string id, string labelKey, string icon, Action run)
    {
        Id = id;
        LabelKey = labelKey;
        Icon = icon;
        Command = new RelayCommand(run);
    }

    public string Id { get; }

    public string LabelKey { get; }

    public string Icon { get; }

    public string Label => Loc.Tr(LabelKey);

    public string AutomationId => "shell.navTab." + Id;

    public RelayCommand Command { get; }

    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public void RefreshText() => OnPropertyChanged(nameof(Label));
}

/// <summary>삼선 서랍의 메뉴 줄 1개(그룹 제목 또는 항목).</summary>
public sealed class MobileNavItem : ObservableObject
{
    private bool _isEnabled;
    private bool _isOn;
    private string? _tip;

    public MobileNavItem(NavEntry entry, Action<NavEntry> run)
    {
        Entry = entry;
        Command = new RelayCommand(() => run(entry));
    }

    public NavEntry Entry { get; }

    public string Text => MenuLabel.Of(Entry.TextKey);

    public string AutomationId => "shell.navDrawer." + Entry.Id;

    public bool IsGroup => Entry.IsGroup;

    public double IndentPx => Entry.Depth * 14;

    public RelayCommand Command { get; }

    public bool IsEnabled { get => _isEnabled; private set => SetProperty(ref _isEnabled, value); }

    public bool IsOn { get => _isOn; private set { if (SetProperty(ref _isOn, value)) { OnPropertyChanged(nameof(Mark)); } } }

    /// <summary>켜진 토글·선택된 라디오 표시.</summary>
    public string Mark => _isOn ? "✓" : "";

    /// <summary>비활성일 때 사유 문구(활성이면 null).</summary>
    public string? Tip { get => _tip; private set => SetProperty(ref _tip, value); }

    public void Apply(bool enabled, string? reasonKey, bool on)
    {
        IsEnabled = enabled || Entry.IsGroup;
        Tip = enabled || reasonKey is null ? null : Loc.Tr(reasonKey);
        IsOn = on;
        OnPropertyChanged(nameof(Text));
    }
}

/// <summary>모바일형 화면 상태 — 하단 탭, 삼선 서랍(메뉴 전체 트리·탐색기·열린 문서), 항목 실행.</summary>
public sealed partial class MobileNavViewModel : ObservableObject
{
    public const string PanelMenu = "menu";
    public const string PanelExplorer = "explorer";
    public const string PanelDocs = "docs";

    private string _panel = PanelMenu;
    private SettingsWindowViewModel? _settingsSheet;

    public MobileNavViewModel(MainViewModel main)
    {
        Main = main;
        MenuItems = new ObservableCollection<MobileNavItem>(NavMenuCatalog.Entries.Select(e => new MobileNavItem(e, Execute)));
        var tabs = NavMenuCatalog.Tabs.Select(t => new MobileNavTab(t.Id, t.TabKey!, t.Icon!, () => OpenTab(t))).ToList();
        tabs.Add(new MobileNavTab("more", NavMenuCatalog.MoreTabKey, NavMenuCatalog.MoreIcon, () => TogglePanel(PanelMenu)));
        Tabs = new ObservableCollection<MobileNavTab>(tabs);
        ToggleMenuCommand = new RelayCommand(() => TogglePanel(PanelMenu));
        CloseSettingsSheetCommand = new RelayCommand(CloseSettingsSheet);
        SystemHost.OpenSettingsSheet = OpenSettingsSheet;
        Loc.Instance.LanguageChanged += Refresh;
        main.Compact.PropertyChanged += OnChildChanged;
        main.Compact.Style.PropertyChanged += OnChildChanged;
        main.WindowMenu.PropertyChanged += OnChildChanged;
        main.SystemMenu.PropertyChanged += OnChildChanged;
        main.PropertyChanged += OnMainChanged;
        Refresh();
    }

    public MainViewModel Main { get; }

    public CompactModeViewModel Compact => Main.Compact;

    public ObservableCollection<MobileNavTab> Tabs { get; }

    public ObservableCollection<MobileNavItem> MenuItems { get; }

    /// <summary>상단 왼쪽 삼선 버튼(서랍의 전체 메뉴 트리를 연다).</summary>
    public RelayCommand ToggleMenuCommand { get; }

    /// <summary>전체 화면 설정 시트의 내용(요건 7). 닫혀 있으면 null.</summary>
    public SettingsWindowViewModel? SettingsSheet
    {
        get => _settingsSheet;
        private set
        {
            if (SetProperty(ref _settingsSheet, value))
            {
                OnPropertyChanged(nameof(IsSettingsSheetOpen));
            }
        }
    }

    public bool IsSettingsSheetOpen => _settingsSheet is not null;

    public RelayCommand CloseSettingsSheetCommand { get; }

    public string Panel
    {
        get => _panel;
        private set
        {
            if (SetProperty(ref _panel, value))
            {
                OnPropertyChanged(nameof(IsMenuPanel));
                OnPropertyChanged(nameof(IsExplorerPanel));
                OnPropertyChanged(nameof(IsDocsPanel));
            }
        }
    }

    public bool IsMenuPanel => _panel == PanelMenu;

    public bool IsExplorerPanel => _panel == PanelExplorer;

    public bool IsDocsPanel => _panel == PanelDocs;

    /// <summary>원격 진단 nav.menu 용 — 현재 런타임 상태(활성·사유)를 담은 JSON.</summary>
    public string ToJson() => NavMenuCatalog.ToJson(e => { var (en, why, _) = Evaluate(e); return (en, why); });

    private void OpenTab(NavEntry t)
    {
        if (t.Param is PanelExplorer or PanelDocs) { TogglePanel(t.Param); return; }
        Compact.IsDrawerOpen = false;
        if (t.Param == "settings")
        {
            if (IsSettingsSheetOpen) { CloseSettingsSheet(); } else { OpenSettingsSheet(); }
        }
        else
        {
            CloseSettingsSheet();
            if (t.Param == "notify") { Compact.ToggleNotifyCommand.Execute(null); }
        }

        Refresh();
    }

    private void OpenSettingsSheet()
    {
        Compact.IsDrawerOpen = false;
        Compact.IsNotifyOpen = false;
        SettingsSheet ??= new SettingsWindowViewModel(Main) { CloseAction = CloseSettingsSheet };
        Refresh();
    }

    private void CloseSettingsSheet()
    {
        if (_settingsSheet is not { } sheet) { return; }
        SettingsSheet = null;
        sheet.Dispose();
        Refresh();
    }

    private void TogglePanel(string panel)
    {
        CloseSettingsSheet();
        Compact.IsNotifyOpen = false;
        Compact.IsDrawerOpen = !(Compact.IsDrawerOpen && _panel == panel);
        Panel = panel;
        Refresh();
    }

    private void Execute(NavEntry e)
    {
        if (e.IsGroup || !Evaluate(e).Enabled) { return; }
        var p = e.Param;
        switch (e.Command)
        {
            case "Panel": if (p is PanelExplorer or PanelDocs) { TogglePanel(p); } else { OpenTab(e); } return;
            case "Open": Main.OpenCommand.Execute(p); break;
            case "Bench": Main.Extras.OpenBenchCommand.Execute(p); break;
            case "SaveLayout": Main.SaveLayoutCommand.Execute(null); break;
            case "LoadLayout": Main.LoadLayoutCommand.Execute(null); break;
            case "LayoutSave": Main.WindowMenu.SaveCurrentLayoutCommand.Execute(null); break;
            case "LayoutManage": Main.WindowMenu.ManageLayoutsCommand.Execute(null); break;
            case "Pin": Main.PinCommand.Execute(null); break;
            case "CloseDoc": Main.CloseCommand.Execute(null); break;
            case "CloseOthers": Main.CloseOthersCommand.Execute(null); break;
            case "CloseAll": Main.CloseAllCommand.Execute(null); break;
            case "Next": Main.NextCommand.Execute(null); break;
            case "Prev": Main.PreviousCommand.Execute(null); break;
            case "ResetLayout": Main.ResetLayoutCommand.Execute(null); break;
            case "FontReset": Main.SettingsPanel.ResetFontCommand.Execute(null); break;
            case "ToggleTheme": Main.SettingsPanel.ToggleThemeCommand.Execute(null); break;
            case "SystemView": Main.SystemMenu.SystemViewCommand.Execute(p); break;
        }

        Compact.IsDrawerOpen = false; // 실행한 뒤에는 서랍을 닫아 결과 화면을 보인다
    }

    /// <summary>항목의 (활성, 비활성 사유 키, 켜짐) 판정. 모바일형 비적용 항목은 사유와 함께 비활성.</summary>
    private (bool Enabled, string? Reason, bool On) Evaluate(NavEntry e)
    {
        if (!e.Applicable) { return (false, e.ReasonKey, false); }
        switch (e.Command)
        {
            case "Bench": return Gate(Main.Extras.BenchAvailable);
            case "SystemView" when e.Param == "RestartRequired": return Gate(Main.SystemMenu.SysGpuRestartPending);
            default: return (true, null, false);
        }
    }

    private static (bool, string?, bool) Gate(bool ok) => (ok, ok ? null : NavMenuCatalog.ReasonUnavailable, false);

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Compact.IsCompact)
        {
            CloseSettingsSheet(); // 모바일형을 벗어나면 시트는 가려진 채 남지 않게 닫는다
        }

        Refresh();
    }

    private void Refresh()
    {
        foreach (var it in MenuItems)
        {
            var (en, why, on) = Evaluate(it.Entry);
            it.Apply(en, why, on);
        }

        foreach (var t in Tabs)
        {
            t.RefreshText();
            t.IsActive = t.Id switch
            {
                PanelExplorer or PanelDocs => Compact.IsDrawerOpen && _panel == t.Id,
                "notify" => Compact.IsNotifyOpen,
                "settings" => IsSettingsSheetOpen,
                _ => Compact.IsDrawerOpen && _panel == PanelMenu, // 더보기
            };
        }
    }
}

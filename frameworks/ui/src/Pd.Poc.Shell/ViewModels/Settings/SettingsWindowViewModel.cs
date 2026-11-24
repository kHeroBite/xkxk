// 통합 설정 창 뷰모델(요건 7) — 범주 목록·선택 페이지·검색 필터·범주별/전체 기본값. 값 변경은 각 페이지가 UiSettings 로 즉시 적용·저장한다
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels.Settings;

/// <summary>페이지 뷰모델 공통 — 설정·언어 변경을 화면에 다시 알리고 창이 닫히면 구독을 푼다.</summary>
public abstract class SettingsPageViewModel : ObservableObject, IDisposable
{
    protected SettingsPageViewModel(MainViewModel main)
    {
        Main = main;
        main.Settings.PropertyChanged += OnSettingChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    protected MainViewModel Main { get; }

    protected UiSettings Settings => Main.Settings;

    /// <summary>이 범주의 값을 기본값으로 되돌린다.</summary>
    public abstract void ResetToDefaults();

    public virtual void Dispose()
    {
        Main.Settings.PropertyChanged -= OnSettingChanged;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>값이 어디서 바뀌든(메뉴·단축키·원격) 페이지 표시를 전부 다시 읽게 한다. 페이지의 속성 수가 적고 드물게 불린다.</summary>
    protected virtual void OnSettingChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);

    protected virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);

    /// <summary>콤보가 보낸 순번이 유효하고 지금 값과 다를 때만 true(콤보가 늦게 되써도 설정이 바뀌지 않게, Q17).</summary>
    protected static bool IsNewIndex(int value, int count, int current) => value >= 0 && value < count && value != current;
}

/// <summary>왼쪽 범주 목록의 항목 1개.</summary>
public sealed class SettingsCategoryItem : ObservableObject
{
    internal SettingsCategoryItem(SettingsCategoryId id, SettingsPageViewModel page, bool canReset)
    {
        Id = id;
        Page = page;
        CanReset = canReset;
    }

    public SettingsCategoryId Id { get; }

    public SettingsPageViewModel Page { get; }

    /// <summary>되돌릴 값이 있는 범주인가(레이아웃은 명령뿐이라 false).</summary>
    public bool CanReset { get; }

    public string Title => Loc.Tr(SettingsInventory.TitleKey(Id));

    public string AutomationId => "settings.category." + Id.ToString().ToLowerInvariant();

    public override string ToString() => Title; // 목록 항목의 접근성·원격 조회 문구

    internal void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

/// <summary>설정 창 전체. 창이 열릴 때 만들고 닫힐 때 Dispose 한다(SystemDialogHost 규칙).</summary>
public sealed class SettingsWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>SystemDialogHost 창 이름(원격 sys.close name= 값, AutomationId 는 sys.dlg.settings).</summary>
    public const string DialogName = "settings";

    /// <summary>창 제목 사전 키.</summary>
    public const string WindowTitleKey = "settings.window.title";

    private readonly IReadOnlyList<SettingsCategoryItem> _all;
    private SettingsCategoryItem? _selected;
    private string _searchText = "";
    private bool _refiltering;

    public SettingsWindowViewModel(MainViewModel main)
    {
        Appearance = new AppearancePageViewModel(main);
        WindowOptions = new WindowPageViewModel(main);
        Language = new LanguagePageViewModel(main);
        Layout = new LayoutPageViewModel(main);
        SystemOptions = new SystemPageViewModel(main);
        _all = new[]
        {
            new SettingsCategoryItem(SettingsCategoryId.Appearance, Appearance, canReset: true),
            new SettingsCategoryItem(SettingsCategoryId.Window, WindowOptions, canReset: true),
            new SettingsCategoryItem(SettingsCategoryId.Language, Language, canReset: true),
            new SettingsCategoryItem(SettingsCategoryId.Layout, Layout, canReset: false),
            new SettingsCategoryItem(SettingsCategoryId.System, SystemOptions, canReset: true),
        };
        Categories = new ObservableCollection<SettingsCategoryItem>(_all);
        _selected = _all[0];
        ResetSelectedCommand = new RelayCommand(() => { if (_selected is { } c) { ResetCategory(c.Id); } }, () => _selected?.CanReset == true);
        ResetAllCommand = new RelayCommand(ResetAll);
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>닫기를 호출자가 맡을 때 넣는다(스마트폰 ☰ 시트). 비어 있으면 SystemDialogHost 창을 닫는다.</summary>
    public Action? CloseAction { get; set; }

    public AppearancePageViewModel Appearance { get; }

    public WindowPageViewModel WindowOptions { get; }

    public LanguagePageViewModel Language { get; }

    public LayoutPageViewModel Layout { get; }

    public SystemPageViewModel SystemOptions { get; }

    /// <summary>검색어에 맞는 범주(검색어가 없으면 전부).</summary>
    public ObservableCollection<SettingsCategoryItem> Categories { get; }

    public SettingsCategoryItem? SelectedCategory
    {
        get => _selected;
        set
        {
            if (_refiltering || (value is null && Categories.Count > 0))
            {
                return; // 목록을 고치는 중 콤보·리스트가 되쓰는 null 은 선택 해제가 아니다
            }

            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(SelectedPage));
                ResetSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>오른쪽에 보일 페이지 뷰모델(뷰는 SettingsView 의 DataTemplates 가 고른다).</summary>
    public SettingsPageViewModel? SelectedPage => _selected?.Page;

    /// <summary>검색어. 행 숨김은 SettingsRow 가, 범주 거르기는 여기서 한다(같은 비교 규칙).</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>맞는 항목이 하나도 없어 안내 문구를 보일 때 true.</summary>
    public bool HasNoResults => Categories.Count == 0;

    public RelayCommand ResetSelectedCommand { get; }

    public RelayCommand ResetAllCommand { get; }

    /// <summary>범주 하나를 기본값으로 되돌린다. 다른 범주의 값은 건드리지 않는다.</summary>
    public void ResetCategory(SettingsCategoryId id) => _all.First(c => c.Id == id).Page.ResetToDefaults();

    /// <summary>모든 범주를 기본값으로 되돌린다.</summary>
    public void ResetAll()
    {
        foreach (var item in _all)
        {
            item.Page.ResetToDefaults();
        }
    }

    public void Dispose()
    {
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        foreach (var item in _all)
        {
            item.Page.Dispose();
        }
    }

    private void OnLanguageChanged()
    {
        foreach (var item in _all)
        {
            item.RefreshTitle();
        }

        ApplyFilter(); // 항목 이름이 바뀌었으니 다시 거른다
    }

    private void ApplyFilter()
    {
        var keep = _all.Where(c => SettingsInventory.CategoryMatches(c.Id, _searchText)).ToList();
        var target = _selected is not null && keep.Contains(_selected) ? _selected : keep.FirstOrDefault();
        _refiltering = true;
        try
        {
            for (var i = Categories.Count - 1; i >= 0; i--)
            {
                if (!keep.Contains(Categories[i]))
                {
                    Categories.RemoveAt(i);
                }
            }

            for (var i = 0; i < keep.Count; i++)
            {
                if (i >= Categories.Count || !ReferenceEquals(Categories[i], keep[i]))
                {
                    Categories.Insert(i, keep[i]);
                }
            }
        }
        finally
        {
            _refiltering = false;
        }

        _selected = target;
        OnPropertyChanged(nameof(SelectedCategory)); // 목록이 선택을 지웠어도 항상 다시 맞춘다
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(HasNoResults));
        ResetSelectedCommand.NotifyCanExecuteChanged();
    }
}

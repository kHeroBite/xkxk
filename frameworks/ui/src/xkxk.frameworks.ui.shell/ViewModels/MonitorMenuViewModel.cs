// '창 ▸ 멀티 모니터' 하위 메뉴 뷰모델(U20) — 호스트가 알려 준 모니터 목록을 라디오 항목으로 보여 주고 고른 모니터 id 를 UI 설정에 저장한다
using xkxk.frameworks.ui.data.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>
/// 목록·선택 표시만 맡는다(UI 레이어 바인딩 대상). 모니터 조회는 IMonitorProvider(호스트), 선택값 저장은 UiSettings.SelectedMonitorId(데이터),
/// 최대화 때 선택 모니터로 옮기는 실행은 호스트(Desktop MonitorFitService)가 한다. 웹·모바일은 Provider=null → 빈 목록(HasMonitors=false).
/// </summary>
public sealed class MonitorMenuViewModel : ObservableObject
{
    private IMonitorProvider? _provider;

    // 앱 컨텍스트(UiAppContext)만 만든다(B05). 생성자에서 XkxkUi.Current.Monitors 를 다시 읽지 않는다(Lazy 재진입 금지).
    internal MonitorMenuViewModel()
    {
        XkxkUi.Current.Localization.LanguageChanged += Refresh;
        XkxkUi.Current.Settings.PropertyChanged += OnSettingChanged;
    }

    /// <summary>옛 정적 접근자. 앱 컨텍스트(XkxkUi.Current.Monitors)로 위임한다(B05, B16 Obsolete 예고·B21 제거).</summary>
    public static MonitorMenuViewModel Instance => XkxkUi.Current.Monitors;

    /// <summary>옛 호스트 등록 접근자. 앱 컨텍스트로 위임한다(B05, 새 호스트는 UiHostOptions.MonitorProvider, B16 Obsolete 예고·B21 제거).</summary>
    public static IMonitorProvider? Provider
    {
        get => XkxkUi.Current.Monitors.CurrentProvider;
        set => XkxkUi.Current.Monitors.Attach(value);
    }

    /// <summary>지금 연결된 모니터 공급자(없으면 null).</summary>
    public IMonitorProvider? CurrentProvider => _provider;

    /// <summary>메뉴 항목(모니터 1, 모니터 2 …). 비어 있으면 메뉴가 '모니터 정보 없음'을 보인다.</summary>
    public ObservableCollection<MonitorItemViewModel> Items { get; } = new();

    public bool HasMonitors => Items.Count > 0;

    /// <summary>현재 저장된 선택 id(null = 주 모니터).</summary>
    public static string? SelectedId => XkxkUi.Current.Settings.SelectedMonitorId;

    /// <summary>원격·시험용 선택 진입점: 메뉴 클릭과 같은 경로. 목록에 없는 id 면 false.</summary>
    public bool Select(string id)
    {
        foreach (var item in Items)
        {
            if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                XkxkUi.Current.Settings.SelectedMonitorId = item.Id;
                PdLog.Write(PdLogLevel.Information, PdLogCategory.User, $"monitor-select id={item.Id} header='{item.Header}'");
                return true;
            }
        }

        return false;
    }

    /// <summary>모니터 목록을 다시 읽어 항목을 만든다(언어 변경·모니터 변경·등록 때). 선택 표시는 저장값 → 없으면 주 모니터(MonitorSelection.Resolve).</summary>
    public void Refresh()
    {
        Items.Clear();
        var monitors = _provider?.GetMonitors() ?? Array.Empty<MonitorInfo>();
        var selected = MonitorSelection.Resolve(monitors, SelectedId, out _);
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var header = Loc.Tr("menu.monitor.item", i + 1, m.Name, m.Bounds.Width, m.Bounds.Height)
                + (m.IsPrimary ? Loc.Tr("menu.monitor.primary") : string.Empty);
            Items.Add(new MonitorItemViewModel(m.Id, header, $"shell.mnuWinMonitor{i + 1}", ReferenceEquals(m, selected), this));
        }

        OnPropertyChanged(nameof(HasMonitors));
        // 판정 근거(창을 띄우지 않고 메뉴 목록 확인): 항목 수·머리글·선택 표시
        PdLog.Write(PdLogLevel.Information, PdLogCategory.System,
            $"monitor-menu items={Items.Count} provider={(_provider is null ? "none" : "set")} [{string.Join(" | ", System.Linq.Enumerable.Select(Items, x => (x.IsSelected ? "(*)" : "( )") + x.Header))}]");
    }

    /// <summary>공급자를 바꾸고 목록을 다시 만든다. 호스트 연결(App.BindHost)과 옛 Provider setter 가 부른다.</summary>
    internal void Attach(IMonitorProvider? provider)
    {
        if (_provider is not null)
        {
            _provider.MonitorsChanged -= Refresh;
        }

        _provider = provider;
        if (_provider is not null)
        {
            _provider.MonitorsChanged += Refresh;
        }

        Refresh();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UiSettings.SelectedMonitorId))
        {
            return;
        }

        var monitors = _provider?.GetMonitors() ?? Array.Empty<MonitorInfo>();
        var selected = MonitorSelection.Resolve(monitors, SelectedId, out _);
        foreach (var item in Items)
        {
            item.IsSelected = selected is not null && string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>모니터 메뉴 항목 1개(라디오).</summary>
public sealed class MonitorItemViewModel : ObservableObject
{
    private bool _isSelected;

    internal MonitorItemViewModel(string id, string header, string automationId, bool isSelected, MonitorMenuViewModel owner)
    {
        Id = id;
        Header = header;
        AutomationId = automationId;
        _isSelected = isSelected;
        SelectCommand = new RelayCommand(() => owner.Select(id));
    }

    public string Id { get; }

    public string Header { get; }

    public string AutomationId { get; }

    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

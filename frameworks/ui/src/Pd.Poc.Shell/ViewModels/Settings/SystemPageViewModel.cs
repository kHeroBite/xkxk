// 설정 창 '시스템' 페이지 뷰모델(요건 7) — GPU(재시작 필요 표시)·중복 실행·모니터 선택·디버그모드·로그 수준
using Pd.Client.Data;
using Pd.Client.Data.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Pd.Poc.Shell.Rendering;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels.Settings;

public sealed class SystemPageViewModel : SettingsPageViewModel
{
    private static readonly PdLogLevel[] s_logLevels =
        { PdLogLevel.Trace, PdLogLevel.Debug, PdLogLevel.Information, PdLogLevel.Warning, PdLogLevel.Error, PdLogLevel.Critical };

    private static readonly IReadOnlyList<LocalizedOption> s_logLevelNames = LocalizedOption.List(
        "menu.sys.log.level.trace", "menu.sys.log.level.debug", "menu.sys.log.level.info",
        "menu.sys.log.level.warn", "menu.sys.log.level.error", "menu.sys.log.level.critical");

    public SystemPageViewModel(MainViewModel main)
        : base(main)
    {
        ShowRestartCommand = new RelayCommand(() => main.SystemMenu.SystemViewCommand.Execute(nameof(SystemView.RestartRequired)));
        Monitors.CollectionChanged += OnMonitorsChanged;
    }

    /// <summary>GPU 가속: 켬 = 자동(GPU 우선), 끔 = 소프트웨어. 바꾸면 저장되고 재시작 안내가 뜬다(시스템 메뉴와 같은 경로).</summary>
    public bool GpuEnabled
    {
        get => Main.SystemMenu.SysGpuEnabled;
        set => Main.SystemMenu.SysGpuEnabled = value;
    }

    /// <summary>저장한 GPU 모드가 이번 기동 적용값과 달라 다시 시작해야 반영되는 상태. 사유 문구와 [지금 다시 시작] 진입을 보인다.</summary>
    public bool GpuRestartPending => Settings.GpuRestartPending;

    /// <summary>이번 기동에 실제 채택된 렌더러.</summary>
    public string GpuCurrent => Main.SystemMenu.SysGpuCurrent;

    public RelayCommand ShowRestartCommand { get; }

    public bool AllowDuplicate { get => Settings.AllowDuplicateInstance; set => Settings.AllowDuplicateInstance = value; }

    /// <summary>Android 는 OS 가 태스크를 관리해 해당 없음.</summary>
    public bool DuplicateAvailable => Main.SystemMenu.SysDuplicateAvailable;

    public string DuplicateTip => Main.SystemMenu.SysDuplicateTip;

    /// <summary>모니터 목록(호스트 제공, 메뉴와 같은 항목). 웹·모바일은 비어 있어 행이 비활성이다.</summary>
    public ObservableCollection<MonitorItemViewModel> Monitors => MonitorMenuViewModel.Instance.Items;

    public bool HasMonitors => Monitors.Count > 0;

    /// <summary>선택 모니터(저장값 Settings.SelectedMonitorId, null = 주 모니터).</summary>
    public MonitorItemViewModel? SelectedMonitor
    {
        get => Monitors.FirstOrDefault(m => m.IsSelected);
        set
        {
            if (value is not null && value.Id != Settings.SelectedMonitorId)
            {
                MonitorMenuViewModel.Instance.Select(value.Id);
            }
        }
    }

    /// <summary>문서 전환 가속(탭형 비활성 문서 유지·문서 본문 즉시 표시). 기본 켬.</summary>
    public bool FastDocumentSwitch { get => Settings.FastDocumentSwitch; set => Settings.FastDocumentSwitch = value; }

    /// <summary>탭 머리 먼저 그리기(탭 선택 표시 먼저, 본문 교체는 다음 프레임 뒤). 기본 웹만 켬.</summary>
    public bool TabHeaderFirst { get => Settings.TabHeaderFirst; set => Settings.TabHeaderFirst = value; }

    public bool DebugMode { get => Settings.DebugMode; set => Settings.DebugMode = value; }

    public IReadOnlyList<LocalizedOption> LogLevelNames => s_logLevelNames;

    /// <summary>유효 로그 수준의 순번. 고르면 LogMinLevel 로 저장된다.</summary>
    public int LogLevelIndex
    {
        get => Array.IndexOf(s_logLevels, Settings.EffectiveLogLevel);
        set
        {
            if (IsNewIndex(value, s_logLevels.Length, LogLevelIndex)) { Settings.LogMinLevel = s_logLevels[value]; }
        }
    }

    public override void ResetToDefaults()
    {
        Main.SystemMenu.SysGpuAuto = true; // 시스템 메뉴와 같은 경로(바뀌면 재시작 안내)
        Settings.AllowDuplicateInstance = false;
        Settings.SelectedMonitorId = null;
        Settings.FastDocumentSwitch = true;
        Settings.TabHeaderFirst = UiSettings.DefaultTabHeaderFirst;
        Settings.DebugMode = AppChannel.DebugDefault;
        Settings.LogMinLevel = null;
    }

    public override void Dispose()
    {
        Monitors.CollectionChanged -= OnMonitorsChanged;
        base.Dispose();
    }

    private void OnMonitorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasMonitors));
        OnPropertyChanged(nameof(SelectedMonitor));
    }
}

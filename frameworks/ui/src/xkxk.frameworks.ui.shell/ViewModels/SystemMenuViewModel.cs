// 메인 뷰모델의 하위 뷰모델(UI-17) — 시스템 메뉴: 디버그모드·GPU 가속모드·중복모드 토글, 로그 레벨, 시스템 창 열기(로그·모니터·문의·업데이트·변경이력·도움말·정보)
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Rendering;
using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Rendering;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>시스템 메뉴가 여는 창·동작 종류. 창 구현은 odev-1r(Views/System)이 SystemHost.Views 로 제공한다.</summary>
public enum SystemView
{
    LogView,
    LogFolder,
    LogExport,
    AppMonitor,
    Inquiry,
    InquiryList,
    Update,
    Changelog,
    Help,
    About,

    /// <summary>통합 설정 창(요건 7). 데스크톱 = 별도 창 1개, 웹·Android = 플라이아웃, 스마트폰 NAV = ☰ 설정 시트.</summary>
    Settings,

    /// <summary>'다시 시작해야 적용됩니다' 대화([지금 다시 시작]은 SystemHost.Restart 호출).</summary>
    RestartRequired,
}

/// <summary>시스템 창 열기 계약(UI-17). 플랫폼에서 할 수 없는 항목은 CanOpen=false → 메뉴 비활성.</summary>
public interface ISystemViews
{
    bool CanOpen(SystemView view);

    void Open(SystemView view);
}

/// <summary>시스템 메뉴의 바깥 연결점. 창 제공자와 재시작 실행은 호스트·창 담당이 기동 때 넣는다.</summary>
public static class SystemHost
{
    private static ISystemViews? s_views;

    /// <summary>시스템 창 제공자(odev-1r). 바뀌면 메뉴 활성 상태를 다시 계산한다.</summary>
    public static ISystemViews? Views
    {
        get => s_views;
        set
        {
            s_views = value;
            XkxkUi.Current.MainViewModel?.SystemMenu.RefreshSystemViews();
        }
    }

    /// <summary>스마트폰 NAV 의 설정 시트 열기(요건 7). 모바일형 화면이 만들어질 때 MobileNavViewModel 이 넣는다.</summary>
    public static Action? OpenSettingsSheet { get; set; }

    /// <summary>비전경 시험 모드에서 실제로 열지 않은 OS 창 기록(예: "LogFolder C:\…\logs"). 시험은 원격 sys.get 의 suppressed 로 읽는다. 없으면 null.</summary>
    public static string? LastSuppressed { get; set; }

    /// <summary>앱 다시 시작(GPU 모드 적용). 데스크톱 = 새 프로세스 뒤 종료, 웹 = 페이지 새로고침. null 이면 재시작할 수 없는 플랫폼(Android).</summary>
    public static Action? Restart { get; set; }

    /// <summary>
    /// 재시작 대화의 [지금 다시 시작]이 부른다. 설정 저장(GpuMode 등, 비동기)이 끝나기를 최대 1.5초 기다린 뒤 Restart 를 부른다 —
    /// 토글 직후 누르면 저장 전에 새 프로세스·새로고침이 설정을 읽는 경합을 막는다(odev-4b 지적). Restart 가 없으면 아무것도 하지 않는다.
    /// </summary>
    public static async System.Threading.Tasks.Task RestartAsync()
    {
        if (Restart is not { } restart)
        {
            return;
        }

        await UiSettings.Current.FlushAsync(TimeSpan.FromSeconds(1.5));
        restart();
    }
}

/// <summary>시스템 메뉴 토글·로그 레벨·시스템 창 열기. 바인딩 경로는 MainViewModel.SystemMenu.X.</summary>
public sealed class SystemMenuViewModel : ObservableObject
{
    private readonly ShellContext _ctx;

    public SystemMenuViewModel(ShellContext ctx)
    {
        _ctx = ctx;
        SystemViewCommand = new RelayCommand<string>(
            s => { if (ParseSystemView(s) is { } v) { OpenSystemView(v); } },
            s => ParseSystemView(s) is { } v && CanOpenSystemView(v));
        Settings.PropertyChanged += OnSystemSettingChanged;
        // 실제 렌더러는 첫 렌더 뒤에야 확정된다(odev-3 RenderInfo.Resolved) — 그 전에 읽은 '확인 불가'를 갱신한다
        RenderInfo.Resolved += () => OnPropertyChanged(nameof(SysGpuCurrent));
        if (RenderInfo.IsResolved)
        {
            OnPropertyChanged(nameof(SysGpuCurrent));
        }
        XkxkUi.Current.Localization.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(SysGpuCurrent));
            OnPropertyChanged(nameof(SysDuplicateTip));
            OnPropertyChanged(nameof(SysLogFolderTip));
        };
    }

    private UiSettings Settings => _ctx.Settings;

    /// <summary>시스템 창 열기(인자 = SystemView 이름). 제공자가 없거나 플랫폼에서 불가하면 비활성.</summary>
    public RelayCommand<string> SystemViewCommand { get; }

    public bool SysDebugMode
    {
        get => Settings.DebugMode;
        set => Settings.DebugMode = value;
    }

    public bool SysGpuAuto
    {
        get => Settings.GpuMode == GpuMode.Auto;
        set { if (value) { SetGpuMode(GpuMode.Auto); } }
    }

    public bool SysGpuOff
    {
        get => Settings.GpuMode == GpuMode.Off;
        set { if (value) { SetGpuMode(GpuMode.Off); } }
    }

    /// <summary>설정 화면 GPU 가속 체크: 켬 = 자동(GPU 우선), 끔 = 소프트웨어.</summary>
    public bool SysGpuEnabled
    {
        get => Settings.GpuMode == GpuMode.Auto;
        set => SetGpuMode(value ? GpuMode.Auto : GpuMode.Off);
    }

    /// <summary>이번 기동에 실제 채택된 렌더러(설정값과 다를 수 있다 — 재시작 대기).</summary>
    public string SysGpuCurrent => Loc.Tr("menu.sys.gpu.current", RendererText());

    /// <summary>실제 채택 렌더러의 사용자 표시 문구(sys.renderer.{ActiveKind}). DisplayText 는 로그·진단용 영문으로 둔다(odev-3).</summary>
    public static string RendererText() => Loc.Tr("sys.renderer." + RenderInfo.ActiveKind);

    public bool SysGpuRestartPending => Settings.GpuRestartPending;

    public bool SysAllowDuplicate
    {
        get => Settings.AllowDuplicateInstance;
        set => Settings.AllowDuplicateInstance = value;
    }

    /// <summary>Android 는 OS 가 태스크를 관리해 해당 없음(설계 §3.4).</summary>
    public bool SysDuplicateAvailable => !OperatingSystem.IsAndroid();

    public string SysDuplicateTip => Loc.Tr(SysDuplicateAvailable ? "sys.nextLaunch" : "sys.notApplicable");

    public bool SysLogTrace { get => IsLogLevel(PdLogLevel.Trace); set => SetLogLevel(PdLogLevel.Trace, value); }
    public bool SysLogDebug { get => IsLogLevel(PdLogLevel.Debug); set => SetLogLevel(PdLogLevel.Debug, value); }
    public bool SysLogInfo { get => IsLogLevel(PdLogLevel.Information); set => SetLogLevel(PdLogLevel.Information, value); }
    public bool SysLogWarn { get => IsLogLevel(PdLogLevel.Warning); set => SetLogLevel(PdLogLevel.Warning, value); }
    public bool SysLogError { get => IsLogLevel(PdLogLevel.Error); set => SetLogLevel(PdLogLevel.Error, value); }
    public bool SysLogCritical { get => IsLogLevel(PdLogLevel.Critical); set => SetLogLevel(PdLogLevel.Critical, value); }

    /// <summary>로그 폴더가 없는 플랫폼(웹)은 해당 없음 툴팁.</summary>
    public string? SysLogFolderTip => CanOpenSystemView(SystemView.LogFolder) ? null : Loc.Tr("sys.notApplicable");

    /// <summary>창 제공자가 바뀌었을 때(SystemHost.Views) 메뉴 활성 상태를 다시 계산한다.</summary>
    internal void RefreshSystemViews()
    {
        SystemViewCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SysLogFolderTip));
    }

    private static SystemView? ParseSystemView(string? name) => Enum.TryParse<SystemView>(name, out var v) ? v : null;

    private static bool CanOpenSystemView(SystemView view) => SystemHost.Views?.CanOpen(view) == true;

    private void OpenSystemView(SystemView view)
    {
        PdLog.WriteUser(PdLogLevel.Information, "menu.sys " + view); // 사용자 행동 감사: 메뉴 키만(값 없음)
        SystemHost.Views?.Open(view);
    }

    private void SetGpuMode(GpuMode mode)
    {
        if (Settings.GpuMode == mode)
        {
            return;
        }

        Settings.GpuMode = mode; // 즉시 저장(렌더러는 다시 시작해야 바뀐다)
        PdLog.WriteSystem(PdLogLevel.Information, "GpuMode saved: " + mode + " (applied " + GpuSetting.Applied + ")");
        if (Settings.GpuRestartPending && CanOpenSystemView(SystemView.RestartRequired))
        {
            OpenSystemView(SystemView.RestartRequired);
        }
    }

    private bool IsLogLevel(PdLogLevel level) => Settings.EffectiveLogLevel == level;

    private void SetLogLevel(PdLogLevel level, bool on)
    {
        if (on)
        {
            Settings.LogMinLevel = level;
        }
    }

    private void OnSystemSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UiSettings.DebugMode):
                OnPropertyChanged(nameof(SysDebugMode));
                break;
            case nameof(UiSettings.GpuMode):
                OnPropertyChanged(nameof(SysGpuAuto));
                OnPropertyChanged(nameof(SysGpuOff));
                OnPropertyChanged(nameof(SysGpuEnabled));
                OnPropertyChanged(nameof(SysGpuRestartPending));
                break;
            case nameof(UiSettings.AllowDuplicateInstance):
                OnPropertyChanged(nameof(SysAllowDuplicate));
                break;
            case nameof(UiSettings.EffectiveLogLevel):
                OnPropertyChanged(nameof(SysLogTrace));
                OnPropertyChanged(nameof(SysLogDebug));
                OnPropertyChanged(nameof(SysLogInfo));
                OnPropertyChanged(nameof(SysLogWarn));
                OnPropertyChanged(nameof(SysLogError));
                OnPropertyChanged(nameof(SysLogCritical));
                break;
        }
    }
}

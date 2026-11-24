// 메인 뷰모델의 하위 뷰모델 — Mars '윈도우' 메뉴 13항목(U16 이후 '파일'·'창' 메뉴): 자동저장·Esc 창닫기·자석·캡처 방지 설정과 호스트 명령(④~⑩)·레이아웃 저장/관리(⑪⑫)·열린 창 목록
using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>윈도우 메뉴 설정·명령·레이아웃 관리. 바인딩 경로는 MainViewModel.WindowMenu.X.</summary>
public sealed class WindowMenuViewModel : ObservableObject
{
    private readonly ShellContext _ctx;
    private bool _suppressAutoSave;

    public WindowMenuViewModel(ShellContext ctx)
    {
        _ctx = ctx;
        Settings.PropertyChanged += OnWindowSettingChanged;
        WindowMenuCommand = new RelayCommand<string>(p => RunWindowCommand(Parse(p)), p => CanRunWindowCommand(Parse(p)));
        LayoutManager = new LayoutManagerViewModel(() => App.Layouts, ctx.AddLog);
        SaveCurrentLayoutCommand = new RelayCommand(LayoutManager.OpenSave);
        ManageLayoutsCommand = new RelayCommand(LayoutManager.OpenManage);
        ShowToolCommand = new RelayCommand<string>(id => ctx.Commands.ShowTool(id ?? WindowCommands.ExplorerToolId));
        OpenWindows = new OpenWindowListViewModel(ctx.Commands);
    }

    /// <summary>'창' 메뉴 끝의 열린 창 목록 1~9(U16, MDI 관례).</summary>
    public OpenWindowListViewModel OpenWindows { get; }

    private UiSettings Settings => _ctx.Settings;

    /// <summary>이번 실행만 자동 저장·기동 복원을 끈다(--fresh·자동화 인자). 저장된 설정값은 바꾸지 않는다.</summary>
    public bool SuppressAutoSave
    {
        get => _suppressAutoSave;
        set
        {
            if (SetProperty(ref _suppressAutoSave, value))
            {
                OnPropertyChanged(nameof(ShouldAutoSave));
            }
        }
    }

    /// <summary>종료 저장·기동 복원을 실제로 할지: 설정이 켜져 있고 이번 실행에서 끄지 않았을 때.</summary>
    public bool ShouldAutoSave => !_suppressAutoSave && Settings.WindowAutoSave;

    /// <summary>① 윈도우 자동저장(Mars 윈도우자동저장). 저장되는 설정.</summary>
    public bool AutoSaveOnExit
    {
        get => Settings.WindowAutoSave;
        set => Settings.WindowAutoSave = value;
    }

    /// <summary>② Esc 창닫기(Mars Esc닫기). 저장되는 설정.</summary>
    public bool EscCloseEnabled
    {
        get => Settings.EscClose;
        set => Settings.EscClose = value;
    }

    /// <summary>③ 자석 켜기.</summary>
    public bool MagnetEnabled
    {
        get => Settings.MagnetEnabled;
        set => Settings.MagnetEnabled = value;
    }

    /// <summary>③ 자석 범위(메뉴 슬라이더, 0~48·2px 단위).</summary>
    public double MagnetRangeValue
    {
        get => Settings.MagnetRange;
        set => Settings.MagnetRange = UiSettings.NormalizeMagnetRange(value);
    }

    /// <summary>③ 메뉴 라벨(현재값 표시).</summary>
    public string MagnetRangeLabel => Loc.Tr("menu.ws.magnetRange", Settings.MagnetRange);

    /// <summary>⑬ 화면 캡처 방지 설정값. 실제 적용은 App.WindowHost(데스크톱)·MainActivity(Android, 이 속성 변경 알림 구독).</summary>
    public bool CaptureProtection
    {
        get => Settings.CaptureProtection;
        set => Settings.CaptureProtection = value;
    }

    /// <summary>④~⑩ 명령(인자 = WindowHostCommand 이름). 실행할 수 없는 플랫폼에서는 메뉴가 비활성.</summary>
    public RelayCommand<string> WindowMenuCommand { get; }

    /// <summary>비활성 사유 툴팁(U5 계열): 실행할 수 있으면 null(툴팁 없음), 없으면 호스트 사유 → 없으면 일반 문구.</summary>
    public string? ResetMainTip => UnavailableTip(WindowHostCommand.ResetMainWindow);

    public string? SingleMonitorTip => UnavailableTip(WindowHostCommand.FitSingleMonitor);

    public string? MonitorMaximizeTip => UnavailableTip(WindowHostCommand.MaximizeOnSelectedMonitor);

    // 웹 관리형 호스트일 때만 그 사유('웹에서는 …')를 쓰고, 다른 호스트(데스크톱 등)는 일반 문구
    private static string? UnavailableTip(WindowHostCommand c) => CanRunWindowCommand(c) ? null
        : (App.WindowHost is ManagedWindowHostCommands ? ManagedWindowHostCommands.UnavailableReason(c) : null) ?? Loc.Tr("menu.window.unavailable");

    /// <summary>
    /// 사유 툴팁을 다시 계산하게 알린다. 실행 가능 여부는 호스트 상태(데스크톱은 메인 창이 붙은 뒤 가능)에 따라 바뀌므로
    /// '창' 메뉴가 화면에 붙을 때·열릴 때(WindowMenu 코드 비하인드)와 언어 전환 때 부른다.
    /// </summary>
    public void RefreshTips()
    {
        OnPropertyChanged(nameof(ResetMainTip));
        OnPropertyChanged(nameof(SingleMonitorTip));
        OnPropertyChanged(nameof(MonitorMaximizeTip));
    }

    /// <summary>⑪ 현재화면 수동저장 · ⑫ 화면 백업/복원 관리(UI-11).</summary>
    public LayoutManagerViewModel LayoutManager { get; }

    public RelayCommand SaveCurrentLayoutCommand { get; }

    public RelayCommand ManageLayoutsCommand { get; }

    /// <summary>보기 메뉴 '탐색기'·'로그'(인자 = 도구 Id). 접힌 패널로 옮겨 간 도구를 되돌리거나 그 탭을 활성화한다.</summary>
    public RelayCommand<string> ShowToolCommand { get; }

    /// <summary>보기 메뉴 '로그' 표시 여부 — 제품 빌드는 로그 패널을 숨기므로(team-lead 결정) 메뉴도 숨긴다.</summary>
    public bool IsLogToolAvailable => PerfProbe.IsEnabled;

    /// <summary>언어가 바뀌면 뷰모델이 만든 라벨을 다시 알린다(SettingsPanelViewModel.LanguageChanged 에서 MainViewModel 이 부른다).</summary>
    internal void RefreshLabels()
    {
        OnPropertyChanged(nameof(MagnetRangeLabel));
        RefreshTips();
        OpenWindows.Refresh();
    }

    /// <summary>
    /// Esc 창닫기: 옵션이 켜져 있고 이름 변경·레이아웃 대화 중이 아니면 문서 창을 닫는다. doc=null 이면 활성 문서. 닫았으면 true.
    /// 입력란·콤보 판정은 키 입력 쪽(EscCloseInput)이 먼저 한다.
    /// </summary>
    public bool TryEscClose(PocDocument? doc)
    {
        if (!EscCloseEnabled || _ctx.Main.IsRenaming || LayoutManager.IsOpen)
        {
            return false;
        }

        doc ??= _ctx.Main.ActiveDocument;
        if (doc is null)
        {
            return false;
        }

        _ctx.Commands.CloseDocument(doc);
        _ctx.AddLog(Loc.Tr("log.window.escClosed", doc.Title));
        return true;
    }

    private static WindowHostCommand? Parse(string? name) =>
        Enum.TryParse<WindowHostCommand>(name, ignoreCase: false, out var c) ? c : null;

    private static bool CanRunWindowCommand(WindowHostCommand? command) => command is { } c
        && (App.WindowHost?.CanExecute(c) == true || WindowCommands.IsShellWindowCommand(c));

    private void RunWindowCommand(WindowHostCommand? command)
    {
        if (command is not { } c)
        {
            return;
        }

        using var op = PerfProbe.Current.TimeOperation("ui.window." + c.ToString().ToLowerInvariant());
        try
        {
            if (App.WindowHost is { } host && host.CanExecute(c))
            {
                var ok = host.Execute(c);
                _ctx.AddLog(Loc.Tr(ok ? "log.menu.done" : "log.menu.hostFailed", c));
            }
            else
            {
                var count = _ctx.Commands.RunShellWindowCommand(c);
                _ctx.AddLog(count < 0 ? Loc.Tr("log.menu.unsupported", c) : Loc.Tr("log.menu.sdiCount", c, count));
            }
        }
        catch (Exception ex)
        {
            _ctx.AddLog(Loc.Tr("log.menu.error", c, ex.Message)); // 호스트 계약 위반(예외)도 화면을 멈추지 않게
        }
    }

    private void OnWindowSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UiSettings.WindowAutoSave):
                OnPropertyChanged(nameof(AutoSaveOnExit));
                OnPropertyChanged(nameof(ShouldAutoSave));
                break;
            case nameof(UiSettings.EscClose):
                OnPropertyChanged(nameof(EscCloseEnabled));
                break;
            case nameof(UiSettings.MagnetEnabled):
                OnPropertyChanged(nameof(MagnetEnabled));
                break;
            case nameof(UiSettings.MagnetRange):
                OnPropertyChanged(nameof(MagnetRangeValue));
                OnPropertyChanged(nameof(MagnetRangeLabel));
                break;
            case nameof(UiSettings.CaptureProtection):
                OnPropertyChanged(nameof(CaptureProtection));
                ApplyCaptureProtection(Settings.CaptureProtection);
                break;
        }
    }

    /// <summary>⑬ 적용은 호스트가 한다. 호스트가 없으면(Android 는 MainActivity 가 이 속성을 구독) 설정만 저장된다.</summary>
    private void ApplyCaptureProtection(bool enabled)
    {
        if (App.WindowHost is not { } host)
        {
            return;
        }

        try
        {
            var ok = host.SetCaptureProtection(enabled);
            _ctx.AddLog(Loc.Tr(!ok ? "log.capture.unsupported" : enabled ? "log.capture.on" : "log.capture.off"));
        }
        catch (Exception ex)
        {
            _ctx.AddLog(Loc.Tr("log.capture.error", ex.Message));
        }
    }
}

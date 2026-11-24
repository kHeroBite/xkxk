// 시스템 트레이 메뉴의 바인딩 원본(Application.DataContext) — 표시 규칙(저장값·시험 억제·원격 강제)과 보이기·숨기기·종료 명령
using Pd.Poc.Shell;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Poc.Shell.Platform;

/// <summary>
/// 메뉴 배치는 App.axaml 의 TrayIcon.Icons 가 정본이다(Avalonia 문서 권장 — Application 에 DataContext).
/// 창 초기화·모니터 맞춤은 윈도우 메뉴(Vm.WindowMenuCommand), 도움말·정보는 시스템 메뉴(Vm.SystemViewCommand)와 같은 명령을 쓴다.
/// </summary>
public sealed partial class TrayMenuModel : ObservableObject
{
    private readonly Window _window;
    private bool? _remoteOverride;

    public TrayMenuModel(Window window, MainViewModel vm)
    {
        _window = window;
        Vm = vm;
        ShowCommand = new RelayCommand(Show);
        HideCommand = new RelayCommand(Hide);
        ExitCommand = new RelayCommand(Exit);
        vm.Extras.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellExtrasViewModel.TrayEnabled))
            {
                OnPropertyChanged(nameof(Visible));
            }
        };
    }

    public MainViewModel Vm { get; }

    /// <summary>
    /// 트레이 아이콘 실제 표시 여부(App.axaml IsVisible). 사용자 저장값(Vm.Extras.TrayEnabled, 기본 끔)을 따르되,
    /// 비전경 시험(--bg-pos·--offscreen-guard on → ShellPlatform.SuppressSavedTray)에서는 저장값을 무시하고 끈다.
    /// 진단 원격 tray.enable 로 정한 값(RemoteOverride)만 그보다 우선한다(team-lead 결정 — 시험 중 사용자 알림 영역 노출 방지).
    /// </summary>
    public bool Visible => _remoteOverride ?? (Vm.Extras.TrayEnabled && !ShellPlatform.SuppressSavedTray);

    /// <summary>진단 원격 tray.enable 이 넣는 이번 실행 한정 값(저장하지 않음). null 이면 저장값 규칙을 따른다.</summary>
    public bool? RemoteOverride
    {
        get => _remoteOverride;
        set
        {
            if (SetProperty(ref _remoteOverride, value))
            {
                OnPropertyChanged(nameof(Visible));
            }
        }
    }

    /// <summary>보이기(Windows 좌클릭 Command 와 같음): 숨김·최소화에서 일반 상태로 되돌린다. 비전경 시험 모드에서는 활성화하지 않는다.</summary>
    public RelayCommand ShowCommand { get; }

    public RelayCommand HideCommand { get; }

    /// <summary>종료: App.RequestExit → 메인 창 Close → 종료 자동 저장(레이아웃·설정 플러시) → Shutdown. X 버튼과 같은 경로다.</summary>
    public RelayCommand ExitCommand { get; }

    /// <summary>마지막으로 실행한 트레이 항목(진단 tray.state 판정용).</summary>
    public string? LastInvoked { get; private set; }

    internal void Mark(string item) => LastInvoked = item;

    private void Show()
    {
        Mark("show");
        ShellPlatform.RestoreMain(_window, activate: !Pd.Poc.Shell.Platform.SystemDialogHost.BackgroundMode);
    }

    private void Hide()
    {
        Mark("hide");
        ShellPlatform.HideMain(_window); // 소유 SDI 목록을 기억해 보이기에서 함께 복원(D-C3-8)
    }

    private void Exit()
    {
        Mark("exit");
        App.RequestExit();
    }
}

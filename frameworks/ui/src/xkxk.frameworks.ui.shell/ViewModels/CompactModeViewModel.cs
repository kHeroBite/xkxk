// 메인 뷰모델의 하위 뷰모델 — 좁은 폭(모바일·390px) 단일 문서 모드와 탐색기 드로어·알림 오버레이(Q16 후속)·화면 모드 스위치 상태
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>단일 문서 모드(모바일형)·화면 방식 연결. 바인딩 경로는 MainViewModel.Compact.X.</summary>
public sealed class CompactModeViewModel : ObservableObject
{
    private readonly ShellContext _ctx;
    private bool _isCompact;
    private bool _isDrawerOpen;
    private bool _isNotifyOpen;

    public CompactModeViewModel(ShellContext ctx)
    {
        _ctx = ctx;
        ToggleDrawerCommand = new RelayCommand(() => IsDrawerOpen = !IsDrawerOpen);
        ToggleNotifyCommand = new RelayCommand(() => IsNotifyOpen = !IsNotifyOpen);
        Style = new UiStyleViewModel(ctx);

        // 패널 기능 게이트 연결: 저장 모드 반영 + 적용 형 구독. 최초 적용은 StyleChanged 가 안 울리므로 Current 변경으로 따라간다.
        var panelFeature = ctx.Factory.PanelFeature;
        panelFeature.SetMode(ctx.Settings.PanelFeatureMode);
        panelFeature.Attach(Style);
        ctx.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiSettings.PanelFeatureMode)) // 설정 비동기 복원·콤보 변경 모두 같은 길
            {
                panelFeature.SetMode(ctx.Settings.PanelFeatureMode);
            }
        };
        Style.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiStyleViewModel.Current))
            {
                IsCompact = Style.Current == UiStyle.Mobile; // 모바일형 = 단일 문서 화면
                panelFeature.SetApplied(Style.Current);
            }

            if (e.PropertyName is nameof(UiStyleViewModel.Current) or nameof(UiStyleViewModel.SelectorVisible))
            {
                OnPropertyChanged(nameof(ShowsDesktopChrome));
            }
        };
        ctx.Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveDocument) && _isCompact)
            {
                OnPropertyChanged(nameof(CompactDocument));
            }
        };

        // 드로어에서 문서를 열거나 고르면 드로어·알림 오버레이를 닫아 문서를 보이게 한다.
        ctx.Commands.ActiveChanged += _ =>
        {
            if (_isCompact)
            {
                IsDrawerOpen = false;
                IsNotifyOpen = false;
            }
        };
    }

    /// <summary>화면 방식(PC형·태블릿형·모바일형) 선택·적용·전환 컨트롤.</summary>
    public UiStyleViewModel Style { get; }

    /// <summary>
    /// 단일 문서 모드: Dock 영역(탐색기·문서·로그·알림 패널)을 숨기고 활성 문서 1개만 전체 화면으로 보인다.
    /// Dock 레이아웃 모델은 건드리지 않으므로 폭이 돌아오면 원래 배치가 그대로 다시 보인다.
    /// </summary>
    public bool IsCompact
    {
        get => _isCompact;
        private set
        {
            if (SetProperty(ref _isCompact, value))
            {
                OnPropertyChanged(nameof(IsWide));
                OnPropertyChanged(nameof(CompactDocument));
                OnPropertyChanged(nameof(DrawerExplorer));
                IsDrawerOpen = false;
                IsNotifyOpen = false;
                _ctx.AddLog(Loc.Tr(value ? "log.window.compactOn" : "log.window.compactOff"));
            }
        }
    }

    public bool IsWide => !_isCompact;

    /// <summary>
    /// 제목줄 메뉴·도구 모음 표시. 폰 기기의 모바일형만 숨겨 ☰ 서랍·하단 탭으로 대신한다(06_모바일_화면설계 §2, ADR-051 결정 11).
    /// PC·태블릿에서 모바일형을 고른 경우는 캡션 버튼과 형 전환 스위치가 여기 있어 남긴다.
    /// </summary>
    public bool ShowsDesktopChrome => !(Style.Current == UiStyle.Mobile && !Style.SelectorVisible);

    /// <summary>단일 문서 모드에서만 활성 문서를 내준다. 넓은 폭에서는 null 이라 숨은 화면에 뷰가 중복 생성되지 않는다(데스크톱 성능 회귀 방지).</summary>
    public PocDocument? CompactDocument => _isCompact ? _ctx.Main.ActiveDocument : null;

    /// <summary>단일 문서 모드 드로어의 탐색기(넓은 폭에서는 null).</summary>
    public ExplorerTool? DrawerExplorer => _isCompact ? _ctx.Explorer : null;

    /// <summary>탐색기 드로어(햄버거) 열림 여부. 열면 알림 오버레이는 닫는다.</summary>
    public bool IsDrawerOpen
    {
        get => _isDrawerOpen;
        set
        {
            if (SetProperty(ref _isDrawerOpen, value) && value)
            {
                IsNotifyOpen = false;
            }
        }
    }

    /// <summary>
    /// 단일 문서 모드의 알림 오버레이 열림 여부(Q16 후속 — 접힘 화면에는 알림 패널이 보이지 않아 따로 여는 경로). 열면 드로어는 닫는다.
    /// 문서를 고르면(활성 변경) 드로어와 같은 규칙으로 닫힌다.
    /// </summary>
    public bool IsNotifyOpen
    {
        get => _isNotifyOpen;
        set
        {
            if (SetProperty(ref _isNotifyOpen, value))
            {
                if (value)
                {
                    IsDrawerOpen = false;
                }

                OnPropertyChanged(nameof(CompactNotifications));
            }
        }
    }

    /// <summary>
    /// 오버레이에 넣을 알림 패널 모델(odev-1 Factory.Notifications). 단일 문서 모드에서 열려 있을 때만 내준다 —
    /// 넓은 폭에서는 Dock 안 알림 패널이 같은 모델을 그리므로, 보이지 않는 곳에 두 번째 뷰를 만들지 않는다.
    /// </summary>
    public object? CompactNotifications => _isCompact && _isNotifyOpen ? _ctx.Factory.Notifications : null;

    public RelayCommand ToggleDrawerCommand { get; }
    public RelayCommand ToggleNotifyCommand { get; }

    private double _viewWidth;

    /// <summary>현재 뷰 폭(DIP, Android 는 dp). 진단 조회용.</summary>
    public double ViewWidth => _viewWidth;

    /// <summary>뷰 크기 변경 시 호출(MainView.SizeChanged). 형 판정은 Style(UiStyleRules)이 한다 — 높이는 기기 판별(짧은 변)용, 0 이면 폭으로 대신한다.</summary>
    public void UpdateWidth(double width, double height = 0)
    {
        if (width > 0)
        {
            _viewWidth = width;
            Style.UpdateSize(width, height);
        }
    }
}

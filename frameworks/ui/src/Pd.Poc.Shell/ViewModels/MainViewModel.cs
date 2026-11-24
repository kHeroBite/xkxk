// 셸 메인 뷰모델 — Dock 레이아웃·창 관리 명령·로그·상태바(FPS)·이름 변경 상태와 하위 뷰모델(제목 줄·단일 문서 모드·부가 기능·설정·시스템·윈도우 메뉴)
using Pd.Client.Data.Layout;
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels;

// 탐색기·로그 도구 패널 모델(ExplorerTool·LogTool)은 각자 파일(공개 타입 1개 = 파일 1개, 분할규칙 3.3)
public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 200;
    private string _statusText = ""; // 진단 빌드는 FPS, 제품 빌드는 비어 있음(문구는 다국어 대상이 아님)
    private PocDocument? _activeDocument;
    private PocDocument? _renameTarget;
    private string _renameText = "";
    private bool _doubleClickCloseEnabled = true;

    public MainViewModel(ILayoutStore store)
    {
        var explorer = new ExplorerTool(this) { Id = "Explorer", Title = Loc.Tr("panel.explorer"), CanClose = false };
        var log = new LogTool(this) { Id = "Log", Title = Loc.Tr("panel.log"), CanClose = false };
        Loc.Instance.LanguageChanged += () =>
        {
            explorer.Title = Loc.Tr("panel.explorer"); // 도구 탭 제목도 언어를 따라간다(키는 odev-1 panel 영역)
            log.Title = Loc.Tr("panel.log");
        };
        Explorer = explorer;
        Factory = new PocDockFactory(explorer, log);
        Layout = Factory.CreateLayout();
        Factory.InitLayout(Layout);
        Commands = new WindowCommands(Factory, store, AddLog);
        Commands.ActiveChanged += SyncActive;

        OpenCommand = new RelayCommand<string>(k => Commands.OpenDocument(ParseKind(k)));
        OpenSdiCommand = new RelayCommand<string>(k => Commands.OpenDocument(ParseKind(k), sdi: true));
        ArrangeCommand = new RelayCommand<string>(a => Commands.Arrange(Enum.Parse<ShellArrange>(a ?? nameof(ShellArrange.Cascade))));
        PresetCommand = new RelayCommand<string>(p => Commands.ApplyPreset(Enum.Parse<LayoutPreset>(p ?? nameof(LayoutPreset.MdiCascade))));
        MinimizeAllCommand = new RelayCommand(Commands.MinimizeAll);
        RestoreAllCommand = new RelayCommand(Commands.RestoreAll);
        NextCommand = new RelayCommand(Commands.ActivateNext);
        PreviousCommand = new RelayCommand(Commands.ActivatePrevious);
        CloseAllCommand = new RelayCommand(Commands.CloseAll);
        ActivateCommand = new RelayCommand<PocDocument>(d => Run(d, Commands.Activate));
        CloseCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, Commands.CloseDocument));
        CloseOthersCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, Commands.CloseOthers));
        FloatCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, Commands.Float));
        RedockCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, Commands.Redock));
        PinCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, x => Commands.TogglePin(x)));
        BeginRenameCommand = new RelayCommand<PocDocument>(BeginRename);
        ConfirmRenameCommand = new RelayCommand(ConfirmRename);
        CancelRenameCommand = new RelayCommand(() => RenameTarget = null);
        TabClickCommand = new RelayCommand<PocDocument>(d => Run(d, x => Commands.ActivateOrMinimize(x, ActivationOrigin.TitleTab)));
        MinimizeCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, x => Commands.SetState(x, MdiWindowState.Minimized)));
        MaximizeToggleCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, x =>
            Commands.SetState(x, x.MdiState == MdiWindowState.Maximized ? MdiWindowState.Normal : MdiWindowState.Maximized)));
        RestoreCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, x => Commands.SetState(x, MdiWindowState.Normal)));
        RestoreLastMinimizedCommand = new RelayCommand(() => Commands.RestoreLastMinimized());
        ToggleSdiCommand = new RelayCommand<PocDocument>(d => Run(d ?? ActiveDocument, doc => Busy.BusyHooks.RunAfterFrame(doc.Id, "sdi", () => { if (OpenDocuments.Contains(doc)) { Commands.ToggleSdi(doc); } }))); // 진행 표시(요건 2): 스피너를 먼저 그린 뒤 전환
        ResetLayoutCommand = new RelayCommand(Commands.ResetLayout);
        EscCloseCommand = new RelayCommand(() => TryEscClose(null)); // 키 입력은 EscCloseInput(메인·SDI 공통), 자동화도 같은 경로
        SaveLayoutCommand = new AsyncRelayCommand(Commands.SaveLayoutAsync);
        LoadLayoutCommand = new AsyncRelayCommand(async () => await Commands.LoadLayoutAsync());

        // 하위 뷰모델 생성 순서 = 옛 Init* 호출 순서(Settings.PropertyChanged·Loc.LanguageChanged·ActiveChanged 구독 순서 보존)
        var ctx = new ShellContext(this);
        Extras = new ShellExtrasViewModel(ctx);
        Compact = new CompactModeViewModel(ctx);
        SettingsPanel = new SettingsPanelViewModel(ctx);
        WindowMenu = new WindowMenuViewModel(ctx);
        SettingsPanel.LanguageChanged += WindowMenu.RefreshLabels;
        SettingsPanel.InitLanguage();
        SystemMenu = new SystemMenuViewModel(ctx); // UI-17 시스템 메뉴
        // 제목줄 즐겨찾기(REQ-113) — 기기 로컬 저장소(호스트가 주입한 개인 저장소, 실행 격리 포함). 제목 줄이 표시 조건에 쓰므로 그보다 먼저 만든다
        FavoriteBar = new FavoriteBarViewModel(ctx, new Pd.Client.Data.Navigation.LocalPersonalFavoriteStore(App.LayoutLibraryStore ?? new MemoryKeyValueStore()));
        TitleStrip = new TitleStripViewModel(ctx); // Compact 를 읽으므로 그 뒤에 만든다
        Commands.ApplyProductToolVisibility(); // 제품 빌드: 로그 도구 숨김(진단 기능)
        _ = new BackgroundFallbackNotifier(Factory.Notifications); // 배경 낮춤 알림(09/21 §6.2) — 정적 이벤트 구독이 수명을 잡는다
    }

    /// <summary>탐색기 도구 모델(단일 문서 모드 드로어에서도 같은 DataTemplate 으로 표시).</summary>
    public ExplorerTool Explorer { get; }

    public PocDockFactory Factory { get; }

    public IRootDock Layout { get; }

    public WindowCommands Commands { get; }

    /// <summary>UI 설정 서비스(테마·글자 크기·폰트). 값 변경은 즉시 전 창에 반영된다.</summary>
    public UiSettings Settings => UiSettings.Current;

    /// <summary>단일 문서 모드·화면 모드(태블릿/폰).</summary>
    public CompactModeViewModel Compact { get; }

    /// <summary>창 반짝임·프로젝트 문맥·트레이·벤치.</summary>
    public ShellExtrasViewModel Extras { get; }

    /// <summary>설정 패널·언어 선택.</summary>
    public SettingsPanelViewModel SettingsPanel { get; }

    /// <summary>시스템 메뉴(UI-17).</summary>
    public SystemMenuViewModel SystemMenu { get; }

    /// <summary>윈도우 메뉴(자동저장·Esc·자석·캡처 방지·호스트 명령·레이아웃 관리).</summary>
    public WindowMenuViewModel WindowMenu { get; }

    /// <summary>제목 줄(제목 탭·독립 창 아이콘).</summary>
    public TitleStripViewModel TitleStrip { get; }

    /// <summary>제목줄 즐겨찾기 영역(REQ-113).</summary>
    public FavoriteBarViewModel FavoriteBar { get; }

    /// <summary>열린 문서 전체(열린 순서). 제목 줄 제목 탭은 TitleStrip.TitleTabs 를 쓴다.</summary>
    public ObservableCollection<PocDocument> OpenDocuments => Commands.OpenDocuments;

    public ObservableCollection<string> LogLines { get; } = new();

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>활성 문서. UI 선택(TwoWay)으로 바뀌면 그 문서를 활성화한다.</summary>
    public PocDocument? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            // 단일 출처는 WindowCommands.ActiveDocument(=Dock 활성 문서). UI(탭·콤보) 선택은 활성화 요청으로만 쓰고,
            // null·같은 값 푸시는 현재 활성으로 되돌려 탭 강조가 Dock 과 어긋나지 않게 한다(MDI-10).
            if (value is not null && !ReferenceEquals(value, Commands.ActiveDocument))
            {
                Commands.Activate(value); // ActiveChanged → SyncActive 로 반영
            }
            else
            {
                SyncActive(Commands.ActiveDocument);
            }
        }
    }

    /// <summary>이름 변경 중인 문서. null 이면 이름 변경 패널 숨김.</summary>
    public PocDocument? RenameTarget
    {
        get => _renameTarget;
        private set
        {
            if (SetProperty(ref _renameTarget, value))
            {
                OnPropertyChanged(nameof(IsRenaming));
            }
        }
    }

    public bool IsRenaming => _renameTarget is not null;

    /// <summary>작업표시줄 탭 더블클릭으로 창 닫기 옵션(Mars Fn더블클릭창닫기, MDI-08). 기본 켬.</summary>
    public bool DoubleClickCloseEnabled
    {
        get => _doubleClickCloseEnabled;
        set => SetProperty(ref _doubleClickCloseEnabled, value);
    }

    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }

    public RelayCommand<string> OpenCommand { get; }
    public RelayCommand<string> OpenSdiCommand { get; }
    public RelayCommand<string> ArrangeCommand { get; }
    public RelayCommand<string> PresetCommand { get; }
    public RelayCommand MinimizeAllCommand { get; }
    public RelayCommand RestoreAllCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand CloseAllCommand { get; }
    public RelayCommand<PocDocument> ActivateCommand { get; }
    public RelayCommand<PocDocument> CloseCommand { get; }
    public RelayCommand<PocDocument> CloseOthersCommand { get; }
    public RelayCommand<PocDocument> FloatCommand { get; }
    public RelayCommand<PocDocument> RedockCommand { get; }
    public RelayCommand<PocDocument> PinCommand { get; }
    public RelayCommand<PocDocument> BeginRenameCommand { get; }
    public RelayCommand<PocDocument> TabClickCommand { get; }
    public RelayCommand<PocDocument> MinimizeCommand { get; }
    public RelayCommand<PocDocument> MaximizeToggleCommand { get; }
    public RelayCommand<PocDocument> RestoreCommand { get; }
    public RelayCommand RestoreLastMinimizedCommand { get; }
    public RelayCommand<PocDocument> ToggleSdiCommand { get; }
    public RelayCommand ResetLayoutCommand { get; }
    public RelayCommand EscCloseCommand { get; }
    public RelayCommand ConfirmRenameCommand { get; }
    public RelayCommand CancelRenameCommand { get; }
    public AsyncRelayCommand SaveLayoutCommand { get; }
    public AsyncRelayCommand LoadLayoutCommand { get; }

    public void AddLog(string line)
    {
        LogLines.Insert(0, $"{DateTime.Now:HH:mm:ss} {line}");
        while (LogLines.Count > MaxLogLines)
        {
            LogLines.RemoveAt(LogLines.Count - 1);
        }
    }

    /// <summary>Esc 창닫기(본문은 WindowMenuViewModel.TryEscClose). 키 입력(EscCloseInput)·EscCloseCommand 가 부른다.</summary>
    public bool TryEscClose(PocDocument? doc) => WindowMenu.TryEscClose(doc);

    /// <summary>WindowCommands 활성 문서를 그대로 반영(값이 같아도 알림을 보내 탭 선택을 강제 동기화).</summary>
    private void SyncActive(PocDocument? doc)
    {
        _activeDocument = doc;
        OnPropertyChanged(nameof(ActiveDocument));
    }

    private static DocumentKind ParseKind(string? kind) =>
        Enum.TryParse<DocumentKind>(kind, ignoreCase: true, out var k) ? k : DocumentKind.Grid;

    private static void Run(PocDocument? doc, Action<PocDocument> action)
    {
        if (doc is not null)
        {
            action(doc);
        }
    }

    private void BeginRename(PocDocument? doc)
    {
        doc ??= ActiveDocument;
        if (doc is null)
        {
            return;
        }

        RenameText = doc.Title;
        RenameTarget = doc;
    }

    private void ConfirmRename()
    {
        if (RenameTarget is not null && !string.IsNullOrWhiteSpace(RenameText))
        {
            Commands.Rename(RenameTarget, RenameText.Trim());
        }

        RenameTarget = null;
    }
}

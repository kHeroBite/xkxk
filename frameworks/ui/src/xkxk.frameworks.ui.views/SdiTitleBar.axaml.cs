// SDI 창 제목줄 동작 — 제목·활성/항상 위 틀 색 갱신, 끌기 이동·더블클릭 최대화, 최소/최대/재결합/닫기(셸 명령 경유)
using xkxk.frameworks.ui.shell;
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

public partial class SdiTitleBar : UserControl
{
    private PocDocument? _doc;

    /// <summary>이 제목줄이 보여 주는 문서(창 메뉴 대상).</summary>
    public PocDocument? Document => _doc;

    /// <summary>문서 없이 패널 탭만 남은 창이면 그 창(창 메뉴·제목줄 버튼 대상). 문서가 있으면 null 이다.</summary>
    public IDockWindow? PanelOnlyWindow => _doc is null ? _root?.Window : null;

    private Window? _window;

    private readonly SdiDocumentWatch _watch;

    public SdiTitleBar()
    {
        _watch = new SdiDocumentWatch(() => _doc, RefreshDocument);
        InitializeComponent();
        Bar.SizeChanged += (_, _) => FitButtons();
    }

    private double _buttonsWidth; // 기능 버튼 줄이 보일 때 마지막으로 잰 폭

    /// <summary>
    /// U9 ⑩: 폭이 "최소 창 폭(캡션 3버튼·로고·제목 최소·틀, WindowMinSize) + 기능 버튼 줄" 보다 좁으면 기능 버튼 줄을 숨겨 캡션 3버튼이 항상 보이게 한다
    /// (c2dev_g 실측 — 좁은 창에서 기능 버튼이 캡션을 창 밖으로 밀어냄). 넓어지면 다시 보인다.
    /// </summary>
    private void FitButtons()
    {
        if (Buttons.IsVisible && Buttons.Bounds.Width > 0)
        {
            _buttonsWidth = Buttons.Bounds.Width;
        }

        Buttons.IsVisible = Bar.Bounds.Width <= 0 || Bar.Bounds.Width >= WindowMinSize.Current.Width + _buttonsWidth;
    }

    private IRootDock? _root;

    /// <summary>DataContext = SDI 창의 IRootDock(HostWindow 내용). 그 안의 문서를 찾아 제목·버튼에 연결한다.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_root is INotifyPropertyChanged oldRoot)
        {
            oldRoot.PropertyChanged -= OnRootChanged;
        }

        _root = DataContext as IRootDock;
        if (_root is INotifyPropertyChanged newRoot)
        {
            newRoot.PropertyChanged += OnRootChanged;
        }

        SetDocument(DataContext as PocDocument ?? Find(_root?.FocusedDockable) ?? Find(_root));
        _watch.Watch(_root?.Factory as PocDockFactory);
    }

    private void OnRootChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IRootDock.FocusedDockable))
        {
            return;
        }

        if (Find(_root?.FocusedDockable) is { } doc)
        {
            SetDocument(doc);
        }
        else
        {
            RefreshDocument();
        }
    }

    /// <summary>
    /// 보여 주던 문서가 이 창에서 빠졌으면(닫힘·다른 곳으로 이동) 남은 문서로 바꾼다. 패널 탭만 남으면 문서 없이 패널 제목을 보인다.
    /// 닫힌 문서를 붙들고 있으면 제목이 남고 제목줄 버튼이 듣지 않는다. 초점이 패널 탭에 있으면 문서를 닫아도 초점 변경이 오지 않으므로 팩토리의 제거 알림으로도 부른다.
    /// </summary>
    private void RefreshDocument()
    {
        if (_doc is not null && _root is not null && LayoutSerializer.Contains(_root, _doc))
        {
            return;
        }

        var live = Find(_root);
        if (!ReferenceEquals(live, _doc))
        {
            SetDocument(live);
        }

        if (live is null)
        {
            TitleText.Text = SdiDocumentWatch.ActiveLeaf(_root)?.Title ?? "";
        }
    }

    private void SetDocument(PocDocument? doc)
    {
        if (_doc is not null)
        {
            _doc.PropertyChanged -= OnDocChanged;
            WindowCommands.ChromeOf(_doc).PropertyChanged -= OnChromeChanged;
        }

        _doc = doc;
        if (_doc is not null)
        {
            _doc.PropertyChanged += OnDocChanged;
            WindowCommands.ChromeOf(_doc).PropertyChanged += OnChromeChanged; // 항상 위 → 적갈색 틀
        }

        Buttons.DataContext = _doc;
        (DocIcon.Document, DocIcon.IsVisible) = (_doc, _doc is not null); // 로고 옆 제목 앞 창 아이콘(22 §2.3)
        TitleText.Text = _doc?.Title ?? "";
        OnActiveChanged(null, EventArgs.Empty);
    }

    private void OnChromeChanged(object? sender, PropertyChangedEventArgs e) => OnActiveChanged(null, EventArgs.Empty);

    /// <summary>도킹 트리에서 활성 쪽 우선으로 첫 PocDocument — ChromeButtons 와 같은 공용 규칙(WindowCommands.FindDocument).</summary>
    private static PocDocument? Find(IDockable? dockable) => WindowCommands.FindDocument(dockable);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        _watch.Watch(_root?.Factory as PocDockFactory);
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowProperty;
        }

        XkxkUi.Current.Localization.LanguageChanged -= UpdateMaxIcon;
        XkxkUi.Current.Localization.LanguageChanged += UpdateMaxIcon; // 코드로 넣은 최대화 툴팁을 언어 전환 때 다시 만든다(Q17)
        CaptionLayout.Applied -= OnCaptionApplied;
        CaptionLayout.Applied += OnCaptionApplied; // 크롬 스타일(Windows/macOS) 전환 때 기호·네이티브 신호등 사용을 다시 맞춘다
        OnActiveChanged(null, EventArgs.Empty);
        OnCaptionApplied();
    }

    /// <summary>macOS 실행 + macOS 스타일이면 OS 신호등(창 장식 Full)을 쓰고 앱 3버튼을 숨긴다. Windows 에서 강제 macOS 면 앱이 그린 신호등(검수용).</summary>
    private void OnCaptionApplied()
    {
        CaptionOps.IsVisible = !MacNativeCaption.Apply(Bar, _window, ownsDecorations: true);
        UpdateMaxIcon();
        OnActiveChanged(null, EventArgs.Empty); // 스타일 전환 때 틀 클래스(pdWinChrome)도 다시 맞춘다
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _watch.Watch(null);
        XkxkUi.Current.Localization.LanguageChanged -= UpdateMaxIcon;
        CaptionLayout.Applied -= OnCaptionApplied;
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowProperty;
        }

        _window = null;
    }

    private void OnDocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDockable.Title))
        {
            TitleText.Text = _doc?.Title ?? "";
        }
    }

    /// <summary>틀(Border.pdSdiFrame: 여백 띠+제목줄 바탕) 색: 활성 진한 색(.active), 항상 위 적갈색(.pinned) — 스타일은 Themes/MdiChrome.axaml.</summary>
    private void OnActiveChanged(object? sender, EventArgs e)
    {
        Border? frame = null;
        for (var v = this.GetVisualParent(); v is not null && frame is null; v = v.GetVisualParent())
        {
            frame = v is Border b && b.Classes.Contains("pdSdiFrame") ? b : null;
        }

        frame?.Classes.Set("active", _window?.IsActive == true);
        frame?.Classes.Set("pinned", _doc is not null && WindowCommands.ChromeOf(_doc).IsPinned);
        frame?.Classes.Set("pdWinChrome", WindowCommands.EffectiveStyle == ChromeStyleKind.Windows); // C3 B1: Windows 스타일 SDI 는 Dock 문서 테두리 0(외곽선은 OS DWM 1px r=8) — MdiChrome.axaml
        if (_window is not null)
        {
            _window.CanResize = !IsLocked; // 위치 고정: OS 수준 크기 조절 잠금(이동은 OnDragPressed 가 막음)
        }
    }

    private bool IsLocked => _doc is not null && WindowCommands.ChromeOf(_doc).IsLocked;

    private void OnRedock(object? sender, RoutedEventArgs e)
    {
        if (_doc is { } doc && XkxkUi.Current.Shell is { } shell)
        {
            // 진행 표시(요건 2): 스피너를 먼저 그린 뒤 재결합한다. 그 사이 닫힌 문서는 건너뛴다
            xkxk.frameworks.ui.shell.Busy.BusyHooks.RunAfterFrame(doc.Id, "redock", () =>
            {
                if (System.Linq.Enumerable.Contains(shell.Documents, doc))
                {
                    shell.Redock(doc);
                }
            });
        }
    }

    private void OnWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaxIcon();
        }
        else if (e.Property == WindowBase.IsActiveProperty)
        {
            OnActiveChanged(null, EventArgs.Empty); // Activated 이벤트는 IsActive 를 올리기 전에 와서 쓸 수 없다(디컴파일 WindowBase.HandleActivated, w12 Z4)
        }
    }

    private void UpdateMaxIcon()
    {
        var max = _window?.WindowState == WindowState.Maximized;
        ToolTip.SetTip(BtnMax, Loc.Tr(max ? "chrome.tip.restore" : "chrome.tip.maximize"));
        var key = max ? "PdCapRestoreGeometry" : "PdCapMaxGeometry"; // 스타일별 기호 별칭(Windows = Dock 기호, macOS = 확대 삼각형)
        if (this.TryFindResource(key, out var g) && g is Geometry geometry)
        {
            MaxIcon.Data = geometry;
        }
    }

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is not null && !IsLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1)
        {
            _window.BeginMoveDrag(e);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => OnMax(sender, e);

    private void OnMin(object? sender, RoutedEventArgs e) => SetState(MdiWindowState.Minimized);

    private void OnMax(object? sender, RoutedEventArgs e) =>
        SetState(_window?.WindowState == WindowState.Maximized ? MdiWindowState.Normal : MdiWindowState.Maximized);

    private void SetState(MdiWindowState state)
    {
        if (IsLocked && WindowCommands.LockBlocksMinMax)
        {
            return; // 옵션: 위치 고정 중 최소·최대 차단(IDE 조사 결론 대기, 기본 허용)
        }

        if (_doc is not null && XkxkUi.Current.Shell is { } shell)
        {
            shell.SetState(_doc, state);
        }
        else if (_doc is null)
        {
            RunOnPanelWindow(state switch
            {
                MdiWindowState.Minimized => WindowMenuAction.Minimize,
                MdiWindowState.Maximized => WindowMenuAction.Maximize,
                _ => WindowMenuAction.Restore,
            });
        }
    }

    /// <summary>패널 탭만 남은 창의 제목줄 버튼. 창 메뉴와 같은 경로다(WindowMenuOps).</summary>
    private void RunOnPanelWindow(WindowMenuAction action) => WindowMenuOps.Run(new WindowMenuTarget(WindowMenuKind.Sdi, null, null, PanelOnlyWindow), action);

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (_doc is not null && XkxkUi.Current.Shell is { } shell)
        {
            shell.CloseDocument(_doc);
        }
        else if (_doc is null)
        {
            RunOnPanelWindow(WindowMenuAction.Close); // 패널 탭만 남은 창은 창째 닫는다. 패널은 보기 메뉴로 되찾는다(웹 관리형 창 ✕ 와 같음)
        }
    }
}

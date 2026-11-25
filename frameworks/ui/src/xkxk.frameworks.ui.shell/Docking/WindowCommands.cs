// MDI/SDI 창 관리 명령의 파사드(IShellCommands 구현) — 동작은 문서 생명주기·활성화·창 상태·레이아웃 복원과 크롬·Z 순서·배율·패널·SDI·자동 제목 서비스가 맡는다
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// IShellCommands 구현. Mars 창 관리 Fn 을 Dock API 로 대응한다(대응표: plan/04_기술스택/03_PoC_UI_합격기준_Mars실측.md MDI-01~20).
/// W3(partial 해소): 공개 API 는 이 클래스에 그대로 두고 본문은 서비스로 옮겼다. 공유 상태는 WindowSession 이 가진다.
/// </summary>
[WindowCommandsPart]
public sealed partial class WindowCommands : IShellCommands
{
    private readonly WindowSession _s;

    public WindowCommands(PocDockFactory factory, ILayoutStore store, Action<string> log)
    {
        // 서비스는 생성자에서 s 만 저장한다(구독·설치 없음). 모두 만든 뒤 아래에서 원래 순서대로 설치·구독한다(R3).
        _s = new WindowSession(this, factory, store, log);
        AutoTitles = new DocumentAutoTitles(_s);
        ChromeService = new DocumentChromeService(_s);
        MdiZOrder = new MdiZOrder(_s);
        DocumentZoom = new DocumentZoom(_s);
        SidePanels = new SidePanelVisibility(_s);
        SdiPlacement = new SdiPlacement(_s);
        Activation = new WindowActivation(_s);
        StateOps = new WindowStateOps(_s);
        Restorer = new LayoutRestorer(_s);
        Lifecycle = new DocumentLifecycle(_s);

        AutoTitles.InstallAutoTitles(); // 자동 제목 언어 갱신(DocumentAutoTitles)
        new MdiActiveSync(_s).Install(); // 분리 창 활성 중 MDI 제목줄 표시
#if PD_DIAGNOSTICS
        // D-12 진단: 열린 문서 목록에서 빠지는 모든 경로(DockableClosed 를 거치지 않는 제거·Clear 포함)를 호출 위치와 함께 남긴다
        OpenDocuments.CollectionChanged += (_, e) =>
        {
            if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Add or System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            {
                return;
            }

            var gone = e.OldItems?.OfType<PocDocument>().Select(d => d.Id) ?? Enumerable.Empty<string?>();
            var frames = ViaFrames(6);
            _s.Log($"[d12] list-{e.Action} ids={string.Join(",", gone)} count={OpenDocuments.Count} via={string.Join(">", frames)}");
        };
#endif
        factory.DockableClosed += (_, e) =>
        {
#if PD_DIAGNOSTICS
            // D-12 진단: 어떤 경로로 문서가 닫혔는지(떠 있는 창 소속·목록 소속·호출 위치) — layout save/load 왕복 중 문서 유실 원인 판정용
            if (e.Dockable is PocDocument d12)
            {
                var frames = ViaFrames(8);
                // floating 은 넣지 않는다 — Dock 이 창에서 뺀 뒤 이 이벤트를 보내 늘 False 였다(i1113 60회 실측). SDI 여부는 CloseAll 직전 줄로 본다
                _s.Log($"[d12] closed id={d12.Id} inList={OpenDocuments.Contains(d12)} via={string.Join(">", frames)}");
            }
#endif
            if (e.Dockable is PocDocument doc && OpenDocuments.Remove(doc))
            {
                _s.Mru.Remove(doc);
                _s.Minimized.Remove(doc);
                WindowLoadState.Default.Release(doc); // 닫힘 해제 — 토큰 무효·오버레이·커서·예약 정리(23 §2.1)
                Activation.ClearOrigin(); // 닫은 뒤 다음 창 활성은 사용자 전환이 아니다(출처 오귀속 방지)
                _s.Log(Loc.Tr("log.window.closed", doc.Title));
                if (ReferenceEquals(ActiveDocument, doc))
                {
                    SetActive(_s.Mru.FirstOrDefault()); // 닫은 문서 이름이 상태줄에 남지 않게 한다(Dock 은 활성 없음을 알리지 않는다). 다음 활성 변경이 오면 그 값이 덮는다
                }

                Dispatcher.UIThread.Post(factory.Hide.SyncSidePanels); // 닫기는 MoveDockable 을 타지 않는다. 마지막 탭이 빠진 패널을 바로 접는다
            }
        };
        factory.ActiveDockableChanged += (_, e) =>
        {
            if (e.Dockable is not PocDocument doc)
            {
                return;
            }

            // 레이아웃 복원 직후 Dock 이 늦게 보내는 활성 변경(마지막 추가 문서)은 무시하고 저장된 활성 문서를 다시 적용(MDI-10).
            if (_s.RestoreTarget is { } target && !ReferenceEquals(doc, target)
                && Environment.TickCount64 < _s.RestoreUntil && OpenDocuments.Contains(target))
            {
                Dispatcher.UIThread.Post(() => Activation.ActivateCore(target, restoreMinimized: false));
                return;
            }

            SetActive(doc);
        };
        // 관리형 창(웹·Android SDI) 클릭은 Dock 이 창 래퍼(ManagedDockWindowDocument)만 활성화해 ActiveDockableChanged 가 오지 않는다(D-WPF-F4)
        // 이미 Dock 활성인 MDI 문서를 클릭해 분리 창에서 돌아올 때도 변경 이벤트가 없어 문서 활성화 알림으로 받는다(디컴파일 MdiDocumentWindow)
        factory.DockableActivated += (_, e) =>
        {
            if (e.Dockable is ManagedDockWindowDocument or PocDocument or MdiTabGroupDock) // 탭 묶음 창 클릭은 묶음이 활성화된다 → 그 활성 탭(FindDocument)
            {
                ActivateFromWindow(e.Dockable);
            }
        };
        // OS 분리 창이 전경이 되면 Dock 은 창 안의 문서 영역만 알린다(디컴파일 HostWindow_Activated). 창 활성화로 그 문서를 활성으로 둔다
        factory.WindowActivated += (_, e) => ActivateFromWindow(e.Window?.Layout);
        // 패널·탭 모드 문서는 본문을 눌러도 활성화 알림이 없다. 활성 탭이 문서일 때만 받는다(도구 탭이면 건드리지 않는다)
        Avalonia.Input.InputElement.PointerPressedEvent.AddClassHandler<Dock.Avalonia.Controls.DocumentControl>(
            (c, _) => ActivateFromWindow((c.DataContext as IDock)?.ActiveDockable as PocDocument), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        StateOps.InstallTabClickGuard(); // 분리 창에서 돌아오는 첫 클릭을 가린다
    }

    private void ActivateFromWindow(object? context)
    {
        // 앱 모달(설정 창) 중에는 누름·창 활성화 알림으로 문서를 바꾸지 않는다(03_2 §8.2 — 아래 누름 처리기는 handledEventsToo 라 입력 관문의 Handled 를 무시한다)
        if (Platform.ModalScope.IsActive || FindDocument(context) is not { } doc || ReferenceEquals(doc, ActiveDocument) || !OpenDocuments.Contains(doc))
        {
            return;
        }

        // 레이아웃 복원 보호 기간에는 위 ActiveDockableChanged 처리기와 같은 규칙으로 무시한다(MDI-10)
        if (_s.RestoreTarget is { } target && !ReferenceEquals(doc, target)
            && Environment.TickCount64 < _s.RestoreUntil && OpenDocuments.Contains(target))
        {
            return;
        }

        SetActive(doc);
    }

    /// <summary>작업표시줄 탭 스트립이 바인딩하는 열린 문서 목록(열린 순서).</summary>
    public ObservableCollection<PocDocument> OpenDocuments { get; } = new();

    public IReadOnlyList<PocDocument> Documents => OpenDocuments;

    public PocDocument? ActiveDocument { get; private set; }

    /// <summary>활성 문서 변경 알림(탭 스트립 하이라이트용).</summary>
    public event Action<PocDocument?>? ActiveChanged;

    // ── 서비스(배정표 w3_assignment.md 2절 — 이름 고정) ──
    internal DocumentLifecycle Lifecycle { get; }
    internal WindowActivation Activation { get; }
    internal WindowStateOps StateOps { get; }
    internal LayoutRestorer Restorer { get; }
    internal DocumentChromeService ChromeService { get; }
    internal MdiZOrder MdiZOrder { get; }
    internal DocumentZoom DocumentZoom { get; }
    internal SidePanelVisibility SidePanels { get; }
    internal SdiPlacement SdiPlacement { get; }
    internal DocumentAutoTitles AutoTitles { get; }

    internal void SetActive(PocDocument? doc)
    {
        if (doc is not null)
        {
            _s.Mru.Remove(doc);
            _s.Mru.Insert(0, doc);
        }

        var changed = !ReferenceEquals(ActiveDocument, doc);
        ActiveDocument = doc;
        Activation.NotifyActive(doc, changed, () => ActiveChanged?.Invoke(doc)); // 구독자는 CurrentActivationOrigin 으로 출처를 읽는다(.Origin.cs)
    }

    internal void AddDocument(PocDocument doc, bool activate = true) => Lifecycle.AddDocument(doc, activate);
#if PD_DIAGNOSTICS

    /// <summary>
    /// D-12 진단 via= 의 호출 위치(형식.메서드, 가까운 쪽부터 take 개). 옛 한 클래스 판과 같은 문자열을 내려고
    /// [WindowCommandsPart] 형식은 nameof(WindowCommands) 로 쓰고, 파사드·서비스 전달자 때문에 생긴 같은 이름의 연속 프레임은 하나로 합친다.
    /// 호출한 이벤트 처리기 람다(1프레임)와 이 메서드(1프레임)는 옛 StackTrace(1) 과 같게 건너뛴다.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] // 건너뛸 프레임 수(2)가 흔들리지 않게
    private static List<string> ViaFrames(int take)
    {
        var names = new List<string>();
        foreach (var m in new System.Diagnostics.StackTrace(2, false).GetFrames().Select(f => f.GetMethod()).Where(m => m is not null))
        {
            var type = m!.DeclaringType;
            var name = (type is not null && Attribute.IsDefined(type, typeof(WindowCommandsPartAttribute), false) ? nameof(WindowCommands) : type?.Name) + "." + m.Name;
            if (names.Count > 0 && names[^1] == name)
            {
                continue; // 전달자 프레임(같은 이름) — 옛 판에는 없던 한 단계
            }

            names.Add(name);
            if (names.Count == take)
            {
                break;
            }
        }

        return names;
    }
#endif

    // ── 문서 생명주기(DocumentLifecycle) ──
    public PocDocument OpenDocument(DocumentKind kind, string? title = null, bool sdi = false, bool allowDuplicate = false) => Lifecycle.OpenDocument(kind, title, sdi, allowDuplicate);
    public void Arrange(ShellArrange arrange) => Lifecycle.Arrange(arrange);
    public void ApplyPreset(LayoutPreset preset) => Lifecycle.ApplyPreset(preset);
    public void CloseDocument(PocDocument document) => Lifecycle.CloseDocument(document);
    public void CloseOthers(PocDocument keep) => Lifecycle.CloseOthers(keep);
    public void CloseAll() => Lifecycle.CloseAll();
    public void Rename(PocDocument document, string title) => Lifecycle.Rename(document, title);

    // ── 활성화·SDI·핀(WindowActivation) ──
    public void Activate(PocDocument document) => Activation.Activate(document);
    public void ActivateNext() => Activation.ActivateNext();
    public void ActivatePrevious() => Activation.ActivatePrevious();
    public void Float(PocDocument document) => Activation.Float(document);
    public void Redock(PocDocument document) => Activation.Redock(document);
    public void ToggleSdi(PocDocument document) => Activation.ToggleSdi(document);
    public bool TogglePin(PocDocument document) => Activation.TogglePin(document);

    // ── 창 상태·레이아웃 저장(WindowStateOps)·복원(LayoutRestorer) ──
    public void MinimizeAll() => StateOps.MinimizeAll();
    public void RestoreAll() => StateOps.RestoreAll();
    public void SetState(PocDocument document, MdiWindowState state) => StateOps.SetState(document, state);
    public bool RestoreLastMinimized() => StateOps.RestoreLastMinimized();
    public void ActivateOrMinimize(PocDocument document) => StateOps.ActivateOrMinimize(document);
    public void MoveAll(double dx, double dy) => StateOps.MoveAll(dx, dy);
    public void ResetLayout() => StateOps.ResetLayout();
    public string SerializeLayout() => StateOps.SerializeLayout();
    public LayoutSnapshot CaptureLayout() => StateOps.CaptureLayout();
    public Task SaveLayoutAsync() => StateOps.SaveLayoutAsync();
    public Task<bool> LoadLayoutAsync() => StateOps.LoadLayoutAsync();
    public bool ApplyLayout(string? json) => Restorer.ApplyLayout(json);
    public IReadOnlyList<PocDocument> SdiDocuments() => StateOps.SdiDocuments();
    public DockWindowState? SdiState(PocDocument document) => StateOps.SdiState(document);
    public int RunShellWindowCommand(WindowHostCommand command) => StateOps.RunShellWindowCommand(command);
    public static bool IsShellWindowCommand(WindowHostCommand command) => WindowStateOps.IsShellWindowCommand(command);

    // ── 문서 크롬(DocumentChromeService) ──
    public static DocumentChromeState ChromeOf(PocDocument document) => DocumentChromeService.ChromeOf(document);
    public IReadOnlyCollection<DocumentKind> DuplicateKinds => ChromeService.DuplicateKinds;
    public bool IsDuplicateAllowed(DocumentKind kind) => ChromeService.IsDuplicateAllowed(kind);
    public bool IsFloating(PocDocument document) => ChromeService.IsFloating(document);
    public void ChromeToggleSdi(PocDocument document) => ChromeService.ChromeToggleSdi(document);
    public bool ChromeTogglePin(PocDocument document) => ChromeService.ChromeTogglePin(document);
    public bool ChromeToggleLock(PocDocument document) => ChromeService.ChromeToggleLock(document);
    public bool ChromeToggleSync(PocDocument document) => ChromeService.ChromeToggleSync(document);
    public bool ChromeToggleDuplicate(PocDocument document) => ChromeService.ChromeToggleDuplicate(document);
    public void SetDuplicateAllowed(DocumentKind kind, bool allowed) => ChromeService.SetDuplicateAllowed(kind, allowed);
    public PocDocument ChromeRefresh(PocDocument document) => ChromeService.ChromeRefresh(document);
    public bool ChromeToggleCache(PocDocument document) => ChromeService.ChromeToggleCache(document);
    public string ChromeHelpText(PocDocument document) => ChromeService.ChromeHelpText(document);
    public void SyncChrome(PocDocument document) => ChromeService.SyncChrome(document);

    // ── Z 순서(MdiZOrder)·배율(DocumentZoom)·측면 패널(SidePanelVisibility)·SDI 배치(SdiPlacement)·자동 제목(DocumentAutoTitles) ──
    public static bool LockBlocksMinMax { get => MdiZOrder.LockBlocksMinMax; set => MdiZOrder.LockBlocksMinMax = value; }
    public static PocDocument? FindDocument(object? context) => MdiZOrder.FindDocument(context);
    public IReadOnlyList<PocDocument> GetMdiZOrder() => MdiZOrder.GetMdiZOrder();
    public void SetMdiZOrder(IReadOnlyList<PocDocument> bottomToTop) => MdiZOrder.SetMdiZOrder(bottomToTop);
    public void SetMdiZOrder(PocDocument document, int rank) => MdiZOrder.SetMdiZOrder(document, rank);
    public void EnsureZOrderHook() => MdiZOrder.EnsureZOrderHook();
    public const double ZoomStep = DocumentZoom.ZoomStep;
    public bool WindowZoomMode { get => DocumentZoom.WindowZoomMode; set => DocumentZoom.WindowZoomMode = value; }
    public void SetDocumentZoom(PocDocument document, double zoom) => DocumentZoom.SetDocumentZoom(document, zoom);
    public void ZoomIn(PocDocument? document = null) => DocumentZoom.ZoomIn(document);
    public void ZoomOut(PocDocument? document = null) => DocumentZoom.ZoomOut(document);
    public void ResetZoom(PocDocument? document = null) => DocumentZoom.ResetZoom(document);
    public bool HandleZoomInput(Visual? source, int delta) => DocumentZoom.HandleZoomInput(source, delta);
    public const string LogToolId = SidePanelVisibility.LogToolId;
    public const string ExplorerToolId = SidePanelVisibility.ExplorerToolId;
    public bool SidePanelsHidden => SidePanels.SidePanelsHidden;
    public void SetToolPanelsHidden(bool hidden) => SidePanels.SetToolPanelsHidden(hidden);
    public void ApplyProductToolVisibility() => SidePanels.ApplyProductToolVisibility();
    public bool ShowTool(string toolId) => SidePanels.ShowTool(toolId);
    public const double SdiCascadeOrigin = SdiPlacement.SdiCascadeOrigin;
    public static Point SdiDefaultPosition(Rect workArea) => SdiPlacement.SdiDefaultPosition(workArea);
    public int SdiResetPositions(Func<Rect> layerArea) => SdiPlacement.SdiResetPositions(layerArea);
    public static string AutoTitle(DocumentKind kind, int seq) => DocumentAutoTitles.AutoTitle(kind, seq);
    public static bool IsAutoTitle(PocDocument document) => DocumentAutoTitles.IsAutoTitle(document);

    // ── 크롬 스타일·진단(static — 외부 호출부 13파일 호환, W5 정리 대상) ──
    public static event Action<ChromeStyleKind>? ChromeStyleChanged { add => WindowChromeStyle.ChromeStyleChanged += value; remove => WindowChromeStyle.ChromeStyleChanged -= value; }
    public static event Action? ChromeApplied { add => WindowChromeStyle.Applied += value; remove => WindowChromeStyle.Applied -= value; } // W1: Desktop SdiShadowStack 구독
    public static bool ShadowEnabled { get => WindowChromeStyle.ShadowEnabled; set => WindowChromeStyle.ShadowEnabled = value; }
    public static double ShadowSpread { get => WindowChromeStyle.ShadowSpread; set => WindowChromeStyle.ShadowSpread = value; }
    public static double ShadowOpacity { get => WindowChromeStyle.ShadowOpacity; set => WindowChromeStyle.ShadowOpacity = value; }
    public static bool AccentTitleBar { get => WindowChromeStyle.AccentTitleBar; set => WindowChromeStyle.AccentTitleBar = value; }
    public static double HighlightThickness { get => WindowChromeStyle.HighlightThickness; set => WindowChromeStyle.HighlightThickness = value; }
    public static ChromeStyleKind EffectiveStyle => WindowChromeStyle.EffectiveStyle;
    public static ChromeStyleKind ChromeStyle { get => WindowChromeStyle.ChromeStyle; set => WindowChromeStyle.ChromeStyle = value; }
    public static bool MarsFrame { get => WindowChromeStyle.MarsFrame; set => WindowChromeStyle.MarsFrame = value; }
    public static void ApplyShadow() => WindowChromeStyle.ApplyShadow();
    internal static void Alias(Application app, string alias, string source) => WindowChromeStyle.Alias(app, alias, source);
    internal static void Set(Application app, string key, object value) => WindowChromeStyle.Set(app, key, value);
#if PD_DIAGNOSTICS
    public static string ChromeButtonsMode => WindowChromeDiag.ChromeButtonsMode;
    public static string ChromeDiagState => WindowChromeDiag.ChromeDiagState;
    public static bool SetInactiveDimMode(string? mode) => WindowChromeDiag.SetInactiveDimMode(mode);
    public static bool SetInactiveOpacity(string? value) => WindowChromeDiag.SetInactiveOpacity(value);
    public static bool SetChromeButtonsMode(string? mode) => WindowChromeDiag.SetChromeButtonsMode(mode);
#endif
}

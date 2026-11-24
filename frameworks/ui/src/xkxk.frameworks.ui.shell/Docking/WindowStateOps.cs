// 창 상태(최소/최대·전체 최소화/복원·이동·초기화)·레이아웃 저장과 윈도우 메뉴 SDI 일괄 명령 — 옛 WindowCommands.State·WindowMenu, 공유 상태는 WindowSession
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>WindowCommands 파사드가 위임하는 창 상태·레이아웃 저장·SDI 일괄 명령. 생성자는 상태만 저장한다(구독 없음).</summary>
[WindowCommandsPart]
internal sealed partial class WindowStateOps
{
    private readonly WindowSession _s;

    internal WindowStateOps(WindowSession s) => _s = s;

    // 옛 WindowCommands 필드·도우미 이름 — 아래 본문을 옮기기만 하려고 같은 이름으로 잇는다.
    private PocDockFactory _factory => _s.Factory;
    private ILayoutStore _store => _s.Store;
    private Action<string> _log => _s.Log;
    private List<PocDocument> _mru => _s.Mru;
    private List<PocDocument> _minimized => _s.Minimized;
    private IDocumentDock Docs => _s.Docs;
    private IRootDock Root => _s.Root;
    private ObservableCollection<PocDocument> OpenDocuments => _s.Owner.OpenDocuments;
    private PocDocument? ActiveDocument => _s.Owner.ActiveDocument;
    private IReadOnlyCollection<DocumentKind> DuplicateKinds => _s.Owner.DuplicateKinds;
    private IDockWindow? FindWindow(PocDocument doc) => _s.FindWindow(doc);
    private IEnumerable<PocDocument> MdiDocs() => _s.MdiDocs();
    private void Activate(PocDocument document) => _s.Owner.Activation.Activate(document);
    private void CloseDocument(PocDocument document) => _s.Owner.CloseDocument(document);
    private bool ApplyLayout(string? json) => _s.Owner.Restorer.ApplyLayout(json);
    private static DocumentChromeState ChromeOf(PocDocument document) => DocumentChromeService.ChromeOf(document);

    public void MinimizeAll()
    {
        using var op = PerfProbe.Current.TimeOperation("ui.minimizeAll"); // PERF-UI
        foreach (var doc in OpenDocuments.ToList())
        {
            SetState(doc, MdiWindowState.Minimized);
        }

        _log(Loc.Tr("log.window.minimizedAll"));
    }

    public void RestoreAll()
    {
        using var op = PerfProbe.Current.TimeOperation("ui.restoreAll");
        MdiLayoutHelper.RestoreDocuments(Docs);
        foreach (var window in Root.Windows?.ToList() ?? new List<IDockWindow>())
        {
            SetHostState(window, DockWindowState.Normal);
        }

        _minimized.Clear();
        _log(Loc.Tr("log.window.restoredAll"));
    }

    public void SetState(PocDocument document, MdiWindowState state)
    {
        using var op = PerfProbe.Current.TimeOperation("ui.state." + state.ToString().ToLowerInvariant());
        if (PocDockFactory.PanelOf(document) is not null)
        {
            return; // 측면 패널 탭 안 문서는 최소·최대 상태가 없다(odev-1 합의)
        }

        _minimized.Remove(document);
        if (state == MdiWindowState.Minimized)
        {
            _minimized.Add(document);
        }

        if (FindWindow(document) is { } window)
        {
            SetHostState(window, state switch
            {
                MdiWindowState.Minimized => DockWindowState.Minimized,
                MdiWindowState.Maximized => DockWindowState.Maximized,
                _ => DockWindowState.Normal,
            });
        }
        else
        {
            MdiTabGroup.WindowOf(document)!.MdiState = state; // 탭 묶음 안이면 묶음 창 상태
            if (state != MdiWindowState.Minimized && MdiTabGroup.GroupOf(document) is { } g)
            {
                _minimized.RemoveAll(d => g.Documents.Contains(d)); // 묶음이 복원되면 같은 묶음 문서도 최소화 목록에서 뺀다
            }
        }
    }

    /// <summary>
    /// LayoutSerializer.SetHostState 와 같되 호스트를 IHostWindow 로 부른다. 데스크톱 SdiWindowHost 의 재구현(비활성 최소화·복원, w13 Z6)을 타게 하려는 것이다.
    /// LayoutSerializer.cs 는 저장 형식 보호 파일이라 바꾸지 않는다. 관리형 창(웹)은 같은 메서드라 동작이 같다.
    /// </summary>
    private static void SetHostState(IDockWindow window, DockWindowState state)
    {
        window.WindowState = state;
        window.Host?.SetWindowState(state);
    }

    public bool RestoreLastMinimized()
    {
        var last = _minimized.LastOrDefault();
        if (last is not null)
        {
            SetState(last, MdiWindowState.Normal);
            Activate(last);
        }

        return last is not null;
    }

    /// <summary>분리 창이 활성이 된 뒤 메인에서 아직 누름이 없었는가.</summary>
    private bool _sdiWasActive;

    /// <summary>지금 처리 중인 누름이 분리 창에서 메인으로 돌아오는 첫 누름인가.</summary>
    private bool _pressFromSdi;

    /// <summary>진단 스위치(w13 U4 원인 가르기): true 면 메인 쪽 누름을 기록하지 않는다. 제품판에서는 늘 false 다.</summary>
    internal static bool DiagPressDisabled { get; set; }

    /// <summary>분리 창 활성과 메인 쪽 누름을 받는다. 분리 창 안 누름은 메인 쪽 누름이 아니다. WindowCommands 생성자가 1회 부른다.</summary>
    internal void InstallTabClickGuard()
    {
        _factory.WindowActivated += (_, e) =>
        {
            if (e.Window is not null && Root.Windows?.Contains(e.Window) == true)
            {
                _sdiWasActive = true; // 메인 창 활성도 같은 이벤트로 온다. 분리 창 목록에 있는 창만 본다
            }
        };
        Avalonia.Input.InputElement.PointerPressedEvent.AddClassHandler<Avalonia.Controls.TopLevel>((top, _) =>
        {
            if (top is not Dock.Avalonia.Controls.HostWindow && !DiagPressDisabled)
            {
                NoteMainPressed();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>메인 쪽 누름(터널)마다 부른다. 활성 전환이 누름보다 먼저 일어나므로 탭 클릭 시점의 활성 문서만으로는 돌아오는 클릭을 가릴 수 없다.</summary>
    internal void NoteMainPressed()
    {
        _pressFromSdi = _sdiWasActive;
        _sdiWasActive = false;
    }

    /// <summary>탭 클릭. 활성 탭을 누르면 최소화, 아니면 활성화다. 분리 창에서 돌아오는 첫 누름은 활성 전환만 한다(w12 Z5).</summary>
    public void ActivateOrMinimize(PocDocument document)
    {
        var returning = _pressFromSdi;
        _pressFromSdi = false;
        if (ReferenceEquals(document, ActiveDocument) && !IsMinimized(document) && !returning)
        {
            SetState(document, MdiWindowState.Minimized);
        }
        else
        {
            Activate(document);
        }
    }

    // MoveAll·PreserveLocked·ResetLayout 는 창 단위(탭 묶음) 판정과 함께 WindowStateOps.Windows.cs 에 있다(300줄 상한).

    public string SerializeLayout() => LayoutSerializer.Serialize(CaptureLayout());

    /// <summary>현재 화면을 스냅샷(DTO)으로 읽는다 — UI 스레드에서 호출. JSON 변환은 호출자가 다른 스레드에서 해도 된다(자동 저장).</summary>
    public LayoutSnapshot CaptureLayout()
    {
        foreach (var window in Root.Windows ?? Enumerable.Empty<IDockWindow>())
        {
            window.Save(); // 호스트 창의 현재 위치·크기를 모델(X/Y/Width/Height)로 반영(자동 저장·개인 레이아웃 공통)
        }

        var snapshot = LayoutSerializer.Capture(Docs, Root, OpenDocuments, ActiveDocument);
        snapshot.DuplicateKinds = DuplicateKinds.ToList(); // 제목줄 '중복 허용'(종류 단위, UI-09)
        snapshot.Panels = _factory.CapturePanels(); // 도구 패널 비율(Version 2, UI-11)
        snapshot.SidePanelsHidden = _factory.SidePanelsHidden; // '집중' 템플릿 측면 패널 숨김

        // Z 순서(사용자 요구): 활성화하면 MDI·SDI 모두 맨 앞으로 오므로 최근 활성 순서(MRU 앞=최상위)가 화면 앞뒤 순서다.
        // Capture 는 OpenDocuments 순서로 만들므로 같은 인덱스로 짝짓는다. 마지막 활성 문서(ActiveId)=최상위가 된다.
        for (var i = 0; i < snapshot.Documents.Count && i < OpenDocuments.Count; i++)
        {
            var mruIndex = _mru.IndexOf(OpenDocuments[i]);
            snapshot.Documents[i].ZOrder = mruIndex < 0 ? 0 : _mru.Count - 1 - mruIndex;
        }

        return snapshot;
    }

    public async Task SaveLayoutAsync()
    {
        using var op = PerfProbe.Current.TimeOperation("ui.layout.save"); // await 뒤 UI 스레드에서 Dispose(저장 완료까지)
        await _store.SaveAsync(SerializeLayout());
        _log(Loc.Tr("log.layout.slotSaved", OpenDocuments.Count));
#if PD_DIAGNOSTICS
        _log("[d12] saved ids=" + string.Join(",", OpenDocuments.Select(d => d.Id + (FindWindow(d) is null ? "" : "(sdi)")))); // D-12 진단
#endif
    }

    /// <summary>자동 저장 슬롯(App.LayoutStore)을 복원한다. 본문은 LayoutRestorer.ApplyLayout.</summary>
    public async Task<bool> LoadLayoutAsync()
    {
        using var op = PerfProbe.Current.TimeOperation("ui.layout.load");
        var json = await _store.LoadAsync();
        return await Busy.BusyHooks.RunLongAsync(Busy.BusyHooks.MainScope, "layout", () => ApplyLayout(json)); // 복원: 스피너를 먼저 그린 뒤 복원한다
    }

    internal bool IsMinimized(PocDocument doc) => _minimized.Contains(doc) || MdiTabGroup.WindowOf(doc)!.MdiState == MdiWindowState.Minimized;

    // ── 윈도우 메뉴 셸 기본 경로(옛 WindowCommands.WindowMenu) — SDI(분리 창) 일괄 최소화·복원·닫기(⑧⑨⑩, 호스트가 맡지 않을 때) ──

    /// <summary>지금 SDI(플로팅)로 떠 있는 문서(열린 순서).</summary>
    public IReadOnlyList<PocDocument> SdiDocuments() =>
        Root.Windows?.Any() == true ? OpenDocuments.Where(d => FindWindow(d) is not null).ToList() : new List<PocDocument>();

    /// <summary>
    /// SDI(플로팅) 문서의 창 상태(OS 창·관리형 창 공통 — Dock IDockWindow.WindowState). 도킹 문서는 null.
    /// 판정용: SDI 최소화·복원은 문서의 MdiState 가 아니라 창 상태를 바꾼다(MdiState 는 MDI 창 전용).
    /// </summary>
    public DockWindowState? SdiState(PocDocument document) => FindWindow(document)?.WindowState;

    /// <summary>셸이 직접 처리할 수 있는 윈도우 메뉴 명령인가(⑧⑨⑩).</summary>
    public static bool IsShellWindowCommand(WindowHostCommand command) => command is
        WindowHostCommand.MinimizeAllSdi or WindowHostCommand.RestoreAllSdi or WindowHostCommand.CloseAllSdi;

    /// <summary>⑧⑨⑩ 셸 기본 경로. 처리한 SDI 창 수를 돌려준다(셸 명령이 아니면 -1).</summary>
    public int RunShellWindowCommand(WindowHostCommand command)
    {
        if (!IsShellWindowCommand(command))
        {
            return -1;
        }

        using var op = PerfProbe.Current.TimeOperation("ui.window.sdi." + command.ToString().ToLowerInvariant());
        var targets = SdiDocuments();
        foreach (var doc in targets)
        {
            switch (command)
            {
                case WindowHostCommand.MinimizeAllSdi: SetState(doc, MdiWindowState.Minimized); break;
                case WindowHostCommand.RestoreAllSdi: SetState(doc, MdiWindowState.Normal); break;
                default: CloseDocument(doc); break;
            }
        }

        return targets.Count;
    }
}

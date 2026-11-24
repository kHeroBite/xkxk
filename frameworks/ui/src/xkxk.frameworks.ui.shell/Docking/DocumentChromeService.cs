// 문서 창 제목줄(크롬) 명령 — Mars 9버튼 중 독립·항상 위·위치 고정·동기화·중복 허용·새로고침(캐시)·도움말 동작과 문서별 상태 보관(UI-09, 옛 WindowCommands.Chrome)
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 문서별 크롬 상태(static 표 s_chrome — 문서 객체가 사라지면 함께 사라짐)와 종류 단위 중복 허용(인스턴스)을 맡는다.
/// 창 전환·항상 위(SDI)·떠 있게 하기는 s.Owner.Activation, Z순서 재적용은 s.Owner.MdiZOrder 를 부른다.
/// </summary>
[WindowCommandsPart]
internal sealed class DocumentChromeService
{
    private const int MockQueriesPerLoad = 8;
    private static readonly ConditionalWeakTable<PocDocument, DocumentChromeState> s_chrome = new();
    private static readonly Dictionary<DocumentKind, int> s_loads = new();
    private readonly HashSet<DocumentKind> _duplicateKinds = new();
    private readonly WindowSession _s;

    internal DocumentChromeService(WindowSession s)
    {
        _s = s;
        // U7: 최소화 상태를 크롬 상태에 맞춘다(탭 상태색). MDI 는 MdiState 변경 알림, SDI 는 활성 전환 때 창 상태로 다시 센다.
        _s.Owner.OpenDocuments.CollectionChanged += OnOpenDocumentsChanged;
        _s.Owner.ActiveChanged += _ => RefreshMinimized();
        // SDI 창을 OS 최소화·복원 버튼으로 바꾸면 활성 전환이 없을 수 있다 → 창 상태 변경을 직접 구독해 즉시 맞춘다(창이 닫히면 해제)
        _s.Factory.WindowOpened += (_, e) => WatchWindow(e.Window);
        _s.Factory.WindowClosed += (_, e) => UnwatchWindow(e.Window);
        // 묶음 창 캡션 닫기(CloseDockable(묶음))는 Dock 기본이 묶음만 트리에서 뺀다 → 안 문서를 셸 닫기 경로로 모두 닫는다(묶음 전체 닫기). 탭 X 는 Dock 기본(그 탭만)
        _s.Factory.DockableClosed += (_, e) =>
        {
            if (e.Dockable is MdiTabGroupDock g) { MdiTabGroup.CloseGroup(_s.Factory, g, _s.Owner.CloseDocument); }
        };
    }

    private readonly Dictionary<IDockWindow, Action> _windowWatch = new();

    /// <summary>분리 창 상태 구독: OS 창(데스크톱 HostWindow)은 Window.WindowState, 관리형 창(웹)은 모델 IDockWindow.WindowState 알림.</summary>
    private void WatchWindow(IDockWindow? window)
    {
        if (window is null || _windowWatch.ContainsKey(window) || window.Host is null)
        {
            return; // Host 가 아직 없으면 다음 RefreshMinimized 때 다시 시도한다
        }

        Action undo = () => { };
        if (window.Host is Avalonia.Controls.Window host)
        {
            EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs> onHost = (_, e) =>
            {
                if (e.Property == Avalonia.Controls.Window.WindowStateProperty) { RefreshMinimized(); }
            };
            host.PropertyChanged += onHost;
            undo += () => host.PropertyChanged -= onHost;
            // 호스트 창이 닫히면 구독을 바로 푼다. SDI 재결합(Redock)은 RemoveWindow 뒤 Exit 라 HostWindow.OnClosed 때 Window 가 비어
            // Factory.WindowClosed 가 오지 않았다 → 닫힌 SDI 창과 그 시각 트리 전체가 _windowWatch 에 남았다(A11 힙 덤프: 분리 12회 = 남은 SdiWindowHost 12개)
            EventHandler onClosed = (_, _) => UnwatchWindow(window);
            host.Closed += onClosed;
            undo += () => host.Closed -= onClosed;
        }

        if (window is INotifyPropertyChanged model)
        {
            PropertyChangedEventHandler onModel = (_, e) =>
            {
                if (e.PropertyName == nameof(IDockWindow.WindowState)) { RefreshMinimized(); }
            };
            model.PropertyChanged += onModel;
            undo += () => model.PropertyChanged -= onModel;
        }

        _windowWatch[window] = undo;
    }

    private void UnwatchWindow(IDockWindow? window)
    {
        if (window is not null && _windowWatch.Remove(window, out var undo))
        {
            undo();
        }
    }

    private void OnOpenDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var d in (e.OldItems ?? Array.Empty<object>()).OfType<PocDocument>())
        {
            d.PropertyChanged -= OnDocumentChanged;
        }

        foreach (var d in (e.NewItems ?? Array.Empty<object>()).OfType<PocDocument>())
        {
            d.PropertyChanged += OnDocumentChanged;
            UpdateMinimized(d);
        }
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is PocDocument d && e.PropertyName == nameof(PocDocument.MdiState))
        {
            UpdateMinimized(d);
        }
        else if (sender is PocDocument t && e.PropertyName == nameof(PocDocument.Title))
        {
            MdiTabGroup.GroupOf(t)?.SyncTitle(); // 탭 이름 바꾸기·언어 전환(자동 제목) 뒤 묶음 제목을 활성 탭에 맞춘다
        }
    }

    private void RefreshMinimized()
    {
        foreach (var d in _s.Owner.OpenDocuments)
        {
            UpdateMinimized(d);
        }
    }

    /// <summary>MDI 창은 MdiState, SDI(분리 창)는 창 상태로 판정한다(SDI 최소화는 MdiState 를 바꾸지 않는다 — WindowStateOps.SetState).</summary>
    private void UpdateMinimized(PocDocument d)
    {
        var window = _s.Factory.Root is null ? null : _s.FindWindow(d);
        WatchWindow(window);
        ChromeOf(d).IsMinimized = window is null ? d.MdiState == MdiWindowState.Minimized
            : window.Host is Avalonia.Controls.Window host ? host.WindowState == Avalonia.Controls.WindowState.Minimized
            : window.WindowState == DockWindowState.Minimized;
    }

    public static DocumentChromeState ChromeOf(PocDocument document) => s_chrome.GetValue(document, _ => new DocumentChromeState());

    /// <summary>중복 허용된 문서 종류(레이아웃 저장용).</summary>
    public IReadOnlyCollection<DocumentKind> DuplicateKinds => _duplicateKinds;

    public bool IsDuplicateAllowed(DocumentKind kind) => _duplicateKinds.Contains(kind);

    public bool IsFloating(PocDocument document) => _s.FindWindow(document) is not null;

    /// <summary>
    /// 분리 창을 활성으로 표시할지. OS 창은 SDI 제목 줄 색과 같은 Window.IsActive 로 판정해 메인을 눌러 전경을 잃으면 함께 꺼진다.
    /// 관리형 창(웹)과 비전경 모드(--bg-pos, 설계상 전경을 얻지 않는다)는 OS 활성이 없어 활성 문서 보유 여부로 판정한다.
    /// </summary>
    public bool IsSdiActive(IDockWindow window, bool holdsActiveDocument) =>
        window.Host is Avalonia.Controls.Window host && !xkxk.frameworks.ui.shell.Platform.SystemDialogHost.BackgroundMode ? host.IsActive : holdsActiveDocument;

    /// <summary>① 독립(MDI↔SDI 전환).</summary>
    public void ChromeToggleSdi(PocDocument document)
    {
        // 진행 표시(요건 2): 스피너를 먼저 그린 뒤 전환한다. 그 사이 닫힌 문서는 건너뛴다
        Busy.BusyHooks.RunAfterFrame(document.Id, "sdi", () =>
        {
            if (_s.Owner.OpenDocuments.Contains(document))
            {
                using var op = PerfProbe.Current.TimeOperation("chrome.sdi");
                _s.Owner.Activation.ToggleSdi(document);
            }
        });
    }

    /// <summary>② 항상 위. SDI 는 기존 TogglePin(OS Topmost), MDI 는 Z순서 재적용(Mars WM_WINDOWPOSCHANGING 대응).</summary>
    public bool ChromeTogglePin(PocDocument document)
    {
        using var op = PerfProbe.Current.TimeOperation("chrome.pin");
        var state = ChromeOf(document);
        state.IsPinned = IsFloating(document) ? _s.Owner.Activation.TogglePin(document) : !state.IsPinned;
        _s.Owner.MdiZOrder.EnsurePinHook();
        _s.Owner.MdiZOrder.RaisePinned();
        _s.Log(Loc.Tr(state.IsPinned ? "chrome.log.pinOn" : "chrome.log.pinOff", document.Title));
        return state.IsPinned;
    }

    /// <summary>②-2 위치 고정 — MDI 는 끌기·크기 손잡이 차단, SDI 는 OS 이동·크기 조절 차단(ChromeButtons/SdiTitleBar 가 적용).</summary>
    public bool ChromeToggleLock(PocDocument document)
    {
        using var op = PerfProbe.Current.TimeOperation("chrome.lock");
        var state = ChromeOf(document);
        state.IsLocked = !state.IsLocked;
        _s.Log(Loc.Tr(state.IsLocked ? "chrome.log.lockOn" : "chrome.log.lockOff", document.Title));
        return state.IsLocked;
    }

    /// <summary>③ 동기화 — 창 간 프로젝트 동기화 수신 토글(MainViewModel.SyncProject 가 SyncEnabled=false 창을 건너뜀).</summary>
    public bool ChromeToggleSync(PocDocument document)
    {
        using var op = PerfProbe.Current.TimeOperation("chrome.sync");
        var state = ChromeOf(document);
        state.SyncEnabled = !state.SyncEnabled;
        _s.Log(Loc.Tr(state.SyncEnabled ? "chrome.log.syncOn" : "chrome.log.syncOff", document.Title));
        return state.SyncEnabled;
    }

    /// <summary>④ 중복 허용 — 종류 단위. 같은 종류 열린 창 전체 표시가 함께 바뀐다(Mars 같은 이름 창 전체 동기화).</summary>
    public bool ChromeToggleDuplicate(PocDocument document)
    {
        using var op = PerfProbe.Current.TimeOperation("chrome.dup");
        SetDuplicateAllowed(document.Kind, !IsDuplicateAllowed(document.Kind));
        return IsDuplicateAllowed(document.Kind);
    }

    public void SetDuplicateAllowed(DocumentKind kind, bool allowed)
    {
        _ = allowed ? _duplicateKinds.Add(kind) : _duplicateKinds.Remove(kind);
        foreach (var doc in _s.Owner.OpenDocuments.Where(d => d.Kind == kind))
        {
            ChromeOf(doc).AllowDuplicate = allowed;
        }

        _s.Log(Loc.Tr(allowed ? "chrome.log.dupOn" : "chrome.log.dupOff", kind.DisplayName()));
    }

    /// <summary>
    /// ⑤ 새로고침(=캐시, Mars F5): 창을 닫고 같은 종류·제목·위치·크기·상태·배율·프로젝트·SDI 여부로 다시 연다. 새 문서를 반환.
    /// 확인창은 제목줄 버튼(사람 조작)만 띄우고, 자동화·원격은 바로 실행한다.
    /// </summary>
    public PocDocument ChromeRefresh(PocDocument document)
    {
        using var op = PerfProbe.Current.TimeOperation("chrome.refresh");
        var old = ChromeOf(document);
        var window = _s.FindWindow(document);
        window?.Save(); // 호스트 창의 현재 위치·크기를 모델로
        var b = document.MdiBounds;
        var item = new DocumentSnapshot
        {
            Kind = document.Kind, Title = document.Title, X = b.X, Y = b.Y, Width = b.Width, Height = b.Height,
            MdiState = document.MdiState, ZIndex = document.MdiZIndex, Zoom = document.Zoom, IsPinned = old.IsPinned,
        };
        var host = window is null ? null : new WindowSnapshot
        {
            X = window.X, Y = window.Y, Width = window.Width, Height = window.Height, Topmost = window.Topmost, State = window.WindowState,
        };
        var (project, sync, preFloat) = (document.Project, old.SyncEnabled, document.PreFloatBounds);

        _s.Owner.CloseDocument(document);
        var fresh = _s.Owner.OpenDocument(item.Kind, item.Title, sdi: false, allowDuplicate: true);
        LayoutSerializer.ApplyDocument(fresh, item);
        fresh.Project = project;
        ChromeOf(fresh).SyncEnabled = sync;
        ChromeOf(fresh).IsLocked = old.IsLocked;
        if (item.MdiState == MdiWindowState.Minimized)
        {
            _s.Minimized.Add(fresh);
        }

        if (host is not null)
        {
            fresh.PreFloatBounds = preFloat;
            _s.Owner.Activation.Float(fresh);
            LayoutSerializer.ApplyWindow(_s.FindWindow(fresh), host);
        }

        _s.Owner.MdiZOrder.RaisePinned();
        _s.Log(Loc.Tr("chrome.log.refresh", fresh.Title));
        return fresh;
    }

    /// <summary>호환용(odev-3·odev-6r 기존 연결): 새로고침을 실행하고 true.</summary>
    public bool ChromeToggleCache(PocDocument document)
    {
        ChromeRefresh(document);
        return true;
    }

    /// <summary>⑥ 도움말 — 화면 도움말(PoC 모의 메뉴 DB). 등록되지 않은 화면이면 '없음' 안내(동작 사양은 Mars 와 같고 문구는 PD 문체).</summary>
    public string ChromeHelpText(PocDocument document)
    {
        using var op = PerfProbe.Current.TimeOperation("chrome.help");
        _s.Log(Loc.Tr("chrome.log.help", document.Title));
        return document.Kind switch
        {
            DocumentKind.Grid => Loc.Tr("chrome.help.grid", document.Title),
            DocumentKind.Gantt => Loc.Tr("chrome.help.gantt", document.Title),
            _ => Loc.Tr("chrome.help.none"),
        };
    }

    /// <summary>제목줄이 문서에 붙을 때(=데이터 로드 시작) 호출: 중복 허용 표시를 맞추고 캐시 적중 모의값을 새로 센다.</summary>
    public void SyncChrome(PocDocument document)
    {
        var state = ChromeOf(document);
        state.AllowDuplicate = IsDuplicateAllowed(document.Kind);
        if (state.CacheTotal == 0)
        {
            // 모의: 같은 종류를 이번 세션에 여러 번 읽을수록 서버 캐시가 데워진다 → 0% → 37% → 75% → 100%.
            var loads = s_loads[document.Kind] = s_loads.GetValueOrDefault(document.Kind) + 1;
            state.CacheTotal = MockQueriesPerLoad;
            state.CacheHits = Math.Min(MockQueriesPerLoad, (loads - 1) * 3);
            state.IsCached = state.CacheHits > 0;
        }
    }

    // 창 틀·그림자·크롬 스타일(Windows/macOS/MarsClassic)은 WindowChromeStyle.cs
}

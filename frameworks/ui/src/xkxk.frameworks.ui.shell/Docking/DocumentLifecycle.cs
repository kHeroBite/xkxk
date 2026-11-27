// 문서 생명주기(새 문서 열기·중복 처리·계단식/바둑판 배치·프리셋·닫기·이름 변경·문서 추가) — 옛 WindowCommands.cs 본문, 공유 상태는 WindowSession
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>WindowCommands 파사드가 위임하는 문서 열기·배치·닫기. 생성자는 상태만 저장한다(구독 없음).</summary>
[WindowCommandsPart]
internal sealed class DocumentLifecycle
{
    private readonly WindowSession _s;

    internal DocumentLifecycle(WindowSession s) => _s = s;

    // 옛 WindowCommands 필드·도우미 이름 — 아래 본문을 옮기기만 하려고 같은 이름으로 잇는다.
    private PocDockFactory _factory => _s.Factory;
    private Action<string> _log => _s.Log;
    private Dictionary<string, int> _seq => _s.Seq;
    private List<PocDocument> _mru => _s.Mru;
    private List<PocDocument> _minimized => _s.Minimized;
    private IDocumentDock Docs => _s.Docs;
    private ObservableCollection<PocDocument> OpenDocuments => _s.Owner.OpenDocuments;
    private IDockWindow? FindWindow(PocDocument doc) => _s.FindWindow(doc);
    private IEnumerable<PocDocument> MdiDocs() => _s.MdiDocs();
    private void SetActive(PocDocument? doc) => _s.Owner.SetActive(doc);
    private bool IsDuplicateAllowed(string windowId) => _s.Owner.IsDuplicateAllowed(windowId);
    private void Activate(PocDocument document) => _s.Owner.Activation.Activate(document);
    private void Float(PocDocument document) => _s.Owner.Activation.Float(document);
    private void PreserveLocked(Action arrange) => _s.Owner.StateOps.PreserveLocked(arrange);
    private static string AutoTitle(string windowId, int seq) => DocumentAutoTitles.AutoTitle(windowId, seq);
    private static void SetAutoTitle(PocDocument document, bool auto) => DocumentAutoTitles.SetAutoTitle(document, auto);

    /// <summary>
    /// 기존 4인자 모양(B09 이전 호출부 호환). Title=title, Placement=sdi ? Sdi : Default, AllowDuplicate=allowDuplicate ? true : null 로
    /// 새 오버로드에 위임한다 — 데모 3종은 정의 Placement 가 Default 라 기존 동작과 같다(a10 §7).
    /// </summary>
    public PocDocument OpenDocument(string windowId, string? title = null, bool sdi = false, bool allowDuplicate = false) =>
        OpenDocument(windowId, new UiWindowOpenOptions
        {
            Title = title,
            Placement = sdi ? UiWindowPlacement.Sdi : UiWindowPlacement.Default,
            AllowDuplicate = allowDuplicate ? true : null,
        });

    /// <summary>
    /// 창을 문서로 연다(I8). windowId 는 ID·별칭이며 TryResolve 로 정규 ID 를 얻는다. 모르면 받은 값 그대로 자리표시 문서를 열고 XKUI0505(Warning).
    /// 분리창 = Placement==Sdi 또는 (Default 이고 정의가 Sdi). 중복 허용 = AllowDuplicate ?? IsDuplicateAllowed(id). Parameter 는 문서에 싣는다(저장본에는 남지 않음).
    /// </summary>
    public PocDocument OpenDocument(string windowId, UiWindowOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(windowId);
        ArgumentNullException.ThrowIfNull(options);
        var app = XkxkUi.Current;
        UiWindowDefinition? def = null;
        if (app.Windows.TryResolve(windowId, out var found))
        {
            def = found;
        }
        else
        {
            app.Diagnostics.Report(UiDiagnosticIds.WindowNotRegistered, UiDiagnosticSeverity.Warning,
                $"등록되지 않은 창 '{windowId}' 을 자리표시 문서로 열었다."); // i18n-ok 개발자 진단
        }

        var id = def?.Id ?? windowId;
        var sdi = options.Placement == UiWindowPlacement.Sdi
            || (options.Placement == UiWindowPlacement.Default && def?.Placement == UiWindowPlacement.Sdi);
        var allowDuplicate = options.AllowDuplicate ?? IsDuplicateAllowed(id); // 제목줄 '중복 허용' 버튼(창 단위, UI-09 — DocumentChromeService)
        var title = options.Title;
        if (!allowDuplicate && title is not null && OpenDocuments.FirstOrDefault(d => d.WindowId == id && d.Title == title) is { } existing)
        {
            Activate(existing);
            _log(Loc.Tr("log.window.duplicateActivated", title));
            return existing;
        }

        var seq = _seq.GetValueOrDefault(id) + 1;
        _seq[id] = seq;
        var requestKey = $"{WindowDisplay.LegacyOrId(id)}-{seq}"; // 데모 3종은 옛 형식(Grid-1) 유지 — 저장본·판정기 호환
        var openToken = WindowLoadState.Default.BeginRequest(requestKey, "open"); // 1단계: 열기 요청 순간(문서 생성 전, 23 §2.2)
        PocDocument doc;
        try
        {
            doc = new PocDocument { Id = requestKey, WindowId = id, Parameter = options.Parameter, Seq = seq, Title = title ?? AutoTitle(id, seq) };
            SetAutoTitle(doc, title is null); // 자동 제목은 언어를 따라 바뀐다(DocumentAutoTitles)
            doc.MdiBounds = PocDockFactory.NextMdiBounds(MdiDocs().Select(d => MdiTabGroup.WindowOf(d)!).Distinct().Select(w => w.MdiBounds)); // 30px 계단(MDI-20) — 창 단위(묶음 안 문서는 묶음 자리)
            using (PerfProbe.Current.TimeOperation("docs.open.add")) { AddDocument(doc, requestKey: requestKey); } // K15 열기 동기 구간 분해(제품판 빈 범위)
        }
        catch (Exception ex)
        {
            openToken.Fail(ex); // 문서가 생기지 않았다 — 요청 토큰은 실패로 끝난다
            throw;
        }

        if (WindowLoadState.Default.RequestCount(requestKey) > 0)
        {
            openToken.Cancel(); // 결합되지 않았다(복원 수집 중 — 지연 생성이 자기 토큰을 건다)
        }
        if (sdi)
        {
            Float(doc);
        }

        // 열기 로그는 렌더 뒤에 남긴다 — 로그 패널 항목 추가(headless 8ms)가 문서 첫 프레임을 늦추지 않게(K15). 문구는 지금 만든다(제목 고정)
        var openedLine = Loc.Tr(sdi ? "log.window.openedSdi" : "log.window.opened", doc.Title);
        Dispatcher.UIThread.Post(() => _log(openedLine), DispatcherPriority.Background);
        return doc;
    }

    public void Arrange(ShellArrange arrange)
    {
        if (arrange == ShellArrange.Tabbed)
        {
            using var op = PerfProbe.Current.TimeOperation("ui.arrange.tabbed"); // PERF-UI(실입력 포함 앱 내부 계측)
            _factory.SetDocumentDockLayoutModeTabbed(Docs);
            _log(Loc.Tr("log.window.arranged", ShellArrange.Tabbed));
            return;
        }

        if (Docs.LayoutMode != DocumentLayoutMode.Mdi)
        {
            // 탭 → MDI: 문서 영역 크기를 이미 알면 모드를 바꾸기 전에 배치 경계를 먼저 정한다(perf15_split.md B1 — 옛 경계로 만든 뒤 다시 배치하던 것 방지).
            // 단 이 크기는 탭 모드 배치에서 기록된 값이라 MDI 영역과 다르다(MDI 는 위 제목 탭 줄 19px 가 생기고 로그 패널이 4px 내려감 — o21 s2b 캡처).
            // 그대로 두면 첫 계단식 창이 MDI 영역보다 약 9px 길어 아래 테두리·가로 스크롤 막대가 잘린다(odebug-grid C1).
            // 그래서 모드 전환 뒤 MDI 영역 크기로 한 번 더 배치한다(경계가 같으면 사실상 비용 없음).
            if (HasLayoutSize())
            {
                ApplyArrange(arrange);
                _factory.SetDocumentDockLayoutModeMdi(Docs);
                Dispatcher.UIThread.Post(() => ApplyArrange(arrange), DispatcherPriority.Background);
                _log(Loc.Tr("log.window.arranged", arrange));
                return;
            }

            _factory.SetDocumentDockLayoutModeMdi(Docs);
        }

        // 모드 전환 직후에는 MDI 패널 크기가 아직 없을 수 있어 배치를 한 프레임 뒤로 미룬다.
        Dispatcher.UIThread.Post(() => ApplyArrange(arrange), DispatcherPriority.Background);
        _log(Loc.Tr("log.window.arranged", arrange));
    }

    /// <summary>계단식·바둑판 배치 경계를 문서 모델에 적용한다. 위치 고정 창은 배치에서 제외한다(WindowStateOps).</summary>
    private void ApplyArrange(ShellArrange arrange)
    {
        using var op = PerfProbe.Current.TimeOperation("ui.arrange." + arrange.ToString().ToLowerInvariant());
        PreserveLocked(() =>
        {
            switch (arrange)
            {
                case ShellArrange.Cascade: MdiLayoutHelper.CascadeDocuments(Docs); MdiTileMinSize.EnforceMin(Docs); break; // D-26 최소 크기 보장
                case ShellArrange.TileHorizontal: MdiLayoutHelper.TileDocumentsHorizontal(Docs); MdiTileMinSize.Enforce(Docs, vertical: false); break; // U9 ⑩ 최소 크기 보정
                case ShellArrange.TileVertical: MdiLayoutHelper.TileDocumentsVertical(Docs); MdiTileMinSize.Enforce(Docs, vertical: true); break;
            }
        });
    }

    /// <summary>문서 영역의 보이는 크기를 아는가(MdiLayoutHelper 의 배치 가능 조건과 같다 — 0·NaN 이면 모른다).</summary>
    private bool HasLayoutSize()
    {
        Docs.GetVisibleBounds(out _, out _, out var width, out var height);
        return !double.IsNaN(width) && !double.IsNaN(height) && width > 0 && height > 0;
    }

    public void ApplyPreset(LayoutPreset preset) => Arrange(preset switch
    {
        LayoutPreset.Tabbed => ShellArrange.Tabbed,
        LayoutPreset.MdiTileVertical => ShellArrange.TileVertical,
        _ => ShellArrange.Cascade,
    });

    public void CloseDocument(PocDocument document) => _factory.CloseDockable(document);

    public void CloseOthers(PocDocument keep)
    {
        using var op = PerfProbe.Current.TimeOperation("ui.closeOthers");
        foreach (var doc in OpenDocuments.Where(d => !ReferenceEquals(d, keep)).ToList())
        {
            CloseDocument(doc);
        }

        Activate(keep);
    }

    public void CloseAll()
    {
#if PD_DIAGNOSTICS
        _log("[d12] closeAll ids=" + string.Join(",", OpenDocuments.Select(d => d.Id + (FindWindow(d) is null ? "" : "(sdi)")))); // D-12 진단: 닫기 전 SDI 여부
#endif
        foreach (var doc in OpenDocuments.ToList())
        {
            CloseDocument(doc);
        }

        foreach (var doc in OpenDocuments)
        {
            WindowLoadState.Default.Release(doc); // 닫힘 알림 없이 목록에서 빠지는 문서도 불러오는 중을 해제한다
        }

        OpenDocuments.Clear();
        _mru.Clear();
        _minimized.Clear();
        SetActive(null);
    }

    public void Rename(PocDocument document, string title)
    {
        _log(Loc.Tr("log.window.renamed", document.Title, title));
        SetAutoTitle(document, false); // 사용자가 지은 이름은 언어가 바뀌어도 유지
        document.Title = title;
    }

    /// <summary>문서 추가. activate=false 는 레이아웃 복원용(문서마다 활성화하면 Dock 재배치가 문서 수만큼 일어남 — PERF-UI).</summary>
    internal void AddDocument(PocDocument doc, bool activate = true, string? requestKey = null)
    {
        _s.Owner.Activation.ClearOrigin(); // 열기·복원으로 생기는 활성 변경은 사용자 전환이 아니다 — 남은 출처 표식을 가져가지 않게 지운다

        // 복원 지연(B5): 복원 중 추가는 보이는지 아직 모른다 — 시작점·스코프는 화면 층 Build 판단 때 RestoreViewDeferral 이 찍는다
        if (!RestoreViewDeferral.MarkIfRestoring(doc))
        {
            // 열기 시간 시작점(셸). 종료점은 뷰 첫 렌더(PerfProbe.TrackOpen — 대기열이 있으면 시작점을 다시 찍지 않음)
            PerfProbe.Current.BeginOpen(WindowDisplay.LegacyOrId(doc.WindowId).ToLowerInvariant()); // 데모 3종은 grid·gantt·form 그대로
            if (requestKey is not null)
            {
                WindowLoadState.Default.Bind(requestKey, doc); // 2단계: 요청 토큰을 문서에 묶는다(오버레이·커서 포함)
            }
            else
            {
                WindowLoadState.Default.BeginOpen(doc); // 요청 없이 생긴 문서(레이아웃 복원 즉시 생성)
            }
        }

        using (PerfProbe.Current.TimeOperation("docs.open.dock")) { _factory.AddDockable(Docs, doc); } // K15 분해(제품판 빈 범위)
        using (PerfProbe.Current.TimeOperation("docs.open.list")) { OpenDocuments.Add(doc); }
        if (activate)
        {
            using (PerfProbe.Current.TimeOperation("docs.open.activate")) { Activate(doc); }
        }
    }
}

// 레이아웃 복원 본문 — 스냅샷 JSON 을 현재 화면에 적용(자동 저장 슬롯·개인 레이아웃 공용, MDI-10·UI-11). 옛 WindowCommands.Restore, 공유 상태는 WindowSession
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Diagnostics;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.Docking;

/// <summary>WindowCommands 파사드가 위임하는 레이아웃 복원. 생성자는 상태만 저장한다(구독 없음).</summary>
[WindowCommandsPart]
internal sealed class LayoutRestorer
{
    private readonly WindowSession _s;

    internal LayoutRestorer(WindowSession s) => _s = s;

    // 옛 WindowCommands 필드·도우미 이름 — 아래 본문을 옮기기만 하려고 같은 이름으로 잇는다.
    private PocDockFactory _factory => _s.Factory;
    private Action<string> _log => _s.Log;
    private Dictionary<DocumentKind, int> _seq => _s.Seq;
    private List<PocDocument> _mru => _s.Mru;
    private List<PocDocument> _minimized => _s.Minimized;
    private PocDocument? _restoreTarget { get => _s.RestoreTarget; set => _s.RestoreTarget = value; }
    private long _restoreUntil { set => _s.RestoreUntil = value; }
    private IDocumentDock Docs => _s.Docs;
    private ObservableCollection<PocDocument> OpenDocuments => _s.Owner.OpenDocuments;
    private IDockWindow? FindWindow(PocDocument doc) => _s.FindWindow(doc);
    private void CloseAll() => _s.Owner.CloseAll();
    private void AddDocument(PocDocument doc, bool activate = true) => _s.Owner.AddDocument(doc, activate);
    private bool IsDuplicateAllowed(DocumentKind kind) => _s.Owner.IsDuplicateAllowed(kind);
    private void SetDuplicateAllowed(DocumentKind kind, bool allowed) => _s.Owner.SetDuplicateAllowed(kind, allowed);
    private static void RestoreAutoTitle(PocDocument doc, DocumentSnapshot item) => DocumentAutoTitles.RestoreAutoTitle(doc, item);
    private void ApplySidePanels(bool? hidden) => _s.Owner.SidePanels.ApplySidePanels(hidden);
    private void SetMdiZOrder(IReadOnlyList<PocDocument> bottomToTop) => _s.Owner.SetMdiZOrder(bottomToTop);
    private void Float(PocDocument document) => _s.Owner.Activation.Float(document);
    private void ActivateCore(PocDocument document, bool restoreMinimized) => _s.Owner.Activation.ActivateCore(document, restoreMinimized);

    /// <summary>
    /// 레이아웃 JSON(Version 1·2)을 적용한다. 현재 문서를 모두 닫고 저장 상태로 다시 연다. JSON 이 비었거나 손상됐으면 아무것도 바꾸지 않고 false.
    /// </summary>
    public bool ApplyLayout(string? json)
    {
        using var op = PerfProbe.Current.TimeOperation("ui.layout.apply");
        var snapshot = LayoutSerializer.Deserialize(json);
        if (snapshot is null)
        {
            _log(Loc.Tr("log.layout.restoreEmpty"));
            return false;
        }

        using (RestoreViewDeferral.BeginCollect(_s)) // B5: 설정이 지연이면 보이는 문서만 즉시 만든다(판정은 화면 층 Build 때 — RestoreViewDeferral)
        {
            RestoreSnapshot(snapshot);
        }

        _log(Loc.Tr("log.layout.restored", OpenDocuments.Count));
#if PD_DIAGNOSTICS
        // D-12 진단: 스냅샷 문서 수·Id 와 복원 뒤 열린 문서 Id 를 나란히 남긴다(저장 쪽 누락인지 복원 쪽 유실인지 가른다)
        _log($"[d12] restore snapshot={snapshot.Documents.Count}[{string.Join(",", snapshot.Documents.Select(x => x.Id + (x.Window is null ? "" : "(sdi)")))}] open={OpenDocuments.Count}[{string.Join(",", OpenDocuments.Select(d => d.Id))}]");
#endif
        return true;
    }

    private void RestoreSnapshot(LayoutSnapshot snapshot)
    {
        // 분해 계측(team-lead): 단계별 동기 시간만 ui.layout.apply.<단계> 로 남긴다. 첫 렌더까지는 바깥 ui.layout.load/apply 의 frame 값.
        var t = Stopwatch.GetTimestamp();
        var keep = ReusableDocuments(snapshot); // 재사용할 열린 MDI 문서(Id→문서). null 이면 예전처럼 전부 닫고 다시 연다
        if (keep is null)
        {
            CloseAll();
        }
        else
        {
            CloseExcept(keep);
        }

        MdiTabGroup.Tidy(_factory, Docs); // 닫기로 비거나 1개 남은 탭 묶음 정리(묶음은 아래에서 스냅샷대로 다시 만든다)
        Step("close", ref t);
        _factory.SetDocumentDockLayoutMode(Docs, snapshot.Mode);
        foreach (var kind in Enum.GetValues<DocumentKind>())
        {
            var want = snapshot.DuplicateKinds.Contains(kind); // 옛 저장본(빈 목록)은 모두 끔
            if (IsDuplicateAllowed(kind) != want)
            {
                SetDuplicateAllowed(kind, want);
            }
        }

        Step("mode", ref t);

        // PERF-UI 복원 개선(team-lead 승인 ①~③). 문서마다 활성화하던 것(Dock 재배치 ×N)과 탭 재정렬(MoveDockable ×N)을 없앴다.
        // ② 저장된 탭 순서(snapshot.Documents = 저장 시 OpenDocuments 순)대로 활성화 없이 추가 → 탭 순서가 처음부터 맞다(MDI-10).
        var docs = new List<(PocDocument Doc, DocumentSnapshot Item)>();
        foreach (var item in snapshot.Documents)
        {
            _seq[item.Kind] = Math.Max(_seq.GetValueOrDefault(item.Kind), item.Seq);
            PocDocument doc;
            if (keep is not null && keep.TryGetValue(item.Id, out var kept))
            {
                doc = kept; // 뷰를 다시 만들지 않는다(PERF-UI layout.load·Q2 #21). 저장된 속성은 아래에서 스냅샷 값으로 다시 적용한다
                doc.Title = item.Title;
            }
            else
            {
                doc = new PocDocument { Id = item.Id, Kind = item.Kind, Seq = item.Seq, Title = item.Title };
                AddDocument(doc, activate: false);
            }

            LayoutSerializer.ApplyDocument(doc, item);
            RestoreAutoTitle(doc, item); // 자동 제목은 현재 언어로(Q17)
            if (item.MdiState == MdiWindowState.Minimized)
            {
                _minimized.Add(doc);
            }

            docs.Add((doc, item));
        }

        Step("add", ref t); // 문서 모델 생성·Dock 추가(뷰 생성은 첫 렌더 때 — frame 쪽에 잡힌다)

        // 측면 패널 탭 안에 있던 문서는 그 패널로 보낸 뒤(odev-1 계약), 패널 비율·탭 순서·알림 위치를 적용한다(Version 1·2 는 null → 현재 유지).
        foreach (var (doc, item) in docs.Where(d => d.Item.PanelId is not null))
        {
            _factory.SendDocumentToPanel(doc, item.PanelId!);
        }

        _factory.ApplyPanels(snapshot.Panels);
        ApplySidePanels(snapshot.SidePanelsHidden); // 순서 계약(odev-1): 문서 복원 → ApplyPanels → 측면 패널 숨김 → (제품) 로그 숨김
        var panelDocs = docs.Where(d => d.Item.PanelId is not null).Select(d => d.Doc).ToList();
        docs.RemoveAll(d => d.Item.PanelId is not null); // 아래 MDI Z 순서·SDI 처리 대상에서 뺀다(MRU 에는 맨 뒤로 넣는다)
        Step("panels", ref t);

        // 저장 당시 아래→위 순서. ZOrder(MDI·SDI 공통 MRU 기준)가 없는 옛 저장본은 MDI ZIndex 순(기존 동작).
        var bottomToTop = (docs.All(d => d.Item.ZOrder is not null)
            ? docs.OrderBy(d => d.Item.ZOrder)
            : docs.OrderBy(d => d.Item.ZIndex)).ToList();

        LayoutGroupCodec.Restore(_factory, Docs, snapshot, docs.Where(d => d.Item.Window is null).Select(d => d.Doc)); // MDI 탭 묶음(Version 5) — Z 확정 전에 만들어 SetMdiZOrder 가 묶음 단위로 매긴다
        // Z 순서(사용자 22:24): MDI 쌓임은 odev-7c 공용 API 로 확정(항상 위 그룹은 그룹 안 순서를 지켜 맨 위로).
        SetMdiZOrder(bottomToTop.Where(d => d.Item.Window is null).Select(d => d.Doc).ToList());
        Step("zorder", ref t);

        // ③ SDI 는 MDI 추가가 끝난 뒤 아래→위 순으로 일괄 Float(호스트가 나중에 Show 된 창을 위에 둔다 — odev-4b 계약).
        foreach (var (doc, item) in bottomToTop.Where(d => d.Item.Window is not null))
        {
            Float(doc);
            LayoutSerializer.ApplyWindow(FindWindow(doc), item.Window!);
        }

        Step("float", ref t);

        // ① MRU 를 저장 Z 순서로 직접 구성(앞=최상위) — 다음 저장의 ZOrder·odev-7c Z 재매김 훅(ApplyMruZOrder)의 기준.
        // Float·추가 중 Dock 활성 변경이 MRU 를 건드렸어도 여기서 덮는다. 활성화는 아래에서 1회만.
        _mru.Clear();
        _mru.AddRange(bottomToTop.Select(d => d.Doc).Reverse());
        _mru.AddRange(panelDocs);

        // 마지막 1회 활성화: 저장된 활성 문서(= 최상위), 없으면 최상위 문서.
        if ((OpenDocuments.FirstOrDefault(d => d.Id == snapshot.ActiveId) ?? _mru.FirstOrDefault()) is { } active)
        {
            // 최소화 상태를 풀지 않고 활성만 맞춘다. Dock 이 문서 추가 뒤 늦게 보내는 활성 변경(마지막 추가 문서)이
            // 탭 강조를 덮어쓰지 않도록 한 프레임 뒤 한 번 더 적용한다(추정 원인 — otest 역라우팅 #2 MDI-10).
            _restoreTarget = active;
            _restoreUntil = Environment.TickCount64 + 1500; // 1.5초 보호(명시적 Activate 가 오면 즉시 해제)
            ActivateCore(active, restoreMinimized: false);
            Dispatcher.UIThread.Post(() =>
            {
                if (OpenDocuments.Contains(active) && ReferenceEquals(_restoreTarget, active))
                {
                    ActivateCore(active, restoreMinimized: false);
                }
            }, DispatcherPriority.Background);
        }

        Step("activate", ref t);
    }

    /// <summary>
    /// 재사용할 열린 문서(odev-perf2 A, team-lead 승인). 지금 주 문서 영역 MDI 에 있는 문서(분리 창·측면 패널 아님)가
    /// 스냅샷의 MDI 항목과 Id·종류가 모두 같을 때만 재사용한다(Id 가 같아도 종류가 다르면 새로 만든다).
    /// 탭 순서(MDI-10)를 그대로 지키려고, 재사용 문서가 스냅샷 앞쪽에 모여 있고 열린 목록·Dock 목록에서도 스냅샷과 같은 순서일 때만 쓴다.
    /// 하나라도 어긋나면 null(예전 경로 — 전부 닫고 다시 연다).
    /// </summary>
    private Dictionary<string, PocDocument>? ReusableDocuments(LayoutSnapshot snapshot)
    {
        var open = OpenDocuments.ToList();
        var dockOrder = Docs.VisibleDockables?.ToList() ?? new List<IDockable>();
        var keep = new Dictionary<string, PocDocument>(StringComparer.Ordinal);
        var (lastOpen, lastDock, newSeen) = (-1, -1, false);
        foreach (var item in snapshot.Documents)
        {
            var doc = item.Window is null && item.PanelId is null
                ? open.FirstOrDefault(d => d.Id == item.Id && d.Kind == item.Kind && ReferenceEquals(d.Owner, Docs) && FindWindow(d) is null)
                : null;
            if (doc is null || keep.ContainsKey(item.Id))
            {
                newSeen = true;
                continue;
            }

            var (iOpen, iDock) = (open.IndexOf(doc), dockOrder.IndexOf(doc));
            if (newSeen || iOpen < lastOpen || iDock < lastDock)
            {
                return null; // 재사용 문서 사이에 새 문서가 끼거나 순서가 다르다 → 탭 순서를 지키려고 예전 경로
            }

            (lastOpen, lastDock) = (iOpen, iDock);
            keep[item.Id] = doc;
        }

        return keep.Count > 0 ? keep : null;
    }

    /// <summary>재사용 문서만 남기고 닫는다. CloseAll 과 같이 목록 잔여·MRU·최소화 목록을 정리한다(활성·MRU 는 뒤에서 스냅샷대로 다시 만든다).</summary>
    private void CloseExcept(IReadOnlyDictionary<string, PocDocument> keep)
    {
        var kept = new HashSet<PocDocument>(keep.Values);
        foreach (var doc in OpenDocuments.Where(d => !kept.Contains(d)).ToList())
        {
            _s.Owner.CloseDocument(doc);
        }

        foreach (var doc in OpenDocuments.Where(d => !kept.Contains(d)).ToList())
        {
            OpenDocuments.Remove(doc); // 닫기 알림을 거치지 않은 잔여(CloseAll 의 Clear 와 같은 정리)
        }

        _mru.Clear();
        _minimized.Clear();
    }

    /// <summary>복원 단계 동기 시간 기록(진단 빌드만) 후 기준 시각을 지금으로 옮긴다.</summary>
    private static void Step(string name, ref long t)
    {
        var now = Stopwatch.GetTimestamp();
        if (PerfProbe.IsEnabled)
        {
            var ms = Stopwatch.GetElapsedTime(t, now).TotalMilliseconds;
            PerfProbe.Current.RecordOperation("ui.layout.apply." + name, ms, ms);
        }

        t = now;
    }
}

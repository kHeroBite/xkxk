// MDI 탭 묶음 창 — 문서 dock(탭형)이면서 MDI 창인 형식과 묶기·꺼내기·풀기·닫기·열거 계약(B안, S0 확정 2026-11-10)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// MDI 문서 칸 안의 탭 묶음 창. MDI dock 의 VisibleDockables 에 IMdiDocument 로 들어가 MDI 창 하나로 그려지고,
/// 창 안은 Dock 의 문서 dock 템플릿(LayoutMode=Tabbed)이 탭 줄·탭 클릭·탭 끌어내기를 그린다(S0 판정 4조건 통과).
/// 묶음 제목은 활성 탭 제목을 따른다(ActiveDockable 이 바뀌면 자동, 탭 제목만 바뀌면 SyncTitle 호출).
/// </summary>
public sealed class MdiTabGroupDock : DocumentDock, IMdiDocument
{
    private DockRect _mdiBounds;
    private MdiWindowState _mdiState;
    private int _mdiZIndex;

    public MdiTabGroupDock()
    {
        LayoutMode = DocumentLayoutMode.Tabbed;
        CanCreateDocument = false;
        PropertyChanged += OnSelfChanged;
    }

    public DockRect MdiBounds
    {
        get => _mdiBounds;
        set => SetProperty(ref _mdiBounds, value);
    }

    public MdiWindowState MdiState
    {
        get => _mdiState;
        set => SetProperty(ref _mdiState, value);
    }

    public int MdiZIndex
    {
        get => _mdiZIndex;
        set => SetProperty(ref _mdiZIndex, value);
    }

    /// <summary>묶음 안 문서(탭 순서).</summary>
    public IReadOnlyList<PocDocument> Documents =>
        (VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<PocDocument>().ToList();

    /// <summary>제목을 활성 탭 제목으로 맞춘다(탭 이름 바꾸기·언어 전환 뒤 호출).</summary>
    public void SyncTitle()
    {
        if (ActiveDockable is { } active && Title != active.Title)
        {
            Title = active.Title;
        }
    }

    private void OnSelfChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActiveDockable))
        {
            SyncTitle();
        }
    }
}

/// <summary>묶음 변화 종류 — MdiTabGroup.Changed 로 알린다(창 메뉴·작업표시줄 탭·진단이 다시 그릴 신호).</summary>
public enum MdiTabGroupChange
{
    Created,
    Joined,
    Detached,
    Dissolved,
    Closed,
}

/// <summary>
/// 묶기·꺼내기·풀기·닫기·열거. 문서는 Dock 트리 안에 남으므로 FindDockable 등 트리 연산이 그대로 동작한다.
/// 닫기 규칙(team-lead 2026-11-10): 탭 X = 그 탭만(Dock 기본 CloseDockable), 묶음 창 캡션 닫기 = CloseGroup(묶음 전체),
/// 문서가 1개 남으면 풀어 보통 MDI 창으로 — Dock 의 탭 끌어내기·탭 닫기 뒤에는 Tidy 를 불러야 한다(S0 실측: Dock 은 스스로 풀지 않음).
/// </summary>
public static class MdiTabGroup
{
    private static int s_seq;

    /// <summary>묶음이 생기거나 바뀌거나 풀리거나 닫혔다.</summary>
    public static event Action<MdiTabGroupDock, MdiTabGroupChange>? Changed;

    /// <summary>MDI dock 바로 아래 문서와 묶음 안 문서를 모두 센다(Wave3 OfType&lt;PocDocument&gt; 대체).</summary>
    public static IEnumerable<PocDocument> Documents(IDock dock)
    {
        foreach (var d in dock.VisibleDockables ?? Enumerable.Empty<IDockable>())
        {
            if (d is PocDocument doc)
            {
                yield return doc;
            }
            else if (d is MdiTabGroupDock g)
            {
                foreach (var inner in g.Documents)
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>MDI dock 바로 아래 묶음들.</summary>
    public static IEnumerable<MdiTabGroupDock> Groups(IDock dock) =>
        (dock.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<MdiTabGroupDock>();

    /// <summary>문서가 묶음 안에 있으면 그 묶음, 아니면 null.</summary>
    public static MdiTabGroupDock? GroupOf(IDockable doc) => doc.Owner as MdiTabGroupDock;

    /// <summary>문서가 MDI 창으로 보일 때의 창 단위(묶음 안이면 묶음, 아니면 문서 자신). 위치·Z·상태는 이 대상에서 읽는다.</summary>
    public static IMdiDocument? WindowOf(PocDocument doc) => GroupOf(doc) is { } g ? g : doc;

    /// <summary>
    /// source 창(보통 문서 또는 묶음)의 문서 전부를 target 창 탭 줄 끝에 붙인다. 대상 위치·크기는 그대로, 끌던 문서(source 가 묶음이면 그 활성 탭)가 활성.
    /// target 이 보통 문서면 그 자리에 묶음을 만든다. 빈 원천 묶음은 지운다.
    /// </summary>
    public static MdiTabGroupDock Join(IFactory factory, IDocumentDock mdi, IMdiDocument target, IMdiDocument source)
    {
        target = target is PocDocument t ? WindowOf(t)! : target; // 묶음 안 문서를 주면 그 묶음 창(창 단위)으로 — 기존 묶음에서 빼 새 묶음을 만들지 않는다
        source = source is PocDocument s ? WindowOf(s)! : source;
        if (ReferenceEquals(target, source))
        {
            throw new ArgumentException("같은 창끼리는 합칠 수 없습니다.", nameof(source));
        }

        var group = target as MdiTabGroupDock ?? CreateGroup(factory, mdi, (PocDocument)target);
        var moving = source is MdiTabGroupDock sg ? sg.Documents.ToList() : new List<PocDocument> { (PocDocument)source };
        var active = source is MdiTabGroupDock sg2 ? sg2.ActiveDockable as PocDocument ?? moving[^1] : moving[0];
        foreach (var doc in moving) // 마지막 탭을 기준으로 줘야 끝에 붙는다(null 이면 Dock 은 마지막 탭 앞에 넣는다 — FactoryBase.MoveDockable 디컴파일)
        {
            factory.MoveDockable(doc.Owner as IDock ?? mdi, group, doc, group.VisibleDockables?.LastOrDefault());
        }

        if (source is MdiTabGroupDock empty)
        {
            Remove(factory, empty);
        }

        group.ActiveDockable = active;
        factory.SetActiveDockable(group);
        Changed?.Invoke(group, MdiTabGroupChange.Joined);
        return group;
    }

    /// <summary>
    /// first 문서 자리에 묶음을 만든다(위치·크기·상태·Z 를 이어받음). id 를 주면 그 Id 로 만든다(레이아웃 복원용, 이미 있으면 새 번호).
    /// </summary>
    public static MdiTabGroupDock CreateGroup(IFactory factory, IDocumentDock mdi, PocDocument first, string? id = null)
    {
        var group = new MdiTabGroupDock
        {
            Id = UniqueId(mdi, id),
            Title = first.Title,
            VisibleDockables = factory.CreateList<IDockable>(),
            MdiBounds = first.MdiBounds,
            MdiState = first.MdiState,
            MdiZIndex = first.MdiZIndex,
        };
        factory.AddDockable(mdi, group);
        factory.InitDockable(group, mdi);
        factory.MoveDockable(first.Owner as IDock ?? mdi, group, first, null);
        group.ActiveDockable = first;
        Changed?.Invoke(group, MdiTabGroupChange.Created);
        return group;
    }

    /// <summary>탭 하나를 묶음 밖 MDI 창으로 꺼낸다(묶음 왼쪽 위에서 30 DIP 계단). 1개 남으면 묶음을 푼다.</summary>
    public static void Detach(IFactory factory, IDocumentDock mdi, PocDocument doc)
    {
        if (GroupOf(doc) is not { } group)
        {
            return;
        }

        factory.MoveDockable(group, mdi, doc, null);
        var b = group.MdiBounds;
        doc.MdiBounds = new DockRect(b.X + 30, b.Y + 30, b.Width, b.Height);
        doc.MdiState = MdiWindowState.Normal;
        doc.MdiZIndex = TopZ(mdi) + 1;
        Changed?.Invoke(group, MdiTabGroupChange.Detached);
        DissolveIfSingle(factory, mdi, group);
    }

    /// <summary>묶음에 문서가 1개 이하면 같은 위치·크기·상태·Z 의 보통 MDI 창으로 되돌리고 묶음을 지운다. 풀었으면 true.</summary>
    public static bool DissolveIfSingle(IFactory factory, IDocumentDock mdi, MdiTabGroupDock group)
    {
        var rest = group.Documents;
        if (rest.Count > 1)
        {
            return false;
        }

        foreach (var doc in rest)
        {
            factory.MoveDockable(group, mdi, doc, null);
            doc.MdiBounds = group.MdiBounds;
            doc.MdiState = group.MdiState;
            doc.MdiZIndex = group.MdiZIndex;
            factory.SetActiveDockable(doc);
        }

        Remove(factory, group);
        Changed?.Invoke(group, MdiTabGroupChange.Dissolved);
        return true;
    }

    /// <summary>
    /// 트리 변화 뒤 정리 — MDI dock 의 묶음 중 문서 1개 이하인 것을 모두 푼다(멱등). 탭 끌어내기·탭 X·문서 이동 뒤 부른다.
    /// 연결 지점: PocDockFactory.OnDockableMoved·OnDockableRemoved·OnDockableClosed 뒤(Dispatcher 한 바퀴 뒤 권장 — Dock 이 끌기 처리 중일 수 있음).
    /// </summary>
    public static int Tidy(IFactory factory, IDocumentDock mdi)
    {
        var n = 0;
        foreach (var g in Groups(mdi).ToList())
        {
            n += DissolveIfSingle(factory, mdi, g) ? 1 : 0;
        }

        return n;
    }

    /// <summary>묶음 창 캡션 닫기 = 묶음 전체 닫기. closeDocument 는 셸의 문서 닫기 경로(OpenDocuments 정리 포함)를 넘긴다.</summary>
    public static void CloseGroup(IFactory factory, MdiTabGroupDock group, Action<PocDocument> closeDocument)
    {
        foreach (var doc in group.Documents)
        {
            closeDocument(doc);
        }

        Remove(factory, group);
        Changed?.Invoke(group, MdiTabGroupChange.Closed);
    }

    private static void Remove(IFactory factory, MdiTabGroupDock group)
    {
        if (group.Owner is IDock owner && owner.VisibleDockables?.Contains(group) == true)
        {
            factory.RemoveDockable(group, false);
        }
    }

    private static string UniqueId(IDocumentDock mdi, string? wanted)
    {
        var used = new HashSet<string>(Groups(mdi).Select(g => g.Id), StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(wanted) && !used.Contains(wanted))
        {
            return wanted;
        }

        string id;
        do { id = "TabGroup-" + (++s_seq); } while (used.Contains(id));
        return id;
    }

    private static int TopZ(IDock mdi) =>
        (mdi.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<IMdiDocument>().Select(d => d.MdiZIndex).DefaultIfEmpty(0).Max();
}

#if PD_DIAGNOSTICS // 개발 진단 mdigroup.probe — Wave3 정식 명령(window.tabJoin·placement.state.groups)과 이름이 겹치지 않는 모델 직접 조작용
internal static class MdiTabGroupDiag
{
#pragma warning disable CA2255 // 등록 파일(RemoteCommandRouter.cs)이 배정 밖이라 모듈 초기화로 붙인다
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init() =>
        Diagnostics.Remote.RemoteCommandRouter.Default.Register("mdigroup.probe",
            "MDI 탭 묶음 모델 직접 조작 op=join target=<문서·묶음 Id> source=<문서·묶음 Id> | op=detach doc= | op=tidy | op=state → {groups:[{id,title,active,docs,bounds}], mdiChildren, allDocs}", a =>
            {
                var f = Diagnostics.Remote.RemoteDockAccess.Factory;
                var mdi = f.Documents ?? throw new InvalidOperationException("레이아웃 미초기화");
                string? Arg(string key) => a[key]?.ToString();
                IDockable? Find(string key) => f.Root is { } r ? f.FindDockable(r, d => d.Id == Arg(key)) : null;
                var op = Arg("op");
                if (op == "join") { MdiTabGroup.Join(f, mdi, Find("target") as IMdiDocument ?? throw new ArgumentException("target"), Find("source") as IMdiDocument ?? throw new ArgumentException("source")); }
                else if (op == "detach") { MdiTabGroup.Detach(f, mdi, Find("doc") as PocDocument ?? throw new ArgumentException("doc")); }
                else if (op == "tidy") { MdiTabGroup.Tidy(f, mdi); }
                var groups = new System.Text.Json.Nodes.JsonArray(MdiTabGroup.Groups(mdi).Select(g => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = g.Id, ["title"] = g.Title, ["active"] = g.ActiveDockable?.Id,
                    ["docs"] = new System.Text.Json.Nodes.JsonArray(g.Documents.Select(d => (System.Text.Json.Nodes.JsonNode?)d.Id).ToArray()),
                    ["bounds"] = $"{g.MdiBounds.X},{g.MdiBounds.Y},{g.MdiBounds.Width},{g.MdiBounds.Height}",
                }).ToArray());
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["groups"] = groups,
                    ["mdiChildren"] = new System.Text.Json.Nodes.JsonArray((mdi.VisibleDockables ?? Enumerable.Empty<IDockable>()).Select(d => (System.Text.Json.Nodes.JsonNode?)(d.Id + ":" + d.GetType().Name)).ToArray()),
                    ["allDocs"] = MdiTabGroup.Documents(mdi).Count(),
                };
            });
}
#endif

// 원격 진단 — placement.state 의 탭 묶음(groups) 항목과 시험 명령 window.tabJoin·window.tabJoinTargets(UI 시험 L0 경로). 진단 빌드 전용, 제품 동작 불변
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static partial class RemotePlacementCommands
{
#pragma warning disable CA2255 // 등록 지점(Register·RemoteCommandRouter.cs)이 배정 밖이라 모듈 초기화로 붙인다(MdiTabGroupDiag·MdiTabJoinDiag 와 같은 방식)
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void InitGroups()
    {
        var r = RemoteCommandRouter.Default;
        r.Register("window.tabJoin",
            "탭 합류 직접 실행(끌기 없이 합류 경로만) source=<문서·묶음 Id> target=<문서·묶음 Id> [kind=icon|strip 기본 icon] — 둘 다 MDI 면 MdiTabGroup.Join, 둘 다 SDI 분리 창이면 WindowTabJoinService.Join → {status, mode, kind, ...}",
            a => TabJoin(a));
        r.Register("window.tabJoinTargets",
            "탭 합류 표적 목록 [source=<MDI 문서·묶음 Id> 지정 시 그 창 기준(자기 창 제외)] → {mdi:[{id,kind,window,z,rect(MDI 문서 칸 DIP)}], sdi:[{key,docs,targets:[{kind,rect(창 DIP)}]}], mdiOutcome, sdiOutcome}",
            a => TabJoinTargets(a));
    }

    /// <summary>placement.state 의 groups — MDI 칸별 탭 묶음(Id·제목·활성·문서 순서·위치·상태·Z). 묶음이 없으면 빈 배열.</summary>
    private static JsonArray GroupsJson()
    {
        var list = new JsonArray();
        if (RemoteDockAccess.Factory.Root is not { } root)
        {
            return list;
        }

        foreach (var mdi in MdiDocks(root))
        {
            foreach (var g in MdiTabGroup.Groups(mdi))
            {
                list.Add(new JsonObject
                {
                    ["id"] = g.Id,
                    ["mdi"] = mdi.Id,
                    ["title"] = g.Title,
                    ["active"] = g.ActiveDockable?.Id,
                    ["docs"] = new JsonArray(g.Documents.Select(d => (JsonNode?)d.Id).ToArray()),
                    ["bounds"] = BoundsJson(g.MdiBounds),
                    ["state"] = g.MdiState.ToString(),
                    ["z"] = g.MdiZIndex,
                });
            }
        }

        return list;
    }

    private static JsonObject TabJoin(JsonObject a)
    {
        var f = RemoteDockAccess.Factory;
        var root = f.Root ?? throw new RemoteNotReadyException("no-layout", "레이아웃이 아직 없습니다.");
        var kind = a.Str("kind") ?? "icon";
        if (kind is not ("icon" or "strip"))
        {
            throw new RemoteCommandException("kind 는 icon 또는 strip 입니다.");
        }

        var source = FindAny(root, a.ReqStr("source")) ?? throw new RemoteCommandException("source 없음");
        var target = FindAny(root, a.ReqStr("target")) ?? throw new RemoteCommandException("target 없음");
        var sourceWin = LayoutSerializer.FindWindowOf(root, source);
        var targetWin = LayoutSerializer.FindWindowOf(root, target);
        if (sourceWin is null && targetWin is null)
        {
            return MdiJoin(f, source, target, kind);
        }

        if (sourceWin is null || targetWin is null)
        {
            return RemoteDockAccess.Rejected("cross-mdi-sdi"); // MDI↔SDI 교차 합류는 범위 밖(정본 01 §4.4)
        }

        return SdiJoin(sourceWin, targetWin, source as PocDocument, kind);
    }

    /// <summary>MDI 창 단위(묶음 안 문서면 그 묶음)로 바꿔 MdiTabGroup.Join. strip 은 대상이 묶음(탭 줄이 있는 창)일 때만 받는다.</summary>
    private static JsonObject MdiJoin(IFactory f, IDockable source, IDockable target, string kind)
    {
        var s = source is PocDocument sd ? MdiTabGroup.WindowOf(sd) : source as IMdiDocument;
        var t = target is PocDocument td ? MdiTabGroup.WindowOf(td) : target as IMdiDocument;
        if (s is not (PocDocument or MdiTabGroupDock) || t is not (PocDocument or MdiTabGroupDock))
        {
            return RemoteDockAccess.Rejected("not-mdi-window");
        }

        if (ReferenceEquals(s, t))
        {
            return RemoteDockAccess.Rejected("same-window");
        }

        if (s.Owner is not IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } mdi || mdi is PanelDocumentDock || !ReferenceEquals(t.Owner, mdi))
        {
            return RemoteDockAccess.Rejected("not-same-mdi");
        }

        if (kind == "strip" && t is not MdiTabGroupDock)
        {
            return RemoteDockAccess.Rejected("no-tab-strip"); // 탭 줄은 이미 묶인 창에만 있다
        }

        var g = MdiTabGroup.Join(f, mdi, t, s);
        return new JsonObject
        {
            ["status"] = "Done",
            ["mode"] = "mdi",
            ["kind"] = kind,
            ["group"] = g.Id,
            ["active"] = g.ActiveDockable?.Id,
            ["docs"] = new JsonArray(g.Documents.Select(d => (JsonNode?)d.Id).ToArray()),
            ["bounds"] = BoundsJson(g.MdiBounds),
        };
    }

    /// <summary>SDI 분리 창끼리 WindowTabJoinService.Join(원천 루트, 대상 루트, 끌던 문서). strip 은 대상 표면이 탭 줄 표적을 낼 때만 받는다(표면 미등록 호스트는 검사 생략).</summary>
    private static JsonObject SdiJoin(IDockWindow sourceWin, IDockWindow targetWin, PocDocument? dragged, string kind)
    {
        if (ReferenceEquals(sourceWin, targetWin))
        {
            return RemoteDockAccess.Rejected("same-window");
        }

        if (sourceWin.Layout is not IRootDock sourceRoot || targetWin.Layout is not IRootDock targetRoot)
        {
            return RemoteDockAccess.Rejected("no-window-root");
        }

        var surface = WindowTabJoinService.Surfaces.FirstOrDefault(x => ReferenceEquals(x.Root, targetRoot));
        if (kind == "strip" && surface is not null && !surface.TargetElements().Any(e => e.Kind == TabJoinKind.TabStrip))
        {
            return RemoteDockAccess.Rejected("no-tab-strip");
        }

        var done = WindowTabJoinService.Join(sourceRoot, targetRoot, dragged);
        var docs = Leaves(targetRoot).OfType<PocDocument>().ToList();
        return new JsonObject
        {
            ["status"] = done ? "Done" : "Rejected",
            ["mode"] = "sdi",
            ["kind"] = kind,
            ["targetWindow"] = surface?.Key ?? targetWin.Id, // 표면 키(window.tabJoinTargets 의 sdi[].key 와 같은 값), 표면 없는 호스트는 창 Id
            ["active"] = WindowCommands.FindDocument(targetRoot)?.Id,
            ["docs"] = new JsonArray(docs.Select(d => (JsonNode?)d.Id).ToArray()),
            ["sourceClosed"] = sourceWin.Owner is null || !Leaves(sourceRoot).Any(),
        };
    }

    private static JsonObject TabJoinTargets(JsonObject a)
    {
        var want = a.Str("source");
        var windows = RemoteDockAccess.Factory.DockControls.OfType<Control>()
            .SelectMany(c => TabSlotMarker.Descendants(c).OfType<MdiDocumentWindow>())
            .Where(w => w.DataContext is IMdiDocument and (PocDocument or MdiTabGroupDock))
            .Distinct().ToList();
        if (want is not null)
        {
            windows = windows.Where(w => w.DataContext is IDockable d && d.Id == want).ToList();
            if (windows.Count == 0)
            {
                throw new RemoteCommandException("source 창 없음");
            }
        }

        var mdi = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in windows) // source 미지정이면 창마다 모은 표적의 합(자기 창 제외 목록이라 창이 둘 이상이면 전부 나온다)
        {
            foreach (var t in MdiTabJoinDrag.CollectTargets(w).Where(t => seen.Add(t.Id)))
            {
                mdi.Add(new JsonObject
                {
                    ["id"] = t.Id,
                    ["kind"] = t.Kind == TabJoinKind.Icon ? "icon" : "strip",
                    ["window"] = t.WindowKey,
                    ["z"] = t.Z,
                    ["rect"] = RectJson(t.Rect),
                });
            }
        }

        var sdi = new JsonArray();
        foreach (var s in WindowTabJoinService.Surfaces)
        {
            var targets = new JsonArray();
            foreach (var (k, element, local) in s.TargetElements())
            {
                var p = s.Frame is { } frame ? element.TranslatePoint(local.TopLeft, frame) : null;
                targets.Add(new JsonObject
                {
                    ["kind"] = k == TabJoinKind.Icon ? "icon" : "strip",
                    ["rect"] = p is { } q ? RectJson(new Rect(q, local.Size)) : null,
                });
            }

            sdi.Add(new JsonObject
            {
                ["key"] = s.Key,
                ["docs"] = new JsonArray((s.Root is { } r ? Leaves(r).OfType<PocDocument>() : Enumerable.Empty<PocDocument>()).Select(d => (JsonNode?)d.Id).ToArray()),
                ["targets"] = targets,
            });
        }

        return new JsonObject
        {
            ["mdi"] = mdi,
            ["sdi"] = sdi,
            ["mdiOutcome"] = MdiTabJoinDrag.LastOutcome,
            ["sdiOutcome"] = WindowTabJoinService.LastOutcome,
        };
    }

    private static JsonObject RectJson(Rect r) => new() { ["x"] = r.X, ["y"] = r.Y, ["w"] = r.Width, ["h"] = r.Height };

    /// <summary>레이아웃의 MDI 문서 칸(분할로 여럿일 수 있다). 패널·묶음(Tabbed)·SDI 창 루트 아래는 제외(PocDockFactory.TabGroups 와 같은 기준).</summary>
    private static IEnumerable<IDocumentDock> MdiDocks(IDockable node)
    {
        if (node is IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } mdi and not PanelDocumentDock and not MdiTabGroupDock)
        {
            yield return mdi;
            yield break;
        }

        if (node is MdiTabGroupDock || node is not IDock dock)
        {
            yield break;
        }

        foreach (var child in dock.VisibleDockables ?? Enumerable.Empty<IDockable>())
        {
            foreach (var m in MdiDocks(child))
            {
                yield return m;
            }
        }
    }

    /// <summary>Id 로 노드를 찾는다 — 주 레이아웃 트리, 없으면 SDI 분리 창 루트들(Windows)까지.</summary>
    private static IDockable? FindAny(IRootDock root, string id)
    {
        static IDockable? In(IDockable node, string id)
        {
            if (node.Id == id) { return node; }
            return node is IDock dock ? (dock.VisibleDockables ?? Enumerable.Empty<IDockable>()).Select(c => In(c, id)).FirstOrDefault(x => x is not null) : null;
        }

        return In(root, id) ?? (root.Windows ?? Enumerable.Empty<IDockWindow>()).Select(w => w.Layout is { } l ? In(l, id) : null).FirstOrDefault(x => x is not null);
    }

    /// <summary>dock 이 아닌 잎(문서·도구) — SDI 창 안 문서 목록·빈 창 판정.</summary>
    private static IEnumerable<IDockable> Leaves(IDockable node)
    {
        if (node is not IDock dock)
        {
            yield return node;
            yield break;
        }

        foreach (var child in (dock.VisibleDockables ?? Enumerable.Empty<IDockable>()).Where(c => c is not IProportionalDockSplitter))
        {
            foreach (var leaf in Leaves(child))
            {
                yield return leaf;
            }
        }
    }
}
#endif

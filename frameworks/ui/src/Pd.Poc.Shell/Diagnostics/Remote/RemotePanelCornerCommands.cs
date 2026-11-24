// 원격 명령(요건 3 패널 끝 확장 판정) — 소유·순서·버튼 조회, 버튼과 같은 경로의 클릭, 규칙 순수 함수 표본 계산, 뷰 동일성 지문
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemotePanelCornerCommands
{
    private static PocDockFactory Factory => RemoteDockAccess.Factory;

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("panel.corner.state", "패널 끝 확장 상태 → {supported, owners, order, placements[{id,edge,rank,visible}], truncated[{panel,corner}], buttons[{id,panel,corner,direction,glyph,tip,focusable,visible,shown,hitTest,rect{x,y,w,h}}], proximity{distance,windows[{buttons,subscribed,moves,hitTests}]}, tree{OuterPane|MainPane|CenterPane:{orientation,children}}}", _ => State());
        r.Register("panel.corner.click", "버튼과 같은 경로 panel=<패널 Id> corner=TopLeft|TopRight|BottomLeft|BottomRight → {status: Done|Rejected}", a =>
        {
            if (!Enum.TryParse<PanelCorner>(a.ReqStr("corner"), out var corner))
            {
                throw new RemoteCommandException("corner 값이 잘못됐습니다");
            }

            return new JsonObject { ["status"] = Factory.Corners.ClaimCorner(a.ReqStr("panel"), corner) ? "Done" : "Rejected" };
        });
        r.Register("panel.corner.release", "모서리 기록 지우기 corner=<모서리> → {status: Done|Rejected}", a =>
        {
            if (!Enum.TryParse<PanelCorner>(a.ReqStr("corner"), out var corner))
            {
                throw new RemoteCommandException("corner 값이 잘못됐습니다");
            }

            return new JsonObject { ["status"] = Factory.Corners.ReleaseCorner(corner) ? "Done" : "Rejected" };
        });
        r.Register("panel.corner.rules", "규칙 순수 함수 표본 panels=id:Edge:rank[:hidden],… owners=Corner:id,… → {pinwheel, order, truncated, claimable}", a => Rules(a.ReqStr("panels"), a.Str("owners")));
        r.Register("panel.corner.content", "패널 내용 상태 pane=<패널 Id> [select=n] [scrollY=dip] [text=값] → {found, list{hash,selected,count}, scroll{hash,y,extent,viewport}, text{hash,value}}", ContentState);
        r.Register("panel.corner.holdLeave", "시험 전용: 실제 커서가 창 밖이라 OS 가 곧바로 보내는 창 떠남을 근접 판정에서 무시 on=true|false → {on}", a =>
        {
            PanelCornerProximity.HoldExitForTest = a.Bool("on") == true;
            return new JsonObject { ["on"] = PanelCornerProximity.HoldExitForTest };
        });
        r.Register("panel.corner.focus", "포커스 지정(OS 입력 아님) id=<버튼 Id> method=Tab|Pointer, 또는 method=None 으로 포커스 해제 → {focused}", FocusButton);
        r.Register("panel.corner.views", "뷰 동일성 지문 → {controls:[{type,id,hash}]} — 도구 패널·문서 칸·문서 내용 컨트롤의 인스턴스 해시. 전후 비교로 뷰 재생성 여부를 가른다", _ => Views());
    }

    /// <summary>패널 내용 상태 조회·설정: 패널 안 첫 ScrollViewer·ListBox·TextBox 의 값과 인스턴스 해시. 버튼 전후 비교로 상태 소실을 가른다.</summary>
    private static JsonObject ContentState(JsonObject a)
    {
        var pane = RemoteDockAccess.FindDock(a.ReqStr("pane"));
        var under = VisualTreeInspector.Main().GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.GetType().Name == "ToolChromeControl" && ReferenceEquals(c.DataContext, pane))
            ?.GetVisualDescendants().OfType<Control>().ToList() ?? new List<Control>();
        var list = under.OfType<ListBox>().FirstOrDefault();
        var scroll = under.OfType<ScrollViewer>().OrderByDescending(s => s.Extent.Height - s.Viewport.Height).FirstOrDefault(); // 머리줄 탭 줄(범위 0)이 아니라 실제로 스크롤되는 것
        var text = under.OfType<TextBox>().FirstOrDefault();
        if (a.Str("select") is { } select && list is not null)
        {
            list.SelectedIndex = int.Parse(select);
        }

        if (a.Str("scrollY") is { } scrollY && scroll is not null)
        {
            scroll.Offset = new Avalonia.Vector(scroll.Offset.X, double.Parse(scrollY, System.Globalization.CultureInfo.InvariantCulture));
        }

        if (a.Str("gridSelect") is { } gridSelect && under.OfType<DataGrid>().FirstOrDefault() is { } target)
        {
            target.SelectedIndex = int.Parse(gridSelect);
        }

        if (a.Str("text") is { } value && text is not null)
        {
            text.Text = value;
        }

        return new JsonObject
        {
            ["found"] = under.Count,
            ["list"] = list is null ? null : new JsonObject { ["hash"] = RuntimeHelpers.GetHashCode(list), ["selected"] = list.SelectedIndex, ["count"] = list.ItemCount },
            ["scroll"] = scroll is null ? null : new JsonObject { ["hash"] = RuntimeHelpers.GetHashCode(scroll), ["y"] = scroll.Offset.Y, ["extent"] = scroll.Extent.Height, ["viewport"] = scroll.Viewport.Height },
            ["text"] = text is null ? null : new JsonObject { ["hash"] = RuntimeHelpers.GetHashCode(text), ["value"] = text.Text },
            ["grid"] = under.OfType<DataGrid>().FirstOrDefault() is { } grid ? new JsonObject { ["hash"] = RuntimeHelpers.GetHashCode(grid), ["selected"] = grid.SelectedIndex } : null,
        };
    }

    private static JsonObject FocusButton(JsonObject a)
    {
        var main = VisualTreeInspector.Main();
        var method = a.ReqStr("method");
        if (method == "None")
        {
            main.FocusManager?.Focus(null!, NavigationMethod.Unspecified, KeyModifiers.None);
            return new JsonObject { ["focused"] = false };
        }

        var id = a.ReqStr("id");
        var button = main.GetVisualDescendants().OfType<PanelCornerButton>().FirstOrDefault(b => Avalonia.Automation.AutomationProperties.GetAutomationId(b) == id);
        return new JsonObject { ["focused"] = button?.Focus(Enum.Parse<NavigationMethod>(method)) == true };
    }

    private static JsonObject State()
    {
        var c = Factory.Corners;
        var panels = c.Placements();
        var main = VisualTreeInspector.Main();
        var buttons = main.GetVisualDescendants().OfType<PanelCornerButton>().Select(b => (JsonNode?)new JsonObject
        {
            ["id"] = Avalonia.Automation.AutomationProperties.GetAutomationId(b),
            ["corner"] = b.Tag?.ToString(),
            ["direction"] = b.Direction.ToString(),
            ["glyph"] = b.GlyphName,
            ["tip"] = ToolTip.GetTip(b)?.ToString(),
            ["focusable"] = b.Focusable,
            ["tabStop"] = b.IsTabStop,
            ["opacity"] = b.Opacity,
            ["visible"] = b.IsEffectivelyVisible, // 트리에 있는가. 근접 표시 상태는 shown 이다
            ["shown"] = b.IsShown,
            ["hitTest"] = b.IsHitTestVisible,
            ["rect"] = b.TranslatePoint(default, main) is { } o ? new JsonObject { ["x"] = o.X, ["y"] = o.Y, ["w"] = b.Bounds.Width, ["h"] = b.Bounds.Height } : null,
        }).ToArray();
        var tree = new JsonObject();
        foreach (var id in new[] { PocDockFactory.OuterPaneId, "MainPane", "CenterPane", PocDockFactory.ExtraLevelId, PocDockFactory.DocumentsHostId })
        {
            if (Factory.Root is { } root && Factory.FindDockable(root, d => d.Id == id) is IDock dock)
            {
                tree[id] = new JsonObject
                {
                    ["orientation"] = (dock as Dock.Model.Controls.IProportionalDock)?.Orientation.ToString(),
                    ["children"] = new JsonArray(dock.VisibleDockables?.Select(d => (JsonNode?)(d is ISplitter ? "|" : d.Id)).ToArray() ?? Array.Empty<JsonNode?>()),
                    ["shares"] = new JsonArray(dock.VisibleDockables?.Where(d => d is not ISplitter).Select(d => (JsonNode?)(double.IsNaN(d.Proportion) ? "NaN" : d.Proportion.ToString("R"))).ToArray() ?? Array.Empty<JsonNode?>()),
                };
            }
        }

        var owners = new JsonObject();
        foreach (var (k, v) in c.Capture() ?? new Dictionary<string, string>())
        {
            owners[k] = v;
        }

        return new JsonObject
        {
            ["supported"] = PanelCornerService.IsSupported,
            ["owners"] = owners,
            ["order"] = new JsonArray(c.CurrentOrder().Select(x => (JsonNode?)x).ToArray()),
            ["placements"] = PlacementsJson(panels),
            ["truncated"] = TruncatedJson(PanelCornerRules.TruncatedCorners(panels, c.Ownership)),
            ["buttons"] = new JsonArray(buttons),
            ["proximity"] = new JsonObject
            {
                ["distance"] = PanelCornerProximity.RevealDistance,
                ["scaling"] = main.RenderScaling,
                ["windows"] = new JsonArray(PanelCornerProximity.All.Select(w => (JsonNode?)new JsonObject
                {
                    ["buttons"] = w.ButtonCount, ["subscribed"] = w.IsSubscribed, ["moves"] = w.MoveCount, ["hitTests"] = w.HitTestCount,
                }).ToArray()),
            },
            ["tree"] = tree,
        };
    }

    private static JsonObject Rules(string panelsArg, string? ownersArg)
    {
        var panels = panelsArg.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s =>
        {
            var p = s.Split(':');
            return new PanelPlacement(p[0], Enum.Parse<PanelEdge>(p[1]), int.Parse(p[2]), !(p.Length > 3 && p[3] == "hidden"));
        }).ToList();
        var stored = (ownersArg ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(':')).ToDictionary(p => p[0], p => p[1]);
        var ownership = PanelCornerOwnership.FromStored(stored);
        var order = PanelCornerRules.PeelOrder(panels, ownership);
        return new JsonObject
        {
            ["pinwheel"] = PanelCornerRules.IsPinwheel(panels, ownership),
            ["order"] = order is null ? null : new JsonArray(order.Select(x => (JsonNode?)x.Id).ToArray()),
            ["truncated"] = TruncatedJson(PanelCornerRules.TruncatedCorners(panels, ownership)),
            ["claimable"] = order is null ? new JsonArray() : TruncatedJson(PanelCornerRules.ClaimableCorners(panels, ownership)),
        };
    }

    private static JsonArray PlacementsJson(IEnumerable<PanelPlacement> panels) => new(panels.Select(p => (JsonNode?)new JsonObject
    {
        ["id"] = p.Id, ["edge"] = p.Edge.ToString(), ["rank"] = p.Rank, ["visible"] = p.Visible,
    }).ToArray());

    private static JsonArray TruncatedJson(IEnumerable<TruncatedCorner> items) => new(items.Select(t => (JsonNode?)new JsonObject
    {
        ["panel"] = t.PanelId, ["corner"] = t.Corner.ToString(),
    }).ToArray());

    private static JsonObject Views()
    {
        var all = VisualTreeInspector.Main().GetVisualDescendants().OfType<Control>().ToList();
        var names = new HashSet<string> { "DocumentDockControl", "ToolDockControl", "ProportionalDockControl", "ToolChromeControl", "MdiDocumentWindow" };
        var interesting = all.Where(c => names.Contains(c.GetType().Name) || (c.DataContext is PocDocument && c.GetVisualParent()?.DataContext is not PocDocument))
            .Select(c => (JsonNode?)new JsonObject
            {
                ["type"] = c.GetType().Name,
                ["id"] = (c.DataContext as IDockable)?.Id,
                ["hash"] = RuntimeHelpers.GetHashCode(c),
            }).ToArray();
        return new JsonObject { ["controls"] = new JsonArray(interesting) };
    }
}
#endif

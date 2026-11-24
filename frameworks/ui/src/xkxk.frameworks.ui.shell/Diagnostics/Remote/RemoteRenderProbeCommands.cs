// 원격 render.*(진단) — U3 MDI 오렌더 원인 가르기: 창 Z순서·합성 순서·히트 일치 조사와 가설 H1~H4 진단 스위치(제품 빌드 제외)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using xkxk.frameworks.ui.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Rendering;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 계획서 D2 의 판별 실험용이다. 스위치는 재기동 전까지 유지되고 제품 동작에는 들어가지 않는다.
/// H1 비활성 제목줄 반투명 끔(PdChromeInactiveOpacity=1) · H2 Z 변경 때 MDI 패널 다시 배치 + 순서가 바뀐 창 하위 전체 다시 그리기 ·
/// H3 사용자 정의 둥근 클립(ClipBorder.Clip) 끔 · H4 MDI 창 ClipToBounds=True(그림자 밖 그리기 끔).
/// render.probe 는 MDI 패널 자식의 ZIndex·MdiZIndex·합성(Composition) 자식 순서·히트 테스트 결과가 서로 맞는지 본다.
/// </summary>
internal static class RemoteRenderProbeCommands
{
    private const string OpacityKey = "PdChromeInactiveOpacity";
    private static readonly Dictionary<string, bool> Switches = new() { ["h1"] = false, ["h2"] = false, ["h3"] = false, ["h4"] = false, ["fixoff"] = false };
    private static readonly List<string> Trace = new();
    private static readonly System.Diagnostics.Stopwatch _traceClock = new();
    private static object? _savedOpacity;
    private static bool _hooked, _traceOn;
    private static int _frame;

    private static void Note(string kind, string text)
    {
        if (_traceOn && Trace.Count < 2000)
        {
            Trace.Add($"{_traceClock.Elapsed.TotalMilliseconds:0.0} f{_frame} {kind} {text}");
        }
    }

    private static void OnFrame(System.TimeSpan _)
    {
        _frame++;
        Note("frame", "");
        if (_traceOn)
        {
            TopLevel.GetTopLevel(VisualTreeInspector.Main())?.RequestAnimationFrame(OnFrame);
        }
    }

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("render.probe", "MDI 창 그리기 순서 조사 → {render, panels:[{children:[{title,zIndex,mdiZIndex,compIndex,active,bounds}], zOrderOk, compOrderOk, hit:{tested,mismatch,samples}}], switches}", _ => Probe());
        r.Register("render.switch", "U3 가설 진단 스위치 name=h1|h2|h3|h4|fixoff(제품 수정 끔) on=true|false(기본 true) → switches", a =>
        {
            var name = a.ReqStr("name").ToLowerInvariant();
            if (!Switches.ContainsKey(name))
            {
                throw new RemoteCommandException("name 은 h1|h2|h3|h4|fixoff 중 하나입니다.");
            }

            Switches[name] = a.Str("on") != "false";
            EnsureHooks();
            Apply(name);
            return SwitchJson();
        });
        // 활성 전환 없이 Z순서(모델 MdiZIndex)만 바꾼다 — 제목줄 색 변화 같은 다른 다시 그리기 원인을 빼고 'Z 재정렬만으로 다시 그려지는가'(H2)를 결정적으로 본다
        r.Register("render.zorder", "MDI 창 Z순서만 변경(활성 그대로) mode=reverse|rotate(맨 아래→맨 위) → {before, after}", a =>
        {
            var docs = VisualTreeInspector.Main().GetVisualDescendants().OfType<MdiLayoutPanel>().SelectMany(p => p.Children)
                .Select(c => c.DataContext).OfType<IMdiDocument>().Distinct().OrderBy(d => d.MdiZIndex).ToList();
            var before = new JsonArray(docs.Select(d => (JsonNode?)((d as Dock.Model.Core.IDockable)?.Title)).ToArray());
            var order = a.Str("mode") == "rotate" ? docs.Skip(1).Append(docs.FirstOrDefault()).OfType<IMdiDocument>().ToList() : Enumerable.Reverse(docs).ToList();
            for (var i = 0; i < order.Count; i++)
            {
                order[i].MdiZIndex = i;
            }

            return new JsonObject { ["before"] = before, ["after"] = new JsonArray(order.Select(d => (JsonNode?)((d as Dock.Model.Core.IDockable)?.Title)).ToArray()) };
        });
        // ③ 원인 좁히기: 활성 전환 때 Z 변경(Dock 재정렬·MRU 재정렬)·IsActive 변경·프레임 경계가 어떤 순서로 오는지 시각(ms)과 프레임 번호로 남긴다
        r.Register("render.trace", "프레임 순서 추적 on=true(시작·비움)|false(멈춤) → {events:[\"ms f# 종류 내용\"]} (on 없이 부르면 읽기만)", a =>
        {
            if (a.Str("on") is { } on)
            {
                Trace.Clear();
                _traceOn = on != "false";
                _traceClock.Restart();
                EnsureHooks();
                if (_traceOn)
                {
                    TopLevel.GetTopLevel(VisualTreeInspector.Main())?.RequestAnimationFrame(OnFrame);
                }
            }

            return new JsonObject { ["on"] = _traceOn, ["frames"] = _frame, ["events"] = new JsonArray(Trace.Select(e => (JsonNode?)e).ToArray()) };
        });
        r.Register("render.invalidate", "문서 창 내용 다시 그리기 요청(호버와 같은 부분 다시 그리기 유발) doc=<문서 Id> | all=true(메인 창 전체 — 오렌더 비교 기준 화면) → {doc, count}", a =>
        {
            if (a.Bool("all"))
            {
                var all = VisualTreeInspector.Main().GetSelfAndVisualDescendants().ToList();
                all.ForEach(v => v.InvalidateVisual());
                return new JsonObject { ["doc"] = "all", ["count"] = all.Count };
            }

            var id = a.ReqStr("doc");
            var window = Windows().FirstOrDefault(w => w.DataContext is PocDocument d && d.Id == id)
                ?? throw new RemoteCommandException($"MDI 창 없음: {id}");
            var content = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == TemplateParts.ContentBorder) ?? (Control)window;
            var visuals = content.GetSelfAndVisualDescendants().ToList();
            visuals.ForEach(v => v.InvalidateVisual());
            return new JsonObject { ["doc"] = id, ["count"] = visuals.Count };
        });
    }

    private static IEnumerable<MdiDocumentWindow> Windows() =>
        VisualTreeInspector.Main().GetVisualDescendants().OfType<MdiDocumentWindow>();

    private static JsonObject SwitchJson() => new(Switches.Select(k => new KeyValuePair<string, JsonNode?>(k.Key, k.Value)));

    /// <summary>스위치 1개를 현재 화면에 반영한다. h2·h3 는 이후 변경도 훅으로 따라간다.</summary>
    private static void Apply(string name)
    {
        var on = Switches[name];
        switch (name)
        {
            case "fixoff": // 제품 수정(MdiZOrder Z 변경 다시 그리기 보정)을 끈다 — 수정 전/후 A/B
                MdiZOrder.DiagRepaintDisabled = on;
                break;
            case "h1":
                var res = Application.Current!.Resources;
                if (on)
                {
                    _savedOpacity ??= res.TryGetValue(OpacityKey, out var v) ? v : null;
                    res[OpacityKey] = 1.0;
                }
                else if (_savedOpacity is not null)
                {
                    res[OpacityKey] = _savedOpacity;
                }

                break;
            case "h3": // 끔 해제는 다음 배치 때 ClipBorder 가 클립을 다시 만든다(Clip=null 이면 재계산)
                foreach (var b in Windows().SelectMany(w => w.GetVisualDescendants().OfType<ClipBorder>()))
                {
                    if (on) { b.Clip = null; } else { b.InvalidateArrange(); }
                }

                break;
            case "h4":
                foreach (var w in Windows())
                {
                    if (on) { w.ClipToBounds = true; } else { w.ClearValue(Visual.ClipToBoundsProperty); }
                }

                break;
        }
    }

    private static void EnsureHooks()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        Visual.ZIndexProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (c.GetVisualParent() is MdiLayoutPanel)
            {
                Note("z", $"{Title(c)} {e.OldValue}->{e.NewValue}");
            }

            if (Switches["h2"] && c.GetVisualParent() is MdiLayoutPanel panel)
            {
                // Avalonia ZIndexChanged 는 그 컨테이너 자신만 InvalidateVisual 한다(자신은 그릴 것이 없을 수 있음) → 순서가 바뀐 창의 하위 전체를 다시 그리게 한다
                panel.InvalidateArrange();
                panel.InvalidateVisual();
                foreach (var v in c.GetSelfAndVisualDescendants())
                {
                    v.InvalidateVisual();
                }
            }
        });
        Visual.ClipProperty.Changed.AddClassHandler<ClipBorder>((b, _) =>
        {
            if (Switches["h3"] && b.Clip is not null)
            {
                Dispatcher.UIThread.Post(() => b.Clip = null);
            }
        });
        MdiDocumentWindow.IsActiveProperty.Changed.AddClassHandler<MdiDocumentWindow>((w, e) =>
        {
            Note("active", $"{(w.DataContext as Dock.Model.Core.IDockable)?.Title} {e.NewValue}");
            if (Switches["h4"])
            {
                w.ClipToBounds = true; // 나중에 열린 창도 켠다
            }
        });
    }

    private static JsonObject Probe()
    {
        var panels = new JsonArray();
        foreach (var panel in VisualTreeInspector.Main().GetVisualDescendants().OfType<MdiLayoutPanel>())
        {
            panels.Add(ProbePanel(panel));
        }

        return new JsonObject { ["render"] = RenderInfo.DisplayText(), ["panels"] = panels, ["switches"] = SwitchJson() };
    }

    private static JsonObject ProbePanel(MdiLayoutPanel panel)
    {
        var kids = panel.Children.Where(c => c.IsVisible && c.Bounds.Width > 0).ToList();
        var comp = (ElementComposition.GetElementVisual(panel) as CompositionContainerVisual)?.Children.ToList() ?? new List<CompositionVisual>();
        var expected = kids.Select((c, i) => (c, i)).OrderBy(t => t.c.ZIndex).ThenBy(t => t.i).Select(t => t.c).ToList();
        var children = new JsonArray();
        foreach (var c in kids)
        {
            var win = c as MdiDocumentWindow ?? c.GetVisualDescendants().OfType<MdiDocumentWindow>().FirstOrDefault();
            var doc = c.DataContext as IMdiDocument;
            children.Add(new JsonObject
            {
                ["title"] = (c.DataContext as Dock.Model.Core.IDockable)?.Title,
                ["type"] = c.GetType().Name,
                ["zIndex"] = c.ZIndex,
                ["mdiZIndex"] = doc?.MdiZIndex,
                ["compIndex"] = ElementComposition.GetElementVisual(c) is { } cv ? comp.IndexOf(cv) : -2,
                ["active"] = win?.IsActive,
                ["opacity"] = c.Opacity,
                ["clipToBounds"] = win?.ClipToBounds,
                ["bounds"] = $"{c.Bounds.X:0},{c.Bounds.Y:0},{c.Bounds.Width:0},{c.Bounds.Height:0}",
            });
        }

        var compIdx = expected.Select(c => ElementComposition.GetElementVisual(c) is { } v ? comp.IndexOf(v) : -1).ToList();
        var byMdi = kids.Where(c => c.DataContext is IMdiDocument).OrderBy(c => ((IMdiDocument)c.DataContext!).MdiZIndex).ToList();
        var byZ = expected.Where(c => c.DataContext is IMdiDocument).ToList();
        return new JsonObject
        {
            ["count"] = kids.Count,
            ["children"] = children,
            ["zOrderOk"] = byMdi.SequenceEqual(byZ), // MdiZIndex 순서 == ZIndex 순서(바인딩 반영)
            ["compOrderOk"] = compIdx.All(i => i >= 0) && compIdx.Zip(compIdx.Skip(1), (x, y) => x < y).All(b => b), // 합성 순서 == ZIndex 순서
            ["hit"] = HitCheck(panel, expected),
        };
    }

    /// <summary>각 창 안 여러 점에서 히트 테스트가 '그 점을 덮는 맨 위 창'을 맞히는지. 렌더·히트는 같은 합성 순서를 쓰므로 순서 어긋남의 간접 증거다.</summary>
    private static JsonObject HitCheck(MdiLayoutPanel panel, List<Control> bottomToTop)
    {
        var top = TopLevel.GetTopLevel(panel);
        var (tested, mismatch) = (0, 0);
        var samples = new JsonArray();
        if (top is null)
        {
            return new JsonObject { ["tested"] = 0 };
        }

        foreach (var c in bottomToTop)
        {
            var b = c.Bounds;
            foreach (var p in new[] { new Point(b.X + b.Width / 2, b.Y + 50), new Point(b.X + 30, b.Bottom - 30), new Point(b.Right - 30, b.Y + b.Height / 2) })
            {
                var owner = bottomToTop.LastOrDefault(k => k.Bounds.Contains(p));
                if (owner is null || panel.TranslatePoint(p, top) is not { } tp)
                {
                    continue;
                }

                tested++;
                var hit = top.InputHitTest(tp) as Visual;
                var actual = hit is null ? null : bottomToTop.FirstOrDefault(k => ReferenceEquals(k, hit) || hit.GetVisualAncestors().Contains(k));
                if (!ReferenceEquals(actual, owner))
                {
                    mismatch++;
                    if (samples.Count < 10)
                    {
                        samples.Add($"{p.X:0},{p.Y:0} 기대={Title(owner)} 실제={Title(actual)}");
                    }
                }
            }
        }

        return new JsonObject { ["tested"] = tested, ["mismatch"] = mismatch, ["samples"] = samples };
    }

    private static string? Title(Control? c) => (c?.DataContext as Dock.Model.Core.IDockable)?.Title;
}
#endif

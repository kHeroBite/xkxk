// 원격 명령 placement.state 의 SDI 분리 문서 항목 — 잡을 곳 폭(grab)·기능 버튼 줄 접힘(stripFolded) 진단. 진단 빌드 전용, 제품 동작 불변
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static partial class RemotePlacementCommands
{
    /// <summary>창별 마지막으로 읽은 SDI 값. 최소화·숨김 때는 비우지 않고 이 값을 낸다(설계 03 §3.1 — 상태 필드 sdiState·sdiLive 로 구분).</summary>
    private static readonly ConditionalWeakTable<Window, SdiStripRead> LastSdi = new();

    private sealed record SdiStripRead(double Grab, double BarWidth, bool StripFolded);

    /// <summary>
    /// SDI 로 분리된 문서(window 가 있음)면 item 에 grab·stripFolded·sdiState·sdiLive 를 더한다. MDI 문서(window=null)는 그대로 돌려준다(기존 키 불변).
    /// grab = SDI 제목줄 끌기 칸(SdiTitleBar 의 DragArea) 폭(DIP), stripFolded = 기능 버튼 줄(Buttons)이 좁은 창이라 숨겨졌는가(SdiTitleBar.FitButtons).
    /// SdiTitleBar 는 상위 어셈블리(xkxk.frameworks.ui.views)라 형식 이름과 x:Name 으로 찾는다. 제목줄이 없는 호스트(웹 관리형 창 등)는 null 이다.
    /// </summary>
    private static JsonObject SdiFields(IDockWindow? window, JsonObject item)
    {
        if (window is null)
        {
            return item;
        }

        var w = window.Host as Window;
        var live = w is { IsVisible: true } && w.WindowState != WindowState.Minimized ? ReadSdi(w) : null;
        if (live is not null)
        {
            LastSdi.AddOrUpdate(w!, live);
        }

        var shown = live ?? (w is not null && LastSdi.TryGetValue(w, out var last) ? last : null);
        var min = MdiDragLimit.CurrentMinGrab; // 판정 기준은 MDI 와 같은 mdiMinGrabDip 현재값
        item["grab"] = shown is null ? null : new JsonObject { ["width"] = shown.Grab, ["minGrab"] = min, ["enough"] = shown.Grab >= min, ["barWidth"] = shown.BarWidth };
        item["stripFolded"] = shown?.StripFolded;
        item["sdiState"] = w?.WindowState.ToString();
        item["sdiLive"] = live is not null;
        return item;
    }

    /// <summary>SDI 창의 제목줄에서 끌기 칸 폭과 기능 버튼 줄 표시 여부를 읽는다. 제목줄을 못 찾았거나 아직 배치 전(폭 0)이면 null.</summary>
    private static SdiStripRead? ReadSdi(Window w)
    {
        var bar = w.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.GetType().Name == "SdiTitleBar");
        var parts = bar?.GetVisualDescendants().OfType<Control>().ToList();
        var drag = parts?.FirstOrDefault(c => c.Name == "DragArea");
        var buttons = parts?.FirstOrDefault(c => c.Name == "Buttons");
        if (bar is null || drag is null || buttons is null || bar.Bounds.Width <= 0)
        {
            return null;
        }

        return new SdiStripRead(drag.Bounds.Width, bar.Bounds.Width, !buttons.IsVisible);
    }
}
#endif

// MDI 탭 합류 끌기 개발 진단(tabjoin.drag 원격 명령, 합류 아이콘 켬 카운터 tabjoin.armStats) — PD_DIAGNOSTICS 빌드 전용, MdiTabJoinDrag.cs 에서 300줄 규칙으로 옮김
#if PD_DIAGNOSTICS // 개발 진단 tabjoin.drag — ui.pointerDrag 는 동기라 머무름(250ms)을 못 만든다. 제목줄 누름→표적까지 앱 내부 포인터 이동→hold ms 대기→놓기(OS 입력 없음)
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

internal static class MdiTabJoinDiag
{
    // 합류 아이콘 켬 카운터(마지막 끌기 1회분, 끌기 시작 때 0 으로) — 옵션 A(MdiMoveLight.TabJoinArmNear) 끔/켬 비교용
    private static bool s_near;
    private static int s_armedAtStart;
    private static int s_nearOn;
    private static int s_nearOff;
    private static int s_peak;
    private static int s_drags;

    /// <summary>끌기 시작 1회. near=근접 켬 모드인가, armed=시작 때 아이콘을 켠 창 수(끔이면 끄는 창을 뺀 창 수, 켬이면 0).</summary>
    internal static void NoteStart(bool near, int armed) => (s_near, s_armedAtStart, s_nearOn, s_nearOff, s_peak, s_drags) = (near, armed, 0, 0, armed, s_drags + 1);

    /// <summary>근접 켬/끔 1회. armedNow=지금 켜져 있는 창 수(최대 동시 켬 수 갱신).</summary>
    internal static void NoteNear(bool on, int armedNow)
    {
        if (on) { s_nearOn++; } else { s_nearOff++; }
        Diagnostics.MdiDragFrameDiag.NoteNearToggle(); // 끌기 프레임별 기록에 아이콘 토글 시점 표시(01a P1)
        s_peak = Math.Max(s_peak, armedNow);
    }

#pragma warning disable CA2255 // 등록 파일(RemoteCommandRouter.cs)이 배정 밖이라 모듈 초기화로 붙인다(MdiTabGroupDiag 와 같은 방식)
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        Diagnostics.Remote.RemoteCommandRouter.Default.Register("tabjoin.armStats",
            "MDI 탭 합류 아이콘 켬 카운터(마지막 끌기) → {drags, near, armedAtStart, nearOn, nearOff, peakArmed}",
            _ => ArmStats());
        Diagnostics.Remote.RemoteCommandRouter.Default.Register("tabjoin.drag",
        "MDI 탭 합류 끌기 source=<문서·묶음 Id> [target=<표적 Id 예 Doc-2#icon>] [dx= dy= target 없을 때 이동량 DIP] [hold=ms 기본 300] [steps=10] → {targets[], start, end, outcome}", async a =>
        {
            var win = Diagnostics.Remote.RemoteDockAccess.Factory.DockControls.OfType<Control>().SelectMany(c => TabSlotMarker.Descendants(c).OfType<MdiDocumentWindow>())
                .FirstOrDefault(w => w.DataContext is IDockable d && d.Id == a["source"]?.ToString()) ?? throw new Diagnostics.Remote.RemoteCommandException("source 창 없음");
            var top = TopLevel.GetTopLevel(win) ?? throw new Diagnostics.Remote.RemoteCommandException("화면 트리 밖");
            var title = TabSlotMarker.Descendants(win).OfType<Control>().First(c => c.Name == TemplateParts.TitleGrid);
            var start = title.TranslatePoint(new Point(Math.Min(40, title.Bounds.Width / 2), title.Bounds.Height / 2), top) ?? default;
            var targets = MdiTabJoinDrag.CollectTargets(win);
            var host = win.DataContext is IDockable { Owner: IDocumentDock mdi } ? MdiTabJoinDrag.FindHost(win, mdi) : null;
            var t = targets.FirstOrDefault(x => x.Id == a["target"]?.ToString());
            var end = t is not null && host?.TranslatePoint(t.Rect.Center, top) is { } c2 ? c2 : start + new Vector(Diagnostics.Remote.RemoteProtocol.Num(a, "dx") ?? 0, Diagnostics.Remote.RemoteProtocol.Num(a, "dy") ?? 0);
            var pointer = new Pointer(0x5D_0101, PointerType.Mouse, true);
            var held = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
            ulong Now() => (ulong)Environment.TickCount64;
            Interactive At(Point p) => pointer.Captured as Interactive ?? top.InputHitTest(p) as Interactive ?? win;
            At(start).RaiseEvent(new PointerPressedEventArgs(At(start), pointer, top, start, Now(), new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            if (pointer.Captured is null) { pointer.Capture(At(start) as IInputElement); }
            var steps = Math.Clamp(Diagnostics.Remote.RemoteProtocol.Int(a, "steps") ?? 10, 1, 100);
            for (var i = 1; i <= steps; i++)
            {
                var p = start + ((end - start) * ((double)i / steps));
                At(p).RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, At(p), pointer, top, p, Now(), held, KeyModifiers.None));
            }

            await System.Threading.Tasks.Task.Delay(Math.Clamp(Diagnostics.Remote.RemoteProtocol.Int(a, "hold") ?? 300, 0, 5000));
            At(end).RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, At(end), pointer, top, end, Now(), held, KeyModifiers.None));
            At(end).RaiseEvent(new PointerReleasedEventArgs(At(end), pointer, top, end, Now(), new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            pointer.Capture(null);
            await System.Threading.Tasks.Task.Delay(100); // Post 된 합류가 돌 시간
            return new System.Text.Json.Nodes.JsonObject
            {
                ["targets"] = new System.Text.Json.Nodes.JsonArray(targets.Select(x => (System.Text.Json.Nodes.JsonNode?)$"{x.Id}:{x.Rect}:z={x.Z}").ToArray()),
                ["start"] = start.ToString(), ["end"] = end.ToString(), ["outcome"] = MdiTabJoinDrag.LastOutcome,
                ["arm"] = ArmStats(),
            };
        });
    }

    private static System.Text.Json.Nodes.JsonObject ArmStats() => new()
    {
        ["drags"] = s_drags, ["near"] = s_near, ["armedAtStart"] = s_armedAtStart, ["nearOn"] = s_nearOn, ["nearOff"] = s_nearOff, ["peakArmed"] = s_peak,
    };
}
#endif

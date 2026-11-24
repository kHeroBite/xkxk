// 원격 수식키+휠 재현(진단) — input.wheel·dense.state. 대상 점을 히트 테스트해 휠 이벤트를 RaiseEvent 하고 실제 값을 돌려준다(OS 입력·전경 변화 없음)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Pd.Ui.Input;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemoteWheelCommands
{
    private const int RemotePointerId = 0x5D_0002;

    /// <summary>실입력 대신 공개 생성자로 만든 휠 이벤트를 히트 요소에 올린다. 터널·버블 라우팅과 전역 Ctrl+휠 처리기까지 실입력과 같은 경로를 탄다.</summary>
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("input.wheel",
            "수식키+휠 재현 id=|dock= [doc] [dy=1] [dx=0] [ctrl|alt|shift|meta=true] [x= y=(대상 좌표 DIP, 생략 시 가운데)] → {target, handled, state}", async a =>
            {
                var c = RemoteUiTargets.Target(a);
                var top = TopLevel.GetTopLevel(c) ?? throw new RemoteCommandException("대상이 화면 트리에 없습니다.");
                var local = PointOf(c, a);
                var point = c.TranslatePoint(local, top) ?? throw new RemoteCommandException("대상 좌표를 계산하지 못했습니다.");
                var hit = top.InputHitTest(point) as Interactive;
                if (hit is not Visual hv || !(ReferenceEquals(hv, c) || hv.GetVisualAncestors().Contains(c)))
                {
                    throw new RemoteCommandException($"그 점을 다른 요소가 가립니다: {hit?.GetType().Name ?? "없음"}.");
                }

                var mods = KeyModifiers.None;
                if (a.Bool("ctrl")) { mods |= KeyModifiers.Control; }
                if (a.Bool("alt")) { mods |= KeyModifiers.Alt; }
                if (a.Bool("shift")) { mods |= KeyModifiers.Shift; }
                if (a.Bool("meta")) { mods |= KeyModifiers.Meta; }
                using var pointer = new Pointer(RemotePointerId, PointerType.Mouse, true);
                var props = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);
                var e = new PointerWheelEventArgs(hit, pointer, top, point, (ulong)Environment.TickCount64, props, mods, new Vector(a.Num("dx") ?? 0, a.Num("dy") ?? 1));
                hit.RaiseEvent(e);
                await AutomationBridge.WaitFramesAsync(top, 2); // DataGrid 는 다음 측정에서 스크롤을 반영한다
                return new JsonObject { ["target"] = hit.GetType().Name, ["handled"] = e.Handled, ["state"] = State(c, local) };
            });
        r.Register("dense.state",
            "수식키+휠 대상의 실제 값 id=|dock= [doc] [x= y=] → {kind, rowLevel, columnLevel, rowAt, rowHeight, ...}", a =>
            {
                var c = RemoteUiTargets.Target(a);
                return State(c, PointOf(c, a));
            });
    }

    private static Point PointOf(Control c, JsonObject a) => new(a.Num("x") ?? c.Bounds.Width / 2, a.Num("y") ?? c.Bounds.Height / 2);

    private static JsonObject State(Control c, Point at)
    {
        var t = DenseWheel.FindTarget(c) ?? throw new RemoteCommandException("대상에 공통 휠 조작이 연결되어 있지 않습니다.");
        var o = new JsonObject
        {
            ["kind"] = t.Kind, ["rowLevel"] = t.RowLevel, ["columnLevel"] = t.ColumnLevel, ["rowAt"] = t.RowIndexAt(at),
        };
        foreach (var (k, v) in t.Snapshot()) { o[k] = Math.Round(v, 2); }
        return o;
    }
}
#endif

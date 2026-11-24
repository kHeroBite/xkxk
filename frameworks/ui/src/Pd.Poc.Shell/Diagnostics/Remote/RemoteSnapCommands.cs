// 원격 명령(창 배치 칸 시험 — snapcell.layouts·apply·restore, 최대화 단추 팝업 snapflyout.state·hover·leave·pick·close) — 창 메뉴 '배치' 와 같은 WindowMenuOps 경로를 부른다. 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>
/// 기존 snap.get/set 은 자석(MagnetSnap) 명령이라 이름이 겹치지 않게 snapcell.* 로 둔다.
/// 대상은 winmenu.* 와 같은 kind=main|sdi|mdi|tab [doc=] 규칙이다. 등록은 RemoteCommandRouter 가 Register 를 부른다(라우터 1줄).
/// </summary>
internal static class RemoteSnapCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("snapcell.layouts",
            "창 메뉴 '배치' 에 보일 칸 종류와 칸별 적용 가능 여부(옵션 SnapEnabled·SnapLayouts 반영) kind=main|sdi|mdi|tab [doc=] → {enabled, keyboard, layouts:[{id, cells:[{index, canApply}]}]}",
            a =>
            {
                var target = Target(a);
                var layouts = new JsonArray();
                foreach (var layout in WindowMenuOps.PlacementLayouts)
                {
                    var cells = new JsonArray();
                    for (var i = 0; i < layout.Cells.Count; i++)
                    {
                        cells.Add((JsonNode)new JsonObject { ["index"] = i, ["canApply"] = WindowMenuOps.CanApplyCell(target, layout.Id, i) });
                    }

                    layouts.Add((JsonNode)new JsonObject { ["id"] = layout.Id, ["cells"] = cells });
                }

                return new JsonObject
                {
                    ["kind"] = target.Kind.ToString(),
                    ["doc"] = target.Document?.Id,
                    ["enabled"] = WindowMenuOps.PlacementLayouts.Count > 0,
                    ["keyboard"] = SnapKeyboardCommands.Enabled,
                    ["layouts"] = layouts,
                };
            });
        r.Register("snapcell.apply",
            "대상 창을 칸에 배치(메뉴 클릭과 같은 경로) kind=main|sdi|mdi|tab [doc=] layout=<칸 종류 id> cell=<칸 번호 0부터> → {done, canApply, window:창 스냅샷}",
            a =>
            {
                var target = Target(a);
                var layout = a.ReqStr("layout");
                var cell = a.Int("cell") ?? 0;
                var can = WindowMenuOps.CanApplyCell(target, layout, cell);
                var done = WindowMenuOps.ApplyCell(target, layout, cell);
                return new JsonObject { ["done"] = done, ["canApply"] = can, ["window"] = RemoteWindowMenuCommands.WindowSnapshot() };
            });
        r.Register("snapcell.restore",
            "스냅된 창을 스냅 전 크기로 / 최대화·최소화 창은 보통 상태로(단축키 '복원') kind=main|sdi|mdi|tab [doc=] → {done, window}",
            a =>
            {
                var done = WindowMenuOps.RestoreSnap(Target(a));
                return new JsonObject { ["done"] = done, ["window"] = RemoteWindowMenuCommands.WindowSnapshot() };
            });
        r.Register("snapcell.lastdrag",
            "MDI 끌기 스냅 진단(MdiSnapDrag) → {lastDrag:마지막 놓기 판정 근거, lastOutcome, live:끄는 중 상태, zone:현재 판정 종류}",
            _ => new JsonObject
            {
                ["lastDrag"] = MdiSnapDrag.LastDrag,
                ["lastOutcome"] = MdiSnapDrag.LastOutcome,
                ["live"] = MdiSnapDrag.LiveState,
                ["zone"] = MdiSnapDrag.CurrentZone.Kind.ToString(),
                // 웹·Android 관리형 창 끌기(ManagedSnapDrag): 마지막 누름 조건별 판정·마지막 놓기·끌기 중 이동 수·현재 판정
                ["managed"] = new JsonObject
                {
                    ["lastPress"] = ManagedSnapDrag.LastPress,
                    ["lastOutcome"] = ManagedSnapDrag.LastOutcome,
                    ["moves"] = ManagedSnapDrag.Moves,
                    ["zone"] = ManagedSnapDrag.CurrentZone.Kind.ToString(),
                },
            });
        RegisterFlyout(r);
    }

    /// <summary>최대화 단추 호버 배치 칸 팝업(SnapFlyoutState) 조회·시험 진입 — 실입력 없이 지연·열림·칸 클릭·닫힘을 판정한다(AC-025).</summary>
    private static void RegisterFlyout(RemoteCommandRouter r)
    {
        r.Register("snapflyout.state",
            "최대화 단추 배치 칸 팝업 상태 → {installed, enabled, delayMs, pending, open, kind, doc, highlighted:{layout, cell}, skip, layouts:[{id, cells:[{index, canApply}]}]}",
            _ => FlyoutState());
        r.Register("snapflyout.hover",
            "대상 창의 최대화 단추에 포인터가 들어온 것처럼 지연(SnapFlyoutDelayMs)을 시작 kind=main|sdi|mdi|tab [doc=<문서 id>] → {found, state}(skip: sdi-off=SnapSdiMode Off, os-flyout=Windows 메인 창은 OS 메뉴에 맡김)",
            a =>
            {
                var hover = SnapFlyoutState.Hover ?? throw new RemoteCommandException("팝업 처리기 미설치(최대화 단추가 아직 만들어지지 않음)");
                return new JsonObject { ["found"] = hover(Target(a)), ["state"] = FlyoutState() };
            });
        r.Register("snapflyout.leave",
            "포인터가 단추와 팝업을 모두 벗어난 것처럼 처리(지연 취소·팝업 닫기) → {state}",
            _ =>
            {
                (SnapFlyoutState.Leave ?? throw new RemoteCommandException("팝업 처리기 미설치")).Invoke();
                return new JsonObject { ["state"] = FlyoutState() };
            });
        r.Register("snapflyout.pick",
            "열린 팝업의 칸을 누른 것처럼 배치(칸 클릭과 같은 경로, 배치는 다음 UI 틱) layout=<칸 종류 id> cell=<칸 번호 0부터> → {done, state}",
            a =>
            {
                var pick = SnapFlyoutState.Pick ?? throw new RemoteCommandException("팝업 처리기 미설치");
                return new JsonObject { ["done"] = pick(a.ReqStr("layout"), a.Int("cell") ?? 0), ["state"] = FlyoutState() };
            });
        r.Register("snapflyout.options",
            "팝업 옵션 변경(준 값만, 즉시 저장되므로 시험이 원복한다) [enabled=true|false] [delayMs=0~3000] [snapEnabled=true|false] → {state}",
            a =>
            {
                var s = Pd.Poc.Shell.Theming.UiSettings.Current;
                if (a.Str("enabled") is { Length: > 0 } en) { s.SnapFlyoutEnabled = bool.Parse(en); }
                if (a.Int("delayMs") is { } ms) { s.SnapFlyoutDelayMs = ms; }
                if (a.Str("snapEnabled") is { Length: > 0 } se) { s.SnapEnabled = bool.Parse(se); }
                return new JsonObject { ["state"] = FlyoutState() };
            });
        r.Register("snapflyout.close",
            "열린 팝업을 닫음(Esc 와 같은 경로) → {state}",
            _ =>
            {
                (SnapFlyoutState.Close ?? throw new RemoteCommandException("팝업 처리기 미설치")).Invoke();
                return new JsonObject { ["state"] = FlyoutState() };
            });
    }

    private static JsonObject FlyoutState()
    {
        var target = SnapFlyoutState.Target;
        var layouts = new JsonArray();
        foreach (var layout in SnapFlyoutState.Layouts)
        {
            var cells = new JsonArray();
            for (var i = 0; i < layout.Cells.Count; i++)
            {
                cells.Add((JsonNode)new JsonObject { ["index"] = i, ["canApply"] = target is not null && WindowMenuOps.CanApplyCell(target, layout.Id, i) });
            }

            layouts.Add((JsonNode)new JsonObject { ["id"] = layout.Id, ["cells"] = cells });
        }

        var (hl, hc) = SnapFlyoutState.Highlighted;
        return new JsonObject
        {
            ["installed"] = SnapFlyoutState.Hover is not null,
            ["enabled"] = SnapFlyoutState.Enabled,
            ["delayMs"] = Pd.Poc.Shell.Theming.UiSettings.Current.SnapFlyoutDelayMs,
            ["pending"] = SnapFlyoutState.Pending,
            ["open"] = SnapFlyoutState.IsOpen,
            ["kind"] = target?.Kind.ToString(),
            ["doc"] = target?.Document?.Id,
            ["highlighted"] = new JsonObject { ["layout"] = hl, ["cell"] = hc },
            ["skip"] = SnapFlyoutState.LastSkipReason,
            ["hover"] = SnapFlyoutState.LastHover,
            ["pointerMoves"] = SnapFlyoutState.PointerMoves,
            ["layouts"] = layouts,
        };
    }

    private static WindowMenuTarget Target(JsonObject a)
    {
        var kind = a.ReqStr("kind").ToLowerInvariant() switch
        {
            "main" => WindowMenuKind.Main,
            "sdi" => WindowMenuKind.Sdi,
            "mdi" => WindowMenuKind.Mdi,
            "tab" => WindowMenuKind.Tab,
            var other => throw new RemoteCommandException($"알 수 없는 kind: {other}"),
        };
        return kind == WindowMenuKind.Main
            ? new WindowMenuTarget(kind, null, VisualTreeInspector.Main() as Window)
            : new WindowMenuTarget(kind, RemoteShellAccess.Doc(a), null);
    }
}
#endif

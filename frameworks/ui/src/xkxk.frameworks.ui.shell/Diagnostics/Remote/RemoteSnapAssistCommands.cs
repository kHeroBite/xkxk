// 원격 명령(Snap Assist 시험 — snapassist.state·pick·zone·skip·cancel) — 칸 선택 뒤 남은 칸 창 고르기 세션(SnapAssistSession)을 조회·조작한다. 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.IO;
using System.Text.Json.Nodes;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 화면은 모르고 Shell 의 SnapAssistSession 상태 머신 API 만 부른다(레이어 분리). 옵션 3종은 settings.get/set 경로로 바꾼다.
/// 등록은 RemoteCommandRouter 가 Register 를 부른다(라우터 1줄).
/// </summary>
internal static class RemoteSnapAssistCommands
{
    private static string? _lastEnd;

    internal static void Register(RemoteCommandRouter r)
    {
        // 끝 사유를 기억해 state 에서 보여 준다(중복 구독 방지로 1회만 구독).
        SnapAssistSession.Ended += reason => _lastEnd = reason;

        r.Register("snapassist.state",
            "Snap Assist 세션 상태 → {isActive, thumbs:[{index,title,status,detail}], thumbFail, state:진단 1줄, lastSkipReason, lastEnd, options:{enabled, onEdge, thumbnails}}",
            _ => Snapshot());
        r.Register("snapassist.pick",
            "활성 칸에 후보 index 번 창을 배치(카드 클릭과 같은 경로) index=<후보 번호 0부터> → {done, ...state}",
            a =>
            {
                var done = SnapAssistSession.Pick(a.Int("index") ?? 0);
                return WithDone(done);
            });
        r.Register("snapassist.zone",
            "활성 칸을 cell 번 칸으로 바꿈(흐린 칸 클릭과 같은 경로) cell=<칸 번호 0부터> → {done, ...state}",
            a =>
            {
                var done = SnapAssistSession.SelectZone(a.Int("cell") ?? 0);
                return WithDone(done);
            });
        r.Register("snapassist.skip",
            "활성 칸 건너뛰기(원격 전용) → {done, ...state}",
            _ => WithDone(SnapAssistSession.Skip()));
        r.Register("snapassist.thumbdump",
            "활성 칸 후보 썸네일 진단 저장 [dir=<폴더, 기본 앱 데이터\\snapthumbs\\시각>] → {dir, lines:[번호·대상·크기·옛 방식/새 방식 내용 범위·파일]} (웹은 저장 없이 줄만)",
            a =>
            {
                var dir = a.Str("dir");
                if (string.IsNullOrEmpty(dir) && !OperatingSystem.IsBrowser())
                {
                    var data = Path.GetDirectoryName(xkxk.frameworks.ui.data.Logging.PdLog.FileDirectory ?? string.Empty) is { Length: > 0 } d0
                        ? d0 : Path.GetDirectoryName(RemoteControlServer.InfoFilePath)!;
                    dir = Path.Combine(data, "snapthumbs", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
                }

                if (!OperatingSystem.IsBrowser())
                {
                    Directory.CreateDirectory(dir!);
                }

                var lines = new JsonArray();
                foreach (var line in SnapAssistSession.DumpThumbs(dir ?? string.Empty))
                {
                    lines.Add((JsonNode?)line); // 비제네릭 Add(JsonNode) — 제네릭 Add<T> 는 트리밍 경고(IL2026)
                }

                return new JsonObject { ["dir"] = dir, ["lines"] = lines };
            });
        r.Register("snapassist.thumbfail",
            "진단 전용: 다음 세션부터 썸네일 찍기를 강제 실패 reason=<사유, 빈 값이면 해제> → {thumbFail}",
            a =>
            {
                var reason = a.Str("reason");
                SnapAssistSession.DiagThumbFail = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
                return new JsonObject { ["thumbFail"] = SnapAssistSession.DiagThumbFail };
            });
        r.Register("snapassist.cancel",
            "세션 취소(Esc 와 같은 효과 — 이미 놓은 창은 남기고 나머지는 무시) → {...state}",
            _ =>
            {
                SnapAssistSession.Cancel("cancel");
                return Snapshot();
            });
    }

    private static JsonObject WithDone(bool done)
    {
        var o = Snapshot();
        o["done"] = done;
        return o;
    }

    private static JsonObject Snapshot()
    {
        var s = XkxkUi.Current.Settings;
        var thumbs = new JsonArray();
        foreach (var t in SnapAssistSession.ThumbDiagnostics)
        {
            thumbs.Add((JsonNode)new JsonObject { ["index"] = t.Index, ["title"] = t.Title, ["status"] = t.Status, ["detail"] = t.Detail });
        }

        return new JsonObject
        {
            ["isActive"] = SnapAssistSession.IsActive,
            ["thumbs"] = thumbs,
            ["thumbFail"] = SnapAssistSession.DiagThumbFail,
            ["state"] = SnapAssistSession.State,
            ["lastSkipReason"] = SnapAssistSession.LastSkipReason,
            ["lastEnd"] = _lastEnd,
            ["options"] = new JsonObject
            {
                ["enabled"] = s.EffectiveSnapAssistEnabled,
                ["onEdge"] = s.EffectiveSnapAssistOnEdge,
                ["thumbnails"] = s.EffectiveSnapAssistThumbnails,
            },
        };
    }
}
#endif

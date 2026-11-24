// 창 움직임 효과 진단 표본(WindowTransitions 의 partial, 300줄 규칙으로 분리) — 원격 motion.probe 의 효과별 단계 표본·전 값 복원 확인과 진행 지점 고정(hold)
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Pd.Poc.Shell.Theming;

namespace Pd.Ui.Views.Depth;

public static partial class WindowTransitions
{
    /// <summary>원격 motion.probe 표본 구간 수(0~1 을 이만큼 나눠 각 경계 상태를 읽는다). 진단 전용.</summary>
    private const int ProbeSegments = 4;

    /// <summary>
    /// 원격 motion.probe — 효과 표의 행을(effect = ID 하나, 비우거나 all 이면 none 을 뺀 전부) 실제 재생 코드로 동기 단계 진행해 표본을 모으고,
    /// 끝 뒤 RenderTransform·Opacity·Clip 이 전 값(같은 객체)인지 본다. resolved = 지금 설정·폴백·배치(placement, 기본 MdiOverlap)·플랫폼 낮춤 뒤 최종 ID
    /// (전환은 keyboard 출처 기준). 대상 = 화면에 보이는 위 문서 창, 없으면 기준 화면. 대상이 없으면 effects 는 비어 있다.
    /// lastClose = 마지막 실제 닫기 1건의 단계(WindowTransitions.Close). 표에 없는 effect 를 주면 표본 재생 없이 이것만 읽는다.
    /// </summary>
    public static string Probe(string? effect, string? placement, string? hold = null)
    {

        var target = Root()?.GetVisualDescendants().OfType<MdiDocumentWindow>().LastOrDefault(w => w.IsEffectivelyVisible && w.Bounds.Width > 0)
            ?? (s_root is not null && s_root.TryGetTarget(out var r) ? r as Control : null);
        if (hold is not null)
        {
            return Hold(target, effect, hold);
        }

        var place = Enum.TryParse<MotionPlacement>(placement, true, out var p) ? p : MotionPlacement.MdiOverlap;
        var enter = DesignThemeResolver.SwitchEffect(place, "keyboard", new[] { "keyboard" });
        var sb = new StringBuilder("{\"platform\":\"").Append(DesignThemeResolver.Platform).Append("\",\"placement\":\"").Append(place)
            .Append("\",\"resolved\":{\"open\":\"").Append(DesignThemeResolver.OpenEffect(place))
            .Append("\",\"close\":\"").Append(DesignThemeResolver.CloseEffect(place))
            .Append("\",\"switch\":\"").Append(enter)
            .Append("\",\"switchExit\":\"").Append(DesignThemeResolver.SwitchExitEffect(place, enter)).Append("\"},\"effects\":[");
        var all = true;
        var first = true;
        var pick = string.IsNullOrWhiteSpace(effect) || string.Equals(effect, "all", StringComparison.OrdinalIgnoreCase) ? null : effect.Trim();
        foreach (var row in MotionEffects.All.Where(e => e.Id != MotionEffects.None && (pick is null || string.Equals(e.Id, pick, StringComparison.OrdinalIgnoreCase))))
        {
            if (target is null || Begin(target, row) is not { } playback)
            {
                continue;
            }

            sb.Append(first ? "" : ",").Append("{\"id\":\"").Append(row.Id).Append("\",\"direction\":\"").Append(row.Direction).Append("\",\"samples\":[");
            first = false;
            for (var i = 0; i <= ProbeSegments; i++)
            {
                playback.Step((double)i / ProbeSegments);
                var s = playback.Last;
                sb.Append(i == 0 ? "" : ",").Append("{\"o\":").Append(N(s.Opacity)).Append(",\"s\":").Append(N(s.Scale)).Append(",\"x\":").Append(N(s.X))
                    .Append(",\"y\":").Append(N(s.Y)).Append(",\"a\":").Append(N(s.Angle))
                    .Append(",\"clip\":").Append(s.Clip is { } c ? "\"" + N(c.X) + "," + N(c.Y) + "," + N(c.Width) + "," + N(c.Height) + "\"" : "null").Append('}');
            }

            playback.Finish();
            var restored = playback.Restored();
            all &= restored;
            sb.Append("],\"restored\":").Append(restored ? "true" : "false").Append('}');
        }

        return sb.Append("],\"allRestored\":").Append(all ? "true" : "false").Append(",\"lastClose\":").Append(LastCloseJson())
            .Append(",\"lastSwitch\":").Append(LastSwitchJson()).Append('}').ToString();
    }

    /// <summary>
    /// 진단 hold — hold=0~1 이면 effect 한 건을 그 진행 지점에 멈춰 두고(화면 캡처로 실제 렌더 확인용), hold=release 면 멈춘 재생을 모두 끝내 전 값으로 되돌린다.
    /// </summary>
    private static string Hold(Control? target, string? effect, string hold)
    {
        if (string.Equals(hold, "release", StringComparison.OrdinalIgnoreCase))
        {
            var running = s_running.Values.ToList();
            running.ForEach(p => p.Finish());
            return "{\"released\":" + running.Count + ",\"allRestored\":" + (running.All(p => p.Restored()) ? "true" : "false") + "}";
        }

        if (!double.TryParse(hold, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) || target is null
            || MotionEffects.All.FirstOrDefault(e => e.Id != MotionEffects.None && string.Equals(e.Id, effect?.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } row
            || Begin(target, row) is not { } playback)
        {
            return "{\"held\":null}";
        }

        playback.Step(t);
        var s = playback.Last;
        return "{\"held\":\"" + row.Id + "\",\"direction\":\"" + row.Direction + "\",\"t\":" + N(t) + ",\"o\":" + N(s.Opacity) + ",\"clip\":"
            + (s.Clip is { } c ? "\"" + N(c.X) + "," + N(c.Y) + "," + N(c.Width) + "," + N(c.Height) + "\"" : "null") + "}";
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

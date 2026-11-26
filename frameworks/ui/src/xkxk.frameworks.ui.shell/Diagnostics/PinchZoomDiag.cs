// 핀치 줌 진단 원격 명령(pinch.sim·pinch.state) — 실제 두 손가락 없이 PinchZoom 의 같은 매핑 함수로 핀치를 모의하고 배율·글자 크기 전후를 돌려준다(진단 빌드 전용)
#if PD_DIAGNOSTICS
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Input;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics;

public static class PinchZoomDiag
{
    /// <summary>원격 명령 등록(RemoteCommandRouter.CreateDefault 가 부른다).</summary>
    public static void RegisterRemote(RemoteCommandRouter r)
    {
        r.Register("pinch.state", "핀치 줌 상태: 처리기 연결 여부·누적 제스처/단계 수·활성 문서 배율·글자 크기·Ctrl+휠 모드", _ => State());
        r.Register("pinch.sim",
            "핀치 모의(PinchZoom.Apply 와 같은 매핑) scales=1.1,1.21,1.33(누적 배율 순서) [x y = 시작 중점(메인 TopLevel 좌표, 기본 = 그 가운데 — 문서 창 위여야 처리됨)] [end=true] → {before, after, accepted}",
            a =>
            {
                var top = VisualTreeInspector.Main();
                var scales = (a.Str("scales") ?? throw new RemoteCommandException("scales 가 필요합니다(예: 1.1,1.21)."))
                    .Trim('[', ']').Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                    .Select(s => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                var origin = new Point(a.Num("x") ?? top.Bounds.Width / 2, a.Num("y") ?? top.Bounds.Height / 2);
                var before = State();
                var accepted = false;
                foreach (var s in scales)
                {
                    accepted |= PinchZoom.Apply(top, origin, s);
                }

                if (a.Bool("end", true))
                {
                    PinchZoom.End(top);
                }

                return new JsonObject { ["before"] = before, ["after"] = State(), ["accepted"] = accepted, ["origin"] = $"{origin.X:0},{origin.Y:0}" };
            });
    }

    private static JsonObject State() => new()
    {
        ["handler"] = PinchZoom.StepHandler is not null, // false 면 연결(UiSettings, odev-2r) 전 — 핀치가 아무 일도 하지 않음
        ["gestures"] = PinchZoom.Gestures,
        ["steps"] = PinchZoom.Steps,
        ["rejections"] = PinchZoom.Rejections,   // 문서 밖 등으로 거절된 핀치 수(제스처 도중 끊김 진단)
        ["lastScale"] = PinchZoom.LastScale,
        ["activeDoc"] = XkxkUi.Current.Shell?.ActiveDocument?.Title,
        ["zoom"] = XkxkUi.Current.Shell?.ActiveDocument?.Zoom,
        ["fontSize"] = UiSettings.Current.BaseFontSize,
        ["wheelMode"] = UiSettings.Current.WheelMode.ToString(),
    };
}
#endif

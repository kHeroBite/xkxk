// MDI 스냅 끌기 놓기 단계(전체·Reset·로그·Apply) 시간 래퍼 — 진단 빌드는 MdiDragFrameDiag(01a P1)에 단계 시간을 더하고, 제품 빌드는 원래 메서드만 부른다(MdiSnapDrag.cs 300줄 규칙으로 분리)
using Avalonia;
using Avalonia.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Logging;

namespace xkxk.frameworks.ui.shell.Docking;

public static partial class MdiSnapDrag
{
    private static void OnReleasedDiag(PointerReleasedEventArgs e, string via)
    {
#if PD_DIAGNOSTICS
        using var diag = Diagnostics.MdiDragFrameDiag.Time(Diagnostics.MdiDragFrameDiag.Stage.RelSnap);
#endif
        OnReleased(e, via);
    }

    // 놓기 안의 Reset(스냅 막대·미리보기 숨김 = 화면 변화)
    private static void ResetOnRelease()
    {
#if PD_DIAGNOSTICS
        using var diag = Diagnostics.MdiDragFrameDiag.Time(Diagnostics.MdiDragFrameDiag.Stage.RelSnapReset);
#endif
        Reset();
    }

    // 놓기 결과 로그 1줄(PdLog — 웹은 콘솔 출력)
    private static void LogRelease(string line)
    {
#if PD_DIAGNOSTICS
        using var diag = Diagnostics.MdiDragFrameDiag.Time(Diagnostics.MdiDragFrameDiag.Stage.RelSnapLog);
#endif
        PdLog.Info("mdi-snap", line);
    }

    // 놓기 뒤 Post 된 스냅 적용(디스패처 차례에 돈다)
    private static void ApplyDiag(IMdiDocument doc, bool cancelled, SnapZone zone, DockRect start, double grabX, Point drop)
    {
#if PD_DIAGNOSTICS
        using var diag = Diagnostics.MdiDragFrameDiag.Time(Diagnostics.MdiDragFrameDiag.Stage.Apply);
#endif
        Apply(doc, cancelled, zone, start, grabX, drop);
    }
}

// MDI 탭 합류 끌기 놓기 단계(전체·Reset) 시간 래퍼 — 진단 빌드는 MdiDragFrameDiag(01a P1)에 단계 시간을 더하고, 제품 빌드는 원래 메서드만 부른다
using Avalonia.Input;

namespace xkxk.frameworks.ui.shell.Docking;

public static partial class MdiTabJoinDrag
{
    private static void OnReleasedDiag(PointerReleasedEventArgs e, string via)
    {
#if PD_DIAGNOSTICS
        using var diag = Diagnostics.MdiDragFrameDiag.Time(Diagnostics.MdiDragFrameDiag.Stage.RelTabJoin);
#endif
        OnReleased(e, via);
    }

    // 놓기 안의 Reset(합류 아이콘·표적 표시 정리 = 화면 변화)
    private static void ResetOnRelease()
    {
#if PD_DIAGNOSTICS
        using var diag = Diagnostics.MdiDragFrameDiag.Time(Diagnostics.MdiDragFrameDiag.Stage.RelTabJoinReset);
#endif
        Reset();
    }
}

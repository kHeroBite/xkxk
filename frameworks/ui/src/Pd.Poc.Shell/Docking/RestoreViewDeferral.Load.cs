// 복원 문서 뷰 지연 생성(B5)의 열기 시작·실패 — 문서 단위 진입점 WindowLoadState 경유(23 §2.1, BusyHooks 직접 호출 이관)
using System;
using Pd.Poc.Shell.Diagnostics;

namespace Pd.Poc.Shell.Docking;

public static partial class RestoreViewDeferral
{
    /// <summary>
    /// 미뤘던 문서를 실제로 만들기 시작한다. 열기 계측 시작 = 실제로 만들기 시작한 때(기다린 시간은 넣지 않는다).
    /// busy=true(보게 된 시점)면 그 문서에만 열기 토큰(오버레이·커서·창 아이콘 스피너)을 건다. 유휴 선생성(false)은 볼 사람이 없어 걸지 않는다.
    /// </summary>
    private static void BeginOpen(PocDocument doc, bool busy)
    {
        PerfProbe.Current.BeginOpen(doc.Kind.ToString().ToLowerInvariant());
        if (busy)
        {
            WindowLoadState.Default.BeginOpen(doc);
        }
    }

    /// <summary>지연 뷰 생성 실패 — 열기 토큰을 실패로 끝낸다(토큰이 없으면 아무 일도 없다).</summary>
    private static void OpenFailed(PocDocument doc, Exception error) => WindowLoadState.Default.FailOpen(doc, error);
}

// 진행 표시 연결부 정리 — 첫 열림 오버레이 종료(EndOpenDisplay)와 문서 닫힘 때 스코프 통째 해제(ReleaseScope: 열림 타이머·한 프레임 뒤 중복 키·커서·오버레이)
using System;
using System.Linq;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Busy;

public static partial class BusyHooks
{
    /// <summary>첫 열림 오버레이·커서만 끝낸다(뷰 부착·유예·안전장치 시간). 창 아이콘의 불러오는 중은 WindowLoadState 가 따로 쥔다.</summary>
    internal static void EndOpenDisplay(string documentId)
    {
        if (s_opening.Remove(documentId, out var opening))
        {
            opening.Timeout.Dispose();
            EndAfterLayout(opening.Scope);
        }
    }

    /// <summary>
    /// 문서 닫힘 해제. 첫 열림 안전장치 타이머를 멈추고, RunAfterFrame 중복 키를 지우고, BusyService 스코프를 비운다(Clear 가 VisibilityChanged(false)·Armed(false)를 내
    /// 오버레이·커서·포인터 추적이 꺼진다). 그 뒤 늦게 오는 토큰 Dispose·레이아웃 뒤 끝 처리는 스코프가 없어 아무 일도 하지 않는다.
    /// </summary>
    public static void ReleaseScope(string scope)
    {
        if (s_opening.Remove(scope, out var opening))
        {
            opening.Timeout.Dispose(); // 토큰은 Dispose 하지 않는다 — 아래 Clear 가 수를 0 으로 둔다
        }

        var prefix = scope + ":";
        s_afterFrame.RemoveWhere(k => k.StartsWith(prefix, StringComparison.Ordinal));
        var cleared = s_service?.Clear(scope) ?? false;
        if (!cleared)
        {
            BusyOverlay.SetScopeActive(scope, false); // 스코프가 이미 없어도 화면에 남은 오버레이·커서는 끈다
            s_cursor?.SetScope(scope, false);
        }

        if (BusyCursorDriver.UsesFollower && s_follower is not null && s_service is not null)
        {
            s_follower.IsActive = s_service.Snapshot().Any(s => s.Visible);
        }
    }
}

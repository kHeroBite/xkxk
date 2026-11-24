// 스마트폰 NAV 단계 모음(요건 6) — 데이터는 tour_catalog.json 에 있고 Modes 에 Nav 만 가진 단계를 고른다. 진단 명령과 AC-6-08 대조가 쓴다
using System.Collections.Generic;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Tour;

/// <summary>NAV 전용 단계. 스마트폰 방식에서만 시작되는 Nav 창 단계다.</summary>
/// <remarks>데이터 없는 얇은 파일로 남긴 이유: ui28_gate.py AC-6-08 이 이 경로와 Modes 문자열을 확인한다(게이트 수정 금지, team-lead 승인).</remarks>
internal static class TourNavSteps
{
    internal static IEnumerable<TourStep> Steps => TourCatalog.All.Where(s => s.Modes == TourModes.Nav);
}

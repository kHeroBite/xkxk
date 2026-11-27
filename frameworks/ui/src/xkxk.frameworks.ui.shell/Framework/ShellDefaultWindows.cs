// 셸 기본 창 3종(xkxk.demo.grid·gantt·form) 등록 — 옛 DocumentKind 값을 창 ID 로 옮긴 데모 창(I6, a10)
using System;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 셸 기본 창을 등록한다. 등록 주체는 <see cref="UiRegistryOwners.Shell"/> 이며 저장소 생성 때 한 번 부른다.
/// </summary>
/// <remarks>
/// <para>창 ID 와 옛 이름(Grid·Gantt·Form)의 정본은 <c>LegacyWindowKinds</c> 다(ID 상수·<c>NameOf</c> 표). 옛 이름을 별칭으로 둔다.
/// 옛 이름·숫자 "0"~"2" 는 영구 예약어라 이 별칭은 셸 주체만 둘 수 있다(D-V2, a10 §4).
/// 해석은 이 별칭에 기대지 않는다 — 레지스트리 해석의 마지막 단계(<c>LegacyWindowKinds.ToId</c> 로 ID 재탐색)가 늘 풀어 주므로,
/// 다른 주체가 Replace 로 교체할 때 옛 이름 별칭을 다시 넣지 않는다(넣으면 XKUI0503 으로 버려진다). 저장본 변환도 <c>LegacyWindowKinds</c> 가 맡는다.</para>
/// <para>세 창 모두 중복 허용 false·배치 기본·내용 함수 없음(화면 층의 기존 보기 생성 — 셸 기본 3종만)이다.</para>
/// </remarks>
internal static class ShellDefaultWindows
{
    /// <summary>3종을 Grid → Gantt → Form 순서로 등록한다.</summary>
    internal static void Register(IUiWindowRegistry r)
    {
        ArgumentNullException.ThrowIfNull(r);
        Add(r, LegacyWindowKinds.Grid, "shell.kind.grid", MenuIconCatalog.GridIconKey, "chrome.help.grid");
        Add(r, LegacyWindowKinds.Gantt, "shell.kind.gantt", MenuIconCatalog.GanttIconKey, "chrome.help.gantt");
        Add(r, LegacyWindowKinds.Form, "shell.kind.form", MenuIconCatalog.FormIconKey, null);
    }

    private static void Add(IUiWindowRegistry r, string id, string titleKey, string icon, string? descriptionKey) =>
        r.Add(id, o =>
        {
            o.TitleKey = titleKey;
            o.Icon = icon;
            o.DescriptionKey = descriptionKey;
            o.Aliases.Add(LegacyWindowKinds.NameOf(id)!); // 셸 기본 3종은 늘 옛 이름이 있다.
        });
}

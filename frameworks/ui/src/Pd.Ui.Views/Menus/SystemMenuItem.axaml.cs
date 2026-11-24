// 제목줄 '시스템' 상위 메뉴(UI-17) — 디버그·GPU·중복 실행·모니터·로그·문의·업데이트·도움말 코드 비하인드
using Pd.Poc.Shell;
using System;
using Avalonia.Controls;

namespace Pd.Ui.Views.Menus;

/// <summary>제목줄 '시스템' 상위 메뉴(UI-17) — 디버그·GPU·중복 실행·모니터·로그·문의·업데이트·도움말. MainTitleBar.MainMenu 의 직속 MenuItem 이라 더보기 재배치(ReflowMenu)·키보드 탐색이 그대로 동작한다.</summary>
public partial class SystemMenuItem : MenuItem
{
    // 파생 형식은 기본 MenuItem 테마를 찾지 못하므로 테마 키를 MenuItem 으로 둔다(ChromeLogo 와 같은 방식)
    protected override Type StyleKeyOverride => typeof(MenuItem);

    public SystemMenuItem() => InitializeComponent();
}

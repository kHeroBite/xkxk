// 앱 컨텍스트 공개 계약 — 문구·화면 등록·주 화면 모델·셸 명령의 단일 진입 표면(UADR-0011)
using System;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Tour;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 앱 전역 UI 상태에 닿는 진입점이다. <see cref="XkxkUi"/> 의 Current 로 얻는다.
/// </summary>
/// <remarks>
/// 프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다.
/// 확장 규칙: 뒤 batch 에서 멤버를 추가만 한다(제거·서명 변경 금지).
/// 옛 정적 접근자(App.MainViewModel·App.Shell·ViewRegistry.Current·Loc.Instance)는 이 컨텍스트로 위임한다.
/// </remarks>
public interface IUiAppContext
{
    /// <summary>문구 서비스. null 이 아니다.</summary>
    IUiLocalizer Localization { get; }

    /// <summary>등록된 화면 등록부.</summary>
    /// <exception cref="InvalidOperationException">아직 <see cref="RegisterViews(IViewRegistry)"/> 로 등록하지 않았을 때.</exception>
    IViewRegistry Views { get; }

    /// <summary>화면 등록부를 등록한다. 다시 부르면 교체한다.</summary>
    /// <param name="views">등록할 화면 등록부.</param>
    /// <exception cref="ArgumentNullException"><paramref name="views"/> 가 null 일 때.</exception>
    void RegisterViews(IViewRegistry views);

    /// <summary>주 화면 모델. 주 창이 만들어지기 전에는 null.</summary>
    MainViewModel? MainViewModel { get; }

    /// <summary>셸 명령. 셸이 준비되기 전에는 null.</summary>
    IShellCommands? Shell { get; }

    /// <summary>UI 설정(테마·글자 크기·폰트 등). null 이 아니다.</summary>
    /// <remarks>처음 접근할 때 만들고, 그 뒤에는 항상 같은 인스턴스를 돌려준다. 옛 UiSettings.Current 는 이 값으로 위임한다(B05).</remarks>
    UiSettings Settings { get; }

    /// <summary>색 테마 목록·선택. null 이 아니다.</summary>
    /// <remarks>처음 접근할 때 만들고, 그 뒤에는 항상 같은 인스턴스를 돌려준다. 옛 ThemeCatalog.Current 는 이 값으로 위임한다(B05).</remarks>
    ThemeCatalog Themes { get; }

    /// <summary>도움말 투어 카탈로그. null 이 아니다.</summary>
    /// <remarks>
    /// 현재 소스에서 처음 접근할 때 읽는다. 읽기에 실패하면 빈 카탈로그를 돌려준다(앱은 멈추지 않는다).
    /// TourCatalog.UseSource 로 소스를 바꾸면 다음 접근 때 다시 읽는다. 옛 TourCatalog.Current 는 이 값으로 위임한다(B05).
    /// </remarks>
    TourCatalogData Tour { get; }

    /// <summary>모니터 메뉴 화면 모델. null 이 아니다.</summary>
    /// <remarks>
    /// 처음 접근할 때 만들고, 그 뒤에는 항상 같은 인스턴스를 돌려준다. 모니터 공급자가 없으면 빈 목록(HasMonitors=false)이다.
    /// 공급자는 <see cref="UiHostOptions.MonitorProvider"/> 로 받는다. 옛 MonitorMenuViewModel.Instance 는 이 값으로 위임한다(B05).
    /// </remarks>
    MonitorMenuViewModel Monitors { get; }
}

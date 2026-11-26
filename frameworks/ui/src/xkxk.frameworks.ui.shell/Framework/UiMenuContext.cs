// 메뉴 평가 문맥 — 표시 조건·툴팁 대리자에 넘기는 읽기 전용 값(UADR-0009 원칙 1, UADR-0014)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="UiMenuItemOptions.IsVisible"/>·<see cref="UiMenuItemOptions.ToolTip"/> 대리자가 받는 평가 문맥이다.
/// </summary>
/// <remarks>
/// 공개 생성자가 없다. 프레임워크(레지스트리)가 <see cref="IUiMenuRegistry.GetState(string, UiMenuSurface)"/> 평가 때 만든다.
/// 확장 규칙: 새 정보는 읽기 전용 속성 추가로만 넓힌다.
/// </remarks>
public sealed class UiMenuContext
{
    /// <summary>평가 문맥을 만든다(레지스트리 전용).</summary>
    /// <param name="app">앱 컨텍스트. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="item">평가하는 메뉴 항목 정의. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="surface">평가를 요청한 화면 종류.</param>
    internal UiMenuContext(IUiAppContext app, UiMenuItemDefinition item, UiMenuSurface surface)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(item);
        App = app;
        Item = item;
        Surface = surface;
    }

    /// <summary>앱 컨텍스트. null 이 아니다. 셸이 아직 없으면 <see cref="IUiAppContext.Shell"/> 이 null 일 수 있다.</summary>
    public IUiAppContext App { get; }

    /// <summary>평가하는 메뉴 항목 정의.</summary>
    public UiMenuItemDefinition Item { get; }

    /// <summary>평가를 요청한 화면 종류.</summary>
    public UiMenuSurface Surface { get; }
}

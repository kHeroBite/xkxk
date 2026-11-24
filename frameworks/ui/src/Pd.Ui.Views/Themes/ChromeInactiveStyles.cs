// 비활성 흐림(브러시 방식) 스타일의 x:Class — 웹 트리밍이 XAML 로더를 지우지 않도록 형식으로 직접 만든다
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Pd.Ui.Views.Themes;

/// <summary>
/// Themes/ChromeInactive.axaml 의 x:Class. 생성자의 AvaloniaXamlLoader.Load(this) 는 XAML 컴파일러가 Populate 직접 호출로 바꾼다.
/// avares URI(StyleInclude) 로 런타임에 찾으면 웹 트리밍(ILLink)이 !XamlLoader 를 지워 "No precompiled XAML found" 가 난다.
/// </summary>
public sealed class ChromeInactiveStyles : Styles
{
    public ChromeInactiveStyles() => AvaloniaXamlLoader.Load(this);
}

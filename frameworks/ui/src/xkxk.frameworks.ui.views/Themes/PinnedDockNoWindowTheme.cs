// Window 없는 플랫폼용 PinnedDock 테마 사본의 x:Class — 웹 트리밍이 XAML 로더를 지우지 않도록 형식으로 직접 만든다
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace xkxk.frameworks.ui.views.Themes;

/// <summary>
/// Themes/PinnedDockNoWindow.axaml 의 x:Class. 생성자의 AvaloniaXamlLoader.Load(this) 는 XAML 컴파일러가 Populate 직접 호출로 바꾼다.
/// avares URI(ResourceInclude) 로 런타임에 찾으면 웹 트리밍(ILLink)이 !XamlLoader 와 Build 메서드를 지워 "No precompiled XAML found" 로 기동이 실패한다(c3dev_f).
/// </summary>
public sealed class PinnedDockNoWindowTheme : ResourceDictionary
{
    public PinnedDockNoWindowTheme() => AvaloniaXamlLoader.Load(this);
}

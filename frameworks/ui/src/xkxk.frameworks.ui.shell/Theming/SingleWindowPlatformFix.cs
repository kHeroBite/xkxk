// Window 가 없는 플랫폼(Android·웹 브라우저) 전용 — Dock 테마의 $parent[Window] 바인딩이 일으키는 Ancestor not found 오류를 없앤다
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 근거(Dock.Avalonia.Themes.Fluent 12.1.0.12): ToolChromeControl.axaml:143 은 IsMaximized 를 $parent[Window].WindowState 에, PinnedDockControl.axaml:34·35·46 은 Background 를 $parent[Window] 에 묶는다.
/// Android·브라우저 최상위는 Window 가 아니라 조상을 못 찾아 도구 패널·고정 패널마다 바인딩 오류가 로그에 남는다(FallbackValue 로 값은 안전).
/// ① ToolChrome 테마의 IsMaximized setter 만 지운다(기본값 False 라 화면 동작 동일). ② PinnedDock 은 템플릿 안 바인딩이라 지울 수 없어
/// $parent[TopLevel] 로 바꾼 사본(Themes/PinnedDockNoWindow.axaml)을 앱 리소스에 합쳐 우선하게 한다. 컨트롤이 만들어지기 전(AfterSetup)에 1회 부른다.
/// 데스크톱 헤드는 부르지 않는다 — 데스크톱 Dock 테마·동작은 바뀌지 않는다.
/// </summary>
public static class SingleWindowPlatformFix
{
    private static bool s_applied;

    public static void Apply()
    {
        var app = Application.Current;
        if (app is null || s_applied)
        {
            return;
        }

        s_applied = true;
        var removed = 0;
        if (app.TryGetResource(typeof(ToolChromeControl), app.ActualThemeVariant, out var res) && res is ControlTheme theme)
        {
            foreach (var s in theme.Setters.OfType<Setter>().Where(s => ReferenceEquals(s.Property, ToolChromeControl.IsMaximizedProperty)).ToArray())
            {
                theme.Setters.Remove(s);
                removed++;
            }
        }

        app.Resources.MergedDictionaries.Add(XkxkUi.Current.Views.CreatePinnedDockNoWindowTheme());
        Console.WriteLine($"[host] 단일 창 아님 보정: ToolChrome IsMaximized setter 제거 {removed}건, PinnedDock 테마 TopLevel 사본 적용");
    }
}

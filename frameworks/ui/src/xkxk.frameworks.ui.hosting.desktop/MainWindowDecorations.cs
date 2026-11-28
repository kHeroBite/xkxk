// 메인 창 Avalonia 자체 제목줄 제거 — Full·확장 제목줄을 유지한 채 Avalonia 가 앱 위에 그리는 제목 글자·캡션 버튼(Overlay)만 빼서 PD 제목줄과 겹치지 않게 한다(Windows 전용)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Templates;
using Avalonia.Styling;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 근거(Avalonia 12.1.4 디컴파일): Win32 WindowImpl 은 확장 시 RequestedDrawnDecorations=TitleBar 를 요청하고, Window.ComputeDecorationParts 는 WindowDecorations=Full 이면 제목줄 파트를 켠다.
/// TopLevelHost 는 Overlay 를 TopLevel 위에 쌓는다. Fluent 템플릿의 Overlay 에는 창 제목(Title)과 캡션 버튼이 있어 PD MainTitleBar 와 겹쳤다.
/// 대응: 기본 테마를 BasedOn 으로 두고 Template 만 감싸 Overlay 를 비운다. Underlay·전체 화면 팝오버·기본 두께 값은 그대로이고 Full 이라 비클라이언트 hit-test 도 바뀌지 않는다.
/// 12.1.4 기본 테마 구조에 의존하므로 찾지 못하면 아무것도 바꾸지 않고 false 를 돌려준다(호스트가 로그로 남긴다).
/// </summary>
internal static class MainWindowDecorations
{
    /// <summary>표시 전에 호출. 적용했으면 true.</summary>
    public static bool RemoveOverlay(Window main)
    {
        if (!OperatingSystem.IsWindows() || Application.Current is not { } app
            || !app.TryFindResource(typeof(WindowDrawnDecorations), out var found) || found is not ControlTheme baseTheme
            || FindTemplate(baseTheme) is not { } inner)
        {
            return false;
        }

        main.WindowDecorationsTheme = new ControlTheme(typeof(WindowDrawnDecorations))
        {
            BasedOn = baseTheme,
            Setters = { new Setter(WindowDrawnDecorations.TemplateProperty, new NoOverlayTemplate(inner)) },
        };
        return true;
    }

    private static IWindowDrawnDecorationsTemplate? FindTemplate(ControlTheme? theme)
    {
        for (; theme is not null; theme = theme.BasedOn)
        {
            foreach (var setter in theme.Setters)
            {
                if (setter is Setter { Value: IWindowDrawnDecorationsTemplate template } s && s.Property == WindowDrawnDecorations.TemplateProperty)
                {
                    return template;
                }
            }
        }

        return null;
    }

    private sealed class NoOverlayTemplate(IWindowDrawnDecorationsTemplate inner) : IWindowDrawnDecorationsTemplate
    {
        public TemplateResult<WindowDrawnDecorationsContent> Build()
        {
            var result = inner.Build();
            if (result.Result is { } content)
            {
                content.Overlay = null;
                content.Underlay = null; // 제목줄 배경·AvaloniaTitleBar(셸 제목줄이 덮어 보이지 않고 hit-test 는 셸 역할이 맡는다)
            }

            return result;
        }

        object? ITemplate.Build() => Build();
    }
}

// 기본 문서 뷰 생성기(창 ID 별 뷰·창 정의 내용·자리표시) — App.axaml.cs 에서 분리한 책임 단위
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 기본 문서 뷰 생성기(I11, a10 §7). 문서의 창 ID 를 창 레지스트리에서 찾아 뷰를 만든다.
/// 정의 없음 → 자리표시(shell.window.missing)만 보인다. XKUI0505 는 셸 입구(열기·복원)가 내므로 여기서는 내지 않는다.
/// 내용 함수가 있으면 <see cref="WindowDisplay.InvokeContent"/> 결과(예외·null·Control 아님이면 자리표시 + XKUI0507).
/// 내용 함수가 없는 창은 모두 자리표시 + XKUI0507(내용 함수 없음)이다(B10, a10 §7 — 데모 3종 내용은 시험대 모듈이 채운다).
/// </summary>
public sealed class DefaultDocumentViewFactory : IDocumentViewFactory
{
    public Control Create(PocDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var app = XkxkUi.Current;
        if (!app.Windows.TryResolve(document.WindowId, out var window))
        {
            return Placeholder(document);
        }

        if (window.CreateContent is not null)
        {
            var content = WindowDisplay.InvokeContent(document, out var failed); // 예외면 셸이 XKUI0507 을 이미 남겼다
            if (content is Control control)
            {
                return control;
            }

            if (!failed)
            {
                app.Diagnostics.Report(new UiDiagnostic(UiDiagnosticIds.WindowContentFailed, UiDiagnosticSeverity.Error,
                    $"창 '{window.Id}' 의 내용 생성 함수가 {(content is null ? "null" : content.GetType().FullName)} 을 돌려줬다(Avalonia Control 이어야 한다).", window.Owner)); // i18n-ok 개발자 진단
            }

            return Placeholder(document);
        }

        app.Diagnostics.Report(new UiDiagnostic(UiDiagnosticIds.WindowContentFailed, UiDiagnosticSeverity.Error,
            $"창 '{window.Id}' 에 내용 생성 함수(CreateContent)가 없다. 화면 층이 대신 만들어 주는 창은 없으므로 CreateContent 를 채워야 한다.", window.Owner)); // i18n-ok 개발자 진단
        return Placeholder(document);
    }

    /// <summary>자리표시 내용: '등록되지 않은 창입니다: {창 ID}'. 문서 ID·제목·위치는 그대로라 다시 저장해도 사라지지 않는다.</summary>
    private static Control Placeholder(PocDocument document) => new TextBlock
    {
        Text = Loc.Tr("shell.window.missing", document.WindowId),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };
}

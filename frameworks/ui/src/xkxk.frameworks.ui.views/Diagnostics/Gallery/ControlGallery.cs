// 진단 전용 컨트롤 갤러리 문서 진입점·원격 명령 — Fluent 강조색 컨트롤을 메인 창 안 문서 1개에 모아 M4·C6a 화소 비교와 Q11 디자인 심사에 쓴다
#if PD_DIAGNOSTICS // 진단 빌드 전용 — 제품 빌드에는 문자열·화면이 남지 않는다
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views.Diagnostics.Gallery;

/// <summary>
/// 갤러리 문서는 새 DocumentKind 를 만들지 않고 Form 종류 + 고정 제목 접두로 연다(BenchEntry 와 같은 방식 — 문서 종류 열거·레이아웃 직렬화·다국어 사전 불변).
/// 뷰는 App.DocumentViews 를 감싼 팩토리가 제목 접두를 보고 GalleryView 로 바꿔 만든다.
/// 한계: 앱 시작 때 레이아웃 복원으로 다시 열린 갤러리 문서는 팩토리 설치 전이면 일반 양식 화면으로 보인다 — 닫고 gallery.open 으로 다시 연다.
/// </summary>
public static class ControlGallery
{
    /// <summary>진단 전용 화면이라 제목은 영어 고정(다국어 사전 미사용).</summary>
    public const string TitlePrefix = "Gallery · ";

    private static bool s_installed;

    /// <summary>갤러리 문서를 연다(이미 열려 있으면 그 문서). 셸 미준비면 null.</summary>
    public static PocDocument? Open()
    {
        if (App.Shell is not { } shell)
        {
            return null;
        }

        EnsureFactory();
        return shell.OpenDocument(DocumentKind.Form, TitlePrefix + "Controls", allowDuplicate: false);
    }

    private static void EnsureFactory()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        App.DocumentViews = new GalleryViewFactory(App.DocumentViews);
    }

    private sealed class GalleryViewFactory(IDocumentViewFactory inner) : IDocumentViewFactory
    {
        public Control Create(PocDocument document) =>
            document.Title is { } t && t.StartsWith(TitlePrefix, StringComparison.Ordinal) ? new GalleryView() : inner.Create(document);
    }

    /// <summary>원격 명령 등록: gallery.open·gallery.set·gallery.state(RemoteCommandRouter.CreateDefault 가 부른다).</summary>
    public static void RegisterRemote(RemoteCommandRouter r)
    {
        r.Register("gallery.open", "진단 컨트롤 갤러리 문서 열기(메인 창 안 문서, Form 종류 + 제목 접두) → {title, state}", async _ =>
        {
            var doc = Open() ?? throw new RemoteCommandException("셸이 준비되지 않았습니다.");
            var view = await WaitViewAsync();
            return new JsonObject { ["title"] = doc.Title, ["state"] = view.State() };
        });
        r.Register("gallery.set",
            "갤러리 상태 state=combo-open|combo-close|focus-text|focus-check|focus-none|disabled-on|disabled-off → 상태(포인터 hover·누름은 정적 캡처 대상이 아니라 없음)",
            async a =>
            {
                var view = await WaitViewAsync();
                var state = a.Str("state") ?? throw new RemoteCommandException("state 가 필요합니다.");
                if (!view.Apply(state))
                {
                    throw new RemoteCommandException("모르는 state: " + state);
                }

                return view.State();
            });
        r.Register("gallery.state", "갤러리 컨트롤 상태(체크·선택·포커스·펼침·사용 가능)", async _ => (await WaitViewAsync()).State());
    }

    /// <summary>열린 갤러리 화면(최대 3초 대기 — gallery.open 직후 화면 생성까지). 없으면 오류.</summary>
    private static async Task<GalleryView> WaitViewAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            if (GalleryView.Latest is { } v)
            {
                return v;
            }

            await Task.Delay(50);
        }

        throw new RemoteCommandException("열린 갤러리 문서가 없습니다. 먼저 gallery.open 을 실행하세요.");
    }
}
#endif

// 벤치 문서 진입점 — 메뉴·원격 명령이 부르는 공개 API. 진단+벤치 빌드(PD_DIAGNOSTICS && PD_BENCH)에서만 동작하고 제품판에는 문자열·로직이 남지 않는다
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// 벤치 문서는 새 창 종류를 만들지 않고 Form 창 + 고정 제목 접두로 연다. 뷰는 App.DocumentViews 를
/// 감싼 팩토리가 제목 접두를 보고 BenchDocumentView 로 바꿔 만든다(창 등록·레이아웃 직렬화·다국어 사전을 건드리지 않기 위함).
/// 한계: 앱 시작 때 레이아웃 복원으로 다시 열린 벤치 문서는 팩토리 설치 전이면 일반 양식 화면으로 보인다 — 닫고 다시 연다.
/// 제품판(진단 꺼짐)에서는 IsAvailable=false, Open 은 null 만 돌려준다.
/// </summary>
public static class BenchEntry
{
#if PD_DIAGNOSTICS && PD_BENCH
    public const bool IsAvailable = true;

    /// <summary>진단 전용 화면이라 제목은 영어 고정(다국어 사전 미사용).</summary>
    public const string TitlePrefix = "Bench · ";
    private const string ChartSuffix = "Chart";
    private const string DashboardSuffix = "Dashboard"; // U4~U6 대시보드 벤치

    /// <summary>벤치 문서를 연다. target=grid|chart(처음 보일 탭)|dashboard(대시보드 벤치 문서). 셸 미준비면 null.</summary>
    public static PocDocument? Open(string? target = null)
    {
        if (XkxkUi.Current.Shell is not { } shell) { return null; }
        EnsureFactory();
        return shell.OpenDocument(LegacyWindowKinds.Form, TitlePrefix + (target == "chart" ? ChartSuffix : target == "dashboard" ? DashboardSuffix : "Grid"), allowDuplicate: true);
    }

    private static bool s_installed;

    public static void EnsureFactory()
    {
        if (s_installed) { return; }
        s_installed = true;
        App.DocumentViews = new BenchViewFactory(App.DocumentViews);
    }

    private sealed class BenchViewFactory : IDocumentViewFactory
    {
        private readonly IDocumentViewFactory _inner;

        public BenchViewFactory(IDocumentViewFactory inner) => _inner = inner;

        public Avalonia.Controls.Control Create(PocDocument document) =>
            document.Title is { } t && t.StartsWith(TitlePrefix, System.StringComparison.Ordinal)
                ? t.EndsWith(DashboardSuffix, System.StringComparison.Ordinal)
                    ? new xkxk.frameworks.ui.views.Documents.Bench.DashboardBenchView()
                    : new xkxk.frameworks.ui.views.Documents.Bench.BenchDocumentView(t.EndsWith(ChartSuffix, System.StringComparison.Ordinal) ? "chart" : "grid")
                : _inner.Create(document);
    }
#else
    public const bool IsAvailable = false;

    /// <summary>벤치 미포함 빌드: 아무것도 하지 않는다.</summary>
    public static PocDocument? Open(string? target = null)
    {
        _ = target;
        return null;
    }
#endif

#if PD_DIAGNOSTICS
    /// <summary>원격 명령 등록(RemoteCommandRouter.CreateDefault 가 부른다). 벤치 미포함 빌드는 등록하지 않는다.</summary>
    public static void RegisterRemote(xkxk.frameworks.ui.shell.Diagnostics.Remote.RemoteCommandRouter r)
    {
#if PD_BENCH
        xkxk.frameworks.ui.views.Documents.Bench.BenchRemote.Register(r);
#else
        _ = r;
#endif
    }
#endif
}

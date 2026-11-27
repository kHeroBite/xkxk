// 벤치 문서 진입점 — 벤치 열기·벤치 화면 생성·원격 명령 등록. 진단+벤치 빌드(PD_DIAGNOSTICS && PD_BENCH)에서만 동작하고 제품판에는 빈 정적 형식 이름만 남고 벤치 로직·문자열은 빠진다
using System;
using System.Threading;
using xkxk.frameworks.ui.shell.Framework;
#if PD_DIAGNOSTICS && PD_BENCH
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.testbed.Bench;
#endif

namespace xkxk.frameworks.ui.testbed;

/// <summary>
/// 벤치 문서는 새 창 종류를 만들지 않고 입력폼 데모 창(<see cref="TestbedWindows.Form"/>) + 고정 제목 접두로 연다.
/// 입력폼 창의 내용 함수(<see cref="TestbedWindows"/>)가 제목 접두를 보고 벤치 화면을 만든다.
/// </summary>
/// <remarks>벤치 미포함 빌드에서는 <see cref="Register"/> 가 아무것도 하지 않는다.</remarks>
public static class TestbedBench
{
#if PD_DIAGNOSTICS && PD_BENCH
    /// <summary>벤치 문서 제목 접두. 진단 전용 화면이라 제목은 영어 고정(다국어 사전 미사용).</summary>
    public const string TitlePrefix = "Bench · ";
    private const string ChartSuffix = "Chart";
    private const string DashboardSuffix = "Dashboard"; // U4~U6 대시보드 벤치
    private const string GridSuffix = "Grid";

    private static int s_remoteAdded;

    /// <summary>벤치 문서를 연다. target=grid|chart(처음 보일 탭)|dashboard(대시보드 벤치 문서). 셸 미준비면 null.</summary>
    /// <param name="target">처음 보일 화면. null 이면 grid.</param>
    /// <returns>연 문서. 셸이 아직 없으면 null.</returns>
    public static PocDocument? Open(string? target = null)
    {
        if (XkxkUi.Current.Shell is not { } shell)
        {
            return null;
        }

        var suffix = target == "chart" ? ChartSuffix : target == "dashboard" ? DashboardSuffix : GridSuffix;
        return shell.OpenDocument(TestbedWindows.Form, TitlePrefix + suffix, allowDuplicate: true);
    }

    /// <summary>제목이 벤치 접두로 시작하면 벤치 화면을 만든다. 아니면 null.</summary>
    /// <param name="title">문서 제목.</param>
    internal static object? CreateView(string? title)
    {
        if (title is null || !title.StartsWith(TitlePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return title.EndsWith(DashboardSuffix, StringComparison.Ordinal)
            ? new DashboardBenchView()
            : new BenchDocumentView(title.EndsWith(ChartSuffix, StringComparison.Ordinal) ? "chart" : "grid");
    }
#endif

    /// <summary>벤치 원격 명령을 원격 라우터 확장으로 등록한다(진단+벤치 빌드만, 앱 수명 동안 한 번).</summary>
    /// <param name="context">모듈 구성 문맥. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static void Register(IUiModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
#if PD_DIAGNOSTICS && PD_BENCH
        // 확장 목록은 앱 전역이라 모듈 구성이 여러 번 돌아도 한 번만 더한다.
        if (Interlocked.Exchange(ref s_remoteAdded, 1) != 0)
        {
            return;
        }

        RemoteCommandRouter.AddExtension(BenchRemote.Register);
#endif
    }
}

// 메뉴 구성(ui.menu.build)·최상위 열림 갱신(ui.menu.refresh) 비용을 PerfProbe 조작 기록에 남기는 화면 층 계측 도우미
using System.Diagnostics;
using xkxk.frameworks.ui.shell.Diagnostics;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>
/// MenuBuilder 내부 구간 계측(B08 D4). <see cref="PerfProbe.IsEnabled"/> 가 거짓(제품판)이면 Start 가 0 을 돌려주고 Stop 은 아무것도 하지 않는다.
/// 기록은 sync=frame=구간 경과 ms 로 남긴다(프레임 대기 없음 — 동기 구성 비용만 잰다). UI 스레드에서만 호출한다.
/// </summary>
internal static class MenuPerf
{
    internal const string Build = "ui.menu.build";
    internal const string Refresh = "ui.menu.refresh";

    /// <summary>구간 시작 타임스탬프. 계측이 꺼져 있으면 0.</summary>
    internal static long Start() => PerfProbe.IsEnabled ? Stopwatch.GetTimestamp() : 0;

    /// <summary>구간 끝. t0==0(계측 꺼짐)이면 무동작, 아니면 경과 ms 를 name 으로 기록한다.</summary>
    internal static void Stop(string name, long t0)
    {
        if (t0 == 0)
        {
            return;
        }

        var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        PerfProbe.Current.RecordOperation(name, ms, ms);
    }
}

// 표본 조회 지연 주입점(진단 빌드 전용) — --sample-load-delay-ms 비동기 지연, --sample-ui-block-ms 호출 스레드 점유(AC-018a~c)
#if PD_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.data.Samples;

/// <summary>불러오는 중 표시 시험용. 실제 조회가 없는 표본 데이터에 '늦은 응답'과 'UI 스레드 막힘'을 흉내 낸다. 기본 0(아무것도 안 함).</summary>
public static class SampleLoadDelay
{
    private const int MaxMs = 60_000;
    private static int s_delayMs;
    private static int s_blockMs;

    /// <summary>비동기 지연(ms).</summary>
    public static int DelayMs { get => s_delayMs; set => s_delayMs = Math.Clamp(value, 0, MaxMs); }

    /// <summary>지연 뒤 부른 스레드(UI)를 점유하는 시간(ms).</summary>
    public static int BlockMs { get => s_blockMs; set => s_blockMs = Math.Clamp(value, 0, MaxMs); }

    /// <summary>부른 동기화 문맥으로 돌아와 점유한다. 둘 다 0 이면 동기 완료다.</summary>
    public static async Task WaitAsync()
    {
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs);
        }

        if (BlockMs > 0)
        {
            // Thread.Sleep 대신 점유한다 — 웹(단일 스레드)에서도 같은 방식으로 막힌다
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < BlockMs)
            {
                Thread.SpinWait(1000);
            }
        }
    }
}
#endif

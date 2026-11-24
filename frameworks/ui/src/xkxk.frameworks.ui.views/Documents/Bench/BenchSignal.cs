// 벤치용 신호 생성·다운샘플(시드 고정 랜덤 워크, LTTB) 순수 함수 모음
using xkxk.frameworks.ui.shell;
using System;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public static class BenchSignal
{
    /// <summary>
    /// LTTB(Largest-Triangle-Three-Buckets) 다운샘플 — x 는 원본 인덱스. 결과는 y 만 돌려준다(벤치 공정 비교용: 세 라이브러리에 같은 점 수).
    /// threshold 가 3 미만이거나 원본보다 크면 원본을 그대로 돌려준다.
    /// </summary>
    public static double[] Lttb(double[] data, int threshold)
    {
        var n = data.Length;
        if (threshold >= n || threshold < 3) { return data; }
        var outp = new double[threshold];
        var every = (double)(n - 2) / (threshold - 2);
        var a = 0;
        outp[0] = data[0];
        for (var i = 0; i < threshold - 2; i++)
        {
            var avgStart = (int)Math.Floor((i + 1) * every) + 1;
            var avgEnd = Math.Min((int)Math.Floor((i + 2) * every) + 1, n);
            double avgX = 0, avgY = 0;
            var cnt = Math.Max(1, avgEnd - avgStart);
            for (var k = avgStart; k < avgEnd; k++) { avgX += k; avgY += data[k]; }
            avgX /= cnt;
            avgY /= cnt;
            var rangeStart = (int)Math.Floor(i * every) + 1;
            var rangeEnd = (int)Math.Floor((i + 1) * every) + 1;
            double maxArea = -1;
            var next = rangeStart;
            for (var k = rangeStart; k < rangeEnd; k++)
            {
                var area = Math.Abs((a - avgX) * (data[k] - data[a]) - (a - k) * (avgY - data[a]));
                if (area > maxArea) { maxArea = area; next = k; }
            }

            outp[i + 1] = data[next];
            a = next;
        }

        outp[threshold - 1] = data[n - 1];
        return outp;
    }

    /// <summary>시드 고정 랜덤 워크(실행마다 같은 모양).</summary>
    public static double[] RandomWalk(int n, int seed)
    {
        var rnd = new Random(seed);
        var ys = new double[n];
        var v = 50.0;
        for (var i = 0; i < n; i++)
        {
            v += (rnd.NextDouble() - 0.5) * 2;
            ys[i] = v;
        }

        return ys;
    }
}

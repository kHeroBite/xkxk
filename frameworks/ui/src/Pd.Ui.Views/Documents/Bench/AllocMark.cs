// 벤치 측정 구간의 관리 할당량·GC 횟수 표식(ChartBenchView 에서 분리)
using Pd.Poc.Shell;
using System;
using System.Text.Json.Nodes;

namespace Pd.Ui.Views.Documents.Bench;

/// <summary>
/// 측정 구간의 관리 할당량과 GC 횟수(ores-chart 제안 M2). 할당량은 GC.GetTotalAllocatedBytes(precise:false) 차이라 근사값이다.
/// 상주 메모리(memory_delta)와 달리 '다시 그리기·스트리밍 중 쓰레기를 얼마나 만드는가'를 본다.
/// </summary>
internal readonly record struct AllocMark(long Bytes, int G0, int G1, int G2)
{
    public static AllocMark Now() => new(GC.GetTotalAllocatedBytes(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    public JsonObject Since(AllocMark s, int frames) => new()
    {
        ["allocated_mb"] = BenchProbe.Mb(Bytes - s.Bytes),
        ["allocated_kb_per_frame"] = frames > 0 ? Math.Round((Bytes - s.Bytes) / 1024.0 / frames, 1) : null,
        ["gc0"] = G0 - s.G0,
        ["gc1"] = G1 - s.G1,
        ["gc2"] = G2 - s.G2,
    };
}

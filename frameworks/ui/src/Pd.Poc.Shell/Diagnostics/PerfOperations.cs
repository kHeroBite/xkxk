// UI 조작 속도 계측(PERF-UI) 저장소(진단 빌드 전용) — 이름별 sync·frame ms 링 버퍼, 긴 프레임·GC 겹침 집계, operations·gc JSON 출력
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static Pd.Poc.Shell.Diagnostics.PerfStats;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>PerfProbe 가 소유하는 조작 기록. 공개 진입점(TimeOperation·RecordOperation 등)은 PerfProbe 에 있다. UI 스레드 전용.</summary>
internal sealed class PerfOperations
{
    private readonly Dictionary<string, (List<double> Sync, List<double> Frame)> _ops = new();

    /// <summary>이미 잰 값을 기록한다. 이름별 최근 PerfProbe.MaxOperationsPerName 건만 유지한다.</summary>
    public void Record(string name, double syncMs, double frameMs)
    {
        if (!_ops.TryGetValue(name, out var lists))
        {
            lists = (new List<double>(), new List<double>());
            _ops[name] = lists;
        }

        if (lists.Frame.Count >= PerfProbe.MaxOperationsPerName)
        {
            lists.Sync.RemoveAt(0); // 이름별 최근 MaxOperationsPerName 건만 유지(링 버퍼 — 장시간 실행 메모리 상한)
            lists.Frame.RemoveAt(0);
        }

        lists.Sync.Add(syncMs);
        lists.Frame.Add(frameMs);
    }

    /// <summary>조작 기록과 긴 프레임·GC 집계 초기화.</summary>
    public void Reset()
    {
        _ops.Clear();
        (_longFrames, _longFramesGc, _longFramesGc2, _longGcMs, _longNoGcMs) = (0, 0, 0, 0, 0);
    }

    /// <summary>이름별 기록 수(시나리오 진행 확인용).</summary>
    public int Count(string name) => _ops.TryGetValue(name, out var l) ? l.Frame.Count : 0;

    // 긴 프레임과 GC 의 겹침(웹 frame p95 가 GC 정지인지 판별 — Q5 웹). 프레임 간격 안에 GC 횟수가 바뀌었으면 그 프레임에 GC 가 있었다고 본다.
    private const double LongFrameMs = 100;
    private int _gcLast = -1;
    private int _gc2Last = -1;
    private int _longFrames;
    private int _longFramesGc;
    private int _longFramesGc2;
    private double _longGcMs;
    private double _longNoGcMs;

    /// <summary>PerfProbe.OnFrame 이 매 프레임 부른다(GC.CollectionCount 2회 — 가볍다).</summary>
    public void NoteFrameGc(double dt)
    {
        var all = GC.CollectionCount(0); // 0세대 횟수는 상위 세대 수집도 포함한다
        var g2 = GC.CollectionCount(2);
        var changed = _gcLast >= 0 && all != _gcLast;
        var changed2 = _gc2Last >= 0 && g2 != _gc2Last;
        (_gcLast, _gc2Last) = (all, g2);
        if (dt < LongFrameMs) { return; }
        _longFrames++;
        if (changed) { _longFramesGc++; _longGcMs += dt; }
        else { _longNoGcMs += dt; }
        if (changed2) { _longFramesGc2++; }
    }

    private void WriteGc(Utf8JsonWriter w)
    {
        w.WriteStartObject("gc");
        w.WriteNumber("collections_0", GC.CollectionCount(0));
        w.WriteNumber("collections_1", GC.CollectionCount(1));
        w.WriteNumber("collections_2", GC.CollectionCount(2));
        w.WriteNumber("total_pause_ms", Round(GC.GetTotalPauseDuration().TotalMilliseconds)); // 런타임이 지원하지 않으면 0(브라우저 Mono 는 확인 필요)
        w.WriteNumber("allocated_mb", Round(GC.GetTotalAllocatedBytes() / 1048576.0));
        w.WriteNumber("long_frame_threshold_ms", LongFrameMs);
        w.WriteNumber("long_frames", _longFrames);
        w.WriteNumber("long_frames_with_gc", _longFramesGc);
        w.WriteNumber("long_frames_with_gen2", _longFramesGc2);
        w.WriteNumber("long_frames_with_gc_ms_sum", Round(_longGcMs));
        w.WriteNumber("long_frames_without_gc_ms_sum", Round(_longNoGcMs));
        w.WriteEndObject();
    }

    /// <summary>operations: [{name, count, sync_p50_ms, sync_p95_ms, frame_p50_ms, frame_p95_ms, frame_max_ms}] 다음 gc 객체 — PerfProbe.ToJson 이 호출.</summary>
    public void Write(Utf8JsonWriter w)
    {
        w.WriteStartArray("operations");
        foreach (var (name, (sync, frame)) in _ops.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteNumber("count", frame.Count);
            var s = sync.OrderBy(v => v).ToList();
            var f = frame.OrderBy(v => v).ToList();
            w.WriteNumber("sync_p50_ms", Round(Percentile(s, 0.50)));
            w.WriteNumber("sync_p95_ms", Round(Percentile(s, 0.95)));
            w.WriteNumber("frame_p50_ms", Round(Percentile(f, 0.50)));
            w.WriteNumber("frame_p95_ms", Round(Percentile(f, 0.95)));
            w.WriteNumber("frame_max_ms", Round(f[^1]));
            // 추가 필드(기존 필드 의미 불변): 선형보간 p95, 콜드(1회차)·웜(2회차~) 분리
            w.WriteNumber("sync_p95_interp_ms", Round(PercentileInterp(s, 0.95)));
            w.WriteNumber("frame_p95_interp_ms", Round(PercentileInterp(f, 0.95)));
            if (frame.Count > 1)
            {
                var warm = frame.Skip(1).OrderBy(v => v).ToList();
                w.WriteNumber("frame_cold_ms", Round(frame[0]));
                w.WriteNumber("frame_warm_p50_ms", Round(Percentile(warm, 0.50)));
                w.WriteNumber("frame_warm_p95_interp_ms", Round(PercentileInterp(warm, 0.95)));
            }
            w.WriteEndObject();
        }

        w.WriteEndArray();
        WriteGc(w);
    }

    /// <summary>제품 빌드용 빈 범위(할당 없음).</summary>
    internal sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>TimeOperation 범위. Dispose 때 PerfProbe.EndOperation 으로 sync·frame 을 기록한다(1회만).</summary>
    internal sealed class OperationScope : IDisposable
    {
        private readonly PerfProbe _owner;
        private readonly string _name;
        private readonly double _start;
        private bool _done;

        public OperationScope(PerfProbe owner, string name, double start)
        {
            _owner = owner;
            _name = name;
            _start = start;
        }

        public void Dispose()
        {
            if (_done) { return; }
            _done = true;
            _owner.EndOperation(_name, _start);
        }
    }
}
#endif

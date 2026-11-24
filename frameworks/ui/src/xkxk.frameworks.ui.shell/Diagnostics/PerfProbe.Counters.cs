// 성능 프로브의 단계·지표 기록과 조작 계측(TimeOperation·RecordOperation) 부분(PerfProbe.cs 의 partial)
#if PD_DIAGNOSTICS // 진단 빌드 전용. 제품 빌드 대체는 PerfProbe.Product.cs
using System;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>PerfProbe 의 지표·조작 기록 멤버(본체는 PerfProbe.cs).</summary>
public sealed partial class PerfProbe
{
    /// <summary>자동화 단계 소요 시간 기록.</summary>
    public void RecordStep(string step, double ms) => _steps.Add((step, ms));

    /// <summary>문자열 지표 설정(덮어쓰기). 예: mode=mdi|tabbed.</summary>
    public void SetText(string key, string value) => _texts[key] = value;

    /// <summary>JSON 값(객체·배열 문자열)을 그대로 삽입. 형식이 잘못되면 쓰기 시 예외가 나므로 신뢰하는 모듈 출력만 넣는다.</summary>
    public void SetRawJson(string key, string json) => _raw[key] = json;

    /// <summary>수치 지표 설정(덮어쓰기).</summary>
    public void SetMetric(string key, double value) => _metrics[key] = value;

    /// <summary>수치 지표를 기존 값과 비교해 큰 값으로 설정(예: grid_rows).</summary>
    public void SetMetricMax(string key, double value)
    {
        _metrics[key] = _metrics.TryGetValue(key, out var old) ? Math.Max(old, value) : value;
    }

    /// <summary>
    /// 조작 계측 범위. 사용: <c>using (PerfProbe.Current.TimeOperation("ui.arrange.tile")) { ... }</c>
    /// sync = 생성→Dispose(동기 처리), frame = 생성→Dispose 뒤 두 번째 애니메이션 프레임(변경이 실제로 그려진 다음 — 사용자가 느끼는 속도).
    /// 프레임 타이머(Attach) 전이거나 TopLevel 이 없으면 frame = sync. UI 스레드에서만 호출한다.
    /// </summary>
    public IDisposable TimeOperation(string name) => IsEnabled ? new PerfOperations.OperationScope(this, name, NowMs) : PerfOperations.NoopScope.Instance;

    /// <summary>이미 잰 값을 직접 기록(비동기 경로·외부 계측용). frameMs 를 모르면 syncMs 를 넣는다.</summary>
    public void RecordOperation(string name, double syncMs, double frameMs)
    {
        if (!IsEnabled) { return; }
        _operations.Record(name, syncMs, frameMs);
    }

    /// <summary>조작 기록 초기화(측정 직전 호출 — 기동 시 설정 복원 등 앞선 기록을 버린다). --cmd perf-reset·원격 perf.reset.</summary>
    public void ResetOperations() => _operations.Reset();

    /// <summary>프레임 표본(frame_*·phases)을 비운다. ResetOperations 는 조작 기록만 비우므로 시행별 프레임 통계가 필요하면 함께 부른다(원격 perf.reset).</summary>
    public void ResetFrames() => _frameLog.Reset();

    /// <summary>이름별 기록 수(시나리오 진행 확인용).</summary>
    public int OperationCount(string name) => _operations.Count(name);

    internal void EndOperation(string name, double start)
    {
        var sync = NowMs - start;
        if (_top is not { } top)
        {
            RecordOperation(name, sync, sync);
            return;
        }

        top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => RecordOperation(name, sync, NowMs - start)));
    }
}
#endif

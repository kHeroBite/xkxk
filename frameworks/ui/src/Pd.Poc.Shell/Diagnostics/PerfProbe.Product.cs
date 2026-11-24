// 제품 빌드(PD_DIAGNOSTICS 없음)용 성능 프로브 — 호출부 시그니처만 유지하고 계측·JSON 출력 본문은 컴파일하지 않는다(문서 열기 시간 Debug 로그만 예외, Q10)
#if !PD_DIAGNOSTICS
using System;
using System.Diagnostics;
using Avalonia.Controls;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>진단 빌드의 PerfProbe(PerfProbe.cs)와 같은 공개 멤버. 모두 아무것도 하지 않는다.</summary>
public sealed class PerfProbe
{
    /// <summary>이름별 보관 상한(진단 빌드와 같은 값 — 호출부 컴파일 호환).</summary>
    public const int MaxOperationsPerName = 2000;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private PerfProbe() { }

    public static PerfProbe Current { get; } = new();

    /// <summary>제품 빌드는 항상 false.</summary>
    public static bool IsEnabled => false;

    public bool KeepRendering { get; set; }

    public double Fps => 0;

    public event Action? FrameRecorded { add { } remove { } }

    /// <summary>경과 ms(화면의 처리 시간 표시용으로만 유지).</summary>
    public double NowMs => _clock.Elapsed.TotalMilliseconds;

    public void MarkProcessStart(DateTime processStartUtc) { }

    public void Attach(TopLevel top) { }

    public IDisposable? AttachIfEnabled(TopLevel top, Action<double>? onFps = null) => null;

    public void Stop() { }

    public void BeginPhase(string? name) { }

    // ── 문서 열기 시간 제품 로그(Q10, lead 승인): 진단판 doc.open.{kind} 과 같은 기준점 ──
    // 시작 = 셸 AddDocument 의 BeginOpen(없으면 뷰 생성자), 끝 = 뷰가 화면에 붙은 뒤 두 번째 애니메이션 프레임.
    // 디버그 레벨 로그가 꺼져 있으면(운영 채널 기본) 아무것도 하지 않는다. 결과는 PdLog "doc.open {kind} {ms}ms"(System, Debug).
    private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Queue<double>> _pendingOpen = new();

    private static bool OpenLogOn => Pd.Client.Data.Logging.PdLog.IsEnabled(Pd.Client.Data.Logging.PdLogLevel.Debug);

    public void BeginOpen(string kind)
    {
        if (!OpenLogOn)
        {
            return;
        }

        if (!_pendingOpen.TryGetValue(kind, out var q))
        {
            q = new System.Collections.Generic.Queue<double>();
            _pendingOpen[kind] = q;
        }

        if (q.Count >= 64)
        {
            q.Dequeue(); // 화면에 붙지 않은 열기(복원 중 숨은 탭 등)가 쌓이지 않게
        }

        q.Enqueue(NowMs);
    }

    public void EndOpen(string kind)
    {
        if (_pendingOpen.TryGetValue(kind, out var q) && q.Count > 0)
        {
            var ms = NowMs - q.Dequeue();
            Pd.Client.Data.Logging.PdLog.WriteSystem(Pd.Client.Data.Logging.PdLogLevel.Debug, $"doc.open {kind} {ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}ms");
        }
    }

    /// <summary>true 인 동안 만든 뷰는 열기 기록을 하지 않는다(예열·준비된 표 뷰 — 화면 밖에 붙였다 떼는 뷰가 doc.open 으로 잡히지 않게, A16). 진단 빌드와 같은 멤버.</summary>
    public static bool SuppressOpenTrack { get; set; }

    public void TrackOpen(Control view, string kind)
    {
        if (!OpenLogOn || SuppressOpenTrack)
        {
            return;
        }

        if (!_pendingOpen.TryGetValue(kind, out var q) || q.Count == 0)
        {
            BeginOpen(kind); // 셸이 시작점을 찍지 않은 경로(뷰 단독 생성)
        }

        void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
        {
            view.AttachedToVisualTree -= OnAttached;
            if (TopLevel.GetTopLevel(view) is not { } top)
            {
                EndOpen(kind);
                return;
            }

            top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => EndOpen(kind)));
        }

        view.AttachedToVisualTree += OnAttached;
    }

    public void RecordStep(string step, double ms) { }

    public void SetText(string key, string value) { }

    public void SetRawJson(string key, string json) { }

    public void SetMetric(string key, double value) { }

    public void SetMetricMax(string key, double value) { }

    public string ToJson(string platform) => "{}";

    public IDisposable TimeOperation(string name) => NoopScope.Instance;

    public void RecordOperation(string name, double syncMs, double frameMs) { }

    public void ResetOperations() { }

    public void ResetFrames() { }

    public int OperationCount(string name) => 0;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose() { }
    }
}
#endif

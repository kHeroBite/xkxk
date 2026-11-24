// 문서 열기 시간 계측(진단 빌드 전용) — 뷰 생성(BeginOpen)과 첫 렌더(EndOpen)를 kind 별 대기열로 짝지어 doc.open.{kind} 로 기록
#if PD_DIAGNOSTICS
using System.Collections.Generic;
using Avalonia.Controls;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>PerfProbe 가 소유하는 열기 기록. 공개 진입점(BeginOpen·EndOpen·TrackOpen)은 PerfProbe 에 있다. UI 스레드 전용.</summary>
internal sealed class PerfOpenTracker
{
    private readonly PerfProbe _probe;
    private readonly Dictionary<string, Queue<double>> _pendingOpen = new();

    public PerfOpenTracker(PerfProbe probe) => _probe = probe;

    /// <summary>짝지어진 열기 기록(kind, ms). ToJson 의 open_* 와 opens 배열 원본.</summary>
    public List<(string Kind, double Ms)> Opens { get; } = new();

    /// <summary>문서 뷰 생성 시점 기록. kind 는 grid|gantt|form.</summary>
    public void Begin(string kind)
    {
        if (!_pendingOpen.TryGetValue(kind, out var q))
        {
            q = new Queue<double>();
            _pendingOpen[kind] = q;
        }

        q.Enqueue(_probe.NowMs);
    }

    /// <summary>문서 뷰 첫 렌더 시점 기록(같은 kind 의 가장 오래된 Begin 과 짝짓는다).</summary>
    public void End(string kind)
    {
        if (_pendingOpen.TryGetValue(kind, out var q) && q.Count > 0)
        {
            Record(kind, _probe.NowMs - q.Dequeue());
        }
    }

    private void Record(string kind, double ms)
    {
        Opens.Add((kind, ms));
        _probe.RecordOperation("doc.open." + kind, ms, ms); Pd.Client.Data.Logging.PdLog.WriteSystem(Pd.Client.Data.Logging.PdLogLevel.Debug, $"doc.open {kind} {ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}ms"); // PERF-UI 표 + 제품판과 같은 로그 줄(Q10 대조)
    }

    /// <summary>
    /// 뷰 생성자에서 호출: 지금 BeginOpen 하고, 뷰가 화면에 붙은 뒤 두 번째 애니메이션 프레임
    /// (= 첫 프레임이 실제로 그려진 다음)에서 EndOpen 한다. 화면에 붙지 않으면 기록하지 않는다.
    /// </summary>
    public void Track(Control view, string kind)
    {
        // 셸(WindowCommands.OpenDocument)이 이미 BeginOpen 했으면 그 시각을 쓴다(이중 대기열 방지).
        // 셸 시작 시각이 없으면 공용 대기열에 넣지 않고 이 뷰의 생성 시각을 따로 쥔다 — 화면에 붙지 않고 버려지는 뷰(DocumentPrewarm 예열)가
        // 대기열에 남아 다음 실제 열기와 짝지어지면 doc.open 이 예열 시각부터 재져 수 초~수십 초로 부풀었다(A16 a16diag doc.open.grid 중앙값 10472ms)
        var ownStart = _pendingOpen.TryGetValue(kind, out var q) && q.Count > 0 ? (double?)null : _probe.NowMs;

        void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
        {
            view.AttachedToVisualTree -= OnAttached;
            var top = TopLevel.GetTopLevel(view);
            if (top is null)
            {
                Finish();
                return;
            }

            top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => Finish()));
        }

        void Finish()
        {
            if (ownStart is { } t0)
            {
                Record(kind, _probe.NowMs - t0);
            }
            else
            {
                _probe.EndOpen(kind);
            }
        }

        view.AttachedToVisualTree += OnAttached;
    }
}
#endif

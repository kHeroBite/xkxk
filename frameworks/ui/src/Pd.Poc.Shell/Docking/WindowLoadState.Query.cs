// WindowLoadState 실행 묶음·콘텐츠 연결(보고 계약·뷰 부착 완료·열기 실패)·조회 — 원본 파일 300줄 상한 때문에 나눈 partial
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pd.Poc.Shell.Docking;

public sealed partial class WindowLoadState
{
    /// <summary>문서 단위 실행 묶음. 완료·실패·취소 모두 끝을 내고 예외는 다시 던진다(BusyService.RunAsync 는 main 스코프 전용).</summary>
    public async Task RunAsync(PocDocument doc, string reason, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        var token = Begin(doc, reason);
        try
        {
            await work(ct);
            token.Complete();
        }
        catch (OperationCanceledException)
        {
            token.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            token.Fail(ex);
            throw;
        }
    }

    /// <summary>뷰가 콘텐츠를 만들 때 부른다. 콘텐츠가 보고 계약을 구현하면 열기 토큰을 넘기고 true(뷰 부착을 완료로 보지 않는다).</summary>
    public bool AttachContent(PocDocument doc, object? content)
    {
        if (content is not IContentLoadReporter reporter)
        {
            return false;
        }

        if (_states.TryGetValue(doc.Id, out var st))
        {
            st.OpenClaimed = true;
        }

        reporter.Attach(doc.LoadToken ?? _ended, reason => Begin(doc, reason));
        return true;
    }

    /// <summary>문서 뷰가 화면에 붙었다. 보고 계약이 없는 콘텐츠는 이것을 열기 완료로 본다(23 §2.2 한계).</summary>
    public void ViewRealized(string documentId)
    {
        if (_states.TryGetValue(documentId, out var st) && !st.OpenClaimed)
        {
            st.Live.FirstOrDefault(t => t.IsOpen)?.Complete();
        }
    }

    /// <summary>열기 실패(뷰 생성 예외 등) — 열기 토큰을 실패로 끝낸다.</summary>
    public void FailOpen(PocDocument doc, Exception error)
    {
        if (_states.TryGetValue(doc.Id, out var st) && st.Live.FirstOrDefault(t => t.IsOpen) is { } open)
        {
            open.Fail(error);
        }
    }

    /// <summary>문서가 불러오는 중인가(UI 스레드).</summary>
    public bool IsLoading(PocDocument doc) => CountOf(doc.Id) > 0;

    /// <summary>문서의 가장 오래된 진행 작업이 지연 판정 시간을 넘었는가(UI 스레드).</summary>
    public bool IsSlow(PocDocument doc) => _states.TryGetValue(doc.Id, out var st) && st.Slow;

    /// <summary>상태 키(문서 Id)의 진행 작업 수.</summary>
    public int CountOf(string key) => _states.TryGetValue(key, out var st) ? st.Live.Count : 0;

    /// <summary>결합 전 요청의 진행 작업 수.</summary>
    public int RequestCount(string requestKey) => CountOf(RequestPrefix + requestKey);

    /// <summary>남아 있는 지연 판정 예약 수. 작업이 없으면 0 이어야 한다.</summary>
    public int PendingTimerCount => _states.Values.Count(s => s.SlowTimer is not null);

    /// <summary>살아 있는 상태 전부(복사본).</summary>
    public IReadOnlyList<LoadStateInfo> Snapshot() => _states.Values
        .Select(s => new LoadStateInfo(s.Key, s.Doc?.Id, s.Live.Count, s.Slow, string.Join(",", s.Live.Select(t => t.Reason)))).ToArray();
}

// 도움말 투어 건너뛴 단계 기록(요건 6) — 세션마다 단계·사유를 한 번씩 진단 로그에 남긴다. 건너뛰기 자체는 오류가 아니다
using System.Collections.Generic;
using xkxk.frameworks.ui.data.Logging;

namespace xkxk.frameworks.ui.shell.Tour;

internal sealed class TourSkipLog
{
    private readonly List<(string StepId, string Reason)> _items = new();
    private readonly HashSet<string> _seen = new();
    private readonly TourScope _scope;

    public TourSkipLog(TourScope scope) => _scope = scope;

    /// <summary>이번 세션에서 건너뛴 단계와 사유(mode·platform·requires:이름·target-missing·target-hidden).</summary>
    public IReadOnlyList<(string StepId, string Reason)> Items => _items;

    public void Note(string stepId, string reason)
    {
        if (_seen.Add(stepId + "|" + reason))
        {
            _items.Add((stepId, reason));
            PdLog.WriteSystem(PdLogLevel.Information, $"tour step skipped scope={_scope} id={stepId} reason={reason}");
        }
    }
}

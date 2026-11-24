// 진행 표시 토큰 — BusyService.Begin 이 돌려주고 Dispose 하면 그 스코프의 참조 카운트를 1 줄인다
using System;

namespace xkxk.frameworks.ui.shell.Busy;

/// <summary>진행 중인 작업 1건. Dispose 는 여러 번 불러도 한 번만 반영된다. UI 스레드에서 Dispose 한다.</summary>
public sealed class BusyScope : IDisposable
{
    private readonly BusyService _owner;
    private bool _ended;

    internal BusyScope(BusyService owner, string scope, string reason)
    {
        _owner = owner;
        Scope = scope;
        Reason = reason;
    }

    /// <summary>스코프 키(main 또는 문서 Id).</summary>
    public string Scope { get; }

    /// <summary>작업 사유(진단 표시용, 화면에 나오지 않는다).</summary>
    public string Reason { get; }

    public void Dispose()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _owner.End(Scope);
    }
}

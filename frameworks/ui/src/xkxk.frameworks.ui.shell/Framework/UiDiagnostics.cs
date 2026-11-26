// 진단 모음 — 보고 순서대로 쌓고 Reported 이벤트와 시스템 로그로 알린다
using System;
using System.Collections.Generic;
using xkxk.frameworks.ui.data.Logging;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>구성 진단을 모은다.</summary>
/// <remarks>Warning·Error 진단은 시스템 로그(<c>PdLog.WriteSystem</c>, Warning 레벨)에도 남긴다.</remarks>
public sealed class UiDiagnostics
{
    private readonly object _gate = new();
    private readonly List<UiDiagnostic> _entries = new();
    private bool _hasErrors;

    /// <summary>빈 진단 모음을 만든다.</summary>
    public UiDiagnostics()
    {
    }

    /// <summary>보고된 진단(보고 순서). 호출 시점의 사본이다.</summary>
    public IReadOnlyList<UiDiagnostic> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>Error 진단이 하나라도 있으면 true.</summary>
    public bool HasErrors
    {
        get
        {
            lock (_gate)
            {
                return _hasErrors;
            }
        }
    }

    /// <summary>진단이 보고될 때마다 발생한다.</summary>
    public event EventHandler<UiDiagnostic>? Reported;

    /// <summary>진단을 보고한다.</summary>
    /// <param name="diagnostic">진단. null 이면 <see cref="ArgumentNullException"/>.</param>
    public void Report(UiDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        lock (_gate)
        {
            _entries.Add(diagnostic);
            if (diagnostic.Severity == UiDiagnosticSeverity.Error)
            {
                _hasErrors = true;
            }
        }

        if (diagnostic.Severity != UiDiagnosticSeverity.Info)
        {
            PdLog.WriteSystem(PdLogLevel.Warning, "ui framework " + diagnostic);
        }

        Reported?.Invoke(this, diagnostic);
    }

    /// <summary>코드·심각도·메시지로 진단을 만들어 보고한다.</summary>
    public void Report(string id, UiDiagnosticSeverity severity, string message)
    {
        Report(new UiDiagnostic(id, severity, message));
    }
}

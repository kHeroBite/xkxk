// 구성 오류 예외 — ErrorPolicy=Throw 일 때 Build 가 진단 목록을 담아 던진다
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary><see cref="UiErrorPolicy.Throw"/> 정책에서 Error 진단이 있을 때 <see cref="XkxkUiBuilder.Build"/> 가 던진다.</summary>
public sealed class UiConfigurationException : Exception
{
    /// <summary>메시지와 진단 목록으로 예외를 만든다.</summary>
    /// <param name="message">메시지.</param>
    /// <param name="diagnostics">진단 목록. null 이면 <see cref="ArgumentNullException"/>.</param>
    public UiConfigurationException(string message, IReadOnlyList<UiDiagnostic> diagnostics)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics;
    }

    /// <summary>예외 시점의 진단 목록.</summary>
    public IReadOnlyList<UiDiagnostic> Diagnostics { get; }
}

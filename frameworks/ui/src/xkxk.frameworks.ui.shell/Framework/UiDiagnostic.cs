// 진단 1건 — 문자열 ID·심각도·메시지·출처를 담는 불변 값(UADR-0009 원칙 5)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>구성 중 생긴 진단 1건.</summary>
/// <remarks>확장 규칙: 새 정보는 생성자 오버로드와 속성 추가로만 넓힌다(기본값 파라미터 금지).</remarks>
public sealed class UiDiagnostic
{
    /// <summary>출처 없이 진단을 만든다.</summary>
    /// <param name="id">진단 코드(<see cref="UiDiagnosticIds"/>). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="severity">심각도. 정의 밖 값은 <see cref="UiDiagnosticSeverity.Warning"/> 으로 정규화한다.</param>
    /// <param name="message">메시지. null 이면 <see cref="ArgumentNullException"/>.</param>
    public UiDiagnostic(string id, UiDiagnosticSeverity severity, string message)
        : this(id, severity, message, null)
    {
    }

    /// <summary>출처를 포함해 진단을 만든다.</summary>
    /// <param name="id">진단 코드(<see cref="UiDiagnosticIds"/>). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="severity">심각도. 정의 밖 값은 <see cref="UiDiagnosticSeverity.Warning"/> 으로 정규화한다.</param>
    /// <param name="message">메시지. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="source">출처(예 모듈 ID). null 허용.</param>
    public UiDiagnostic(string id, UiDiagnosticSeverity severity, string message, string? source)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(message);
        Id = id;
        Severity = Normalize(severity);
        Message = message;
        Source = source;
    }

    /// <summary>진단 코드.</summary>
    public string Id { get; }

    /// <summary>심각도(정규화된 값).</summary>
    public UiDiagnosticSeverity Severity { get; }

    /// <summary>메시지.</summary>
    public string Message { get; }

    /// <summary>출처. 없으면 null.</summary>
    public string? Source { get; }

    /// <summary>"[심각도] 코드 (출처): 메시지" 형식 문자열.</summary>
    public override string ToString()
    {
        return Source is null
            ? $"[{Severity}] {Id}: {Message}"
            : $"[{Severity}] {Id} ({Source}): {Message}";
    }

    private static UiDiagnosticSeverity Normalize(UiDiagnosticSeverity severity)
    {
        return severity switch
        {
            UiDiagnosticSeverity.Info => UiDiagnosticSeverity.Info,
            UiDiagnosticSeverity.Warning => UiDiagnosticSeverity.Warning,
            UiDiagnosticSeverity.Error => UiDiagnosticSeverity.Error,
            _ => UiDiagnosticSeverity.Warning,
        };
    }
}

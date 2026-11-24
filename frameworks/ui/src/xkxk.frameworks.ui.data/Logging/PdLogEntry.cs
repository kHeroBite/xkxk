// 제품 로그의 레벨·범주·항목·싱크 계약(UI-17 로그 코어) — 진단 빌드와 무관한 제품 코드
using System;

namespace xkxk.frameworks.ui.data.Logging;

/// <summary>로그 레벨(Microsoft.Extensions.Logging 과 같은 순서).</summary>
public enum PdLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}

/// <summary>로그 범주 이름. 시스템(기동·종료·오류·설정·렌더러·예외)과 사용자(메뉴·명령·문서·레이아웃·언어·테마 — 행동 감사용).</summary>
public static class PdLogCategory
{
    public const string System = "System";
    public const string User = "User";
}

/// <summary>로그 한 줄. 개인정보·업무 데이터 값은 넣지 않고 키·ID 만 넣는다.</summary>
public readonly record struct PdLogEntry(DateTimeOffset Time, PdLogLevel Level, string Category, string Message)
{
    /// <summary>파일·콘솔용 한 줄 형식: "2026-10-29 10:20:31.123 INF System 메시지".</summary>
    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss.fff} {Short(Level)} {Category} {Message}";

    public static string Short(PdLogLevel level) => level switch
    {
        PdLogLevel.Trace => "TRC",
        PdLogLevel.Debug => "DBG",
        PdLogLevel.Information => "INF",
        PdLogLevel.Warning => "WRN",
        PdLogLevel.Error => "ERR",
        _ => "CRT",
    };
}

/// <summary>로그 싱크. Write 는 로그를 쓴 스레드에서 불린다 — 구현은 스레드 안전해야 하고 예외를 밖으로 내지 않는다.</summary>
public interface ILogSink
{
    void Write(in PdLogEntry entry);
}

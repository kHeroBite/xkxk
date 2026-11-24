// 셸 공용 제품 로거 — 레벨 필터·싱크 분배·디버그모드에 따른 기본 최소 레벨(UI-17). 외부 로깅 라이브러리 없이 둔다(웹 번들 크기)
using System;

namespace Pd.Client.Data.Logging;

/// <summary>
/// 정적 로거. 최소 레벨 = 사용자가 고른 값(MinLevelOverride), 없으면 디버그모드 켬→Debug, 끔→Information.
/// 디버그모드 기본값은 채널(AppChannel.DebugDefault)이고 UiSettings 가 저장값으로 Configure 한다. 바꾸면 즉시 반영된다.
/// 메모리 싱크는 항상 있다. 플랫폼 싱크(파일·콘솔)는 호스트가 AddSink 로 붙인다.
/// </summary>
public static class PdLog
{
    private static readonly object Gate = new();
    private static ILogSink[] s_sinks = [];
    private static bool s_debugMode = AppChannel.DebugDefault;
    private static PdLogLevel? s_minOverride;

    static PdLog()
    {
        Memory = new MemoryLogSink();
        s_sinks = [Memory];
    }

    /// <summary>모든 플랫폼 공통 링 버퍼('로그 보기' 창).</summary>
    public static MemoryLogSink Memory { get; }

    /// <summary>데스크톱·Android 파일 로그 폴더(호스트가 설정). 없으면 null(웹).</summary>
    public static string? FileDirectory { get; set; }

    /// <summary>디버그모드(사용자 런타임 기능, PD_DIAGNOSTICS 와 별개). 기본은 채널 기본값.</summary>
    public static bool DebugMode
    {
        get => s_debugMode;
        set => s_debugMode = value;
    }

    /// <summary>사용자가 고른 최소 레벨(null = 디버그모드에 따름).</summary>
    public static PdLogLevel? MinLevelOverride
    {
        get => s_minOverride;
        set => s_minOverride = value;
    }

    /// <summary>현재 유효 최소 레벨. set 은 MinLevelOverride 를 바꾼다.</summary>
    public static PdLogLevel MinLevel
    {
        get => s_minOverride ?? (s_debugMode ? PdLogLevel.Debug : PdLogLevel.Information);
        set => s_minOverride = value;
    }

    /// <summary>저장 설정 적용(UiSettings 가 로드·변경 때 부른다). debugMode=null 이면 채널 기본값.</summary>
    public static void Configure(bool? debugMode, PdLogLevel? minLevel)
    {
        s_debugMode = debugMode ?? AppChannel.DebugDefault;
        s_minOverride = minLevel;
    }

    public static bool IsEnabled(PdLogLevel level) => level >= MinLevel;

    public static void AddSink(ILogSink sink)
    {
        lock (Gate)
        {
            if (Array.IndexOf(s_sinks, sink) < 0)
            {
                s_sinks = [.. s_sinks, sink];
            }
        }
    }

    public static void RemoveSink(ILogSink sink)
    {
        lock (Gate)
        {
            s_sinks = Array.FindAll(s_sinks, s => !ReferenceEquals(s, sink));
        }
    }

    public static void Write(PdLogLevel level, string category, string message)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var entry = new PdLogEntry(DateTimeOffset.Now, level, category, message);
        foreach (var sink in s_sinks) // 배열 교체 방식이라 순회 중 추가·제거에 안전
        {
            try
            {
                sink.Write(entry);
            }
            catch (Exception)
            {
                // 싱크 오류가 앱 동작을 깨지 않게 한다
            }
        }
    }

    public static void WriteSystem(PdLogLevel level, string message) => Write(level, PdLogCategory.System, message);

    public static void WriteUser(PdLogLevel level, string message) => Write(level, PdLogCategory.User, message);

    public static void Info(string category, string message) => Write(PdLogLevel.Information, category, message);

    public static void Error(string category, string message, Exception? ex = null) =>
        Write(PdLogLevel.Error, category, ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");
}

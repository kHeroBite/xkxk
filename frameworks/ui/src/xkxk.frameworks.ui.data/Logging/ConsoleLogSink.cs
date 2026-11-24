// 표준 출력으로 로그를 내보내는 싱크 — Android 는 logcat(DOTNET 태그), 웹은 브라우저 콘솔로 간다(UI-17)
using System;

namespace xkxk.frameworks.ui.data.Logging;

/// <summary>
/// Console.WriteLine 싱크. onlyInDebugMode=true 면 디버그모드일 때만 내보낸다(웹 콘솔 — 운영 채널 기본 끔).
/// Android 는 logcat 이 기본 수집 경로라 false 로 붙인다.
/// </summary>
public sealed class ConsoleLogSink : ILogSink
{
    private readonly bool _onlyInDebugMode;

    public ConsoleLogSink(bool onlyInDebugMode)
    {
        _onlyInDebugMode = onlyInDebugMode;
    }

    public void Write(in PdLogEntry entry)
    {
        if (_onlyInDebugMode && !PdLog.DebugMode)
        {
            return;
        }

        Console.WriteLine("[pd] " + entry);
    }
}

// 로그 내보내기 데이터 처리 — 내보낼 파일 이름 규칙과 메모리 로그 전체를 스트림에 쓰기(화면 LogViewerView 에서 분리, D-LAYER)
using System;
using System.IO;
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.data.Logging;

/// <summary>저장 대화상자는 화면이 띄우고, 고른 파일의 스트림을 받아 내용을 쓰는 일은 여기서 한다.</summary>
public static class LogExport
{
    /// <summary>내보내기 기본 파일 이름(시각 포함).</summary>
    public static string SuggestedFileName() => $"pd-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

    /// <summary>메모리 로그 전체(필터 없음)를 한 줄씩 쓰고 사용자 동작 로그 log.export 를 남긴다.</summary>
    public static async Task WriteAllAsync(Stream stream)
    {
        await using var writer = new StreamWriter(stream);
        foreach (var e in PdLog.Memory.Snapshot())
        {
            await writer.WriteLineAsync(e.ToString());
        }

        PdLog.WriteUser(PdLogLevel.Information, "log.export");
    }
}

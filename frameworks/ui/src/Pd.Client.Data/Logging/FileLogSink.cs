// 일자별 로그 파일(pd-YYYYMMDD.log)에 쓰고 보존 일수가 지난 파일을 지우는 싱크 — 데스크톱·Android 공용(UI-17)
using System;
using System.IO;
using System.Text;

namespace Pd.Client.Data.Logging;

/// <summary>
/// 폴더 안 pd-YYYYMMDD.log 에 한 줄씩 덧붙인다(UTF-8, BOM 없음). 날짜가 바뀌면 새 파일로 넘어간다.
/// 생성 때 보존 일수(기본 14일)를 넘긴 pd-*.log 를 지운다. 쓰기 실패는 삼킨다(로그 때문에 앱이 죽지 않게).
/// 브라우저에는 파일 시스템이 없으므로 쓰지 않는다.
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateOnly _day;

    public FileLogSink(string directory, int retentionDays = 14)
    {
        Directory = directory;
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            Prune(retentionDays);
        }
        catch (Exception)
        {
            // 폴더를 못 만들면 Write 에서 계속 실패를 삼킨다
        }
    }

    public string Directory { get; }

    /// <summary>오늘 파일 경로.</summary>
    public string CurrentPath => Path.Combine(Directory, $"pd-{DateTime.Now:yyyyMMdd}.log");

    public void Write(in PdLogEntry entry)
    {
        lock (_gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(entry.Time.LocalDateTime);
                if (_writer is null || today != _day)
                {
                    _writer?.Dispose();
                    _day = today;
                    var path = Path.Combine(Directory, $"pd-{today:yyyyMMdd}.log");
                    _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                }

                _writer.WriteLine(entry.ToString());
            }
            catch (Exception)
            {
                _writer = null; // 다음 줄에서 다시 연다
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Prune(int retentionDays)
    {
        var limit = DateTime.Now.Date.AddDays(-Math.Max(1, retentionDays));
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "pd-*.log"))
        {
            if (File.GetLastWriteTime(file) < limit)
            {
                File.Delete(file);
            }
        }
    }
}

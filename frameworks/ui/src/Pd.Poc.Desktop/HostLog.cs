// 데스크톱 호스트 로그 — --log 파일과 콘솔에 동시에 쓰고([host] 접두), 처리되지 않은 예외를 남긴다
using System.Text;

namespace Pd.Poc.Desktop;

/// <summary>호스트 로그 출력(Program.cs 에서 분리 — ADR-003 로그 책임). 다른 호스트 파일은 기존대로 Program.Log 를 부른다.</summary>
internal static class HostLog
{
    private static StreamWriter? s_log;

    /// <summary>--log 경로가 있으면 파일 로그를 열고 Console 출력도 함께 기록한다.</summary>
    public static IDisposable? Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        s_log = new StreamWriter(full, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        var sync = TextWriter.Synchronized(s_log);
        Console.SetOut(new TeeWriter(Console.Out, sync));
        Console.SetError(new TeeWriter(Console.Error, sync));
        return s_log;
    }

    public static void Write(string message) =>
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] {message}");

    /// <summary>처리되지 않은 예외(앱 도메인·관찰되지 않은 Task)를 호스트 로그에 남긴다.</summary>
    public static void HookUnhandled()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write($"FATAL unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write($"ERROR unobserved task: {e.Exception}");
            e.SetObserved();
        };
    }

    /// <summary>두 TextWriter 에 동시에 쓰는 래퍼.</summary>
    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => second.Encoding;

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void WriteLine(string? value)
        {
            first.WriteLine(value);
            second.WriteLine(value);
        }

        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }
    }
}

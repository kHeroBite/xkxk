// 원격 제어 연결 정보 파일(remote-control.json) 경로를 인자·환경변수로 정하는 해석기 — 격리 기동(--data-dir)한 시험이 사용자 인스턴스 파일을 덮지 않게 한다
#if PD_DIAGNOSTICS // 진단 빌드 전용. 제품 빌드 대체는 RemoteProductStubs.cs
using System;
using System.Collections.Generic;
using System.IO;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 우선순위: --data-dir X / --data-dir=X 인자 > PD_DATA_DIR 환경변수 > 기존 %LOCALAPPDATA%\PdPoc (Android 등 기존 동작 불변).
/// 데이터 폴더가 정해지면 &lt;X&gt;\remote-control.json 을 쓴다. Configure 는 StartFromArgs 첫 줄에서 1회 호출한다.
/// </summary>
public static class RemoteInfoPath
{
    public const string FileName = "remote-control.json";
    public const string DataDirEnv = "PD_DATA_DIR";

    private static string? _dataDir;

    /// <summary>현재 연결 정보 파일 경로.</summary>
    public static string Current => Path.Combine(_dataDir ?? DefaultDir, FileName);

    private static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdPoc");

    /// <summary>인자·환경변수에서 데이터 폴더를 읽어 고정한다(없으면 기존 경로).</summary>
    public static void Configure(IReadOnlyList<string> args)
    {
        string? dir = null;
        for (var i = 0; i < args.Count && dir is null; i++)
        {
            var a = args[i];
            if (a.Equals("--data-dir", StringComparison.OrdinalIgnoreCase)) { dir = i + 1 < args.Count ? args[i + 1] : null; }
            else if (a.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase)) { dir = a["--data-dir=".Length..]; }
        }

        dir ??= Environment.GetEnvironmentVariable(DataDirEnv);
        _dataDir = string.IsNullOrWhiteSpace(dir) ? null : dir.Trim().Trim('"');
    }
}
#endif

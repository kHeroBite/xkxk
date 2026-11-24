// 제품 빌드(PdDiagnostics=false) 전용 원격 제어 껍데기 — 서버는 항상 꺼짐·라우터는 모든 요청 거부(원격 판정 훅은 진단 빌드에만 있다 — RemoteCommands.cs)
#if !PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>인자·환경변수와 관계없이 서버를 열지 않는다. 소켓·토큰·명령 코드는 제품 빌드에 없다.</summary>
public sealed class RemoteControlServer : IDisposable
{
    public static RemoteControlServer? Current => null;

    public static string InfoFilePath => "";

    public int Port => 0;

    public string Url => "";

    public static RemoteControlServer? StartFromArgs(IReadOnlyList<string> args) => null;

    public void Dispose()
    {
    }
}

/// <summary>명령이 하나도 없고 모든 요청을 거부한다(웹 브리지가 남아 있어도 아무것도 실행되지 않는다).</summary>
public sealed class RemoteCommandRouter
{
    private const string Disabled = "{\"ok\":false,\"cmd\":\"\",\"error\":\"진단 기능이 없는 제품 빌드입니다(PdDiagnostics=false).\"}";

    public static RemoteCommandRouter Default { get; } = new();

    public static Action<string> Trace { get; set; } = _ => { };

    public Task<string> InvokeJsonAsync(string requestJson) => Task.FromResult(Disabled);
}

// RemoteCommands.cs 는 제품·진단 공통 파일이다. 제품 빌드에는 기동 복원 완료 표시(StartupRestoreDone)만 있고 원격 판정 훅은 없다(W2 — partial 해소, team-lead 16:10).
#endif

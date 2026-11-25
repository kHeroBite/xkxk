// 데스크톱 호스트 시작 인자 해석 — 호스트 전용 인자(값 1개 동반)를 떼어 내고 나머지를 셸(App.StartupArgs)로 넘긴다
namespace xkxk.frameworks.ui.desktop;

/// <summary>호스트가 소비하는 인자와 셸로 넘길 인자의 분리 결과. Program.cs 에서 분리(ADR-003 — 인자 해석 책임).</summary>
internal sealed class HostArgs
{
    /// <summary>데이터 폴더 재지정 제품 인자·환경변수(설정·레이아웃·로그 전부). Q9 금지 목록과 겹치지 않는 이름.</summary>
    public const string DataDirArg = "--data-dir";
    private const string DataDirEnv = "PD_DATA_DIR";

    // 호스트가 소비하고 App.StartupArgs 로 넘기지 않는 인자(값 1개씩 동반)
    private static readonly string[] HostOnlyKeys = ["--bg-pos", "--log", "--layout-file", "--render", "--composition", "--host-cmd", "--main-titlebar", SingleInstance.WaitPidArg, DataDirArg,
#if PD_DIAGNOSTICS // 진단 전용 호스트 인자 — 제품 Desktop.dll 에 문자열을 남기지 않는다(Q9)
        "--fit-visible", "--primary-override", "--offscreen-guard", "--multi-instance", "--chrome-style", "--main-size",
#endif
    ];

    private readonly Dictionary<string, string> _host;

    private HostArgs(string[] appArgs, Dictionary<string, string> host)
    {
        AppArgs = appArgs;
        _host = host;
    }

    /// <summary>셸로 넘길 인자(호스트 전용·원격 제어 인자를 뺀 나머지).</summary>
    public string[] AppArgs { get; set; }

    /// <summary>호스트 전용 인자 값(없으면 null).</summary>
    public string? Get(string key) => _host.GetValueOrDefault(key);

    /// <summary>호스트 전용 인자 값이 "on" 인가(대소문자 무시).</summary>
    public bool IsOn(string key) => string.Equals(Get(key), "on", StringComparison.OrdinalIgnoreCase);

    /// <summary>레이아웃 파일 경로: --layout-file > (--data-dir 또는 PD_DATA_DIR)\layout.json > null(기본 위치).
    /// 데이터 폴더 재지정은 제품판 시험이 사용자 %LOCALAPPDATA%\xkxk\frameworks\ui 를 건드리지 않게 하는 수단이다(team-lead 10:2x).</summary>
    public string? LayoutFile()
    {
        var dataDir = Get(DataDirArg) is { Length: > 0 } dd ? dd : Environment.GetEnvironmentVariable(DataDirEnv);
        return Get("--layout-file") ?? (string.IsNullOrWhiteSpace(dataDir) ? null : Path.Combine(Path.GetFullPath(dataDir), "layout.json"));
    }

    public static HostArgs Parse(string[] args)
    {
        var appArgs = new List<string>();
        var host = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (Array.Exists(HostOnlyKeys, k => string.Equals(k, args[i], StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Length)
            {
                host[args[i]] = args[++i];
                continue;
            }

            // 원격 제어(--remote-control [포트] / --remote-control=포트)는 진단 부가 기능이라 셸에 넘기지 않는다.
            // 넘기면 StartupArgs.Count>0 이 되어 종료 자동 저장·기동 자동 복원이 꺼져 사용자 동작이 바뀐다. 서버는 원본 args 로 시작한다.
            // --fresh 는 셸이 소비하므로 그대로 넘긴다.
            // 제품 빌드(lead Q9 05:0x): 원격 제어가 없으므로 걸러 내지 않는다 — 셸에 넘어가도 제품 AutomationBridge 가
            // HasScenarioArgs=false·RunAsync 무동작이라 다른 알 수 없는 인자처럼 무시된다(자동 저장·복원 영향 없음).
#if PD_DIAGNOSTICS
            if (args[i].StartsWith("--remote-control=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(args[i], "--remote-control", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out _))
                {
                    i++;
                }

                continue;
            }
#endif

            appArgs.Add(args[i]);
        }

        return new HostArgs(appArgs.ToArray(), host);
    }
}

// 데이터 폴더 지정 계약 시험 C86~C94 — AppDataFolder.UseSegments 1회 래치·기본값 불변·우선순위(UADR-0018 D4, AC-301·308)를 사례별 자식 프로세스로 판정
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using xkxk.frameworks.ui.data;
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
#endif

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>
/// UseSegments 는 프로세스 정적 래치라 한 프로세스에서 사례를 이어 돌릴 수 없다. 부모(계약 시험 본체)는 사례마다
/// 자기 실행 파일을 <c>--case hosting-identity:&lt;사례&gt;</c> 로 다시 띄우고 자식의 rc·stdout 만 판정한다.
/// 자식은 사례 코드 전에 AppDataFolder.Root·ProfileStores·RemoteInfoPath 를 건드리지 않는다. 폴더를 만들거나 쓰지 않는다(경로 계산만).
/// RemoteInfoPath 사례(C91~C94)는 PD_DIAGNOSTICS 빌드에서만 들어간다(기본 계약 시험 빌드는 PdDiagnostics=false).
/// </summary>
internal static class HostingIdentityChecks
{
    public const string CaseArgPrefix = "hosting-identity:";
    private const string PassMark = "CASE-PASS ";
    private const string FailMark = "CASE-FAIL ";
    private static readonly string[] Testbed = ["xkxk", "frameworks", "ui", "testbed"];

    public static IEnumerable<ContractCase> All()
    {
        yield return Child("C86", "default", "UseSegments 미호출: Root 가 LocalApplicationData/xkxk/frameworks/ui 그대로(자식 프로세스)");
        yield return Child("C87", "apply", "Root 읽기 전 UseSegments(xkxk,frameworks,ui,testbed) 1회: Root 가 …/xkxk/frameworks/ui/testbed(자식 프로세스)");
        yield return Child("C88", "late", "Root 를 읽은 뒤 UseSegments: InvalidOperationException·Root 기본값 유지(자식 프로세스)");
        yield return Child("C89", "twice", "UseSegments 두 번째 호출: InvalidOperationException·첫 지정 유지(자식 프로세스)");
        yield return Child("C90", "invalid", "UseSegments 빈 배열·null·'..'·구분자 항목: ArgumentException, 거부는 래치를 걸지 않음(자식 프로세스)");
#if PD_DIAGNOSTICS
        yield return Child("C91", "rip-args", "RemoteInfoPath 인자만(--data-dir 임시): Current 가 인자 폴더 아래(자식 프로세스)");
        yield return Child("C92", "rip-option", "RemoteInfoPath 옵션만(UseSegments testbed): Current 가 …/ui/testbed 아래(자식 프로세스)");
        yield return Child("C93", "rip-both", "RemoteInfoPath 인자+옵션: 인자 폴더가 우선(자식 프로세스)");
        yield return Child("C94", "rip-none", "RemoteInfoPath 인자·옵션 없음: Current 가 기본 …/xkxk/frameworks/ui 아래(자식 프로세스)");
#endif
    }

    /// <summary>자식 프로세스 진입 — Program.Main 이 --case 를 받으면 다른 일보다 먼저 부른다. rc 0=PASS, 1=FAIL, 2=모르는 사례.</summary>
    public static int RunChild(string name)
    {
        var body = Resolve(name);
        if (body is null)
        {
            Console.WriteLine($"{FailMark}{name} — 모르는 사례");
            return 2;
        }

        try
        {
            body();
            Console.WriteLine($"{PassMark}{name}");
            return 0;
        }
        catch (Exception ex)
        {
            var detail = ex is CheckFailedException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"{FailMark}{name} — {detail}");
            return 1;
        }
    }

    private static Action? Resolve(string name) => name switch
    {
        "default" => CaseDefault,
        "apply" => CaseApply,
        "late" => CaseLate,
        "twice" => CaseTwice,
        "invalid" => CaseInvalid,
#if PD_DIAGNOSTICS
        "rip-args" => CaseRipArgs,
        "rip-option" => CaseRipOption,
        "rip-both" => CaseRipBoth,
        "rip-none" => CaseRipNone,
#endif
        _ => null,
    };

    private static ContractCase Child(string id, string name, string description) =>
        new(id, description, () => RunInChild(name));

    /// <summary>자기 실행 파일을 --case 로 다시 띄워 rc 0 과 stdout 의 CASE-PASS 표식을 함께 요구한다.</summary>
    private static void RunInChild(string name)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true,
        };
        var self = Environment.ProcessPath ?? throw new CheckFailedException("Environment.ProcessPath 가 없어 자식 프로세스를 띄울 수 없다");
        var exe = Path.GetFileNameWithoutExtension(self);
        psi.FileName = self;
        if (string.Equals(exe, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add(typeof(HostingIdentityChecks).Assembly.Location); // dotnet <dll> 로 돌 때
        }

        psi.ArgumentList.Add("--case");
        psi.ArgumentList.Add(CaseArgPrefix + name);
        psi.Environment.Remove("PD_DATA_DIR"); // 부모 환경의 데이터 폴더 지정이 사례에 섞이지 않게 한다

        using var p = Process.Start(psi) ?? throw new CheckFailedException("자식 프로세스 시작 실패");
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(60_000))
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            throw new CheckFailedException($"자식 프로세스 60초 초과: {name}");
        }

        var stderr = stderrTask.Result;
        var passLine = $"{PassMark}{name}";
        Check.True(p.ExitCode == 0 && stdout.Contains(passLine, StringComparison.Ordinal),
            $"자식 rc={p.ExitCode} — {Last(stdout)}{(stderr.Length > 0 ? " | stderr: " + Last(stderr) : "")}");
    }

    private static string Last(string text)
    {
        var t = text.Trim();
        var i = t.LastIndexOf('\n');
        return i < 0 ? t : t[(i + 1)..].Trim();
    }

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string DefaultRoot => Path.Combine(Local, "xkxk", "frameworks", "ui");

    private static string TestbedRoot => Path.Combine(Local, "xkxk", "frameworks", "ui", "testbed");

    private static void Same(string actual, string expected, string what) =>
        Check.True(string.Equals(actual, expected, StringComparison.Ordinal), $"{what}: '{actual}' ≠ 기대 '{expected}'");

    private static void CaseDefault()
    {
        Same(AppDataFolder.Root, DefaultRoot, "미호출 Root");
        Same(AppDataFolder.Root, DefaultRoot, "두 번째 읽기 Root");
    }

    private static void CaseApply()
    {
        AppDataFolder.UseSegments(Testbed);
        Same(AppDataFolder.Root, TestbedRoot, "UseSegments 뒤 Root");
    }

    private static void CaseLate()
    {
        Same(AppDataFolder.Root, DefaultRoot, "먼저 읽은 Root");
        Check.Throws<InvalidOperationException>(() => AppDataFolder.UseSegments(Testbed), "Root 읽은 뒤 UseSegments 가 InvalidOperationException 이 아님");
        Same(AppDataFolder.Root, DefaultRoot, "늦은 호출 뒤 Root");
    }

    private static void CaseTwice()
    {
        AppDataFolder.UseSegments(Testbed);
        Check.Throws<InvalidOperationException>(() => AppDataFolder.UseSegments("xkxk", "other"), "두 번째 UseSegments 가 InvalidOperationException 이 아님");
        Same(AppDataFolder.Root, TestbedRoot, "두 번째 호출 뒤 Root");
    }

    private static void CaseInvalid()
    {
        Check.Throws<ArgumentException>(() => AppDataFolder.UseSegments(), "빈 배열이 ArgumentException 이 아님");
        Check.Throws<ArgumentException>(() => AppDataFolder.UseSegments(Array.Empty<string>()), "빈 배열(명시)이 ArgumentException 이 아님");
        Check.Throws<ArgumentNullException>(() => AppDataFolder.UseSegments(null!), "null 이 ArgumentNullException 이 아님");
        Check.Throws<ArgumentException>(() => AppDataFolder.UseSegments("xkxk", ".."), "'..' 항목이 ArgumentException 이 아님");
        Check.Throws<ArgumentException>(() => AppDataFolder.UseSegments("xkxk/ui"), "구분자 항목이 ArgumentException 이 아님");
        Check.Throws<ArgumentException>(() => AppDataFolder.UseSegments("xkxk", " "), "빈 값 항목이 ArgumentException 이 아님");
        AppDataFolder.UseSegments(Testbed); // 거부된 호출은 래치를 걸지 않는다
        Same(AppDataFolder.Root, TestbedRoot, "거부 뒤 정상 호출 Root");
    }

#if PD_DIAGNOSTICS
    private static string TempDataDir => Path.Combine(Path.GetTempPath(), "xkxk-contract-c9x"); // 경로 계산만, 만들지 않는다

    private static void CaseRipArgs()
    {
        RemoteInfoPath.Configure(["--data-dir", TempDataDir]);
        Same(RemoteInfoPath.Current, Path.Combine(TempDataDir, RemoteInfoPath.FileName), "인자만 Current");
    }

    private static void CaseRipOption()
    {
        AppDataFolder.UseSegments(Testbed);
        RemoteInfoPath.Configure(Array.Empty<string>());
        Same(RemoteInfoPath.Current, Path.Combine(TestbedRoot, RemoteInfoPath.FileName), "옵션만 Current");
    }

    private static void CaseRipBoth()
    {
        AppDataFolder.UseSegments(Testbed);
        RemoteInfoPath.Configure(["--data-dir=" + TempDataDir]);
        Same(RemoteInfoPath.Current, Path.Combine(TempDataDir, RemoteInfoPath.FileName), "인자+옵션 Current(인자 우선)");
    }

    private static void CaseRipNone()
    {
        RemoteInfoPath.Configure(Array.Empty<string>());
        Same(RemoteInfoPath.Current, Path.Combine(DefaultRoot, RemoteInfoPath.FileName), "없음 Current");
    }
#endif
}

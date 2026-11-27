// 공개 계약 시험 진입점 — C01~C81 을 돌려 PASS/FAIL 을 찍고 --out JSON 을 쓴다(--canary 는 일부러 실패 1건 추가)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>검사 실패를 알리는 예외(Check.True 가 던진다).</summary>
internal sealed class CheckFailedException : Exception
{
    public CheckFailedException(string message)
        : base(message)
    {
    }
}

/// <summary>검사 도우미 — 조건이 거짓이면 CheckFailedException.</summary>
internal static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new CheckFailedException(message);
        }
    }

    public static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new CheckFailedException($"{message} — 다른 예외 {ex.GetType().Name}: {ex.Message}");
        }

        throw new CheckFailedException($"{message} — 예외가 나지 않음");
    }
}

/// <summary>검사 1건(ID·설명·본문).</summary>
internal sealed record ContractCase(string Id, string Description, Action Body);

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? outPath = null;
        var canary = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--canary":
                    canary = true;
                    break;
                case "--help":
                    Console.WriteLine("사용: ContractCheck [--out <json>] [--canary]");
                    Console.WriteLine("  --out    결과 JSON 경로(기본 <영역 루트>/logs/contract_check.json)");
                    Console.WriteLine("  --canary 일부러 실패하는 검사 1건을 더한다(rc=1 이어야 정상)");
                    return 0;
                default:
                    Console.Error.WriteLine($"알 수 없는 인자: {args[i]}");
                    return 2;
            }
        }

        var cases = new List<ContractCase>();
        cases.AddRange(BuilderChecks.All());
        cases.AddRange(ServiceChecks.All());
        cases.AddRange(DiagnosticsChecks.All());
        cases.AddRange(HostOptionsChecks.All()); // C22·C23 은 App 정적 상태를 쓰므로 맨 뒤(각 시험이 원복한다)
        cases.AddRange(ContextServiceChecks.All()); // C28~C32 — 컨텍스트 서비스(B05): 투어 소스·모니터 공급자·App 정적 값을 바꾼 뒤 원복한다
        cases.AddRange(IconRegistryChecks.All());
        cases.AddRange(CommandRegistryChecks.All());
        cases.AddRange(MenuRegistryChecks.All()); // C46~C53·C60·C61 — 메뉴 레지스트리(B07): 시험마다 고유 ID, C53 은 셸 기본 메뉴를 교체해 상태를 남기므로 맨 뒤
        cases.AddRange(MenuRuleChecks.All()); // C54~C59·C62 — 메뉴 규칙: 셸 항목을 숨기면 원복하고, 단축키 충돌 시험 명령은 지운다
        cases.AddRange(LayoutMigrationChecks.All()); // C63~C67 — 저장 형식 6(B09): test/common/testdata 고정 표본을 읽기만 하고 앱 상태를 바꾸지 않는다
        cases.AddRange(WindowRegistryChecks.All()); // C68~C73 — 창 레지스트리(B09): 시험마다 고유 ID, C73 만 셸 기본 grid 를 교체해 상태를 남기므로 맨 뒤
        cases.AddRange(WindowContextChecks.All()); // C74~C81 — 순서 의존: C76→C77(창 저장소는 프로세스 전역·Remove 는 소유 모듈 전용이라 원복 불가 — C76 을 맨 앞에 돌려 빈 상태를 본다), C68·C69·C73 은 EnsureDemoWindows 로 데모 창 등록
        cases.AddRange(AppContextChecks.All()); // C24~C27 — XkxkUi.Current 는 프로세스 전역·리셋 없음: C25 는 언어를 원복, C26 은 화면 등록 상태를 남기는 마지막 동작 시험(C27 은 리플렉션 구조 검사만)
        // 실행 순서: C01~C23 → C28~C72(C53 제외) → C24~C27(C26 이 상태를 남긴다) → C53(셸 메뉴 Owner 를 바꾸므로 C47·C60 뒤) → C73(셸 창 Owner 를 바꾸므로 C69 뒤 맨 끝)
        cases.Sort((a, b) =>
        {
            var rank = RunLast(a.Id).CompareTo(RunLast(b.Id));
            return rank != 0 ? rank : string.CompareOrdinal(a.Id, b.Id);
        });
        if (canary)
        {
            cases.Add(new ContractCase("C99", "카나리아 — 시험 장치가 실패를 보고하는지 확인(항상 실패)",
                () => Check.True(false, "카나리아는 항상 실패한다")));
        }

        var results = new List<(ContractCase Case, bool Pass, string Detail)>();
        foreach (var item in cases)
        {
            try
            {
                item.Body();
                results.Add((item, true, ""));
                Console.WriteLine($"PASS {item.Id} {item.Description}");
            }
            catch (Exception ex)
            {
                var detail = ex is CheckFailedException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                results.Add((item, false, detail));
                Console.WriteLine($"FAIL {item.Id} {item.Description} — {detail}");
            }
        }

        var passed = results.FindAll(r => r.Pass).Count;
        var failed = results.Count - passed;
        Console.WriteLine($"요약: 전체 {results.Count} · PASS {passed} · FAIL {failed}");

        var target = outPath ?? Path.Combine(FindAreaRoot(), "logs", "contract_check.json");
        WriteJson(target, passed, failed, results);
        Console.WriteLine($"결과 파일: {target}");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>실행 순위 — 셸 기본 메뉴를 교체하는 C53·셸 기본 창을 교체하는 C73 은 2(맨 끝), 앱 컨텍스트 묶음(C24~C27)은 1, 나머지 0.</summary>
    private static int RunLast(string id) =>
        id == "C76" ? -1 : id is "C53" or "C73" ? 2 : string.CompareOrdinal(id, "C24") >= 0 && string.CompareOrdinal(id, "C27") <= 0 ? 1 : 0;

    private static void WriteJson(string path, int passed, int failed, List<(ContractCase Case, bool Pass, string Detail)> results)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("passed", passed);
        writer.WriteNumber("failed", failed);
        writer.WriteString("generated_at", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        writer.WriteStartArray("checks");
        foreach (var r in results)
        {
            writer.WriteStartObject();
            writer.WriteString("id", r.Case.Id);
            writer.WriteString("result", r.Pass ? "PASS" : "FAIL");
            writer.WriteString("desc", r.Case.Description);
            if (!r.Pass)
            {
                writer.WriteString("detail", r.Detail);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>실행 파일 위치에서 위로 올라가 AGENTS.md·src 가 함께 있는 영역 루트를 찾는다. 없으면 현재 폴더.</summary>
    private static string FindAreaRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

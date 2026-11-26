// 빌더·옵션·모듈 계약 시험 C01~C10·C13 — 기본값, 등록 순서 구성, 진단 규칙, 오류 정책, 컨텍스트 동일성을 본다
using System;
using System.Collections.Generic;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>Configure 호출을 기록하는 시험 모듈(동작은 위임으로 바꾼다).</summary>
internal sealed class RecordingModule : UiModule
{
    private readonly string _id;
    private readonly List<string> _log;
    private readonly Action<IUiModuleContext>? _onConfigure;

    public RecordingModule(string id, List<string> log, Action<IUiModuleContext>? onConfigure = null)
    {
        _id = id;
        _log = log;
        _onConfigure = onConfigure;
    }

    public override string Id => _id;

    public override void Configure(IUiModuleContext context)
    {
        _log.Add(_id);
        _onConfigure?.Invoke(context);
    }
}

/// <summary>AddModule&lt;T&gt;() 용 매개변수 없는 시험 모듈.</summary>
internal sealed class DefaultCtorModule : UiModule
{
    public static int ConfigureCount;

    public override string Id => "test.default-ctor";

    public override void Configure(IUiModuleContext context)
    {
        ConfigureCount++;
    }
}

internal static class BuilderChecks
{
    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C01", "기본 옵션(Args 빈 목록·HostKind unknown·ErrorPolicy Collect·Properties Ordinal)", C01);
        yield return new ContractCase("C02", "CreateBuilder(string[]) 가 배열을 복사한다", C02);
        yield return new ContractCase("C03", "CreateBuilder(options) 는 같은 인스턴스, null 은 ArgumentNullException", C03);
        yield return new ContractCase("C04", "Build 가 등록 순서대로 모듈 Configure 를 1회씩 부른다", C04);
        yield return new ContractCase("C05", "AddModule<T>()(UiModule 파생) 등록·구성", C05);
        yield return new ContractCase("C06", "중복 Id → XKUI0002 Error·뒤 모듈 미구성·Collect 면 예외 없음", C06);
        yield return new ContractCase("C07", "빈 Id → XKUI0001 Error·건너뜀", C07);
        yield return new ContractCase("C08", "Configure 예외 → XKUI0003 Error·다음 모듈 계속", C08);
        yield return new ContractCase("C09", "ErrorPolicy Throw → UiConfigurationException(Diagnostics 포함)", C09);
        yield return new ContractCase("C10", "Build 두 번 → InvalidOperationException", C10);
        yield return new ContractCase("C13", "컨텍스트 ModuleId=모듈 Id, Options·Services·Diagnostics 가 빌더와 같은 인스턴스", C13);
    }

    private static void C01()
    {
        var o = new XkxkUiBuilderOptions();
        Check.True(o.Args is not null && o.Args.Count == 0, "Args 기본값이 빈 목록이 아님");
        Check.True(o.HostKind == UiHostKinds.Unknown && UiHostKinds.Unknown == "unknown", $"HostKind 기본값={o.HostKind}");
        Check.True(o.ErrorPolicy == UiErrorPolicy.Collect, $"ErrorPolicy 기본값={o.ErrorPolicy}");
        Check.True(o.ApplicationId is null, "ApplicationId 기본값이 null 이 아님");
        o.Args = null!;
        Check.True(o.Args is not null && o.Args.Count == 0, "Args 에 null 대입 → 빈 목록이 아님");
        o.HostKind = "  ";
        Check.True(o.HostKind == UiHostKinds.Unknown, "HostKind 공백 대입 → unknown 이 아님");
        o.Properties["Key"] = 1;
        Check.True(!o.Properties.ContainsKey("key"), "Properties 가 Ordinal 비교가 아님(대소문자 무시)");
        var b = XkxkUi.CreateBuilder();
        Check.True(b.Options.Args.Count == 0 && b.Options.HostKind == UiHostKinds.Unknown, "CreateBuilder() 옵션이 기본값이 아님");
    }

    private static void C02()
    {
        var arr = new[] { "--a", "--b" };
        var b = XkxkUi.CreateBuilder(arr);
        arr[0] = "--changed";
        Check.True(b.Options.Args.Count == 2, $"Args 개수={b.Options.Args.Count}");
        Check.True(b.Options.Args[0] == "--a" && b.Options.Args[1] == "--b", "원본 배열 변경이 Args 에 비침(복사 아님)");
        Check.Throws<ArgumentNullException>(() => XkxkUi.CreateBuilder((string[])null!), "CreateBuilder((string[])null)");
    }

    private static void C03()
    {
        var o = new XkxkUiBuilderOptions { HostKind = UiHostKinds.Desktop };
        var b = XkxkUi.CreateBuilder(o);
        Check.True(ReferenceEquals(b.Options, o), "Options 가 넘긴 인스턴스와 다름");
        Check.Throws<ArgumentNullException>(() => XkxkUi.CreateBuilder((XkxkUiBuilderOptions)null!), "CreateBuilder((XkxkUiBuilderOptions)null)");
    }

    private static void C04()
    {
        var log = new List<string>();
        var b = XkxkUi.CreateBuilder();
        b.AddModule(new RecordingModule("m.c", log)).AddModule(new RecordingModule("m.a", log)).AddModule(new RecordingModule("m.b", log));
        Check.True(log.Count == 0, "Build 전에 Configure 가 불림");
        var app = b.Build();
        Check.True(string.Join(",", log) == "m.c,m.a,m.b", $"구성 순서={string.Join(",", log)}");
        Check.True(string.Join(",", app.ModuleIds) == "m.c,m.a,m.b", $"ModuleIds={string.Join(",", app.ModuleIds)}");
        Check.True(app.Diagnostics.Entries.Count == 0, $"진단 {app.Diagnostics.Entries.Count}건");
        Check.True(!app.IsAttached, "UseXkxkUi 전에 IsAttached=true");
    }

    private static void C05()
    {
        DefaultCtorModule.ConfigureCount = 0;
        var app = XkxkUi.CreateBuilder().AddModule<DefaultCtorModule>().Build();
        Check.True(DefaultCtorModule.ConfigureCount == 1, $"Configure 횟수={DefaultCtorModule.ConfigureCount}");
        Check.True(app.ModuleIds.Count == 1 && app.ModuleIds[0] == "test.default-ctor", "ModuleIds 에 제네릭 등록 모듈 없음");
    }

    private static void C06()
    {
        var log = new List<string>();
        var b = XkxkUi.CreateBuilder();
        b.AddModule(new RecordingModule("dup", log)).AddModule(new RecordingModule("dup", log)).AddModule(new RecordingModule("DUP", log));
        var app = b.Build();
        Check.True(log.Count == 2, $"Configure 횟수={log.Count}(첫 dup·DUP 만이어야 함)");
        var e = app.Diagnostics.Entries;
        Check.True(e.Count == 1 && e[0].Id == UiDiagnosticIds.ModuleDuplicate && UiDiagnosticIds.ModuleDuplicate == "XKUI0002", "XKUI0002 1건이 아님");
        Check.True(e[0].Severity == UiDiagnosticSeverity.Error && app.Diagnostics.HasErrors, "XKUI0002 가 Error 가 아님");
        Check.True(string.Join(",", app.ModuleIds) == "dup,DUP", $"ModuleIds={string.Join(",", app.ModuleIds)}");
    }

    private static void C07()
    {
        var log = new List<string>();
        var app = XkxkUi.CreateBuilder().AddModule(new RecordingModule(" ", log)).AddModule(new RecordingModule("ok", log)).Build();
        Check.True(string.Join(",", log) == "ok", $"구성={string.Join(",", log)}");
        var e = app.Diagnostics.Entries;
        Check.True(e.Count == 1 && e[0].Id == "XKUI0001" && e[0].Id == UiDiagnosticIds.ModuleIdEmpty, "XKUI0001 1건이 아님");
        Check.True(e[0].Severity == UiDiagnosticSeverity.Error, "XKUI0001 이 Error 가 아님");
    }

    private static void C08()
    {
        var log = new List<string>();
        var app = XkxkUi.CreateBuilder()
            .AddModule(new RecordingModule("boom", log, _ => throw new FormatException("일부러")))
            .AddModule(new RecordingModule("next", log))
            .Build();
        Check.True(string.Join(",", log) == "boom,next", $"구성={string.Join(",", log)}");
        var e = app.Diagnostics.Entries;
        Check.True(e.Count == 1 && e[0].Id == "XKUI0003" && e[0].Id == UiDiagnosticIds.ModuleConfigureFailed, "XKUI0003 1건이 아님");
        Check.True(e[0].Severity == UiDiagnosticSeverity.Error, "XKUI0003 이 Error 가 아님");
        Check.True(e[0].Message.Contains("FormatException") && e[0].Message.Contains("일부러"), $"메시지에 예외 형식·메시지 없음: {e[0].Message}");
        Check.True(string.Join(",", app.ModuleIds) == "next", $"ModuleIds={string.Join(",", app.ModuleIds)}");
    }

    private static void C09()
    {
        var log = new List<string>();
        var b = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions { ErrorPolicy = UiErrorPolicy.Throw });
        b.AddModule(new RecordingModule("", log));
        UiConfigurationException? caught = null;
        try
        {
            b.Build();
        }
        catch (UiConfigurationException ex)
        {
            caught = ex;
        }

        Check.True(caught is not null, "UiConfigurationException 이 나지 않음");
        Check.True(caught!.Diagnostics.Count == 1 && caught.Diagnostics[0].Id == UiDiagnosticIds.ModuleIdEmpty, "예외 Diagnostics 에 XKUI0001 없음");
        var ok = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions { ErrorPolicy = UiErrorPolicy.Throw }).Build();
        Check.True(!ok.Diagnostics.HasErrors, "오류 없는 Throw 빌드에 오류가 있음");
    }

    private static void C10()
    {
        var b = XkxkUi.CreateBuilder();
        b.Build();
        Check.Throws<InvalidOperationException>(() => b.Build(), "두 번째 Build");
    }

    private static void C13()
    {
        IUiModuleContext? seen = null;
        var b = XkxkUi.CreateBuilder();
        b.AddModule(new RecordingModule("ctx.module", new List<string>(), c => seen = c));
        b.Build();
        Check.True(seen is not null, "Configure 에 컨텍스트가 오지 않음");
        Check.True(seen!.ModuleId == "ctx.module", $"ModuleId={seen.ModuleId}");
        Check.True(ReferenceEquals(seen.Options, b.Options), "컨텍스트 Options 가 빌더와 다름");
        Check.True(ReferenceEquals(seen.Services, b.Services), "컨텍스트 Services 가 빌더와 다름");
        Check.True(ReferenceEquals(seen.Diagnostics, b.Diagnostics), "컨텍스트 Diagnostics 가 빌더와 다름");
    }
}

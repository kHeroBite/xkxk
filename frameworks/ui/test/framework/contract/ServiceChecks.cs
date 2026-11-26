// 서비스 등록부 계약 시험 C11·C12 — Add/Get/TryGet/Contains, 팩토리 지연·1회, 중복 XKUI0101, Replace·Remove 를 본다
using System;
using System.Collections.Generic;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>시험용 서비스 계약.</summary>
internal interface ISampleService
{
    string Name { get; }
}

/// <summary>시험용 서비스 구현.</summary>
internal sealed class SampleService : ISampleService
{
    public SampleService(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

/// <summary>등록하지 않는 서비스 형식(없는 서비스 조회 확인용).</summary>
internal sealed class MissingService
{
}

internal static class ServiceChecks
{
    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C11", "서비스 Add/Get/TryGet/Contains, 팩토리는 첫 조회 때 1회만, 없는 서비스 Get 은 InvalidOperationException", C11);
        yield return new ContractCase("C12", "중복 Add → XKUI0101 Warning·첫 등록 유지, Replace·Remove", C12);
    }

    private static void C11()
    {
        var reg = new UiServiceRegistry();
        Check.True(reg.Count == 0, "새 등록부 Count≠0");
        var first = new SampleService("instance");
        reg.Add<ISampleService>(first);
        Check.True(reg.Contains<ISampleService>() && reg.Count == 1, "Add 뒤 Contains·Count 불일치");
        Check.True(ReferenceEquals(reg.Get<ISampleService>(), first), "Get 이 등록 인스턴스가 아님");
        Check.True(reg.TryGet<ISampleService>(out var got) && ReferenceEquals(got, first), "TryGet 실패");
        Check.True(!reg.Contains<MissingService>(), "등록 안 한 서비스가 Contains=true");
        Check.True(!reg.TryGet<MissingService>(out var none) && none is null, "없는 서비스 TryGet=true");
        Check.Throws<InvalidOperationException>(() => reg.Get<MissingService>(), "없는 서비스 Get");

        var calls = 0;
        IUiServiceProvider? passed = null;
        var lazy = new UiServiceRegistry();
        lazy.Add<SampleService>(p =>
        {
            calls++;
            passed = p;
            return new SampleService("factory");
        });
        Check.True(calls == 0, "팩토리가 등록 시점에 불림(지연 아님)");
        var a = lazy.Get<SampleService>();
        Check.True(lazy.TryGet<SampleService>(out var b) && ReferenceEquals(a, b), "두 번째 조회가 다른 인스턴스");
        Check.True(ReferenceEquals(lazy.Get<SampleService>(), a), "세 번째 조회가 다른 인스턴스");
        Check.True(calls == 1, $"팩토리 호출 {calls}회(1회여야 함)");
        Check.True(ReferenceEquals(passed, lazy), "팩토리에 등록부 자신이 넘어오지 않음");

        var nullFactory = new UiServiceRegistry();
        nullFactory.Add<SampleService>(_ => null!);
        Check.Throws<InvalidOperationException>(() => nullFactory.Get<SampleService>(), "팩토리 null 반환 Get");

        var viaBuilder = XkxkUi.CreateBuilder().ConfigureServices(s => s.Add<ISampleService>(new SampleService("app"))).Build();
        Check.True(viaBuilder.Services.Get<ISampleService>().Name == "app", "Build 뒤 앱 Services 에서 서비스를 못 찾음");
    }

    private static void C12()
    {
        var diags = new UiDiagnostics();
        var reg = new UiServiceRegistry(diags);
        var first = new SampleService("first");
        reg.Add<ISampleService>(first);
        reg.Add<ISampleService>(new SampleService("second"));
        Check.True(ReferenceEquals(reg.Get<ISampleService>(), first), "중복 Add 뒤 첫 등록이 유지되지 않음");
        Check.True(reg.Count == 1, $"중복 Add 뒤 Count={reg.Count}");
        var e = diags.Entries;
        Check.True(e.Count == 1 && e[0].Id == "XKUI0101" && e[0].Id == UiDiagnosticIds.ServiceDuplicate, "XKUI0101 1건이 아님");
        Check.True(e[0].Severity == UiDiagnosticSeverity.Warning && !diags.HasErrors, "XKUI0101 이 Warning 이 아니거나 HasErrors=true");

        var replaced = new SampleService("replaced");
        reg.Replace<ISampleService>(replaced);
        Check.True(ReferenceEquals(reg.Get<ISampleService>(), replaced), "Replace(인스턴스) 미반영");
        reg.Replace<ISampleService>(_ => new SampleService("factory-replaced"));
        Check.True(reg.Get<ISampleService>().Name == "factory-replaced", "Replace(팩토리) 미반영");
        Check.True(diags.Entries.Count == 1, "Replace 가 중복 진단을 냄");
        Check.True(reg.Remove<ISampleService>() && !reg.Contains<ISampleService>() && reg.Count == 0, "Remove 실패");
        Check.True(!reg.Remove<ISampleService>(), "없는 서비스 Remove=true");

        var builder = XkxkUi.CreateBuilder();
        builder.Services.Add<ISampleService>(first);
        builder.Services.Add<ISampleService>(first);
        Check.True(builder.Diagnostics.Entries.Count == 1 && builder.Diagnostics.Entries[0].Id == UiDiagnosticIds.ServiceDuplicate,
            "빌더 Services 의 중복 진단이 빌더 Diagnostics 로 가지 않음");
    }
}

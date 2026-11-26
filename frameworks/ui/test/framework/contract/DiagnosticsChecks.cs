// 진단·공개 표면 계약 시험 C14~C18 — 정의 밖 열거값 안전 처리, Reported 이벤트, 선택 파라미터 0, IUiModule 동결, 생성자 비공개를 본다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class DiagnosticsChecks
{
    private const string FrameworkNamespace = "xkxk.frameworks.ui.shell.Framework";

    // 공개 표면 읽기 전용 플래그 — NonPublic 은 쓰지 않는다(공개 API 만 시험한다)
    private const BindingFlags PublicMembers = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C14", "정의 밖 열거값 — (UiDiagnosticSeverity)99 → Warning, (UiErrorPolicy)99 + Error 진단 → Build 예외 없음", C14);
        yield return new ContractCase("C15", "UiDiagnostics.Reported 이벤트·Entries 순서·HasErrors", C15);
        yield return new ContractCase("C16", "Framework 네임스페이스 공개 메서드·생성자에 선택 파라미터(IsOptional) 0건", C16);
        yield return new ContractCase("C17", "IUiModule 동결(get_Id·Configure, 속성 Id 1, 이벤트 0)·AddModule 인자는 UiModule", C17);
        yield return new ContractCase("C18", "XkxkUiBuilder·XkxkUiApplication public 인스턴스 생성자 0개", C18);
    }

    private static void C14()
    {
        var d = new UiDiagnostic("TEST0001", (UiDiagnosticSeverity)99, "정의 밖 심각도");
        Check.True(d.Severity == UiDiagnosticSeverity.Warning, $"정의 밖 심각도가 {d.Severity} 로 정규화됨");
        var b = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions { ErrorPolicy = (UiErrorPolicy)99 });
        b.AddModule(new RecordingModule("", new List<string>()));
        XkxkUiApplication? app = null;
        try
        {
            app = b.Build();
        }
        catch (Exception ex)
        {
            Check.True(false, $"정의 밖 ErrorPolicy 에서 Build 예외: {ex.GetType().Name}");
        }

        Check.True(app is not null && app.Diagnostics.HasErrors, "Error 진단이 남지 않음");
    }

    private static void C15()
    {
        var diags = new UiDiagnostics();
        var seen = new List<(object? Sender, UiDiagnostic Diagnostic)>();
        diags.Reported += (s, e) => seen.Add((s, e));
        diags.Report("TEST0010", UiDiagnosticSeverity.Info, "정보");
        Check.True(!diags.HasErrors, "Info 뒤 HasErrors=true");
        var warning = new UiDiagnostic("TEST0011", UiDiagnosticSeverity.Warning, "경고", "출처");
        diags.Report(warning);
        diags.Report("TEST0012", UiDiagnosticSeverity.Error, "오류");
        Check.True(diags.HasErrors, "Error 뒤 HasErrors=false");
        Check.True(seen.Count == 3, $"Reported 호출 {seen.Count}회(3회여야 함)");
        Check.True(seen.All(x => ReferenceEquals(x.Sender, diags)), "Reported sender 가 진단 모음이 아님");
        Check.True(ReferenceEquals(seen[1].Diagnostic, warning), "Report(UiDiagnostic) 의 인스턴스가 그대로 전달되지 않음");
        var ids = string.Join(",", diags.Entries.Select(x => x.Id));
        Check.True(ids == "TEST0010,TEST0011,TEST0012", $"Entries 순서={ids}");
        Check.True(warning.Source == "출처" && warning.ToString().Contains("TEST0011"), $"ToString={warning}");
        Check.Throws<ArgumentNullException>(() => diags.Report((UiDiagnostic)null!), "Report(null)");
        Check.Throws<ArgumentNullException>(() => _ = new UiDiagnostic(null!, UiDiagnosticSeverity.Info, "m"), "UiDiagnostic(null id)");
    }

    private static void C16()
    {
        var types = typeof(XkxkUi).Assembly.GetExportedTypes().Where(t => t.Namespace == FrameworkNamespace).ToList();
        Check.True(types.Count >= 17, $"Framework 공개 형식 {types.Count}개 — 검사 대상이 비어 공허한 PASS 위험");
        var offenders = new List<string>();
        var members = 0;
        foreach (var t in types)
        {
            foreach (var m in t.GetMethods(PublicMembers))
            {
                members++;
                Collect(offenders, t, m);
            }

            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                members++;
                Collect(offenders, t, c);
            }
        }

        Check.True(members > 0, "검사한 공개 멤버가 0개");

        // 양성 대조 — 선택 파라미터가 있는 시험 모듈 생성자(onConfigure = null)를 검출기가 실제로 잡는지 본다(죽은 검사 방지)
        var probe = new List<string>();
        foreach (var c in typeof(RecordingModule).GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            Collect(probe, typeof(RecordingModule), c);
        }

        Check.True(probe.Count == 1, $"양성 대조 검출 {probe.Count}건(1건이어야 함) — 검출기가 선택 파라미터를 못 잡음");
        Check.True(offenders.Count == 0, $"선택 파라미터 {offenders.Count}건: {string.Join("; ", offenders)}");
    }

    private static void Collect(List<string> offenders, Type type, MethodBase member)
    {
        foreach (var p in member.GetParameters())
        {
            if (p.IsOptional || p.HasDefaultValue)
            {
                offenders.Add($"{type.Name}.{member.Name}({p.Name})");
            }
        }
    }

    private static void C17()
    {
        var module = typeof(IUiModule);
        var methods = module.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Check.True(string.Join(",", methods) == "Configure,get_Id", $"IUiModule 공개 메서드={string.Join(",", methods)}(동결: Configure,get_Id)");
        var props = module.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Check.True(props.Length == 1 && props[0].Name == "Id" && props[0].PropertyType == typeof(string), "IUiModule 속성이 Id(string) 1개가 아님");
        Check.True(module.GetEvents(BindingFlags.Public | BindingFlags.Instance).Length == 0, "IUiModule 에 이벤트가 있음");
        var configure = module.GetMethod("Configure")!;
        var cps = configure.GetParameters();
        Check.True(cps.Length == 1 && cps[0].ParameterType == typeof(IUiModuleContext), "IUiModule.Configure 서명이 (IUiModuleContext) 가 아님");
        Check.True(typeof(UiModule).IsAbstract && module.IsAssignableFrom(typeof(UiModule)), "UiModule 이 IUiModule 을 구현하는 추상 클래스가 아님");

        var adds = typeof(XkxkUiBuilder).GetMethods(PublicMembers).Where(m => m.Name == "AddModule").ToList();
        Check.True(adds.Count == 2, $"AddModule 오버로드 {adds.Count}개(2개여야 함)");
        var plain = adds.SingleOrDefault(m => !m.IsGenericMethodDefinition);
        Check.True(plain is not null, "비제네릭 AddModule 없음");
        var pp = plain!.GetParameters();
        Check.True(pp.Length == 1 && pp[0].ParameterType == typeof(UiModule), $"비제네릭 AddModule 인자 형식={(pp.Length == 1 ? pp[0].ParameterType.Name : pp.Length.ToString())}");
        var generic = adds.SingleOrDefault(m => m.IsGenericMethodDefinition);
        Check.True(generic is not null && generic.GetParameters().Length == 0, "제네릭 AddModule<T>() 없음");
        var arg = generic!.GetGenericArguments().Single();
        Check.True(arg.GetGenericParameterConstraints().Contains(typeof(UiModule)), "AddModule<T> 제약에 UiModule 없음");
        Check.True((arg.GenericParameterAttributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0, "AddModule<T> 에 new() 제약 없음");
    }

    private static void C18()
    {
        foreach (var t in new[] { typeof(XkxkUiBuilder), typeof(XkxkUiApplication) })
        {
            var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Check.True(ctors.Length == 0, $"{t.Name} public 생성자 {ctors.Length}개(0개여야 함)");
        }
    }
}

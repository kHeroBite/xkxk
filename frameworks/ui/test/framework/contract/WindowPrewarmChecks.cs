// 창 예열 옵션 계약 시험 C82~C85 — UiWindowPrewarm 값·UiWindowOptions/UiWindowDefinition.Prewarm·등록 시 사본·공개 표면 선택 파라미터 0(B10 batch 2, red 먼저)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class WindowPrewarmChecks
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C82", "UiWindowPrewarm 공개 enum: None=0·Warm=1·KeepReady=2(값 추가 가능)·Framework 네임스페이스", C82);
        yield return new ContractCase("C83", "UiWindowOptions.Prewarm 읽기·쓰기 속성 기본 None, UiWindowDefinition.Prewarm 읽기 전용", C83);
        yield return new ContractCase("C84", "창 등록: 옵션 Prewarm 이 정의로 복사(기본 None·Warm·KeepReady)되고 등록 뒤 옵션을 바꿔도 정의는 그대로", C84);
        yield return new ContractCase("C85", "예열 추가 표면(UiWindowPrewarm·UiWindowOptions·UiWindowDefinition·IUiWindowRegistry)에 선택 파라미터 0·레지스트리 Add 오버로드 추가 0", C85);
    }

    private static string NewId(string tag) => $"c8x.prewarm.{tag}.{Guid.NewGuid():N}";

    private static bool IsOptional(ParameterInfo p) => p.IsOptional || p.HasDefaultValue;

    private static UiWindowDefinition Register(string id, Action<UiWindowOptions> configure)
    {
        var reg = XkxkUi.Current.Windows;
        Check.True(reg.Add(id, configure), $"창 등록 실패: {id}");
        Check.True(reg.TryResolve(id, out var def), $"등록한 창을 찾지 못함: {id}");
        return def;
    }

    private static void C82()
    {
        var t = typeof(UiWindowPrewarm);
        Check.True(t.IsPublic && t.IsEnum && t.Namespace == typeof(XkxkUi).Namespace, "UiWindowPrewarm 이 Framework 공개 enum 이 아님");
        Check.True((int)UiWindowPrewarm.None == 0 && (int)UiWindowPrewarm.Warm == 1 && (int)UiWindowPrewarm.KeepReady == 2, "값: None=0·Warm=1·KeepReady=2 기대");
        var names = Enum.GetNames(t);
        foreach (var n in new[] { "None", "Warm", "KeepReady" })
        {
            Check.True(names.Contains(n), $"UiWindowPrewarm.{n} 이 없음");
        }

        Check.True(default(UiWindowPrewarm) == UiWindowPrewarm.None, "default(UiWindowPrewarm) 가 None 이 아님");
    }

    private static void C83()
    {
        var opt = typeof(UiWindowOptions).GetProperty("Prewarm");
        Check.True(opt?.PropertyType == typeof(UiWindowPrewarm) && opt.GetMethod is { IsPublic: true } && opt.SetMethod is { IsPublic: true }, "UiWindowOptions.Prewarm 이 공개 읽기·쓰기 UiWindowPrewarm 이 아님");
        var def = typeof(UiWindowDefinition).GetProperty("Prewarm");
        Check.True(def?.PropertyType == typeof(UiWindowPrewarm) && def.GetMethod is { IsPublic: true } && def.SetMethod is not { IsPublic: true }, "UiWindowDefinition.Prewarm 이 공개 읽기 전용 UiWindowPrewarm 이 아님");

        var probe = Register(NewId("default"), o => o.TitleKey = "shell.kind.grid");
        Check.True(probe.Prewarm == UiWindowPrewarm.None, $"Prewarm 을 지정하지 않은 창의 기본값이 {probe.Prewarm}(None 기대)");
    }

    private static void C84()
    {
        UiWindowOptions? kept = null;
        var warm = Register(NewId("warm"), o => { o.Prewarm = UiWindowPrewarm.Warm; kept = o; });
        Check.True(warm.Prewarm == UiWindowPrewarm.Warm, $"Warm 등록 정의의 Prewarm 이 {warm.Prewarm}");
        var ready = Register(NewId("ready"), o => o.Prewarm = UiWindowPrewarm.KeepReady);
        Check.True(ready.Prewarm == UiWindowPrewarm.KeepReady, $"KeepReady 등록 정의의 Prewarm 이 {ready.Prewarm}");
        Check.True(kept is not null, "configure 대리자가 호출되지 않음");
        kept!.Prewarm = UiWindowPrewarm.KeepReady; // 등록 뒤 옵션 변경은 정의에 닿지 않아야 한다(정의는 사본)
        Check.True(warm.Prewarm == UiWindowPrewarm.Warm, "등록 뒤 옵션을 바꿨더니 정의 Prewarm 이 따라 바뀜(사본이 아님)");
    }

    private static void C85()
    {
        // UADR-0009 바이너리 호환 원칙: 예열은 옵션 속성·enum 으로만 열고 기본값 파라미터·새 Add 오버로드를 만들지 않는다
        var types = new[] { typeof(UiWindowPrewarm), typeof(UiWindowOptions), typeof(UiWindowDefinition), typeof(IUiWindowRegistry) };
        var optional = new List<string>();
        foreach (var t in types)
        {
            foreach (var m in t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)))
            {
                optional.AddRange(m.GetParameters().Where(IsOptional).Select(p => $"{t.Name}.{m.Name}({p.Name})"));
            }
        }

        Check.True(optional.Count == 0, $"선택 파라미터 {optional.Count}건: {string.Join("; ", optional)}");
        var withPrewarm = typeof(IUiWindowRegistry).GetMethods().Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(UiWindowPrewarm))).Select(m => m.Name).ToList();
        Check.True(withPrewarm.Count == 0, $"IUiWindowRegistry 에 UiWindowPrewarm 파라미터 메서드가 추가됨: {string.Join(", ", withPrewarm)}");
    }
}

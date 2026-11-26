// 앱 컨텍스트 계약 시험 C24~C27 — XkxkUi.Current·IUiLocalizer 위임·화면 등록 양방향 위임·공개 표면 구조를 본다(UADR-0011)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class AppContextChecks
{
    /// <summary>{0} 자리표시가 있는 기본 사전 키(common.ko.json·common.en.json).</summary>
    private const string FormatKey = "common.confirmDelete";

    /// <summary>모든 언어 사전에 있는 키(자기 언어 이름).</summary>
    private const string PlainKey = "lang.self";

    /// <summary>언어 변경 1회의 시간 제한 — 앱 없는 콘솔에서 Dispatcher 경로가 멈추면 실패로 드러낸다.</summary>
    private const int LanguageTimeoutMs = 5000;

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C24", "XkxkUi.Current 비 null·같은 인스턴스·구현 형식 비공개·Localization 비 null", C24);
        yield return new ContractCase("C25", "Loc.Instance≡Localization, Tr=Get=인덱서, Tr(k,7)=Format(k,7), Languages=UiLanguage 목록(기본 언어 첫 항목), Language 상호 반영·원복", C25);
        yield return new ContractCase("C26", "미등록 Views·ViewRegistry.Current 예외, RegisterViews(null) 예외, RegisterViews↔ViewRegistry.Current 양방향 위임(상태를 남기는 마지막 동작 시험)", C26);
        yield return new ContractCase("C27", "App.MainViewModel·Shell 대입 불가(CanWrite=false), IUiAppContext·IUiLocalizer 공개 멤버 대조, IUiAppContext 공개 구현 형식 0(공개 리플렉션만)", C27);
    }

    private static void C24()
    {
        var first = XkxkUi.Current;
        Check.True(first is not null, "XkxkUi.Current 가 null");
        Check.True(ReferenceEquals(first, XkxkUi.Current), "XkxkUi.Current 가 부를 때마다 다른 인스턴스");
        var type = first!.GetType();
        Check.True(!type.IsPublic && !type.IsNestedPublic, $"컨텍스트 구현 형식이 공개됨: {type.FullName}");
        Check.True(first.Localization is not null, "XkxkUi.Current.Localization 이 null");
    }

    private static void C25()
    {
        var loc = XkxkUi.Current.Localization;
        Check.True(ReferenceEquals(Loc.Instance, loc), "Loc.Instance 가 XkxkUi.Current.Localization 과 다른 인스턴스(위임 아님)");

        var plain = loc.Get(PlainKey);
        Check.True(plain.Length > 0, $"{PlainKey} 값이 비어 있음");
        Check.True(Loc.Tr(PlainKey) == plain && loc[PlainKey] == plain, "Loc.Tr(k)·Get(k)·인덱서 값 불일치");
        Check.True(loc.HasKey(PlainKey) && !loc.HasKey("contract.no.such.key"), "HasKey 판정 오류");

        var formatted = loc.Format(FormatKey, 7);
        Check.True(formatted.Contains('7') && !formatted.Contains("{0}"), $"Format 이 자리표시를 채우지 않음: {formatted}");
        Check.True(Loc.Tr(FormatKey, 7) == formatted, "Loc.Tr(k,7) 와 Format(k,7) 불일치");

        var languages = loc.Languages;
        Check.True(languages.Count >= 2, $"Languages 수가 2 미만: {languages.Count}");
        Check.True(languages[0].Code == Loc.DefaultLanguage, $"기본 언어가 첫 항목이 아님: {languages[0].Code}");
        var (code, name) = languages[0];
        Check.True(code == languages[0].Code && name == languages[0].Name && name.Length > 0, "UiLanguage Deconstruct 불일치");
        Check.True(languages.Any(l => l.Code == "en"), "Languages 에 en 이 없음");
        Check.Throws<ArgumentNullException>(() => _ = new UiLanguage(null!, "x"), "UiLanguage(null, name)");
        Check.Throws<ArgumentNullException>(() => _ = new UiLanguage("x", null!), "UiLanguage(code, null)");

        CheckLanguageRoundTrip();
    }

    /// <summary>Language 를 en 으로 바꿨다 Loc 쪽으로 되돌려 상호 반영을 보고, 처음 언어·문화권으로 원복한다(5초 제한).</summary>
    private static void CheckLanguageRoundTrip()
    {
        var loc = XkxkUi.Current.Localization;
        var original = loc.Language;
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var changed = 0;
        void OnChanged() => changed++;
        loc.LanguageChanged += OnChanged;
        try
        {
            var task = Task.Run(() =>
            {
                loc.Language = "en";
                Check.True(loc.Language == "en" && Loc.Instance.Language == "en", "Localization.Language=en 이 Loc.Instance 에 반영되지 않음");
                Check.True(loc.Get(PlainKey) == "English", $"en 전환 뒤 {PlainKey} 값이 English 가 아님");
                Loc.Instance.Language = Loc.DefaultLanguage;
                Check.True(loc.Language == Loc.DefaultLanguage, "Loc.Instance.Language 변경이 Localization 에 반영되지 않음");
            });
            if (!task.Wait(LanguageTimeoutMs))
            {
                throw new CheckFailedException($"Language 변경이 {LanguageTimeoutMs}ms 안에 끝나지 않음(앱 없는 콘솔 Dispatcher 경로 의심)");
            }

            Check.True(changed == 2, $"LanguageChanged 횟수 {changed}(2 기대)");
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException is CheckFailedException cf ? cf : new CheckFailedException($"Language 변경 예외 {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }
        finally
        {
            loc.LanguageChanged -= OnChanged;
            if (loc.Language != original)
            {
                loc.Language = original;
            }

            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = defCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defUiCulture;
        }
    }

    private static void C26()
    {
        var ctx = XkxkUi.Current;
        Check.Throws<InvalidOperationException>(() => _ = ctx.Views, "미등록 XkxkUi.Current.Views");
        Check.Throws<InvalidOperationException>(() => _ = ViewRegistry.Current, "미등록 ViewRegistry.Current");
        Check.Throws<ArgumentNullException>(() => ctx.RegisterViews(null!), "RegisterViews(null)");

        var fake1 = new FakeViewRegistry();
        ctx.RegisterViews(fake1);
        Check.True(ReferenceEquals(ctx.Views, fake1), "RegisterViews 뒤 XkxkUi.Current.Views 가 넣은 인스턴스가 아님");
        Check.True(ReferenceEquals(ViewRegistry.Current, fake1), "RegisterViews 뒤 ViewRegistry.Current 가 넣은 인스턴스가 아님(위임 아님)");

        var fake2 = new FakeViewRegistry();
        ViewRegistry.Current = fake2;
        Check.True(ReferenceEquals(ctx.Views, fake2), "ViewRegistry.Current 대입이 XkxkUi.Current.Views 에 반영되지 않음(위임 아님)");
        Check.True(ReferenceEquals(ViewRegistry.Current, fake2), "ViewRegistry.Current 재조회 불일치");
    }

    private static void C27()
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        foreach (var name in new[] { "MainViewModel", "Shell" })
        {
            var prop = typeof(App).GetProperty(name, publicStatic);
            Check.True(prop is not null, $"App.{name} 공개 정적 속성이 없음");
            Check.True(!prop!.CanWrite, $"App.{name} 에 setter 가 있음(CanWrite=true) — 위임 읽기 전용이어야 함");
        }

        CheckMembers(typeof(IUiAppContext), new[]
        {
            "P:Localization:IUiLocalizer:get",
            "P:Views:IViewRegistry:get",
            "P:MainViewModel:MainViewModel:get",
            "P:Shell:IShellCommands:get",
            "M:RegisterViews(IViewRegistry):Void",
        });
        CheckMembers(typeof(IUiLocalizer), new[]
        {
            "P:Language:String:get,set",
            "E:LanguageChanged:Action",
            "P:Languages:IReadOnlyList<UiLanguage>:get",
            "P:Item[String]:String:get",
            "M:Get(String):String",
            "M:Format(String,Object[]):String",
            "M:HasKey(String):Boolean",
            "M:Observe(String):IObservable<String>",
        });
        Check.True(typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(IUiLocalizer)), "IUiLocalizer 가 INotifyPropertyChanged 를 잇지 않음");

        var publicImpls = typeof(IUiAppContext).Assembly.GetExportedTypes()
            .Where(t => t != typeof(IUiAppContext) && typeof(IUiAppContext).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();
        Check.True(publicImpls.Count == 0, $"IUiAppContext 공개 구현 형식이 있음: {string.Join(", ", publicImpls)}");
    }

    /// <summary>기대 서명이 모두 공개 멤버로 있는지 본다(추가 멤버는 허용 — B14 추가만 원칙).</summary>
    private static void CheckMembers(Type type, IReadOnlyList<string> expected)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var index = p.GetIndexParameters();
            var name = index.Length == 0 ? p.Name : $"{p.Name}[{string.Join(",", index.Select(i => TypeName(i.ParameterType)))}]";
            var access = p.CanWrite && p.SetMethod is { IsPublic: true } ? "get,set" : "get";
            actual.Add($"P:{name}:{TypeName(p.PropertyType)}:{access}");
        }

        foreach (var e in type.GetEvents(BindingFlags.Public | BindingFlags.Instance))
        {
            actual.Add($"E:{e.Name}:{TypeName(e.EventHandlerType!)}");
        }

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName))
        {
            actual.Add($"M:{m.Name}({string.Join(",", m.GetParameters().Select(p => TypeName(p.ParameterType)))}):{TypeName(m.ReturnType)}");
        }

        var missing = expected.Where(s => !actual.Contains(s)).ToList();
        Check.True(missing.Count == 0, $"{type.Name} 공개 멤버 누락·서명 불일치: {string.Join(" | ", missing)} (실제: {string.Join(" | ", actual.OrderBy(s => s, StringComparer.Ordinal))})");
    }

    /// <summary>형식 이름(제네릭은 Name&lt;인자&gt;, 네임스페이스 없음).</summary>
    private static string TypeName(Type t)
    {
        if (!t.IsGenericType)
        {
            return t.Name;
        }

        var tick = t.Name.IndexOf('`');
        var bare = tick > 0 ? t.Name[..tick] : t.Name;
        return $"{bare}<{string.Join(",", t.GetGenericArguments().Select(TypeName))}>";
    }
}

// XAML 다국어 마크업 확장 {loc:Tr 키} — 사전 문자열을 바인딩으로 넣어 언어를 바꾸면 그 자리에서 즉시 다시 그린다
using System;
using Avalonia;
using Avalonia.Markup.Xaml;
#if PD_DIAGNOSTICS
using System.Collections.Generic;
using System.Threading;
#endif

namespace Pd.Poc.Shell.Localization;

/// <summary>
/// 사용: <c>xmlns:loc="using:Pd.Poc.Shell.Localization"</c> 선언 뒤
/// <c>&lt;MenuItem Header="{loc:Tr menu.window.autosave}" /&gt;</c>, <c>ToolTip.Tip="{loc:Tr chrome.tip.pin}"</c>.
/// 대상은 Avalonia 속성(StyledProperty·AttachedProperty)이어야 한다(바인딩이므로 일반 CLR 속성에는 쓸 수 없다 — 그때는 코드에서 Loc.Tr).
/// 형식 인자가 필요한 문장은 코드(뷰모델)에서 <c>Loc.Tr(key, args)</c> 로 만들고 LanguageChanged 에 다시 계산한다.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    /// <summary>사전 키(의미 단위: 영역.화면.요소, 예 menu.window.autosave).</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        object? value = null;
        StaticOverride(Key, ref value); // 진단 스위치 --loc static(PD_DIAGNOSTICS 전용) — 제품 빌드에서는 호출부까지 지워진다
        return value ?? Loc.Instance.Observe(Key).ToBinding();
    }

    /// <summary>
    /// 바인딩 대신 고정 문자열을 넣을 기회(진단 스위치 --loc static). 본문은 진단 빌드에만 있다.
    /// Conditional 메서드라서 제품 빌드에서는 호출부까지 컴파일러가 지운다(문자열·분기 0건, 예전 partial void 와 같은 성질).
    /// </summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    private static void StaticOverride(string key, ref object? value)
    {
#if PD_DIAGNOSTICS
        if (!StaticMode)
        {
            return;
        }

        // 언어를 바꿔도 다시 그리지 않는다(관찰·바인딩 없음) — 진단 목적상 의도된 동작
        value = Loc.Tr(key);
        Interlocked.Increment(ref _staticCount);
#endif
    }
#if PD_DIAGNOSTICS

    // 진단 스위치 --loc static — {loc:Tr} 를 바인딩 대신 기동 시점 고정 문자열로 해석해 다국어 바인딩 비용을 분리한다(20창 1 vsync·Android Q4 회귀 후보 판별)
    private static int _staticCount;

    /// <summary>--loc static(또는 --loc=static)으로 기동했는지. 진단 빌드 전용. 읽을 때마다 App.StartupArgs 로 판정한다(App 이 인자를 정리한 뒤 값).</summary>
    public static bool StaticMode => ReadStatic(App.StartupArgs);

    /// <summary>고정 문자열로 해석한 {loc:Tr} 개수(원격 lang.get 의 staticCount — 스위치가 실제로 먹었는지 확인용).</summary>
    public static int StaticCount => Volatile.Read(ref _staticCount);

    private static bool ReadStatic(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Equals("--loc=static", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (a.Equals("--loc", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count
                && args[i + 1].Equals("static", StringComparison.OrdinalIgnoreCase)) { return true; }
        }

        return false;
    }
#endif
}

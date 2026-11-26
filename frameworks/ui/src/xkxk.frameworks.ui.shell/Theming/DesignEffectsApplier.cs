// 디자인 테마 효과 적용기(REQ-104) — 효과 실효값을 앱 리소스(PdStyle*)·최상위 창 클래스로 반영하고 문서 창 그림자(PdMdiShadow)를 덮어쓴다. 서명이 같으면 생략
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 효과 8종(DesignThemes.Effects)의 실효값(DesignThemeResolver.Effective — 폴백 반영)을 표의 ResourceKey 앱 리소스로 쓴다.
/// shadow=BoxShadows·surface=브러시·decor=브러시·motion=ms(double), 그 밖(chrome·depth.*)은 실효값 문자열. chrome 은 최상위 창 클래스(pdStylePill·pdStyleMinimal)로도 단다.
/// ds-classic(shadow inherit·surface flat·decor none·motion inherit·chrome standard)은 기존 리소스를 그대로 두므로 화면·계산값이 바뀌지 않는다(K8 classic_identical).
/// 수치(그림자 모양·장식 간격·불투명도)는 효과 표 설정 키 + 기본값에서 읽는다(REQ-99). 색은 기존 PdBrush* 토큰에서만 가져온다(색 직교).
/// </summary>
public static partial class DesignEffectsApplier
{
    /// <summary>chrome=pill 일 때 최상위 창에 다는 클래스(DesignEffects.axaml 셀렉터와 같은 이름).</summary>
    public const string PillClass = "pdStylePill";

    /// <summary>chrome=minimal 일 때 최상위 창에 다는 클래스.</summary>
    public const string MinimalClass = "pdStyleMinimal";
    public const string DecorClass = "pdStyleDecor"; // decor≠none 일 때 최상위 창에 다는 클래스
    public const string PinButtonBorderClass = "pdPinButtonBorder"; // 사용자·회사가 단추 테두리를 정함 → minimal 단추 테두리 0 을 걸지 않음
    public const string PinInputBorderClass = "pdPinInputBorder"; // 사용자·회사가 입력 테두리를 정함 → minimal 입력 테두리를 걸지 않음
    private const string Inherit = "inherit";

    private static string? _lastSignature;
    private static string? _chromeClass; // 지금 최상위 창에 단 클래스(null = 없음)
    private static BoxShadows? _shadowOverride; // null = inherit(문서 창 그림자를 덮어쓰지 않음)
    private static bool _hooked;
    private static bool _decorOn; // 지금 최상위 창에 pdStyleDecor 를 달아야 하는가
    private static bool _pinButtonBorder; // 사용자·회사 단추 테두리 지정 여부
    private static bool _pinInputBorder; // 사용자·회사 입력 테두리 지정 여부

    /// <summary>효과 실효값을 앱에 반영한다. 테마 적용 경로(UiSettingsApplier)가 매번 부르며, 서명이 같으면 아무것도 쓰지 않는다.</summary>
    public static void Apply(Application app)
    {
        Hook();
        string signature;
        using (UiSettings.Op("ui.apply.style.sig")) { signature = Signature(app); }
        if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastSignature = signature;
        var shadow = DesignThemeResolver.Effective("shadow");
        var previousShadow = _shadowOverride;
        _shadowOverride = shadow == Inherit ? null : ShadowOf(shadow);
        var items = new List<KeyValuePair<object, object?>>();
        var absent = new List<object>();
        foreach (var spec in DesignThemes.Effects)
        {
            if (spec.ResourceKey is not { } key)
            {
                continue;
            }

            var effective = DesignThemeResolver.Effective(spec.Key);
            if (IsAbsent(spec.Key, effective))
            {
                absent.Add(key); // 디자인 테마가 덮어쓰지 않음 — 리소스를 두지 않는다(K9 style_select_each: 그림자 inherit·장식 none = 리소스 없음)
            }
            else if (ValueOf(app, spec.Key, effective) is { } value)
            {
                items.Add(new(key, value));
            }
        }

        // 문서 창 그림자 2키도 같은 SetItems 로 쓴다(알림 1회) — 덮어쓰기·inherit 되돌리기 모두 WindowChromeStyle 이 공급자(DesignShadowOverride)로 계산한다
        var shadowChanged = !Equals(previousShadow, _shadowOverride);
        if (shadowChanged) { items.AddRange(WindowChromeStyle.ShadowResources(app)); }
        using (UiSettings.Op("ui.apply.style.write")) { Write(app, items, absent); }
        if (shadowChanged) { WindowChromeStyle.NotifyShadowApplied(); }
        using (UiSettings.Op("ui.apply.style.chrome")) { SyncChromeClass(app, DesignThemeResolver.Effective("chrome")); }
    }

    /// <summary>시험·진단용: 다음 Apply 가 서명 비교 없이 다시 쓰게 한다.</summary>
    public static void Invalidate() => _lastSignature = null;

    private static void Hook()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        DesignThemeResolver.Changed += OnResolverChanged;
        XkxkUi.Current.Settings.PropertyChanged += OnUiSettingChanged; // 배경 이미지 설정(BackgroundImage*)은 저장만 하므로 여기서 다시 적용한다
        WindowChromeStyle.DesignShadowOverride = () => _shadowOverride; // 크롬 스타일이 그림자를 계산할 때 디자인 테마 값을 함께 쓴다(덮어쓰기 왕복 없음, K13)
        Control.LoadedEvent.AddClassHandler<TopLevel>((top, _) => SyncClass(top), RoutingStrategies.Direct); // 뒤에 열린 SDI 창에도 chrome 클래스
    }

    /// <summary>창 수·움직임 수준 등 저장값 밖 변화로 실효값이 바뀌면 요소 디자인과 효과를 다시 적용한다.</summary>
    private static void OnResolverChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnResolverChanged);
            return;
        }

        if (Application.Current is { } app)
        {
            ThemeDesignApplier.Apply(app, XkxkUi.Current.Themes.Requested);
            Apply(app);
        }
    }

    /// <summary>
    /// 리소스를 두지 않는 실효값: shadow=inherit(문서 창 그림자를 덮지 않음)·decor=none(장식 클래스가 꺼져 브러시를 쓰지 않음).
    /// 두 리소스는 그 상태에서 어떤 스타일도 읽지 않으므로 화면은 같고, 리소스 유무가 '디자인 테마가 덮어쓰는가' 를 그대로 나타낸다.
    /// </summary>
    private static bool IsAbsent(string key, string value) => (key == "shadow" && value == Inherit) || (key == "decor" && value == "none");

    /// <summary>효과 1종의 리소스 값. 변환할 수 없으면 null(리소스를 쓰지 않음).</summary>
    private static object? ValueOf(Application app, string key, string value) => key switch
    {
        "shadow" => ShadowOf(value),
        "surface" => SurfaceOf(app, value),
        "decor" => DecorOf(app, value),
        "motion" => (double)DesignThemeResolver.MotionMs(),
        _ => value,
    };

    /// <summary>바뀐 키만 한 번에 쓴다(하나씩 쓰면 키 수만큼 앱 전체 리소스 알림 — perf15 C).</summary>
    private static void Write(Application app, List<KeyValuePair<object, object?>> items, List<object> absent)
    {
        foreach (var key in absent.Where(app.Resources.ContainsKey))
        {
            app.Resources.Remove(key);
        }

        var changed = items.Where(kv => !app.Resources.TryGetValue(kv.Key, out var old) || !Equals(old, kv.Value)).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        if (app.Resources is ResourceDictionary resources)
        {
            resources.SetItems(changed);
            return;
        }

        foreach (var kv in changed)
        {
            app.Resources[kv.Key] = kv.Value;
        }
    }

    /// <summary>실효값·수치 설정·쓰는 색 토큰·크롬 그림자 상태를 이은 서명. 색 토큰이 바뀌면 그라데이션·장식 브러시를 다시 만들어야 하므로 넣는다.</summary>
    private static string Signature(Application app)
    {
        var sb = new StringBuilder();
        foreach (var spec in DesignThemes.EffectTable)
        {
            sb.Append(spec.Key).Append('=').Append(DesignThemeResolver.Effective(spec.Key)).Append('|');
        }

        sb.Append(DesignThemeResolver.Current).Append('|').Append(DesignThemeResolver.Source("decor.opacity")).Append('|'); // 값이 같아도 농도 보정 여부가 갈린다
        sb.Append(DesignThemeResolver.MotionMs()).Append('|').Append(app.ActualThemeVariant)
            .Append('|').Append(Pinned("button", "cornerRadius")).Append(Pinned("button", "borderThickness")).Append(Pinned("input", "borderThickness")).Append('|').Append(ImageSignature());
        foreach (var token in new[] { "PdBrushSurface", "PdBrushSurfaceAlt", "PdBrushAccent", "PdBrushMdiBackground" })
        {
            sb.Append('|').Append(Token(app, token));
        }

        return sb.ToString();
    }

    private static void SyncChromeClass(Application app, string chrome)
    {
        _chromeClass = chrome switch
        {
            "pill" when !Pinned("button", "cornerRadius") => PillClass,
            "minimal" => MinimalClass,
            _ => null,
        };
        _decorOn = DesignThemeResolver.Effective("decor") != "none";
        _pinButtonBorder = Pinned("button", "borderThickness");
        _pinInputBorder = Pinned("input", "borderThickness");
        foreach (var root in Roots(app))
        {
            SyncClass(root);
        }
    }

    /// <summary>
    /// 사용자(REQ-101)나 회사가 그 요소 속성을 정했는가. 18 문서 §4 요소 속성 우선순위(사용자 ?? 회사 ?? 디자인 테마)에 따라 그때는 chrome 효과
    /// (pill 단추 모서리, minimal 단추·입력 테두리)를 걸지 않는다. 효과 스타일이 요소 디자인 슬롯보다 뒤에 있어 그대로 두면 사용자 값을 덮는다(K9 user_override_wins).
    /// pill 은 클래스를 달지 않고, minimal 은 클래스(계약 이름)는 두고 요소별 표식 클래스로 해당 셀렉터만 뺀다(DesignEffects.axaml).
    /// </summary>
    private static bool Pinned(string el, string prop) =>
        ThemeElements.Resolve(XkxkUi.Current.Themes.Requested, el, prop, out _) is DesignLayer.User or DesignLayer.Company;

    private static void SyncClass(StyledElement root)
    {
        root.Classes.Set(PillClass, _chromeClass == PillClass);
        root.Classes.Set(MinimalClass, _chromeClass == MinimalClass);
        root.Classes.Set(DecorClass, _decorOn);
        root.Classes.Set(PinButtonBorderClass, _pinButtonBorder);
        root.Classes.Set(PinInputBorderClass, _pinInputBorder);
    }

    /// <summary>최상위: 데스크톱은 모든 창(메인·SDI), 단일 뷰(웹·Android)는 메인 뷰의 최상위.</summary>
    private static IEnumerable<StyledElement> Roots(Application app) => app.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime desktop => desktop.Windows,
        ISingleViewApplicationLifetime { MainView: { } view } => new StyledElement[] { TopLevel.GetTopLevel(view) ?? (StyledElement)view },
        _ => Array.Empty<StyledElement>(),
    };
}

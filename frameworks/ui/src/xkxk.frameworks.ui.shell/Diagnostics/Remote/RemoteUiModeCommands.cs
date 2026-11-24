// 원격 UI 형(PC형·태블릿형·모바일형) 조회·설정·자동 전환 확인과 모바일형 메뉴 도달 진단
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using xkxk.frameworks.ui.data.Navigation;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Platform;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>UI 형 원격 명령 군(ui.style·nav.menu, 호환용 ui.mode). RemoteCommandRouter 가 Register 를 부른다.</summary>
public static class RemoteUiModeCommands
{
    /// <summary>데스크톱이 채우는 주 창 Z 올리기(HWND_TOP·비활성화·이동 없음). window.main.activate 가 실제 클릭처럼 전경이어도 Z 를 요청하게 한다 — 설정에 따른 보정은 데스크톱 훅이 한다.</summary>
    public static Action<Window>? MainRaiseZ { get; set; }

    // 공용 멤버 전달자(W2) — 본문은 옛 이름 그대로 쓴다.
    private static ViewModels.MainViewModel Vm => RemoteShellAccess.Vm;

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("ui.style",
            "UI 형 조회 — 인자 없으면 읽기만. set=auto|pc|tablet|mobile 이면 저장값을 바꾼다(허용 밖은 오류, 자동 전환은 디바운스 뒤 반영) " +
            "→ {device, setting, effective, width, allowed[], selectorVisible, lastTransition{from,to,reason,deferred}}(set 이면 {before, after})", a =>
            {
                var vm = Vm;
                if (a.Str("set") is { Length: > 0 } set)
                {
                    if (!Enum.TryParse<UiStyle>(set, ignoreCase: true, out var style))
                    {
                        throw new RemoteCommandException($"set 값 오류: '{set}' ({string.Join("|", Enum.GetNames<UiStyle>())})");
                    }

                    if (!vm.Compact.Style.Allowed.Contains(style))
                    {
                        throw new RemoteCommandException($"이 기기({vm.Compact.Style.Device})에서 허용되지 않는 형: {style}");
                    }

                    var before = StyleJson(vm);
                    vm.Compact.Style.Select(style);
                    return new JsonObject { ["before"] = before, ["after"] = StyleJson(vm) };
                }

                return StyleJson(vm);
            });

        r.Register("ui.classify",
            "기기 정보 판정(순수 함수, 부수 효과 없음). 인자: platform=Unknown|Windows|MacOS|Linux|Android|IOS|Browser, hasTouch·precise·hover·pcClass·foldable·multiWindow=true|false(생략=알 수 없음), " +
            "smallest·windowWidth=dp, idiom=Unknown|Phone|Pad, setting=auto|pc|tablet|mobile(기본 auto), width=창 폭 dp(기본 windowWidth→smallest), previous=직전 형(기본 auto), compact=true|false(compactWindowKeepsTablet, 기본 false). " +
            "기기 인자가 하나도 없으면 현재 HostTraits 와 현재 판정을 돌려준다 → {device, effective, traits}(인자 없음이면 {hostTraits, device, effective, setting, compact})", a =>
            {
                var keys = new[] { "platform", "hasTouch", "precise", "hover", "smallest", "windowWidth", "pcClass", "foldable", "multiWindow", "idiom" };
                if (!keys.Any(k => a.ContainsKey(k)))
                {
                    var s = Vm.Compact.Style;
                    return new JsonObject
                    {
                        ["hostTraits"] = TraitsJson(InputFormFactor.HostTraits),
                        ["device"] = JsonValue.Create(s.Device.ToString()),
                        ["effective"] = JsonValue.Create(s.Current.ToString()),
                        ["setting"] = JsonValue.Create(s.Setting.ToString()),
                        ["compact"] = JsonValue.Create(s.CompactKeepsTablet),
                    };
                }

                var traits = new DeviceTraits(
                    ParseEnum(a.Str("platform"), PlatformKind.Unknown),
                    OptBool(a, "hasTouch"), OptBool(a, "precise"), OptBool(a, "hover"),
                    a.Num("smallest"), a.Num("windowWidth"),
                    OptBool(a, "pcClass"), OptBool(a, "foldable"), OptBool(a, "multiWindow"),
                    ParseEnum(a.Str("idiom"), DeviceIdiom.Unknown));
                var setting = ParseEnum(a.Str("setting"), UiStyle.Auto);
                var previous = ParseEnum(a.Str("previous"), UiStyle.Auto);
                var width = a.Num("width") ?? a.Num("windowWidth") ?? a.Num("smallest") ?? 0;
                var compact = OptBool(a, "compact") ?? false;
                var device = UiStyleRules.Classify(traits);
                var effective = UiStyleRules.Resolve(device, setting, width, previous, compact);
                return new JsonObject { ["device"] = JsonValue.Create(device.ToString()), ["effective"] = JsonValue.Create(effective.ToString()), ["traits"] = TraitsJson(traits) };
            });

        r.Register("style.selftest",
            "기기 판별표 14행 + 변형·하위호환 표본(총 27건, null 하위호환·compactWindowKeepsTablet 켬/끔 포함) → {cases:[{name,pass,detail}], passed, total}", _ => StyleSelfTest());

        r.Register("nav.menu",
            "모바일형 메뉴 도달 조회(읽기 전용) → {tabs[], drawer[{id, group, enabled, reason, desktopIds[]}]}. 하단 탭=TabOrder>=0 항목, 서랍=전체 항목(비적용은 enabled=false+reason)",
            _ =>
            {
                var entries = NavMenuCatalog.Entries.ToList();
                var tabs = new JsonArray();
                foreach (var e in entries.Where(x => x.TabOrder >= 0).OrderBy(x => x.TabOrder))
                {
                    tabs.Add(JsonValue.Create(e.Id)); // WASM 리플렉션 없는 경로
                }

                var drawer = new JsonArray();
                foreach (var e in entries)
                {
                    var ids = new JsonArray();
                    foreach (var d in e.DesktopIds)
                    {
                        ids.Add(JsonValue.Create(d));
                    }

                    drawer.Add(new JsonObject
                    {
                        ["id"] = JsonValue.Create(e.Id),
                        ["group"] = JsonValue.Create(e.IsGroup),
                        ["enabled"] = JsonValue.Create(e.Applicable),
                        ["reason"] = JsonValue.Create(e.Applicable || e.ReasonKey is null ? null : Loc.Tr(e.ReasonKey)),
                        ["desktopIds"] = ids,
                    });
                }

                return new JsonObject { ["tabs"] = tabs, ["drawer"] = drawer };
            });

        r.Register("window.resize",
            "메인 창 크기 지정(DIP, Normal 상태에서만; 데스크톱 전용) w=<폭> h=<높이>(생략하면 현재 값 유지) → {before{w,h}, after{w,h}, uiStyle}. 자동 전환은 디바운스(200ms) 뒤 반영되니 잠시 뒤 ui.style 로 확인",
            a =>
            {
                if (MainWindow() is not { } main)
                {
                    return new JsonObject { ["status"] = "NotApplicable" };
                }

                if (main.WindowState != WindowState.Normal)
                {
                    throw new RemoteCommandException($"메인 창이 Normal 상태가 아닙니다({main.WindowState}). monitor.maximize via=restore 로 먼저 복원하세요.");
                }

                var w = a.Num("w");
                var h = a.Num("h");
                if (w is null && h is null)
                {
                    throw new RemoteCommandException("인자 'w' 또는 'h' 가 필요합니다(DIP).");
                }

                if ((w is { } w0 && w0 <= 0) || (h is { } h0 && h0 <= 0))
                {
                    throw new RemoteCommandException("w·h 는 0보다 커야 합니다.");
                }

                var before = SizeJson(main);
                if (w is { } nw) { main.Width = nw; }
                if (h is { } nh) { main.Height = nh; }
                return new JsonObject { ["before"] = before, ["after"] = SizeJson(main), ["uiStyle"] = StyleJson(Vm) };
            });

        r.Register("window.main.activate",
            "메인 창 활성화(Window.Activate; 데스크톱 전용 — window.activate 는 문서 활성 별칭이라 별개) → {isActive}",
            _ =>
            {
                if (MainWindow() is not { } main)
                {
                    return new JsonObject { ["status"] = "NotApplicable" };
                }

                main.Activate();
                MainRaiseZ?.Invoke(main); // 이미 전경이면 Activate 가 Z 를 바꾸지 않는다(실제 클릭은 올린다)
                return new JsonObject { ["isActive"] = JsonValue.Create(main.IsActive) };
            });

        r.Register("ui.mode",
            "(호환) 옛 화면 모드 명령 — ui.style 로 대체됨. 인자 없으면 ui.style 조회와 같은 값을 돌려준다", _ => StyleJson(Vm));
    }

    private static bool? OptBool(JsonObject a, string key) => a.ContainsKey(key) && a[key] is not null ? a.Bool(key) : null;

    private static T ParseEnum<T>(string? text, T fallback) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        return Enum.TryParse<T>(text, ignoreCase: true, out var v) ? v : throw new RemoteCommandException($"{typeof(T).Name} 값 오류: '{text}' ({string.Join("|", Enum.GetNames<T>())})");
    }

    private static JsonObject? TraitsJson(DeviceTraits? t) => t is null ? null : new JsonObject
    {
        ["platform"] = JsonValue.Create(t.Platform.ToString()),
        ["hasTouch"] = JsonValue.Create(t.HasTouch),
        ["precise"] = JsonValue.Create(t.PrecisePointer),
        ["hover"] = JsonValue.Create(t.HoverCapable),
        ["smallest"] = JsonValue.Create(t.SmallestWidthDp),
        ["windowWidth"] = JsonValue.Create(t.WindowWidthDp),
        ["pcClass"] = JsonValue.Create(t.IsPcClass),
        ["foldable"] = JsonValue.Create(t.IsFoldableCapable),
        ["multiWindow"] = JsonValue.Create(t.IsMultiWindow),
        ["idiom"] = JsonValue.Create(t.Idiom.ToString()),
    };

    /// <summary>정본 08 §1-3-1 판정표 14행 + 변형·하위호환 표본(총 27건, 코드 안 표). 순수 함수 Classify·Resolve 만 부른다.</summary>
    private static JsonObject StyleSelfTest()
    {
        var cases = new JsonArray();
        void Case(string name, DeviceTraits t, DeviceClass wantDevice, UiStyle wantStyle, double width = 0, UiStyle setting = UiStyle.Auto, bool compact = false, UiStyle previous = UiStyle.Auto)
        {
            var d = UiStyleRules.Classify(t);
            var e = UiStyleRules.Resolve(d, setting, width > 0 ? width : (t.WindowWidthDp ?? t.SmallestWidthDp ?? 0), previous, compact);
            cases.Add(new JsonObject { ["name"] = name, ["pass"] = d == wantDevice && e == wantStyle, ["detail"] = $"device={d}(기대 {wantDevice}) effective={e}(기대 {wantStyle})" });
        }

        var and = PlatformKind.Android;
        Case("row01_fold_folded", new(and, true, SmallestWidthDp: 401, WindowWidthDp: 401, IsFoldableCapable: true), DeviceClass.Phone, UiStyle.Mobile);
        Case("row02_fold_unfolded", new(and, true, SmallestWidthDp: 805, WindowWidthDp: 805, IsFoldableCapable: true), DeviceClass.Tablet, UiStyle.Tablet);
        Case("row02b_fold_unfolded_narrow_compact_off", new(and, true, SmallestWidthDp: 805, WindowWidthDp: 500, IsMultiWindow: true), DeviceClass.Tablet, UiStyle.Mobile);
        Case("row02c_fold_unfolded_narrow_compact_on", new(and, true, SmallestWidthDp: 805, WindowWidthDp: 500, IsMultiWindow: true), DeviceClass.Tablet, UiStyle.Tablet, compact: true);
        Case("row03_fold_half_open_inner", new(and, true, SmallestWidthDp: 805, WindowWidthDp: 805, IsFoldableCapable: true), DeviceClass.Tablet, UiStyle.Tablet);
        Case("row04_split_window_off", new(and, true, SmallestWidthDp: 800, WindowWidthDp: 400, IsMultiWindow: true), DeviceClass.Tablet, UiStyle.Mobile);
        Case("row04b_split_window_on", new(and, true, SmallestWidthDp: 800, WindowWidthDp: 400, IsMultiWindow: true), DeviceClass.Tablet, UiStyle.Tablet, compact: true);
        Case("row04c_manual_pc_beats_compact", new(and, true, SmallestWidthDp: 800, WindowWidthDp: 500, IsMultiWindow: true), DeviceClass.Tablet, UiStyle.Pc, width: 500, setting: UiStyle.Pc, compact: true);
        Case("row05_tablet", new(and, true, SmallestWidthDp: 800, WindowWidthDp: 1280), DeviceClass.Tablet, UiStyle.Tablet);
        Case("row06_phone_bar", new(and, true, SmallestWidthDp: 360, WindowWidthDp: 360), DeviceClass.Phone, UiStyle.Mobile);
        Case("row07_desktop_touch_laptop", new(PlatformKind.Windows, HasTouch: true, SmallestWidthDp: 700), DeviceClass.Pc, UiStyle.Pc, width: 1200);
        Case("row08_web_touch_laptop_fine_hover", new(PlatformKind.Browser, true, true, true, 800), DeviceClass.Pc, UiStyle.Pc, width: 1200);
        Case("row08b_web_mobile_fine_but_no_hover", new(PlatformKind.Browser, true, true, false, 400), DeviceClass.Phone, UiStyle.Mobile);
        Case("row09_dex_size_fallback", new(and, true, SmallestWidthDp: 1000), DeviceClass.Tablet, UiStyle.Tablet);
        Case("row10_chromeos_pc_class", new(and, true, SmallestWidthDp: 700, IsPcClass: true), DeviceClass.Pc, UiStyle.Pc, width: 1200);
        Case("row10b_chromeos_touch_only", new(and, true, PrecisePointer: false, SmallestWidthDp: 700, IsPcClass: true), DeviceClass.Tablet, UiStyle.Tablet);
        Case("row11_desktop_browser", new(PlatformKind.Browser, false, true, true, 1000), DeviceClass.Pc, UiStyle.Pc, width: 1200);
        Case("row12_mobile_browser_phone", new(PlatformKind.Browser, true, false, false, 390), DeviceClass.Phone, UiStyle.Mobile);
        Case("row12b_mobile_browser_tablet", new(PlatformKind.Browser, true, false, false, 820), DeviceClass.Tablet, UiStyle.Tablet);
        Case("row13_ios_phone_idiom", new(PlatformKind.IOS, Idiom: DeviceIdiom.Phone, SmallestWidthDp: 800), DeviceClass.Phone, UiStyle.Mobile);
        Case("row13b_ios_pad_idiom", new(PlatformKind.IOS, Idiom: DeviceIdiom.Pad, SmallestWidthDp: 400), DeviceClass.Tablet, UiStyle.Tablet, width: 800);
        Case("row14_leanback_size_fallback", new(and, false, SmallestWidthDp: 960), DeviceClass.Pc, UiStyle.Pc, width: 1920);
        Case("null_compat_touch_small", new(PlatformKind.Unknown, HasTouch: true, SmallestWidthDp: 400), DeviceClass.Phone, UiStyle.Mobile);
        Case("null_compat_touch_big", new(PlatformKind.Unknown, HasTouch: true, SmallestWidthDp: 900), DeviceClass.Tablet, UiStyle.Tablet);
        Case("null_compat_no_touch", new(PlatformKind.Unknown, HasTouch: false, SmallestWidthDp: 400), DeviceClass.Pc, UiStyle.Tablet, width: 700);
        Case("null_all_unknown", new(PlatformKind.Unknown), DeviceClass.Pc, UiStyle.Tablet, width: 700);

        var oldPath = UiStyleRules.Classify(true, 400) == DeviceClass.Phone && UiStyleRules.Classify(true, 900) == DeviceClass.Tablet && UiStyleRules.Classify(false, 400) == DeviceClass.Pc;
        cases.Add(new JsonObject { ["name"] = "legacy_classify_touch_shortside", ["pass"] = oldPath, ["detail"] = "Classify(bool,double) 위임 동일" });

        var passed = cases.Count(c => c?["pass"]?.GetValue<bool>() == true);
        return new JsonObject { ["cases"] = cases, ["passed"] = passed, ["total"] = cases.Count };
    }

    private static Window? MainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static JsonObject SizeJson(Window w) =>
        new() { ["w"] = JsonValue.Create(Math.Round(w.Bounds.Width, 1)), ["h"] = JsonValue.Create(Math.Round(w.Bounds.Height, 1)) };

    /// <summary>UI 형 상태. 전환 직후 값은 디바운스(200ms)·애니메이션 중일 수 있다 — 판정은 잠시 뒤 다시 읽어 확인한다.</summary>
    private static JsonObject StyleJson(ViewModels.MainViewModel vm)
    {
        var s = vm.Compact.Style;
        var allowed = new JsonArray();
        foreach (var x in s.Allowed)
        {
            allowed.Add(JsonValue.Create(x.ToString()));
        }

        var last = s.LastTransition is { } t
            ? new JsonObject { ["from"] = JsonValue.Create(t.From.ToString()), ["to"] = JsonValue.Create(t.To.ToString()), ["reason"] = JsonValue.Create(t.Reason), ["deferred"] = JsonValue.Create(t.Deferred) }
            : null;
        return new JsonObject
        {
            ["device"] = JsonValue.Create(s.Device.ToString()),
            ["setting"] = JsonValue.Create(s.Setting.ToString()),
            ["effective"] = JsonValue.Create(s.Current.ToString()),
            ["width"] = JsonValue.Create(Math.Round(s.ViewWidth, 1)),
            ["allowed"] = allowed,
            ["selectorVisible"] = JsonValue.Create(s.SelectorVisible),
            ["lastTransition"] = last,
        };
    }
}
#endif

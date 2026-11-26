// 원격 명령(UI 설정 — 테마·기준 글자 크기·폰트·Ctrl+휠 동작, Ctrl+휠/Ctrl+0 입력 경로 재현) — UiSettings 를 그대로 호출
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels.Settings;
using xkxk.frameworks.ui.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>설정·테마 원격 명령 군(옛 RemoteCommands.Settings). RemoteCommandRouter 가 Register 를 부른다.</summary>
internal static class RemoteSettingsCommands
{
    // 공용 멤버 전달자(W2) — 본문은 옛 이름 그대로 쓴다.
    private static xkxk.frameworks.ui.shell.Docking.PocDocument Doc(JsonObject a, string key = "doc") => RemoteShellAccess.Doc(a, key);

    private static UiSettings Settings => XkxkUi.Current.Settings;

    // (원격 명령, ThemeAutomation 명령, 인자 키, 설명)
    private static readonly (string Name, string Verb, string? ArgKey, string Help)[] ThemeVerbs =
    {
        ("theme.select", "theme", "id", "테마 적용 id="),
        ("theme.next", "theme-next", null, "다음 테마"),
        ("theme.clone", "theme-clone", "name", "활성 테마 복제 [name=]"),
        ("theme.set", "theme-set", "colors", "활성 사용자 테마 색 colors=Accent=#RRGGBB,Background=#RRGGBB"),
        ("theme.save", "theme-save", null, "사용자 테마 저장"),
        ("theme.delete", "theme-delete", "id", "사용자 테마 삭제 id="),
        ("theme.export", "theme-export", "path", "테마 JSON 내보내기 path= [id=]"),
        ("theme.import", "theme-import", "path", "테마 JSON 가져오기 path="),
        ("theme.openEditor", "theme-open-editor", null, "테마 편집기 열기"),
    };

    // 시험용 회사 값 공급자 — settings.set theme.style.locked 가 회사 계층 값(ThemeElements.Policy.Values)을 흉내 낸다(진단 전용).
    private sealed class DiagnosticPolicy : IThemeDesignPolicy
    {
        public System.Collections.Generic.Dictionary<string, string> Map { get; } = new(StringComparer.Ordinal);

        public System.Collections.Generic.IReadOnlyDictionary<string, string> Values => Map;

        public System.Collections.Generic.IReadOnlyCollection<string> Locked { get; } = Array.Empty<string>();
    }

    private static DiagnosticPolicy EnsurePolicy()
    {
        if (ThemeElements.Policy is not DiagnosticPolicy p)
        {
            ThemeElements.Policy = p = new DiagnosticPolicy();
        }

        return p;
    }

    private static JsonObject SettingsJson() => new()
    {
        ["theme"] = Settings.Theme.ToString(),
        ["actualTheme"] = Avalonia.Application.Current?.ActualThemeVariant?.ToString(),
        ["themeId"] = Settings.ThemeId,
        ["activeThemeId"] = ThemeApplier.Active?.Id,
        ["themeName"] = ThemeApplier.Active?.LocalizedName, // 표시 이름(현재 언어). 저장값은 themeId(D-THEME-NAME-I18N)
        ["baseFontSize"] = Settings.BaseFontSize,
        ["documentDefaultFontSize"] = Settings.DocumentDefaultFontSize,
        ["fontTokens"] = new JsonObject(UiSettings.FontTokens.Select(t =>
            new System.Collections.Generic.KeyValuePair<string, JsonNode?>(t.Key, Math.Max(6, Settings.BaseFontSize + t.Offset)))),
        ["fontName"] = Settings.FontName,
        ["wheelMode"] = Settings.WheelMode.ToString(),
        ["language"] = XkxkUi.Current.Localization.Language,
        ["panelFeature"] = Settings.PanelFeatureMode.ToString(), // 패널 기능 설정값(Auto|On|Off) — 시험이 읽어 원복에 쓴다
        ["activeDocZoom"] = XkxkUi.Current.Shell?.ActiveDocument?.Zoom,
        ["gridViewPool"] = Settings.GridViewPool, // 준비된 표 뷰(기본 끔, A16)
        ["gridCompactCells"] = Settings.GridCompactCells, // 표 단일 요소 셀(기본 켬, A16)
        ["gridDrawnCellText"] = Settings.GridDrawnCellText, // 그리는 셀 글자(기본 켬, K15)
        ["prewarmViews"] = Settings.PrewarmViews, // 문서 뷰 예열
        // 사용자 테마 저장 JSON(UiThemeSettings 가 저장 때 쓰는 것과 같은 직렬화) — 설정 파일 경로가 없는 웹에서도 희소 저장(sparse_save)을 판정하게 한다
        ["userThemesJson"] = XkxkUi.Current.Themes.ExportUserThemesJson(),
    };

    /// <summary>
    /// 켬·끔 인자(키는 대소문자 무시 — gridViewPool·GridViewPool 모두). 없으면 null.
    /// true|false|1|0 만 받고 그 밖의 값은 조용히 끔으로 두지 않고 거부한다(panelFeature 와 같은 원칙).
    /// </summary>
    private static bool? StrictBool(JsonObject a, string key)
    {
        var hit = a.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (hit.Key is null || hit.Value is not JsonValue v) { return null; }
        if (v.TryGetValue<bool>(out var b)) { return b; }
        var s = v.ToString().Trim();
        if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) { return false; }
        throw new RemoteCommandException(key + " 는 true|false 입니다: " + s);
    }

    /// <summary>열려 있는 설정 화면(새 창·플라이아웃·스마트폰 시트 공통). 없으면 null.</summary>
    private static Avalonia.Controls.Control? OpenSettingsView() =>
        VisualTreeInspector.TopLevels().SelectMany(t => t.GetVisualDescendants()).OfType<Avalonia.Controls.Control>().FirstOrDefault(c => c is ISettingsRootView);

    /// <summary>AC-7-01: 인벤토리 전수 + UiSettings 쓰기 가능 공개 속성 중 인벤토리에 없는 것(unmapped).</summary>
    private static JsonObject SettingsList()
    {
        var mapped = SettingsInventory.All.SelectMany(e => new[] { e.Setting, e.OverrideSetting }).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var props = typeof(UiSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead && p.CanWrite).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        return new JsonObject
        {
            ["entries"] = new JsonArray(SettingsInventory.All.Select(e => (JsonNode?)new JsonObject
            {
                ["id"] = e.Id,
                ["category"] = e.Category.ToString(),
                ["labelKey"] = e.LabelKey,
                ["label"] = Localization.Loc.Tr(e.LabelKey),
                ["setting"] = e.Setting,
                ["overrideSetting"] = e.OverrideSetting,
            }).ToArray()),
            ["uiSettings"] = new JsonArray(props.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
            ["unmapped"] = new JsonArray(props.Where(n => !mapped.Contains(n)).Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
        };
    }

    /// <summary>설정 화면 상태: 선택 범주·검색어·남은 범주·지금 트리에 있는 행(페이지를 바꾼 뒤에는 레이아웃이 끝나야 행이 나타난다).</summary>
    private static JsonObject SettingsWindowJson(Avalonia.Controls.Control view, SettingsWindowViewModel vm) => new()
    {
        ["selected"] = vm.SelectedCategory?.Id.ToString(),
        ["search"] = vm.SearchText,
        ["hasNoResults"] = vm.HasNoResults,
        ["categories"] = new JsonArray(vm.Categories.Select(c => (JsonNode?)JsonValue.Create(c.Id.ToString())).ToArray()),
        ["rows"] = new JsonArray(view.GetVisualDescendants().OfType<SettingsRow>().Select(row => (JsonNode?)new JsonObject
        {
            ["id"] = row.SettingId,
            ["visible"] = row.IsEffectivelyVisible,
            ["enabled"] = row.IsEffectivelyEnabled,
        }).ToArray()),
    };

    /// <summary>
    /// AC-7-02: OS 키 입력 없이 설정 화면이 있는 TopLevel 에 Esc 를 흘려보낸다. 실제 키와 같은 터널→버블 경로라
    /// 설정 화면이 처리하지 않으면 전역 EscCloseInput 이 활성 문서를 닫는다 — 문서 수 전후로 그 위험을 잰다.
    /// </summary>
    private static JsonNode? SettingsEsc()
    {
        if (OpenSettingsView() is not { } view || Avalonia.Controls.TopLevel.GetTopLevel(view) is not { } top)
        {
            throw new RemoteCommandException("설정 화면이 열려 있지 않습니다(sys.open view=Settings 먼저).");
        }

        var docsBefore = XkxkUi.Current.MainViewModel?.OpenDocuments.Count ?? 0;
        var args = new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Escape,
            KeyModifiers = Avalonia.Input.KeyModifiers.None,
        };
        top.RaiseEvent(args);
        return new JsonObject
        {
            ["handled"] = args.Handled,
            ["viewOpen"] = OpenSettingsView() is not null,
            ["docsBefore"] = docsBefore,
            ["docsAfter"] = XkxkUi.Current.MainViewModel?.OpenDocuments.Count ?? 0,
        };
    }

    private static JsonNode? SettingsWindow(JsonObject a)
    {
        if (OpenSettingsView() is not { DataContext: SettingsWindowViewModel vm } view)
        {
            throw new RemoteCommandException("설정 화면이 열려 있지 않습니다(sys.open view=Settings 먼저).");
        }

        if (a.Str("search") is { } search)
        {
            vm.SearchText = search;
        }

        if (a.EnumArg<SettingsCategoryId>("category") is { } category)
        {
            vm.SelectedCategory = vm.Categories.FirstOrDefault(c => c.Id == category)
                ?? throw new RemoteCommandException("지금 목록에 없는 범주: " + category);
        }

        if (a.Str("reset") is { Length: > 0 } reset)
        {
            if (reset == "all")
            {
                vm.ResetAll();
            }
            else if (Enum.TryParse<SettingsCategoryId>(reset, out var id))
            {
                vm.ResetCategory(id);
            }
            else
            {
                throw new RemoteCommandException("reset 은 all 또는 범주 이름입니다: " + reset);
            }
        }

        return SettingsWindowJson(view, vm);
    }

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("settings.list", "설정 창 인벤토리 전수(entries)·UiSettings 쓰기 가능 공개 속성(uiSettings)·인벤토리에 없는 속성(unmapped)", _ => SettingsList());
        r.Register("settings.window", "열린 설정 화면 조작·조회 [search=] [category=Appearance|Window|Language|Layout|System] [reset=all|범주] → selected·search·hasNoResults·categories·rows(id·visible·enabled)", SettingsWindow);
        r.Register("settings.esc", "열린 설정 화면의 TopLevel 에 Esc 키 이벤트(OS 입력 아님) → handled·viewOpen·docsBefore·docsAfter", _ => SettingsEsc());
        r.Register("settings.get", "UI 설정(테마·실제 테마·테마 Id·기준 글자 크기·토큰별 크기·폰트·Ctrl+휠 동작·패널 기능 panelFeature·활성 문서 배율·gridViewPool·gridCompactCells·gridDrawnCellText·prewarmViews)", _ => SettingsJson());
        r.Register("settings.set",
            "UI 설정 변경(준 값만): theme=System|Light|Dark themeId= baseFontSize=10~24 documentDefaultFontSize= fontName= wheelMode=FontSize|WindowZoom panelFeature=Auto|On|Off gridViewPool=true|false gridCompactCells=true|false gridDrawnCellText=true|false prewarmViews=true|false theme.style.locked=true|false [theme.style.id=] theme.motion.level=system|full|reduced|off", a =>
            {
                if (a.EnumArg<ThemeMode>("theme") is { } t) { Settings.Theme = t; }
                // 테마 Id 는 ThemeCatalog.Select 로(모드도 테마 기반에 맞춰 바뀜 — odev-5t 안내)
                if (a.Str("themeId") is { Length: > 0 } tid && !XkxkUi.Current.Themes.Select(tid)) { throw new RemoteCommandException("테마 없음: " + tid); }
                if (a.Num("documentDefaultFontSize") is { } dfs) { Settings.DocumentDefaultFontSize = dfs; }
                if (a.Num("baseFontSize") is { } fs) { Settings.BaseFontSize = fs; }
                if (a.Str("fontName") is { Length: > 0 } fn) { Settings.FontName = fn; }
                if (a.EnumArg<CtrlWheelMode>("wheelMode") is { } wm) { Settings.WheelMode = wm; }
                // 패널 기능(Auto|On|Off) — 잘못된 값은 조용히 무시하지 않고 거부한다(숫자 문자열도 거부). 값은 즉시 저장되므로 시험이 원복해야 한다
                if (a.Str("panelFeature") is { } pf)
                {
                    if (!Enum.TryParse<PanelFeatureMode>(pf, true, out var pfm) || !Enum.IsDefined(pfm) || char.IsDigit(pf.Trim().FirstOrDefault()))
                    {
                        throw new RemoteCommandException("panelFeature 는 Auto|On|Off 입니다: " + pf);
                    }

                    Settings.PanelFeatureMode = pfm;
                }

                if (a.Str("language") is { Length: > 0 } lang) { XkxkUi.Current.Localization.Language = lang; } // lang.set 별칭(UiSettings 가 Loc 변경을 따라가 저장)
                // 표 열기 시험 스위치(A16) — ?fresh(설정 저장소 격리)를 유지한 채 켬·끔을 비교하려고 원격으로 바꾼다. 다음에 여는 표부터 반영, 값은 저장되므로 시험이 원복해야 한다
                if (StrictBool(a, "gridViewPool") is { } gvp) { Settings.GridViewPool = gvp; }
                if (StrictBool(a, "gridCompactCells") is { } gcc) { Settings.GridCompactCells = gcc; } // A16 단일 요소 셀(다음에 여는 표부터)
                if (StrictBool(a, "gridDrawnCellText") is { } gdt) { Settings.GridDrawnCellText = gdt; } // K15 그리는 셀 글자(다음에 여는 표부터)
                if (StrictBool(a, "prewarmViews") is { } pwv) { Settings.PrewarmViews = pwv; }
                // 디자인 테마 시험 키 — 회사 잠금(theme.style.locked=true|false, 잠근 ID 는 theme.style.id=) · 움직임 수준(theme.motion.level=system|full|reduced|off)
                var policy = a.Str("theme.style.id") is { Length: > 0 } lockedId ? lockedId : null;
                if (StrictBool(a, "theme.style.locked") is { } lk)
                {
                    var map = EnsurePolicy().Map;
                    map["theme.style.locked"] = lk ? "true" : "false";
                    if (policy is not null) { map["theme.style.id"] = policy; }
                    DesignThemeResolver.Refresh();
                }

                if (a.Str("theme.motion.level") is { Length: > 0 } ml)
                {
                    var (ok, msg) = ThemeAutomation.SetMotionLevel(ml);
                    if (!ok) { throw new RemoteCommandException(msg); }
                }

                return SettingsJson();
            });
        r.Register("themes.get", "테마 카탈로그 상태(ThemeAutomation.DescribeJson — 목록·활성·토큰)", _ => JsonNode.Parse(ThemeAutomation.DescribeJson()));
        // 테마 편집 명령은 ThemeAutomation.TryRun 문자열 명령으로 위임(중복 구현 없음)
        foreach (var (name, verb, argKey, help) in ThemeVerbs)
        {
            r.Register(name, help, a =>
            {
                var arg = argKey is null ? "" : a.Str(argKey) ?? "";
                if (name == "theme.export" && a.Str("id") is { Length: > 0 } id) { arg += "|" + id; }
                ThemeAutomation.TryRun(arg.Length > 0 ? verb + ":" + arg : verb, out var ok, out var message);
                return ok ? JsonValue.Create(message) : throw new RemoteCommandException(message);
            });
        }

        r.Register("settings.fonts", "선택 가능한 폰트 이름 목록", _ =>
            new JsonArray(Settings.FontNames.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()));
        r.Register("input.ctrlWheel",
            "Ctrl+휠 재현 delta=1(위)|-1(아래)|0(Ctrl+0 초기화) [doc](생략=활성 문서) | target=main(메인 크롬 위 — 조절 불가 확인용) → handled", a =>
            {
                var delta = Math.Sign(a.Int("delta") ?? 0);
                // 실입력 대신 이벤트 원천 Visual 만 바꿔 SettingsInput.Step(실제 휠·키 처리와 같은 Step) 을 탄다
                var source = a.Str("target") == "main" ? VisualTreeInspector.Main() : VisualTreeInspector.DocumentHost(Doc(a));
                var before = SettingsJson();
                var handled = source is not null && xkxk.frameworks.ui.shell.Input.SettingsInput.Step(source, delta);
                return new JsonObject { ["handled"] = handled, ["before"] = before, ["after"] = SettingsJson() };
            });
    }
}
#endif

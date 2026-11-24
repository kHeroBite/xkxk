// 테마 카탈로그(UI-07) — 내장+사용자 테마 목록, 선택·복제·색 편집·이름 저장·삭제·JSON 내보내기/가져오기, 시스템 모드 짝 해석
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.Theming;

/// <summary>
/// 단일 인스턴스. 사용자 테마는 UiSettings 저장 JSON 의 UserThemesJson 필드에 함께 저장된다(App.SettingsStore 와 같은 저장소).
/// 사용자 테마가 바뀌면 Changed 를 올리고 UiSettings 가 구독해 저장한다.
/// 선택·색 편집은 UiSettings 를 거쳐 UiSettings.Apply() → ThemeApplier.Apply 로 반영된다(경로 1개).
/// </summary>
public sealed partial class ThemeCatalog
{
    private ThemeDefinition? _selecting; // Select 진행 중인 테마

    private ThemeCatalog()
    {
        foreach (var t in BuiltinThemes.Create())
        {
            Themes.Add(t);
        }

        // 언어 전환(Q17): 목록·콤보에 걸린 테마 표시명(내장 = 번역 이름 + 접미)을 즉시 갱신. 단일 인스턴스라 해제 불필요.
        Loc.Instance.LanguageChanged += () =>
        {
            foreach (var t in Themes)
            {
                t.NotifyDisplayName();
            }
        };
    }

    public static ThemeCatalog Current { get; } = new();

    /// <summary>내장 먼저, 이어서 사용자 테마.</summary>
    public ObservableCollection<ThemeDefinition> Themes { get; } = new();

    /// <summary>사용자 테마 추가·수정·삭제 시(저장 트리거).</summary>
    public event Action? Changed;

    public ThemeDefinition? Find(string? id) =>
        id is null ? null : Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 적용할 테마 결정(UiSettingsApplier.ApplyColorTheme 가 호출). System = OS 다크면 darkId, 아니면 lightId.
    /// Light/Dark = themeId 테마의 기반이 모드와 같으면 그 테마, 아니면 해당 기반의 짝(lightId/darkId).
    /// </summary>
    public ThemeDefinition Resolve(ThemeMode mode, string? themeId, string? lightId, string? darkId, bool osIsDark)
    {
        var light = Find(lightId) is { Base: ThemeBase.Light } l ? l : Find(BuiltinThemes.LightId)!;
        var dark = Find(darkId) is { Base: ThemeBase.Dark } d ? d : Find(BuiltinThemes.DarkId)!;
        var picked = Find(themeId);
        return mode switch
        {
            ThemeMode.Light => picked is { Base: ThemeBase.Light } ? picked : light,
            ThemeMode.Dark => picked is { Base: ThemeBase.Dark } ? picked : dark,
            _ => osIsDark ? dark : light,
        };
    }

    /// <summary>
    /// 지금 요청된 테마 = 설정값에서 정해지는 테마. 값을 바꾼 즉시 새 테마를 돌려준다. 적용은 스피너가 그려진 뒤로 미뤄지므로(UiSettingsApplier)
    /// 선택 표시·편집 대상·다음 테마 계산은 이 값을 쓰고, ThemeApplier.Active(적용 완료값)를 그 자리에서 읽지 않는다. 적용이 끝났다는 신호는 UiSettings.Changed 다.
    /// </summary>
    public ThemeDefinition Requested => _selecting ?? Resolve(UiSettings.Current);

    private ThemeDefinition Resolve(UiSettings s) =>
        Resolve(s.Theme, s.ThemeId, s.SystemLightThemeId, s.SystemDarkThemeId, s.AppInstance is { } app && ThemeApplier.IsOsDark(app));

    /// <summary>테마 선택(편집기·메뉴·자동화 --theme). 모드는 테마 기반으로 고정된다. 없으면 false.</summary>
    public bool Select(string id)
    {
        if (Find(id) is not { } theme)
        {
            return false;
        }

        using var _ = Diagnostics.PerfProbe.Current.TimeOperation("ui.theme.select"); // PERF-UI 계측(sync·다음 프레임)
        var s = UiSettings.Current;
        _selecting = theme; // Id 와 모드를 차례로 바꾸는 사이의 알림에 중간값(새 Id·옛 모드)이 보이지 않게 한다
        try
        {
            s.ThemeId = theme.Id;
            s.Theme = theme.Base == ThemeBase.Dark ? ThemeMode.Dark : ThemeMode.Light;
            s.Apply(); // 모드·Id 가 이미 같아 setter 가 반영하지 않은 경우에도 즉시 적용
        }
        finally
        {
            _selecting = null;
        }

        return true;
    }

    /// <summary>시스템 모드 짝 지정(null·기반 불일치는 무시). UiSettings setter 가 재적용·저장한다.</summary>
    public void SetSystemPair(string? lightId, string? darkId)
    {
        if (Find(lightId) is { Base: ThemeBase.Light } l)
        {
            UiSettings.Current.SystemLightThemeId = l.Id;
        }

        if (Find(darkId) is { Base: ThemeBase.Dark } d)
        {
            UiSettings.Current.SystemDarkThemeId = d.Id;
        }
    }

    /// <summary>목록상 다음 테마로 순환(자동화 --cmd theme-next).</summary>
    public ThemeDefinition SelectNext()
    {
        var next = Themes[(Themes.IndexOf(Requested) + 1) % Themes.Count]; // 적용 대기 중에 또 불려도 한 칸씩 나아간다
        Select(next.Id);
        return next;
    }

    /// <summary>복제 → 편집 가능한 사용자 테마 생성(이름 "원본 복사본", Id 고유).</summary>
    public ThemeDefinition Duplicate(ThemeDefinition source, string? name = null)
    {
        var baseName = source.NameKey is null ? source.Name : Loc.Tr(source.NameKey); // 내장 원본은 현재 언어 이름으로
        var copy = source.Clone(NewId(), UniqueName(name ?? Loc.Tr("theme.copyOf", baseName)));
        Themes.Add(copy);
        Changed?.Invoke();
        return copy;
    }

    /// <summary>사용자 테마의 토큰 색 변경. 활성 테마면 즉시 재적용(미리보기). 내장·형식 오류면 false.</summary>
    public bool SetColor(ThemeDefinition theme, string key, string hex)
    {
        if (theme.IsBuiltin || !Color.TryParse(hex, out var color))
        {
            return false;
        }

        using var _ = Diagnostics.PerfProbe.Current.TimeOperation("ui.theme.setcolor"); // PERF-UI 계측(색 편집 미리보기)
        theme.Colors[key] = color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        ReapplyIfActive(theme);
        Changed?.Invoke();
        return true;
    }

    /// <summary>사용자 테마 이름·기반 변경 저장.</summary>
    public bool Rename(ThemeDefinition theme, string name, ThemeBase? @base = null)
    {
        if (theme.IsBuiltin || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        theme.Name = name.Trim();
        if (@base is { } b && b != theme.Base)
        {
            var wasRequested = ReferenceEquals(Requested, theme); // 기반을 바꾸면 해석 결과가 달라지므로 먼저 읽는다
            theme.Base = b;
            if (wasRequested)
            {
                Select(theme.Id); // 기반이 바뀌면 모드도 따라간다
            }
        }

        theme.NotifyDisplayName(); // 목록 항목을 교체하면 묶인 목록·콤보가 선택을 잃으므로 표시 이름만 알린다
        Changed?.Invoke();
        return true;
    }

    /// <summary>사용자 테마 삭제(내장은 불가). 활성 테마였으면 같은 기반 내장 테마로 전환.</summary>
    public bool Delete(ThemeDefinition theme)
    {
        var wasRequested = ReferenceEquals(Requested, theme); // 목록에서 빼면 Id 로 찾지 못하므로 먼저 읽는다
        if (theme.IsBuiltin || !Themes.Remove(theme))
        {
            return false;
        }

        if (wasRequested)
        {
            Select(theme.Base == ThemeBase.Dark ? BuiltinThemes.DarkId : BuiltinThemes.LightId);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>사용자 테마·선택 상태 즉시 저장 요청(UiSettings 가 Changed 를 받아 저장).</summary>
    public void SaveNow() => Changed?.Invoke();

    /// <summary>테마 1개를 JSON 으로(내보내기 파일·클립보드 텍스트).</summary>
    public static string Export(ThemeDefinition theme) =>
        JsonSerializer.Serialize(theme, ThemeJsonContext.Default.ThemeDefinition);

    /// <summary>JSON 가져오기: 객체 1개 또는 배열. 항상 새 사용자 테마로 추가(Id 재발급). 가져온 목록 반환, 형식 오류면 예외.</summary>
    public IReadOnlyList<ThemeDefinition> Import(string json)
    {
        var text = json.Trim();
        var items = text.StartsWith('[')
            ? JsonSerializer.Deserialize(text, ThemeJsonContext.Default.ListThemeDefinition) ?? new List<ThemeDefinition>()
            : new List<ThemeDefinition> { JsonSerializer.Deserialize(text, ThemeJsonContext.Default.ThemeDefinition) ?? throw new JsonException("빈 테마") }; // i18n-ok 개발 오류(가져오기 실패 로그)
        var added = new List<ThemeDefinition>();
        foreach (var item in items)
        {
            var theme = Normalize(item);
            theme.Id = NewId();
            theme.Name = UniqueName(string.IsNullOrWhiteSpace(item.Name) ? Loc.Tr("theme.imported") : item.Name.Trim());
            Themes.Add(theme);
            added.Add(theme);
        }

        if (added.Count > 0)
        {
            Changed?.Invoke();
        }

        return added;
    }

    /// <summary>사용자 테마 전체 JSON(UiSettings 저장용).</summary>
    public string ExportUserThemesJson() =>
        JsonSerializer.Serialize(Themes.Where(t => !t.IsBuiltin).ToList(), ThemeJsonContext.Default.ListThemeDefinition);

    /// <summary>저장된 사용자 테마 복원(UiSettings 로드 시, 테마 적용 전에 호출). 기존 사용자 테마는 교체. Changed 미발생.</summary>
    public void LoadUserThemesJson(string? json)
    {
        foreach (var old in Themes.Where(t => !t.IsBuiltin).ToList())
        {
            Themes.Remove(old);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            foreach (var item in JsonSerializer.Deserialize(json, ThemeJsonContext.Default.ListThemeDefinition) ?? new List<ThemeDefinition>())
            {
                var theme = Normalize(item);
                if (string.IsNullOrWhiteSpace(theme.Id) || Find(theme.Id) is not null)
                {
                    theme.Id = NewId();
                }

                Themes.Add(theme);
            }
        }
        catch (JsonException)
        {
            // 손상된 사용자 테마는 무시(내장 테마로 계속 동작)
        }
    }

    /// <summary>사용자 테마로 정규화: 누락·형식 오류 토큰은 같은 기반 내장 테마 값으로 채우고 모르는 키는 버린다.</summary>
    private ThemeDefinition Normalize(ThemeDefinition item)
    {
        var baseline = Find(item.Base == ThemeBase.Dark ? BuiltinThemes.DarkId : BuiltinThemes.LightId)!;
        var theme = new ThemeDefinition { Id = item.Id, Name = item.Name, Base = item.Base, IsBuiltin = false };
        foreach (var key in ThemeTokens.Keys)
        {
            theme.Colors[key] = item.Colors.TryGetValue(key, out var hex) && Color.TryParse(hex, out _) ? hex : baseline.Colors[key];
        }

        ThemeTokens.FillMissing(item, theme, Find(BuiltinThemes.LightId)!, Find(BuiltinThemes.DarkId)!);
        NormalizeDesign(item, theme); // 요소별 디자인·기준 템플릿(ThemeCatalog.Custom.cs, REQ-101)
        return theme;
    }

    private void ReapplyIfActive(ThemeDefinition theme)
    {
        if (ReferenceEquals(Requested, theme))
        {
            // 색 편집: Id 가 같아도 반영돼야 하므로 force(셸 계약), 폰트 단계는 불필요(PERF-UI). 같은 색이면 ThemeApplier 서명 비교가 생략한다.
            UiSettings.Current.Apply(ApplyParts.Theme | ApplyParts.DocScope, force: true);
        }
    }

    private static string NewId() => "user-" + Guid.NewGuid().ToString("N")[..8];

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var n = 2; Themes.Any(t => t.Name == candidate); n++)
        {
            candidate = $"{name} {n}";
        }

        return candidate;
    }
}

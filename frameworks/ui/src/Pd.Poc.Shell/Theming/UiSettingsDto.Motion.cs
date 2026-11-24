// UI 설정 저장 형식의 결정 143 키(제목 탭 표시·메뉴 아이콘·즐겨찾기 영역·불러오는 중 아이콘·전환 효과 출처·배경 이미지·끌기 중 그림자) — 희소 저장: 기본값이면 쓰지 않는다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace Pd.Poc.Shell.Theming;

/// <summary>제목 탭 표시 방식(ui.shell.title_tab.display). 저장값은 UiKeyNames 의 snake 이름.</summary>
public enum TitleTabDisplay { IconTitle, Icon, Title }

/// <summary>제목줄 즐겨찾기 영역 자리(ui.shell.favorite_bar.position).</summary>
public enum FavoriteBarPosition { End, BeforeSdi }

/// <summary>즐겨찾기 아이콘 클릭 동작(ui.shell.favorite_bar.click).</summary>
public enum FavoriteBarClick { ActivateOrOpen, AlwaysNew }

/// <summary>표시 방식 title 일 때 불러오는 중 대체 표시(ui.shell.busy_icon.title_only).</summary>
public enum BusyTitleOnly { Inline, Underline, None }

/// <summary>배경 이미지 맞춤(ui.background.image.fit).</summary>
public enum BackgroundImageFit { Fill, Fit, Stretch, Tile, Center }

/// <summary>전환 효과를 쓰는 활성화 출처(ui.motion.switch_sources, 목록).</summary>
[Flags]
public enum MotionSwitchSources { None = 0, Keyboard = 1, TitleTab = 2, WindowList = 4, BodyClick = 8 }

/// <summary>enum ↔ 설정 키 값(snake 이름) 변환. 모르는 값·숫자 문자열은 기본값으로 읽는다(옛 저장본·손 편집 대비).</summary>
public static class UiKeyNames
{
    public static string Of<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) { sb.Append('_'); }
            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }

    public static T Parse<T>(string? text, T fallback) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text)) { return fallback; }
        var t = text.Trim();
        foreach (var v in Enum.GetValues<T>())
        {
            if (string.Equals(Of(v), t, StringComparison.OrdinalIgnoreCase)) { return v; }
        }

        return fallback;
    }

    /// <summary>플래그 목록 → 쉼표 구분 snake 이름(None 은 빈 문자열).</summary>
    public static string OfFlags(MotionSwitchSources value) =>
        string.Join(",", Enum.GetValues<MotionSwitchSources>().Where(f => f != MotionSwitchSources.None && value.HasFlag(f)).Select(f => Of(f)));

    /// <summary>쉼표 구분 목록 → 플래그. null 은 fallback, 빈 문자열은 None(전환 효과 출처 없음), 모르는 이름은 건너뛴다.</summary>
    public static MotionSwitchSources ParseFlags(string? text, MotionSwitchSources fallback)
    {
        if (text is null) { return fallback; }
        var result = MotionSwitchSources.None;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= Parse(part, MotionSwitchSources.None);
        }

        return result;
    }
}

/// <summary>결정 143 키 필드. 모두 null 이면 기본값이고 null 은 파일에 쓰지 않는다(옛 저장본은 필드가 없어 기본값으로 읽힌다).</summary>
public sealed partial class UiSettingsDto
{
    /// <summary>ui.shell.title_tab.display(icon·icon_title·title). null → icon_title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TitleTabDisplay { get; set; }

    /// <summary>ui.shell.title_tab.icon_only_width_dp(운영 값). null → 32.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? TitleTabIconOnlyWidthDp { get; set; }

    /// <summary>ui.menu.icon.visible. null → 켬.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? MenuIconVisible { get; set; }

    /// <summary>ui.shell.favorite_bar.visible. null → 켬.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? FavoriteBarVisible { get; set; }

    /// <summary>ui.shell.favorite_bar.position(end·before_sdi). null → end.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FavoriteBarPosition { get; set; }

    /// <summary>ui.shell.favorite_bar.click(activate_or_open·always_new). null → activate_or_open.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FavoriteBarClick { get; set; }

    /// <summary>ui.shell.favorite_bar.max_width_pct(운영 값). null → 30.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? FavoriteBarMaxWidthPct { get; set; }

    /// <summary>ui.shell.busy_icon.enabled. null → 켬.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? BusyIconEnabled { get; set; }

    /// <summary>ui.shell.busy_icon.show_delay_ms(운영 값). null → 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BusyIconShowDelayMs { get; set; }

    /// <summary>ui.shell.busy_icon.min_visible_ms(운영 값). null → 200.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BusyIconMinVisibleMs { get; set; }

    /// <summary>ui.shell.busy_icon.slow_seconds(운영 값). null → 10.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BusyIconSlowSeconds { get; set; }

    /// <summary>ui.shell.busy_icon.error_badge_seconds(운영 값). null → 5.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BusyIconErrorBadgeSeconds { get; set; }

    /// <summary>ui.shell.busy_icon.title_only(inline·underline·none). null → inline.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BusyIconTitleOnly { get; set; }

    /// <summary>ui.motion.switch_sources(쉼표 구분 목록). null → keyboard,title_tab,window_list. 빈 문자열은 '출처 없음'으로 저장된 값이다.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MotionSwitchSources { get; set; }

    /// <summary>ui.background.image.file(해시 이름만, 기기 저장). null → 배경 이미지 없음.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BackgroundImageFile { get; set; }

    /// <summary>ui.background.image.fit. null → fill.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BackgroundImageFit { get; set; }

    /// <summary>ui.background.image.opacity(0~1). null → 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? BackgroundImageOpacity { get; set; }

    /// <summary>ui.background.image.dim(0~0.8). null → 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? BackgroundImageDim { get; set; }

    /// <summary>ui.background.image.max_bytes(운영 값). null → 10485760.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? BackgroundImageMaxBytes { get; set; }

    /// <summary>ui.background.image.max_dim_px(운영 값, 디코드 축소 폭). null → 3840.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BackgroundImageMaxDimPx { get; set; }

    /// <summary>ui.background.image.max_source_px(운영 값, 원본 가로세로 상한). null → 16384.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BackgroundImageMaxSourcePx { get; set; }

    /// <summary>ui.background.image.formats(운영 값, 쉼표 구분 확장자). null → png,jpg,jpeg,bmp.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BackgroundImageFormats { get; set; }

    /// <summary>theme.style.effectLevel(theme·high·medium·low). null → theme. 효과 덮어쓰기(EffectOverrides)와 따로 둔다(D-591 해석 층).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EffectLevel { get; set; }

    /// <summary>ui.mdi.move_light.shadow(hide·keep) — 사용자가 직접 고른 값만. null → 고르지 않음(효과 단계 묶음·회사 값·hide 순서로 정해진다).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MdiMoveLightShadow { get; set; }

    /// <summary>운영 값 묶음을 옛 저장본에서 그대로 옮겨 쓴다(운영 값은 화면에서 바꾸지 않아 저장 때 읽은 값을 보존한다).</summary>
    internal void CopyOperatingValuesTo(UiSettingsDto target)
    {
        target.TitleTabIconOnlyWidthDp = TitleTabIconOnlyWidthDp;
        target.FavoriteBarMaxWidthPct = FavoriteBarMaxWidthPct;
        target.BusyIconShowDelayMs = BusyIconShowDelayMs;
        target.BusyIconMinVisibleMs = BusyIconMinVisibleMs;
        target.BusyIconSlowSeconds = BusyIconSlowSeconds;
        target.BusyIconErrorBadgeSeconds = BusyIconErrorBadgeSeconds;
        target.BackgroundImageMaxBytes = BackgroundImageMaxBytes;
        target.BackgroundImageMaxDimPx = BackgroundImageMaxDimPx;
        target.BackgroundImageMaxSourcePx = BackgroundImageMaxSourcePx;
        target.BackgroundImageFormats = BackgroundImageFormats;
    }
}

// 디자인 테마 탭 ViewModel(REQ-104, 18 문서 §화면) — 프리셋 카드 5종·효과 행(값 선택·되돌리기)·움직임 수준·추천 색 1클릭(D-375)·효과 전체 되돌리기·회사 잠금 표시
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>
/// 설계 정본 plan/09_화면설계/18_디자인테마_설계.md. 저장은 UiSettings 위임 속성(DesignThemeId·MotionLevel·SetEffectOverride·ClearEffectOverrides)만 쓰고,
/// 표시 값은 DesignThemeResolver(실효값·층·폴백·잠금)에서 읽는다. 색 테마는 추천 1클릭(ThemeCatalog.Select) 외에는 건드리지 않는다(색 직교).
/// </summary>
public sealed partial class DesignThemeViewModel : ObservableObject, IDisposable
{
    private static readonly string[] StateProps =
        { nameof(IsLocked), nameof(IsEditable), nameof(HasSuggestion), nameof(SuggestionText), nameof(FallbackText), nameof(HasFallback), nameof(HasOverrides) };

    private readonly UiSettings _settings = UiSettings.Current;
    private readonly ThemeCatalog _catalog = ThemeCatalog.Current;
    private bool _loading; // 상태 다시 읽는 중 — 선택 속성 setter 가 저장하지 않게
    private DesignThemeCard? _selectedCard;
    private DesignChoice? _selectedMotion;
    private string? _keptSuggestionFor; // '현재 색 유지'를 누른 디자인 테마 ID — 같은 테마에서는 제안을 다시 띄우지 않는다
    private string _status = "";

    public DesignThemeViewModel()
    {
        Cards = DesignThemes.All.Select(d => new DesignThemeCard(d)).ToList();
        Effects = DesignThemes.Effects.Select(s => new DesignEffectRow(this, s)).ToList();
        MotionLevels = DesignChoice.For(DesignThemes.Spec(DesignThemes.MotionLevelKey)!);
        ResetAllCommand = new RelayCommand(ResetAll);
        ApplySuggestionCommand = new RelayCommand(ApplySuggestion);
        KeepColorCommand = new RelayCommand(() => { _keptSuggestionFor = DesignThemeResolver.Current; Reload(); });
        DesignThemeResolver.Changed += Reload;
        _catalog.Changed += Reload;
        XkxkUi.Current.Localization.LanguageChanged += Reload;
        InitExtras(); // 효과 단계·배경 행(.Levels·.Motion partial)
        Reload();
    }

    public void Dispose()
    {
        DesignThemeResolver.Changed -= Reload;
        _catalog.Changed -= Reload;
        XkxkUi.Current.Localization.LanguageChanged -= Reload;
        DisposeExtras();
    }

    public IReadOnlyList<DesignThemeCard> Cards { get; }
    public IReadOnlyList<DesignEffectRow> Effects { get; }
    public IReadOnlyList<DesignChoice> MotionLevels { get; }
    public RelayCommand ResetAllCommand { get; }
    public RelayCommand ApplySuggestionCommand { get; }
    public RelayCommand KeepColorCommand { get; }

    /// <summary>회사 잠금(theme.style.locked) — 디자인 테마 선택·효과 덮어쓰기를 막는다. 움직임 수준(접근성)은 잠금과 무관.</summary>
    public bool IsLocked { get; private set; }
    public bool IsEditable => !IsLocked;

    /// <summary>선택 카드(실효 디자인 테마). 사용자가 고르면 저장한다(잠금 중에는 무시하고 실효 카드로 되돌린다 — 엣지 §9).</summary>
    public DesignThemeCard? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (!SetProperty(ref _selectedCard, value) || _loading || value is null)
            {
                return;
            }

            if (IsLocked)
            {
                Reload();
                return;
            }

            _settings.DesignThemeId = value.Id;
            Status = Loc.Tr("theme.style.status.selected", value.Name);
        }
    }

    public DesignChoice? SelectedMotionLevel
    {
        get => _selectedMotion;
        set
        {
            if (SetProperty(ref _selectedMotion, value) && !_loading && value is not null && Enum.TryParse<MotionLevel>(value.Value, true, out var level))
            {
                _settings.MotionLevel = level;
            }
        }
    }

    public bool HasSuggestion { get; private set; }
    public string SuggestionText { get; private set; } = "";
    public string FallbackText { get; private set; } = ""; // 지금 걸린 폴백 사유 문구(저장값은 보존되고 실효값만 낮아진다)
    public bool HasFallback => FallbackText.Length > 0;
    public bool HasOverrides => Effects.Any(r => r.IsOverridden);
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>효과 행 값 반영(null = 디자인 테마 값으로 되돌리기). 디자인 테마 값과 같으면 덮어쓰기를 지운다(희소 저장). 잠금·허용 목록 밖이면 무시.</summary>
    internal void Commit(DesignEffectRow row, string? value)
    {
        if (!IsLocked && value is null)
        {
            _settings.SetEffectOverride(row.Key, null);
            Status = Loc.Tr("theme.style.status.reset");
        }
        else if (!IsLocked && DesignThemes.TryNormalize(row.Key, value, out var v))
        {
            _settings.SetEffectOverride(row.Key, v == DesignThemeResolver.PresetValue(row.Key) ? null : v);
        }

        Reload();
    }

    private void ResetAll()
    {
        if (!IsLocked)
        {
            var had = _settings.EffectOverrides.Count > 0;
            _settings.ClearEffectOverrides();
            Status = Loc.Tr(had ? "theme.style.status.reset" : "theme.design.status.nothingToReset");
        }

        Reload();
    }

    private void ApplySuggestion()
    {
        if (DesignThemeResolver.SuggestedColorThemeId is { } id && _catalog.Find(id) is { } theme && _catalog.Select(id))
        {
            Status = Loc.Tr("theme.style.status.suggestApplied", theme.DisplayName);
        }

        Reload();
    }

    /// <summary>해석기·카탈로그·언어에서 상태를 다시 읽는다(행·카드 객체는 유지 — 선택 포커스를 잃지 않게). 문구 알림도 여기서 함께 올린다.</summary>
    private void Reload()
    {
        _loading = true;
        try
        {
            IsLocked = DesignThemeResolver.Locked;
            var current = DesignThemeResolver.Current;
            foreach (var refresh in Cards.Select(c => (Action)c.Refresh).Concat(MotionLevels.Select(m => (Action)m.RefreshText)))
            {
                refresh();
            }

            SelectedCard = Cards.FirstOrDefault(c => c.Id == current);
            SelectedMotionLevel = MotionLevels.FirstOrDefault(m => m.Value == DesignThemeResolver.MotionLevel);
            foreach (var r in Effects)
            {
                r.Load(IsLocked);
            }

            var sid = DesignThemeResolver.SuggestedColorThemeId;
            var suggested = sid is null ? null : _catalog.Find(sid); // 추천 색이 카탈로그에 없으면 제안하지 않는다
            HasSuggestion = suggested is not null && _keptSuggestionFor != current && !string.Equals(_catalog.Requested.Id, suggested.Id, StringComparison.OrdinalIgnoreCase);
            SuggestionText = suggested is null ? "" : Loc.Tr("theme.style.suggestColor", suggested.DisplayName);
            FallbackText = string.Join(" ", (DesignThemeResolver.FallbackReason ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r == "maxWindows" ? "theme.style.fallback.flat" : "theme.style.fallback." + r).Distinct().Select(Loc.Tr));
            ReloadExtras();
        }
        finally
        {
            _loading = false;
        }

        foreach (var p in StateProps)
        {
            OnPropertyChanged(p);
        }
    }
}

/// <summary>효과 행 1개 — 값 선택(허용 목록)·덮어씀 표시·폴백으로 낮아진 실효값·디자인 테마 값으로 되돌리기.</summary>
public sealed class DesignEffectRow : ObservableObject
{
    private readonly DesignThemeViewModel _owner;
    private readonly DesignEffectSpec _spec;
    private DesignChoice? _selected;
    private bool _loading;

    public DesignEffectRow(DesignThemeViewModel owner, DesignEffectSpec spec)
    {
        _owner = owner;
        _spec = spec;
        Options = DesignChoice.For(spec);
        ResetCommand = new RelayCommand(() => _owner.Commit(this, null));
    }

    public string Key => _spec.Key;
    public string Label => Loc.Tr(_spec.LabelKey);
    public IReadOnlyList<DesignChoice> Options { get; }
    public RelayCommand ResetCommand { get; }
    public bool IsOverridden { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public string LoweredText { get; private set; } = ""; // 폴백이 낮춘 실효값 문구(낮추지 않았으면 빈 문자열)
    public bool IsLowered => LoweredText.Length > 0;

    public DesignChoice? SelectedOption
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && !_loading && value is not null)
            {
                _owner.Commit(this, value.Value);
            }
        }
    }

    internal void Load(bool locked)
    {
        var stored = DesignThemeResolver.Stored(Key);
        var effective = DesignThemeResolver.Effective(Key);
        _loading = true;
        SelectedOption = Options.FirstOrDefault(o => o.Value == stored);
        _loading = false;
        IsOverridden = DesignThemeResolver.Source(Key) == "user";
        IsEnabled = !locked;
        LoweredText = effective != stored ? Loc.Tr(DesignThemes.ValueLabelKey(Key, effective)) : "";
        foreach (var p in new[] { nameof(Label), nameof(IsOverridden), nameof(IsEnabled), nameof(LoweredText), nameof(IsLowered) })
        {
            OnPropertyChanged(p); // Label 포함 — 언어 전환 뒤 Reload 가 새 문구를 올린다
        }

        foreach (var o in Options)
        {
            o.RefreshText();
        }
    }
}

/// <summary>프리셋 카드 1장 — 이름·설명·사용 중 표시와 정적 미리보기(모서리·그림자·효과 요약). 미리보기 값은 효과 표 설정 키에서 읽는다(REQ-99).</summary>
public sealed class DesignThemeCard : ObservableObject
{
    private static readonly string[] SummaryKeys = { "surface", "decor", "motion", "chrome" };
    private readonly DesignThemeDefinition _def;

    public DesignThemeCard(DesignThemeDefinition def) => _def = def;

    public string Id => _def.Id;
    public string Name => _def.LocalizedName;
    public string Description => _def.LocalizedDescription;

    /// <summary>미리보기 모서리. 디자인 테마가 정하지 않으면 null(뷰가 기본 모서리를 쓴다).</summary>
    public CornerRadius? PreviewCorner =>
        double.TryParse(_def.Element("*", "cornerRadius"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? new CornerRadius(r) : null;

    /// <summary>미리보기 그림자(soft·float 는 theme.style.shadow.&lt;값&gt; 설정 값, 그 밖은 없음). 형식 오류면 그림자 없이.</summary>
    public BoxShadows PreviewShadow
    {
        get
        {
            var key = "shadow." + _def.Effect("shadow");
            try
            {
                return DesignThemes.Spec(key) is null ? default : BoxShadows.Parse(DesignThemeResolver.Stored(key));
            }
            catch (FormatException)
            {
                return default;
            }
        }
    }

    /// <summary>효과 요약(면 재질·배경 장식·움직임·창 크롬 값 문구).</summary>
    public string Summary => string.Join(" · ", SummaryKeys.Select(k => Loc.Tr(DesignThemes.ValueLabelKey(k, _def.Effect(k) ?? DesignThemes.Spec(k)!.Default))));

    /// <summary>문구(언어 전환)·그림자(회사 값 변경)를 다시 읽게 알린다.</summary>
    internal void Refresh()
    {
        foreach (var p in new[] { nameof(Name), nameof(Description), nameof(Summary), nameof(PreviewShadow) })
        {
            OnPropertyChanged(p);
        }
    }
}

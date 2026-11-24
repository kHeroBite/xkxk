// 디자인 테마 탭 효과 단계(21 §5.3) — 단계 4칸(높음·중간(권장)·낮음·테마 기본)·단계 설명·상태 표시·단계 기본으로 되돌리기. 선택 항목 DesignChoice 도 여기 둔다
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

public sealed partial class DesignThemeViewModel
{
    /// <summary>편집기 단계 칸 순서(21 §5.3). 'theme' 는 단계 층을 건너뛰는 '테마 기본(사용자 지정)'이다.</summary>
    private static readonly string[] LevelOrder = { "high", "medium", "low", DesignThemes.LevelTheme };

    private static readonly string[] LevelProps =
        { nameof(LevelText), nameof(LevelDescription), nameof(LevelReducedText), nameof(IsLevelReduced), nameof(HasLevelOverrides) };

    private DesignChoice? _selectedLevel;
    private RelayCommand? _resetLevelCommand;

    /// <summary>효과 단계 선택 항목. 문구 키 theme.style.level.&lt;단계&gt;.</summary>
    public IReadOnlyList<DesignChoice> EffectLevels { get; } = LevelOrder.Select(id => new DesignChoice(id, "theme.style.level." + id)).ToList();

    /// <summary>'단계 기본으로 되돌리기' — 낱개 덮어쓰기(효과·끌기 중 그림자)와 배경 이미지 선택을 지운다(21 §5.1). 단계 선택은 그대로다.</summary>
    public RelayCommand ResetLevelCommand => _resetLevelCommand ??= new RelayCommand(ResetLevel);

    /// <summary>선택 단계. 고르면 theme.style.effectLevel 에 저장하고(덮어쓰기 저장소는 건드리지 않음), 잠금 중이면 무시하고 실효 단계로 되돌린다.</summary>
    public DesignChoice? SelectedEffectLevel
    {
        get => _selectedLevel;
        set
        {
            if (!SetProperty(ref _selectedLevel, value) || _loading || value is null)
            {
                return;
            }

            if (IsLocked)
            {
                Reload();
                return;
            }

            _settings.EffectLevel = value.Value;
            Status = Loc.Tr("theme.style.level.status.selected", value.Text);
        }
    }

    /// <summary>상태 표시: '테마 기본'·'높음' 처럼 단계 이름, 낱개가 있으면 '높음 + 사용자 지정 2개'.</summary>
    public string LevelText { get; private set; } = "";

    /// <summary>단계 설명(부하·바뀌는 항목).</summary>
    public string LevelDescription { get; private set; } = "";

    /// <summary>높음 단계인데 움직임 수준이 줄이기·끄기라 실효값이 낮아졌을 때의 문구(아니면 빈 문자열).</summary>
    public string LevelReducedText { get; private set; } = "";
    public bool IsLevelReduced => LevelReducedText.Length > 0;
    public bool HasLevelOverrides { get; private set; }

    private void ResetLevel()
    {
        if (!IsLocked)
        {
            var had = HasLevelOverrides || _settings.BackgroundImageFile is not null;
            _settings.ClearEffectOverrides();
            _settings.MdiMoveLightShadowUser = null;
            _settings.BackgroundImageFile = null; // 이미지 선택도 낱개 덮어쓰기다(파일은 다시 고를 때 같은 해시 이름으로 재사용)
            Status = Loc.Tr(had ? "theme.style.level.status.reset" : "theme.design.status.nothingToReset");
        }

        Reload();
    }

    /// <summary>Reload 안(_loading 중)에서 부른다 — 단계 선택·표시 문구를 해석기에서 다시 읽는다.</summary>
    private void ReloadLevels()
    {
        foreach (var l in EffectLevels)
        {
            l.RefreshText();
        }

        var (level, overrides) = DesignThemeResolver.LevelDisplay(_settings.MoveShadowUserOverrideCount);
        SelectedEffectLevel = EffectLevels.FirstOrDefault(l => l.Value == level);
        var name = Loc.Tr("theme.style.level." + level);
        HasLevelOverrides = overrides > 0;
        LevelText = HasLevelOverrides ? Loc.Tr("theme.style.level.withOverrides", name, overrides) : name;
        LevelDescription = Loc.Tr("theme.style.level." + level + ".desc");
        LevelReducedText = level == "high" && DesignThemeResolver.EffectiveMotionLevel is "reduced" or "off" ? Loc.Tr("theme.style.level.reducedByMotion") : "";
        foreach (var p in LevelProps)
        {
            OnPropertyChanged(p);
        }
    }
}

/// <summary>값 1개 + 표시 문구(다국어 키). 효과 값·움직임 수준·효과 단계·배경 맞춤 선택 목록 항목.</summary>
public sealed class DesignChoice : ObservableObject
{
    public DesignChoice(string value, string labelKey) => (Value, LabelKey) = (value, labelKey);
    public string Value { get; }
    public string LabelKey { get; }
    public string Text => Loc.Tr(LabelKey);

    /// <summary>키 1행의 허용 목록 → 선택 항목(값 문구 키는 DesignThemes.ValueLabelKey).</summary>
    public static IReadOnlyList<DesignChoice> For(DesignEffectSpec spec) => spec.Allowed.Select(v => new DesignChoice(v, DesignThemes.ValueLabelKey(spec.Key, v))).ToList();

    public void RefreshText() => OnPropertyChanged(nameof(Text));
    public override string ToString() => Text;
}

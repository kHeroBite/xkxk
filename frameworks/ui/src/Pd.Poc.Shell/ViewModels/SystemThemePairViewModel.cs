// 테마 편집기의 시스템 밝음/어두움 따라가기 짝 선택 하위 뷰모델 — ThemeEditorViewModel 에서 분리
using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels;

/// <summary>OS 라이트/다크를 따라 짝 테마를 적용하는 설정. 선택 해제 시 고정할 테마는 호출부가 selected 로 알려 준다.</summary>
public sealed class SystemThemePairViewModel : ObservableObject
{
    private readonly ThemeCatalog _catalog;
    private readonly Func<ThemeDefinition?> _selected;

    public SystemThemePairViewModel(ThemeCatalog catalog, Func<ThemeDefinition?> selected)
    {
        _catalog = catalog;
        _selected = selected;
    }

    /// <summary>체크 = OS 라이트/다크를 따라 아래 짝 테마 적용. 해제 = 선택 테마 고정.</summary>
    public bool FollowSystem
    {
        get => UiSettings.Current.Theme == ThemeMode.System;
        set
        {
            if (value)
            {
                UiSettings.Current.Theme = ThemeMode.System;
            }
            else if (_selected() is { } t)
            {
                _catalog.Select(t.Id);
            }

            OnPropertyChanged();
        }
    }

    public ThemeDefinition[] LightThemes => _catalog.Themes.Where(t => t.Base == ThemeBase.Light).ToArray();

    public ThemeDefinition[] DarkThemes => _catalog.Themes.Where(t => t.Base == ThemeBase.Dark).ToArray();

    /// <summary>시스템 모드에서 OS 가 라이트일 때 쓸 테마.</summary>
    public ThemeDefinition? SystemLightTheme
    {
        get => _catalog.Find(UiSettings.Current.SystemLightThemeId) ?? _catalog.Find(BuiltinThemes.LightId);
        set
        {
            if (value is not null)
            {
                _catalog.SetSystemPair(value.Id, null);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>시스템 모드에서 OS 가 다크일 때 쓸 테마.</summary>
    public ThemeDefinition? SystemDarkTheme
    {
        get => _catalog.Find(UiSettings.Current.SystemDarkThemeId) ?? _catalog.Find(BuiltinThemes.DarkId);
        set
        {
            if (value is not null)
            {
                _catalog.SetSystemPair(null, value.Id);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>테마 선택이 바뀌어 따라가기 체크를 다시 읽게 알린다.</summary>
    public void RefreshFollow() => OnPropertyChanged(nameof(FollowSystem));

    /// <summary>목록·언어가 바뀌어 짝 콤보(새 배열 → 항목 다시 그림)를 다시 읽게 알린다.</summary>
    public void Refresh()
    {
        foreach (var name in new[] { nameof(LightThemes), nameof(DarkThemes), nameof(SystemLightTheme), nameof(SystemDarkTheme) })
        {
            OnPropertyChanged(name);
        }
    }
}

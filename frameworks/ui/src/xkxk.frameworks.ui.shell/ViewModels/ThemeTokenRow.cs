// 테마 편집기 토큰 1행 ViewModel — 표시명(현재 언어)·hex 입력·견본 색·ColorPicker 값. 내장 테마는 읽기 전용
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>토큰 1행: 표시명·hex 입력·견본 색. 내장 테마는 읽기 전용.</summary>
public sealed class ThemeTokenRow : ObservableObject
{
    private readonly ThemeDefinition _theme;
    private string _hex;
    private bool _isValid = true;

    public ThemeTokenRow(ThemeDefinition theme, string key)
    {
        _theme = theme;
        Key = key;
        _hex = theme.Colors.TryGetValue(key, out var h) ? h : "#000000";
        Swatch = new SolidColorBrush(theme.GetColor(key, Colors.Transparent));
    }

    public string Key { get; }

    /// <summary>이 토큰만 기준 템플릿 색으로 되돌린다(REQ-101). 내장 테마(읽기 전용)면 실행 불가.</summary>
    public IRelayCommand ResetToTemplateCommand => _resetCommand ??= new RelayCommand(
        () =>
        {
            var template = ThemeCatalog.Current.BaseTemplateOf(_theme);
            if (template.Colors.TryGetValue(Key, out var hex))
            {
                Hex = hex;
            }
        },
        () => !IsReadOnly);

    private IRelayCommand? _resetCommand;

    /// <summary>표시명 — 읽을 때마다 현재 언어(theme.token.&lt;Key&gt;). 언어 전환 시 RefreshLabel 로 다시 읽게 한다.</summary>
    public string Label => ThemeTokens.LabelOf(Key);

    public bool IsReadOnly => _theme.IsBuiltin;

    /// <summary>견본(인스턴스 고정, Color 만 갱신).</summary>
    public SolidColorBrush Swatch { get; }

    public bool IsValid
    {
        get => _isValid;
        private set => SetProperty(ref _isValid, value);
    }

    /// <summary>언어 전환 후 표시명 갱신(입력 중인 hex·검증 상태는 유지).</summary>
    public void RefreshLabel() => OnPropertyChanged(nameof(Label));

    /// <summary>입력 즉시 검증·적용(#RGB·#RRGGBB·#AARRGGBB·색 이름). 형식 오류면 IsValid=false, 테마 값 유지.</summary>
    public string Hex
    {
        get => _hex;
        set
        {
            if (!SetProperty(ref _hex, value ?? ""))
            {
                return;
            }

            IsValid = ThemeCatalog.Current.SetColor(_theme, Key, _hex.Trim());
            if (IsValid)
            {
                Swatch.Color = _theme.GetColor(Key, Swatch.Color);
                OnPropertyChanged(nameof(Color));
            }
        }
    }

    /// <summary>ColorPicker 양방향 값. 설정하면 hex 로 바꿔 Hex 경로(검증·적용)를 그대로 탄다.</summary>
    public Color Color
    {
        get => Swatch.Color;
        set => Hex = value.A == 255 ? $"#{value.R:X2}{value.G:X2}{value.B:X2}" : $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";
    }
}

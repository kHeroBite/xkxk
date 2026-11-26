// 테마 요소별 디자인 속성 1행·요소 목록 항목 ViewModel(REQ-101) — 요소·속성·현재값·덮어씀·잠금·대비 경고 표시, 값 편집기 종류, 속성 되돌리기 명령
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>요소 목록 항목(표시명은 현재 언어, 덮어씀 표시). 행과 같은 편집 패널 모델이라 이 파일에 둔다.</summary>
public sealed class ThemeDesignElement : ObservableObject
{
    private bool _isOverridden;

    internal ThemeDesignElement(string key) => Key = key;

    public string Key { get; }

    public string Label => ThemeElements.LabelOf(Key);

    /// <summary>그 요소에 사용자 덮어쓰기(디자인 값 또는 색)가 하나라도 있음.</summary>
    public bool IsOverridden { get => _isOverridden; internal set => SetProperty(ref _isOverridden, value); }

    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// 속성 1행. 색 행(PropKey = 색 역할, 매핑 PdBrush* 토큰)과 디자인 행(PropKey = fontFamily 등) 두 종류다.
/// 값 변경·되돌리기는 소유 VM(ThemeDesignViewModel)을 거쳐 ThemeCatalog 로만 간다(레이어 분리 — 행은 저장소를 직접 만지지 않는다).
/// </summary>
public sealed class ThemeDesignRow : ObservableObject
{
    private readonly ThemeDesignViewModel _owner;
    private readonly DesignPropSpec? _spec;
    private string _value = "";
    private string _effective = "";
    private bool _isValid = true;
    private bool _isOverridden;
    private bool _isLocked;
    private bool _canReset;
    private bool _showsContrastWarn;

    /// <summary>디자인 속성 행.</summary>
    internal ThemeDesignRow(ThemeDesignViewModel owner, string el, string prop)
    {
        _owner = owner;
        ElementKey = el;
        PropKey = prop;
        _spec = ThemeElements.Prop(prop);
        Swatch = new SolidColorBrush(Colors.Transparent);
        ResetCommand = new RelayCommand(() => _owner.ResetRow(this), () => CanReset);
        WeightOptions = _spec?.Kind == DesignPropKind.Weight ? BuildWeightOptions(el, prop) : Array.Empty<LocalizedOption>();
    }

    /// <summary>색 행. token = 요소 색 역할에 매핑된 기존 테마 색 토큰.</summary>
    internal ThemeDesignRow(ThemeDesignViewModel owner, string el, string role, string token)
        : this(owner, el, role)
    {
        Token = token;
    }

    public string ElementKey { get; }

    /// <summary>디자인 속성 키 또는 색 역할(background·foreground·border·accent·stripe).</summary>
    public string PropKey { get; }

    /// <summary>색 행이면 매핑 토큰(PdBrush*), 디자인 행이면 null.</summary>
    public string? Token { get; }

    /// <summary>자동화·시험 식별자 "&lt;요소&gt;.&lt;속성&gt;"(뷰 AutomationId 접미).</summary>
    public string AutomationKey => ThemeElements.DesignKey(ElementKey, PropKey);

    public DesignPropKind? Kind => Token is null ? _spec?.Kind : null;

    /// <summary>표시명(현재 언어). 색 행 theme.design.color.&lt;역할&gt;, 디자인 행 theme.design.prop.&lt;속성&gt;.</summary>
    public string Label => Token is null ? ThemeElements.PropLabelOf(PropKey) : Loc.Tr(ThemeElements.SettingPrefix + "color." + PropKey);

    // 편집기 종류(뷰는 하나만 보인다)
    public bool IsColorEditor => Token is not null;
    public bool IsFontEditor => Kind == DesignPropKind.Family;
    public bool IsWeightEditor => Kind == DesignPropKind.Weight;
    public bool IsNumberEditor => Kind is DesignPropKind.SizeOffset or DesignPropKind.Corner or DesignPropKind.Border;
    public bool IsTextEditor => Kind == DesignPropKind.Padding;

    /// <summary>숫자 편집기 범위 — ThemeElements 표의 값(REQ-99, 코드에 박지 않음).</summary>
    public decimal Minimum => (decimal)(_spec?.Min ?? 0);
    public decimal Maximum => (decimal)(_spec?.Max ?? 0);
    public decimal Increment => 1;

    /// <summary>글꼴 후보(편집 가능 콤보). 웹은 번들 글꼴만 들어 있다(UiSettings.FontNames).</summary>
    public IReadOnlyList<string> FontOptions => IsFontEditor ? XkxkUi.Current.Settings.FontNames : Array.Empty<string>();

    /// <summary>굵기 후보. 항목은 ThemeElements.TryNormalize 가 받아 주는 FontWeight 이름만(목록을 따로 박지 않음).</summary>
    public IReadOnlyList<LocalizedOption> WeightOptions { get; }

    public SolidColorBrush Swatch { get; }

    public RelayCommand ResetCommand { get; }

    /// <summary>편집 값. 색 = hex, 글꼴 = 이름, 여백 = "n" · "좌우,상하" · "좌,위,우,아래". 입력 즉시 검증·반영, 형식 오류면 IsValid=false(테마 값 유지).</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value ?? ""))
            {
                return;
            }

            IsValid = _owner.Commit(this, _value.Trim());
            NotifyDerived();
        }
    }

    /// <summary>ColorPicker 양방향 값. 설정하면 hex 로 바꿔 Value 경로를 탄다.</summary>
    public Color Color
    {
        get => Swatch.Color;
        set => Value = value.A == 255 ? $"#{value.R:X2}{value.G:X2}{value.B:X2}" : $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    /// <summary>NumericUpDown 양방향 값. 비우면(null) 그 속성 되돌리기.</summary>
    public decimal? Number
    {
        get => decimal.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
        set
        {
            if (value is { } n)
            {
                Value = n.ToString("0", CultureInfo.InvariantCulture);
            }
            else if (CanReset)
            {
                _owner.ResetRow(this);
            }
        }
    }

    /// <summary>굵기 콤보 선택. 선택 항목 키 끝(theme.design.weight.&lt;이름&gt;)이 값이다.</summary>
    public LocalizedOption? SelectedWeight
    {
        get => WeightOptions.FirstOrDefault(o => string.Equals(WeightName(o), _value, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
            {
                Value = WeightName(value);
            }
        }
    }

    /// <summary>실효값 표시(설정 계층 합성 결과). 상속이면 theme.design.inherit 문구.</summary>
    public string EffectiveValue { get => _effective; private set => SetProperty(ref _effective, value); }

    public bool IsValid { get => _isValid; private set => SetProperty(ref _isValid, value); }

    /// <summary>사용자 층이 값을 정함(덮어씀 점 표시).</summary>
    public bool IsOverridden { get => _isOverridden; private set => SetProperty(ref _isOverridden, value); }

    /// <summary>회사 잠금(자물쇠, 편집기 비활성). 저장된 사용자 값은 지우지 않고 무시된다.</summary>
    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

    public bool IsEnabled => !IsLocked;

    public bool CanReset
    {
        get => _canReset;
        private set
        {
            if (SetProperty(ref _canReset, value))
            {
                ResetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>글자색·바탕색 행에 요소 대비 경고를 함께 표시한다(차단 없음, D-354).</summary>
    public bool ShowsContrastWarn { get => _showsContrastWarn; private set => SetProperty(ref _showsContrastWarn, value); }

    /// <summary>보조 기술용 상태 이름(값·덮어씀·잠금·경고).</summary>
    public string StateText
    {
        get
        {
            var parts = new List<string> { Loc.Tr("theme.design.state.value", EffectiveValue) };
            if (IsOverridden)
            {
                parts.Add(Loc.Tr("theme.design.state.overridden"));
            }

            if (IsLocked)
            {
                parts.Add(Loc.Tr("theme.design.state.locked"));
            }

            if (ShowsContrastWarn)
            {
                parts.Add(Loc.Tr("theme.design.state.contrastWarn"));
            }

            if (!IsValid)
            {
                parts.Add(Loc.Tr("theme.design.state.invalid"));
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// 테마 값에서 상태를 다시 읽는다. keepInput = 입력 중인 행(사용자가 친 글자·검증 상태를 덮지 않는다).
    /// stored = 편집 값, effective = 실효값, contrastWarn = 요소 대비 경고.
    /// </summary>
    internal void Load(string stored, string effective, bool overridden, bool locked, bool canReset, bool contrastWarn, bool keepInput)
    {
        if (!keepInput)
        {
            _value = stored;
            IsValid = true;
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(Number));
            OnPropertyChanged(nameof(SelectedWeight));
        }

        EffectiveValue = effective.Length == 0 ? Loc.Tr("theme.design.inherit") : effective;
        IsOverridden = overridden;
        IsLocked = locked;
        CanReset = canReset;
        ShowsContrastWarn = contrastWarn && PropKey is "foreground" or "background";
        if (Token is not null && Color.TryParse(keepInput ? _value : stored, out var c))
        {
            Swatch.Color = c;
            OnPropertyChanged(nameof(Color));
        }

        OnPropertyChanged(nameof(StateText));
    }

    /// <summary>언어 전환: 표시명·상태 문구 갱신(굵기 후보는 LocalizedOption 이 스스로 갱신).</summary>
    internal void RefreshLabel()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(StateText));
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(SelectedWeight));
        OnPropertyChanged(nameof(StateText));
    }

    private static string WeightName(LocalizedOption o) => o.Key[(o.Key.LastIndexOf('.') + 1)..];

    /// <summary>FontWeight 이름 중 TryNormalize 가 받는 것만, 굵기 순서로(같은 값의 별칭 이름이 있어 값이 아니라 이름으로 거른다).</summary>
    /// <remarks>LocalizedOption 은 언어 구독을 해제하지 않으므로 요소마다 1회만 만들어 재사용한다.</remarks>
    private static IReadOnlyList<LocalizedOption> BuildWeightOptions(string el, string prop) =>
        WeightCache.TryGetValue(el, out var cached) ? cached : WeightCache[el] = CreateWeightOptions(el, prop);

    private static readonly Dictionary<string, IReadOnlyList<LocalizedOption>> WeightCache = new(StringComparer.Ordinal);

    private static IReadOnlyList<LocalizedOption> CreateWeightOptions(string el, string prop) =>
        Enum.GetNames<FontWeight>()
            .Select(name => ThemeElements.TryNormalize(el, prop, name, out var n) ? n : "")
            .Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderBy(n => (int)Enum.Parse<FontWeight>(n))
            .Select(n => new LocalizedOption(ThemeElements.SettingPrefix + "weight." + n))
            .ToList();
}

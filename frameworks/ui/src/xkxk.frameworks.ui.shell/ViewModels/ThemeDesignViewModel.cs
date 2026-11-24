// 테마 요소별 디자인 편집 ViewModel(REQ-101) — 요소 선택·속성 행·즉시 반영(ThemeCatalog.SetDesign·SetColor)·되돌리기 3단(전체는 확인)·회사 잠금·대비 경고(차단 없음)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>
/// 설계 정본 plan/09_화면설계/17_테마_요소별_사용자지정_설계.md §5~§9. 편집 대상은 늘 ThemeCatalog.Requested 이다.
/// 내장 선택 중 첫 변경은 카탈로그가 사용자 사본을 만들어 선택한다(D-353) — 그 뒤 afterChange 로 편집기에 알린다.
/// 확인 대화상자는 뷰에 두지 않고 이 VM 의 확인 상태(IsConfirmingResetAll)로 표현한다(레이어 분리).
/// </summary>
public sealed class ThemeDesignViewModel : ObservableObject, IDisposable
{
    private readonly ThemeCatalog _catalog = ThemeCatalog.Current;
    private readonly Action<bool>? _afterChange;
    private ThemeDesignElement? _selected;
    private bool _busy; // 자기 호출 중 — Catalog.Changed 재진입 갱신 생략(끝난 뒤 한 번만 갱신)
    private bool _contrastWarn;
    private string _contrastText = "";
    private bool _isConfirmingResetAll;
    private string _status = "";

    /// <param name="afterChange">이 VM 이 테마를 바꾼 뒤 호출(인자 = 색도 바뀜). 편집기가 선택 동기화·색 토큰 표 갱신에 쓴다.</param>
    public ThemeDesignViewModel(Action<bool>? afterChange = null)
    {
        _afterChange = afterChange;
        Elements = ThemeElements.All.Select(k => new ThemeDesignElement(k)).ToList();
        ResetElementCommand = new RelayCommand(ResetElement);
        ResetAllCommand = new RelayCommand(() => IsConfirmingResetAll = true);
        ConfirmResetAllCommand = new RelayCommand(ConfirmResetAll);
        CancelResetAllCommand = new RelayCommand(() => IsConfirmingResetAll = false);
        BackCommand = new RelayCommand(() => SelectedElement = null);
        _catalog.Changed += OnCatalogChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        SelectedElement = Elements.FirstOrDefault(); // PC형·태블릿형 2열의 첫 화면. 모바일형 1열은 뷰가 BackCommand 로 목록부터 보인다
    }

    public void Dispose()
    {
        _catalog.Changed -= OnCatalogChanged;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }

    public IReadOnlyList<ThemeDesignElement> Elements { get; }

    public ObservableCollection<ThemeDesignRow> Rows { get; } = new();

    public RelayCommand ResetElementCommand { get; }
    public RelayCommand ResetAllCommand { get; }
    public RelayCommand ConfirmResetAllCommand { get; }
    public RelayCommand CancelResetAllCommand { get; }

    /// <summary>모바일형(NAV) 1열: 속성 화면 → 요소 목록.</summary>
    public RelayCommand BackCommand { get; }

    /// <summary>편집 대상(지금 요청된 테마).</summary>
    internal ThemeDefinition Target => _catalog.Requested;

    public ThemeDesignElement? SelectedElement
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value))
            {
                return;
            }

            BuildRows();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ElementLabel));
        }
    }

    public bool HasSelection => _selected is not null;

    public string ElementLabel => _selected?.Label ?? "";

    /// <summary>내장 선택 중에도 편집할 수 있다(첫 변경 때 자동 사본). 모든 요소 잠금(*)일 때만 false.</summary>
    public bool IsEditable => !ThemeElements.IsLocked("*", "*");

    public bool IsElementOverridden => _selected?.IsOverridden ?? false;

    /// <summary>선택 요소 글자색 대 바탕색 대비 미달(경고만 — 저장·적용은 그대로, D-354).</summary>
    public bool ContrastWarn { get => _contrastWarn; private set => SetProperty(ref _contrastWarn, value); }

    public string ContrastText { get => _contrastText; private set => SetProperty(ref _contrastText, value); }

    public bool IsConfirmingResetAll { get => _isConfirmingResetAll; private set => SetProperty(ref _isConfirmingResetAll, value); }

    public string ResetAllConfirmText => Loc.Tr("theme.design.resetAll.confirm", _catalog.BaseTemplateOf(Target).LocalizedName);

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>편집기 선택 변경·외부 변경 뒤 행 상태를 다시 읽는다(행 객체는 유지 — 입력 포커스를 잃지 않게).</summary>
    public void Refresh() => RefreshState(null);

    /// <summary>행 값 반영. 잠금·형식 오류면 false(테마 값 유지). 색은 hex 검증 뒤에만 사본을 만든다.</summary>
    internal bool Commit(ThemeDesignRow row, string value)
    {
        if (row.IsLocked)
        {
            return false;
        }

        var wasBuiltin = Target.IsBuiltin;
        var ok = RunOwn(() => row.Token is { } token
            ? Color.TryParse(value, out _) && _catalog.SetColor(_catalog.EnsureEditable(), token, value)
            : _catalog.SetDesign(row.ElementKey, row.PropKey, value));
        if (ok)
        {
            if (wasBuiltin && !Target.IsBuiltin)
            {
                Status = Loc.Tr("theme.design.status.customized", Target.DisplayName);
            }

            AfterOwnChange(row.Token is not null, row);
        }

        return ok;
    }

    /// <summary>속성 되돌리기: 디자인 키 1개 삭제, 색은 기준 템플릿 색으로(17 §6).</summary>
    internal void ResetRow(ThemeDesignRow row)
    {
        var t = Target;
        if (t.IsBuiltin || row.IsLocked)
        {
            return;
        }

        RunOwn(() => row.Token is { } token
            ? _catalog.BaseTemplateOf(t).Colors.TryGetValue(token, out var hex) && _catalog.SetColor(t, token, hex)
            : _catalog.ResetDesign(row.ElementKey, row.PropKey));
        Status = Loc.Tr("theme.design.status.reset", row.Label);
        AfterOwnChange(row.Token is not null, null);
    }

    private void ResetElement()
    {
        if (_selected is not { } el)
        {
            return;
        }

        var ok = RunOwn(() => _catalog.ResetDesign(el.Key));
        Status = ok ? Loc.Tr("theme.design.status.resetElement", el.Label) : Loc.Tr("theme.design.status.nothingToReset");
        AfterOwnChange(true, null);
    }

    private void ConfirmResetAll()
    {
        IsConfirmingResetAll = false;
        var ok = RunOwn(_catalog.ResetAll);
        Status = ok ? Loc.Tr("theme.design.status.resetAll") : Loc.Tr("theme.design.status.nothingToReset");
        AfterOwnChange(true, null);
    }

    /// <summary>카탈로그 호출 1회를 자기 호출로 감싼다(그 사이 Catalog.Changed 갱신 생략).</summary>
    private bool RunOwn(Func<bool> op)
    {
        _busy = true;
        try
        {
            return op();
        }
        finally
        {
            _busy = false;
        }
    }

    private void AfterOwnChange(bool colorsChanged, ThemeDesignRow? editing)
    {
        RefreshState(editing);
        _afterChange?.Invoke(colorsChanged);
    }

    /// <summary>다른 경로(색 토큰 표·가져오기·원격 명령)로 바뀐 값을 따라간다. 자기 호출 중에는 끝난 뒤 한 번만 갱신한다.</summary>
    private void OnCatalogChanged()
    {
        if (!_busy)
        {
            RefreshState(null);
        }
    }

    private void OnLanguageChanged()
    {
        foreach (var el in Elements)
        {
            el.RefreshLabel();
        }

        foreach (var row in Rows)
        {
            row.RefreshLabel();
        }

        OnPropertyChanged(nameof(ElementLabel));
        OnPropertyChanged(nameof(ResetAllConfirmText));
        RefreshState(null);
    }

    private void BuildRows()
    {
        Rows.Clear();
        if (_selected is not { } el)
        {
            RefreshState(null);
            return;
        }

        foreach (var (role, token) in ThemeElements.ColorRoles(el.Key))
        {
            Rows.Add(new ThemeDesignRow(this, el.Key, role, token));
        }

        foreach (var prop in ThemeElements.Props(el.Key))
        {
            Rows.Add(new ThemeDesignRow(this, el.Key, prop));
        }

        RefreshState(null);
    }

    /// <summary>요소 덮어씀·대비·행 상태를 테마에서 다시 읽는다. editing = 입력 중인 행(친 글자 유지).</summary>
    private void RefreshState(ThemeDesignRow? editing)
    {
        var t = Target;
        var template = _catalog.BaseTemplateOf(t);
        foreach (var el in Elements)
        {
            el.IsOverridden = !t.IsBuiltin && (ThemeElements.Props(el.Key).Any(p => ThemeElements.Resolve(t, el.Key, p, out _) == DesignLayer.User)
                || ThemeElements.ColorTokens(el.Key).Any(token => ColorOverridden(t, template, token)));
        }

        var warn = false;
        if (_selected is { } sel)
        {
            var (ratio, min, w) = ThemeElements.Contrast(t, sel.Key, EffectiveFontSize(t, sel.Key));
            warn = w;
            ContrastText = Loc.Tr(w ? "theme.design.contrast.warn" : "theme.design.contrast.ok",
                ratio.ToString("0.0", CultureInfo.CurrentCulture), min.ToString("0.#", CultureInfo.CurrentCulture));
        }
        else
        {
            ContrastText = "";
        }

        ContrastWarn = warn;
        foreach (var row in Rows)
        {
            LoadRow(row, t, template, warn, ReferenceEquals(row, editing));
        }

        OnPropertyChanged(nameof(IsElementOverridden));
        OnPropertyChanged(nameof(ResetAllConfirmText));
    }

    private static void LoadRow(ThemeDesignRow row, ThemeDefinition t, ThemeDefinition template, bool warn, bool keepInput)
    {
        var locked = ThemeElements.IsLocked(row.ElementKey, row.PropKey);
        if (row.Token is { } token)
        {
            var hex = t.Colors.TryGetValue(token, out var h) ? h : "";
            var overridden = ColorOverridden(t, template, token);
            row.Load(hex, hex, overridden, locked, overridden && !locked, warn, keepInput);
            return;
        }

        var layer = ThemeElements.Resolve(t, row.ElementKey, row.PropKey, out var effective);
        var stored = !t.IsBuiltin && t.Design.ContainsKey(ThemeElements.DesignKey(row.ElementKey, row.PropKey));
        var shown = row.Kind == DesignPropKind.Weight && ThemeDesignApplier.Convert(t, ThemeElements.Element(row.ElementKey)!, row.PropKey, effective, UiSettings.Current) is FontWeight w // 굵기는 실제 그리는 굵기(§3 실효값 — 합성 금지 강등, 예 Android 번들 글꼴은 Regular 뿐이라 Bold → Normal)
            ? Enum.GetNames<FontWeight>().FirstOrDefault(n => Enum.Parse<FontWeight>(n) == w && ThemeElements.TryNormalize(row.ElementKey, row.PropKey, n, out _)) ?? effective : effective;
        row.Load(effective, shown, layer == DesignLayer.User, locked, stored && !locked, warn, keepInput);
    }

    private static bool ColorOverridden(ThemeDefinition t, ThemeDefinition template, string token) =>
        !t.IsBuiltin && t.Colors.TryGetValue(token, out var hex) && template.Colors.TryGetValue(token, out var baseHex)
        && !string.Equals(hex, baseHex, StringComparison.OrdinalIgnoreCase);

    /// <summary>대비 기준 판정용 실효 글자 크기 = 요소 자기 기본 크기(ThemeElements.BaseFontSize) + 요소 오프셋(적용기와 같은 계산, D-3).</summary>
    private static double EffectiveFontSize(ThemeDefinition t, string el)
    {
        var s = UiSettings.Current;
        var spec = ThemeElements.Element(el)!;
        var size = ThemeElements.BaseFontSize(el, s);
        return ThemeElements.Has(el, "fontSize")
            && ThemeDesignApplier.Convert(t, spec, "fontSize", ThemeElements.Effective(t, el, "fontSize"), s) is double d ? d : size;
    }
}

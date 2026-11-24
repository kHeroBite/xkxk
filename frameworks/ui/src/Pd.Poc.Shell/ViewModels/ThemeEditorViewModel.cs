// 테마 편집기 ViewModel(UI-07) — 테마 목록·즉시 적용·복제·토큰별 hex 색 편집(미리보기 즉시)·이름 저장·삭제·JSON 내보내기/가져오기·시스템 모드 짝 지정
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels;

/// <summary>테마 편집기. 언어 전환을 구독해 토큰 표시명·짝 목록을 즉시 갱신한다 — 화면이 닫히면 Dispose 로 구독 해제(ThemeEditorView).</summary>
public sealed class ThemeEditorViewModel : ObservableObject, IDisposable
{
    private readonly ThemeCatalog _catalog = ThemeCatalog.Current;
    private ThemeDefinition? _selected;
    private string _editName = "";
    private bool _editIsDark;
    private string _exchangeText = "";
    private string _status = "";

    public ThemeEditorViewModel()
    {
        DuplicateCommand = new RelayCommand(Duplicate);
        DeleteCommand = new RelayCommand(Delete);
        SaveNameCommand = new RelayCommand(SaveName);
        ExportTextCommand = new RelayCommand(ExportText);
        ImportTextCommand = new RelayCommand(ImportText);
        _selected = _catalog.Requested;
        Pairs = new SystemThemePairViewModel(_catalog, () => _selected);
        Design = new ThemeDesignViewModel(OnDesignChanged);
        DesignTheme = new DesignThemeViewModel();
        LoadRows();
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    public void Dispose()
    {
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        Design.Dispose();
        DesignTheme.Dispose();
    }

    /// <summary>디자인 테마 영역(REQ-104, 뷰 바인딩 경로 DesignTheme.*).</summary>
    public DesignThemeViewModel DesignTheme { get; }

    /// <summary>요소별 디자인 영역(REQ-101, 뷰 바인딩 경로 Design.*).</summary>
    public ThemeDesignViewModel Design { get; }

    /// <summary>
    /// 요소별 디자인이 테마를 바꾼 뒤: 내장 편집으로 사용자 사본이 생겨 선택이 바뀌었으면 목록 선택·이름·짝을 맞추고,
    /// 색이 바뀌었으면 색 토큰 표를 다시 읽는다. Design.Refresh 는 부르지 않는다(입력 중인 행의 글자를 덮지 않게).
    /// </summary>
    private void OnDesignChanged(bool colorsChanged)
    {
        var requested = _catalog.Requested;
        var switched = !ReferenceEquals(requested, _selected);
        if (switched)
        {
            _selected = requested;
            OnPropertyChanged(nameof(SelectedTheme));
            Pairs.Refresh();
            Pairs.RefreshFollow();
        }

        if (switched || colorsChanged)
        {
            LoadTokenRows();
        }
    }

    /// <summary>
    /// 언어 전환(Q17): 토큰 행 표시명과 OS 짝 콤보(새 배열 → 항목 다시 그림)를 갱신한다. 목록의 테마 이름은 ThemeCatalog 가 각 테마에 알린다.
    /// LoadRows 를 다시 돌리지 않는 이유: 입력 중인 이름·hex 를 잃지 않기 위해서다.
    /// </summary>
    private void OnLanguageChanged()
    {
        foreach (var row in Rows)
        {
            row.RefreshLabel();
        }

        Pairs.Refresh();
    }

    public ObservableCollection<ThemeDefinition> Themes => _catalog.Themes;

    public ObservableCollection<ThemeTokenRow> Rows { get; } = new();

    /// <summary>시스템 모드 짝 지정(뷰 바인딩 경로 Pairs.*).</summary>
    public SystemThemePairViewModel Pairs { get; }

    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveNameCommand { get; }
    public RelayCommand ExportTextCommand { get; }
    public RelayCommand ImportTextCommand { get; }

    /// <summary>목록 선택 = 즉시 적용.</summary>
    public ThemeDefinition? SelectedTheme
    {
        get => _selected;
        set
        {
            if (value is null || !SetProperty(ref _selected, value))
            {
                return;
            }

            _catalog.Select(value.Id);
            LoadRows();
            Pairs.RefreshFollow();
        }
    }

    public bool IsEditable => _selected is { IsBuiltin: false };
    public bool IsBuiltinSelected => !IsEditable;

    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

    /// <summary>편집 중 테마의 기반(체크=Dark). 저장 시 반영.</summary>
    public bool EditIsDark
    {
        get => _editIsDark;
        set => SetProperty(ref _editIsDark, value);
    }

    /// <summary>JSON 내보내기 결과·가져오기 입력(웹은 이 텍스트 붙여넣기로 교환).</summary>
    public string ExchangeText { get => _exchangeText; set => SetProperty(ref _exchangeText, value); }

    public string Status { get => _status; set => SetProperty(ref _status, value); }

    /// <summary>현재 테마 JSON(파일 저장용). 선택 없으면 null.</summary>
    public string? CurrentJson => _selected is { } t ? ThemeCatalog.Export(t) : null;

    /// <summary>JSON 텍스트 가져오기(파일·붙여넣기 공용). 첫 테마를 선택해 즉시 적용.</summary>
    public void ImportJson(string json, string source)
    {
        try
        {
            var added = _catalog.Import(json);
            Pairs.Refresh();
            if (added.Count > 0)
            {
                SelectedTheme = added[0];
            }

            Status = Loc.Tr("theme.status.imported", source, added.Count);
        }
        catch (Exception ex)
        {
            Status = Loc.Tr("theme.status.importFailed", ex.Message);
        }
    }

    private void Duplicate()
    {
        if (_selected is null)
        {
            return;
        }

        var copy = _catalog.Duplicate(_selected);
        Pairs.Refresh();
        SelectedTheme = copy;
        Status = Loc.Tr("theme.status.cloned", copy.Name);
    }

    private void Delete()
    {
        if (_selected is not { IsBuiltin: false } t)
        {
            Status = Loc.Tr("theme.status.deleteBuiltin");
            return;
        }

        _catalog.Delete(t);
        Pairs.Refresh();
        SelectedTheme = _catalog.Requested;
        Status = Loc.Tr("theme.status.deleted", t.Name);
    }

    private void SaveName()
    {
        if (_selected is not { IsBuiltin: false } t)
        {
            Status = Loc.Tr("theme.status.renameBuiltin");
            return;
        }

        Status = _catalog.Rename(t, EditName, EditIsDark ? ThemeBase.Dark : ThemeBase.Light)
            ? Loc.Tr("theme.status.renamed", t.Name)
            : Loc.Tr("theme.status.nameRequired");
        Pairs.Refresh();
    }

    private void ExportText()
    {
        ExchangeText = CurrentJson ?? "";
        Status = Loc.Tr("theme.status.exported");
    }

    private void ImportText()
    {
        if (string.IsNullOrWhiteSpace(ExchangeText))
        {
            Status = Loc.Tr("theme.status.pasteFirst");
        }
        else
        {
            ImportJson(ExchangeText, Loc.Tr("theme.source.text"));
        }
    }

    private void LoadRows()
    {
        LoadTokenRows();
        Design.Refresh();
    }

    private void LoadTokenRows()
    {
        Rows.Clear();
        if (_selected is { } t)
        {
            foreach (var key in ThemeTokens.Keys)
            {
                Rows.Add(new ThemeTokenRow(t, key));
            }

            EditName = t.Name;
            EditIsDark = t.Base == ThemeBase.Dark;
        }

        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsBuiltinSelected));
    }
}

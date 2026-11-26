// 레이아웃 저장·관리 패널 뷰모델(UI-11, 윈도우 메뉴 ⑪⑫) — 이름 입력(기본 = 현재 시각)·덮어쓰기 확인·목록·불러오기·이름 변경·삭제 확인·템플릿 적용
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

public sealed partial class LayoutManagerViewModel : ObservableObject
{
    private readonly Func<ILayoutLibrary?> _library;
    private readonly Action<string> _log;
    private bool _isOpen;
    private bool _isSaveMode;
    private string _saveName = "";
    private bool _confirmOverwrite;
    private bool _confirmDelete;
    private string _message = "";
    private LayoutEntry? _selected;
    private string _renameText = "";
    private bool _listening;

    public LayoutManagerViewModel(Func<ILayoutLibrary?> library, Action<string> log)
    {
        _library = library;
        _log = log;
        SaveCommand = new AsyncRelayCommand(() => SaveAsync(overwrite: false));
        ConfirmOverwriteCommand = new AsyncRelayCommand(() => SaveAsync(overwrite: true));
        LoadCommand = new AsyncRelayCommand(LoadAsync);
        RenameCommand = new AsyncRelayCommand(RenameAsync);
        DeleteCommand = new RelayCommand(() =>
        {
            ConfirmDelete = Selected is { IsReadOnly: false };
            Message = ConfirmDelete ? Loc.Tr("common.confirmDelete", Selected!.Name) : "";
        });
        ConfirmDeleteCommand = new AsyncRelayCommand(DeleteAsync);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync);
        TemplatePicker = new LayoutTemplatePickerViewModel(library, m => Message = m);
        CancelCommand = new RelayCommand(Cancel);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        Backups = new LayoutBackupViewModel(library, m => Message = m);

        // 언어가 바뀌면 템플릿 이름·자동 저장 항목 이름을 새 언어로 다시 읽는다(Q17)
        XkxkUi.Current.Localization.LanguageChanged += () =>
        {
            TemplatePicker.Refresh();
            if (_isOpen && !_isSaveMode) { _ = RefreshAsync(); }
        };
    }

    public ObservableCollection<LayoutEntry> Entries { get; } = new();

    /// <summary>⑫ 전체 백업·복원 영역.</summary>
    public LayoutBackupViewModel Backups { get; }

    /// <summary>기본 템플릿 고르기·적용(뷰 바인딩 경로 TemplatePicker.*).</summary>
    public LayoutTemplatePickerViewModel TemplatePicker { get; }

    public bool IsOpen { get => _isOpen; set => SetProperty(ref _isOpen, value); }

    /// <summary>true = ⑪ 저장 화면(이름 입력), false = ⑫ 관리 화면(목록).</summary>
    public bool IsSaveMode
    {
        get => _isSaveMode;
        set
        {
            if (SetProperty(ref _isSaveMode, value)) { OnPropertyChanged(nameof(IsManageMode)); }
        }
    }

    public bool IsManageMode => !_isSaveMode;

    public string SaveName
    {
        get => _saveName;
        set
        {
            if (SetProperty(ref _saveName, value ?? ""))
            {
                ConfirmOverwrite = false; // 이름을 고치면 덮어쓰기 확인을 다시 묻는다
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    /// <summary>빈 이름(공백만)이면 저장 버튼 비활성.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(_saveName);

    public bool ConfirmOverwrite { get => _confirmOverwrite; private set => SetProperty(ref _confirmOverwrite, value); }

    public bool ConfirmDelete { get => _confirmDelete; private set => SetProperty(ref _confirmDelete, value); }

    /// <summary>안내·결과 문구(빈 이름·중복·실패 등).</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public LayoutEntry? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RenameText = value?.Name ?? "";
                ConfirmDelete = false;
                OnPropertyChanged(nameof(CanEditSelected));
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>이름 변경·삭제는 개인 레이아웃만(자동 저장 슬롯은 읽기 전용).</summary>
    public bool CanEditSelected => _selected is { IsReadOnly: false };

    public string RenameText { get => _renameText; set => SetProperty(ref _renameText, value ?? ""); }

    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ConfirmOverwriteCommand { get; }
    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    public AsyncRelayCommand SaveAsCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>⑪ 현재화면 수동저장: 이름 입력란에 현재 시각을 채워 연다.</summary>
    public void OpenSave()
    {
        if (_library() is not { } lib) { return; }
        ResetPrompts();
        SaveName = lib.DefaultName();
        IsSaveMode = true;
        IsOpen = true;
    }

    /// <summary>⑫ 화면 백업/복원 관리.</summary>
    public void OpenManage()
    {
        ResetPrompts();
        IsSaveMode = false;
        IsOpen = true;
        if (!_listening && _library() is { } lib)
        {
            // 원격·전체 복원 등 다른 경로로 목록이 바뀌어도 열린 관리 창을 새로 고친다
            lib.Changed += () => { if (_isOpen && !_isSaveMode) { _ = RefreshAsync(); } };
            _listening = true;
        }

        _ = RefreshAsync();
        _ = Backups.RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_library() is not { } lib) { return; }
        var keepId = _selected?.Id;
        var list = await lib.ListAsync();
        Entries.Clear();
        foreach (var e in list) { Entries.Add(e); }
        Selected = keepId is null ? null : System.Linq.Enumerable.FirstOrDefault(Entries, e => e.Id == keepId);
        TemplatePicker.Refresh();
    }

    private async Task SaveAsync(bool overwrite)
    {
        if (_library() is not { } lib) { return; }
        var result = await lib.SaveCurrentAsync(_saveName, overwrite);
        switch (result.Status)
        {
            case LayoutSaveStatus.Saved:
                Message = Loc.Tr("layout.msg.saved", _saveName.Trim());
                IsOpen = false;
                break;
            case LayoutSaveStatus.NameExists:
                ConfirmOverwrite = true;
                Message = Loc.Tr("common.confirmOverwrite", _saveName.Trim());
                break;
            default:
                Message = Describe(result.Status);
                _log(Loc.Tr("log.layout.notSaved", result.Status, _saveName.Trim()));
                break;
        }
    }

    private async Task LoadAsync()
    {
        if (_library() is not { } lib || _selected is null) { return; }
        var ok = await lib.LoadAsync(_selected.Id);
        Message = Loc.Tr(ok ? "layout.msg.loaded" : "layout.msg.loadFailed", _selected.Name);
        if (ok) { IsOpen = false; }
    }

    private async Task RenameAsync()
    {
        if (_library() is not { } lib || _selected is null) { return; }
        var result = await lib.RenameAsync(_selected.Id, _renameText);
        Message = result.Ok ? Loc.Tr("layout.msg.renamed") : Describe(result.Status);
        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        if (_library() is not { } lib || _selected is null) { return; }
        var name = _selected.Name;
        var result = await lib.DeleteAsync(_selected.Id);
        ConfirmDelete = false;
        Message = result.Ok ? Loc.Tr("layout.msg.deleted", name) : Describe(result.Status);
        Selected = null;
        await RefreshAsync();
    }

    /// <summary>읽기 전용 항목(자동 저장 슬롯)·템플릿을 화면에 적용한 뒤 새 이름으로 저장하는 화면으로 넘어간다.</summary>
    private async Task SaveAsAsync()
    {
        if (_library() is not { } lib) { return; }
        if (_selected is not null && !await lib.LoadAsync(_selected.Id))
        {
            Message = Loc.Tr("layout.msg.loadFailed", _selected.Name);
            return;
        }

        OpenSave();
    }

    private void Cancel()
    {
        if (_confirmOverwrite || _confirmDelete || Backups.ConfirmOverwrite || Backups.ConfirmRestore || Backups.ConfirmDelete)
        {
            ResetPrompts(); // 확인 줄만 닫는다
            return;
        }

        IsOpen = false;
    }

    private void ResetPrompts()
    {
        ConfirmOverwrite = false;
        ConfirmDelete = false;
        Backups.ResetPrompts();
        Message = "";
    }

    internal static string Describe(LayoutSaveStatus status) => LayoutSaveStatusText.Describe(status);
}

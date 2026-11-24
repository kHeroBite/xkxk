// 전체 백업·복원 영역 뷰모델(윈도우 메뉴 ⑫) — 백업 이름(기본 = 현재 시각)·덮어쓰기 확인·목록·복원 확인(현재 상태 자동 백업 안내)·삭제
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels;

public sealed class LayoutBackupViewModel : ObservableObject
{
    private readonly Func<ILayoutLibrary?> _library;
    private readonly Action<string> _showMessage;
    private string _name = "";
    private BackupEntry? _selected;
    private bool _confirmOverwrite;
    private bool _confirmRestore;
    private bool _confirmDelete;

    public LayoutBackupViewModel(Func<ILayoutLibrary?> library, Action<string> showMessage)
    {
        _library = library;
        _showMessage = showMessage;
        BackupCommand = new AsyncRelayCommand(() => BackupAsync(overwrite: false));
        ConfirmOverwriteCommand = new AsyncRelayCommand(() => BackupAsync(overwrite: true));
        RestoreCommand = new RelayCommand(() =>
        {
            ConfirmRestore = _selected is not null;
            if (_selected is not null) { _showMessage(Loc.Tr("layout.backup.confirmRestore", _selected.Name)); }
        });
        ConfirmRestoreCommand = new AsyncRelayCommand(RestoreAsync);
        DeleteCommand = new RelayCommand(() =>
        {
            ConfirmDelete = _selected is not null;
            if (_selected is not null) { _showMessage(Loc.Tr("common.confirmDelete", _selected.Name)); }
        });
        ConfirmDeleteCommand = new AsyncRelayCommand(DeleteAsync);
        CancelCommand = new RelayCommand(ResetPrompts);
    }

    public ObservableCollection<BackupEntry> Items { get; } = new();

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? "")) { ConfirmOverwrite = false; }
        }
    }

    public BackupEntry? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                ConfirmRestore = false;
                ConfirmDelete = false;
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public bool ConfirmOverwrite { get => _confirmOverwrite; private set => SetProperty(ref _confirmOverwrite, value); }

    public bool ConfirmRestore { get => _confirmRestore; private set => SetProperty(ref _confirmRestore, value); }

    public bool ConfirmDelete { get => _confirmDelete; private set => SetProperty(ref _confirmDelete, value); }

    public AsyncRelayCommand BackupCommand { get; }
    public AsyncRelayCommand ConfirmOverwriteCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public AsyncRelayCommand ConfirmRestoreCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>관리 창을 열 때·작업 뒤 목록 새로 읽기. 이름 입력란은 비어 있으면 현재 시각으로 채운다.</summary>
    public async Task RefreshAsync()
    {
        if (_library() is not { } lib) { return; }
        if (string.IsNullOrWhiteSpace(_name)) { Name = lib.DefaultName(); }
        var keep = _selected?.Id;
        var list = await lib.ListBackupsAsync();
        Items.Clear();
        foreach (var b in list) { Items.Add(b); }
        Selected = Items.FirstOrDefault(b => b.Id == keep);
    }

    public void ResetPrompts()
    {
        ConfirmOverwrite = false;
        ConfirmRestore = false;
        ConfirmDelete = false;
    }

    private async Task BackupAsync(bool overwrite)
    {
        if (_library() is not { } lib) { return; }
        var result = await lib.BackupAllAsync(_name, overwrite);
        if (result.Status == LayoutSaveStatus.NameExists)
        {
            ConfirmOverwrite = true;
            _showMessage(Loc.Tr("common.confirmOverwrite", _name.Trim()));
            return;
        }

        ConfirmOverwrite = false;
        _showMessage(result.Ok ? Loc.Tr("layout.backup.done", _name.Trim()) : LayoutManagerViewModel.Describe(result.Status));
        if (result.Ok) { Name = ""; }
        await RefreshAsync();
    }

    private async Task RestoreAsync()
    {
        if (_library() is not { } lib || _selected is null) { return; }
        var name = _selected.Name;
        var result = await lib.RestoreAllAsync(_selected.Id);
        ConfirmRestore = false;
        _showMessage(result.Ok ? Loc.Tr("layout.backup.restored", name) : LayoutManagerViewModel.Describe(result.Status));
        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        if (_library() is not { } lib || _selected is null) { return; }
        var name = _selected.Name;
        var result = await lib.DeleteBackupAsync(_selected.Id);
        ConfirmDelete = false;
        _showMessage(result.Ok ? Loc.Tr("layout.msg.deleted", name) : LayoutManagerViewModel.Describe(result.Status));
        Selected = null;
        await RefreshAsync();
    }
}

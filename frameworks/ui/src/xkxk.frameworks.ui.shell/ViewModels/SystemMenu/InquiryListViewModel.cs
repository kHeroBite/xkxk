// 문의목록 화면 뷰모델(UI-17) — 로컬 임시함의 내 문의 목록(최신 순)·상세 보기·삭제, 서버 연동 전 상태는 '미전송'
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels.SystemMenu;

/// <summary>목록 한 줄. 상태·분류 문구는 표시 때 현재 언어로 바꾼다(키 보관).</summary>
public sealed class InquiryRow
{
    public InquiryRow(InquiryRecord record)
    {
        Record = record;
    }

    public InquiryRecord Record { get; }

    public string Title => Record.Title;

    public string Time => Record.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string Category => Loc.Tr(Record.Category);

    public string Status => Loc.Tr("sysview.inquiry.status." + Record.Status);
}

public sealed partial class InquiryListViewModel : ObservableObject, System.IDisposable
{
    private readonly InquiryStore _store;
    private InquiryRow? _selected;
    private bool _disposed;

    public InquiryListViewModel(InquiryStore? store = null)
    {
        _store = store ?? InquiryStore.Default;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => _selected is not null);
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<InquiryRow> Rows { get; } = new();

    public InquiryRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(SelectedBody));
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SelectedBody => _selected?.Record.Body ?? "";

    public bool IsEmpty => Rows.Count == 0;

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
        }
    }

    private async Task RefreshAsync()
    {
        var list = await _store.ListAsync();
        Rows.Clear();
        foreach (var r in list)
        {
            Rows.Add(new InquiryRow(r));
        }

        Selected = null;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task DeleteAsync()
    {
        if (_selected is { } row && await _store.DeleteAsync(row.Record.Id))
        {
            await RefreshAsync();
        }
    }

    /// <summary>분류·상태 문구는 행이 계산하므로 목록을 다시 만들어 즉시 바꾼다.</summary>
    private void OnLanguageChanged() => _ = RefreshAsync();
}

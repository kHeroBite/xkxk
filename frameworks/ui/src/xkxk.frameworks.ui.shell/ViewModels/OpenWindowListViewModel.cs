// '창' 메뉴 끝의 열린 창 목록 1~9 와 '창 더 보기…' 전체 목록(Win32 MDI 관례) — 열린 순서·활성 ✓, 고르면 활성화(최소화면 복원)
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>열린 창 목록. 바인딩 경로는 MainViewModel.WindowMenu.OpenWindows.(Slots[i] | MoreEntries | IsMoreOpen …).</summary>
public sealed class OpenWindowListViewModel : ObservableObject
{
    /// <summary>메뉴에 나열하는 최대 개수(접근 키 1~9). 넘으면 '창 더 보기…' 를 보인다.</summary>
    public const int MaxSlots = 9;

    private readonly WindowCommands _commands;
    private bool _isMoreOpen;
    private OpenWindowSlot? _selectedEntry;

    public OpenWindowListViewModel(WindowCommands commands)
    {
        _commands = commands;
        Slots = Enumerable.Range(0, MaxSlots).Select(_ => new OpenWindowSlot()).ToArray();
        SelectCommand = new RelayCommand<OpenWindowSlot>(Select);
        OpenMoreCommand = new RelayCommand(OpenMore);
        ConfirmMoreCommand = new RelayCommand(() => ConfirmMore(SelectedEntry));
        CancelMoreCommand = new RelayCommand(() => IsMoreOpen = false);
        commands.OpenDocuments.CollectionChanged += OnDocumentsChanged;
        commands.ActiveChanged += _ => Refresh();
        Refresh();
    }

    public IReadOnlyList<OpenWindowSlot> Slots { get; }

    /// <summary>열린 문서가 하나라도 있으면 true(목록 앞 구분선 표시).</summary>
    public bool HasAny => Slots[0].IsVisible;

    /// <summary>열린 문서가 9개를 넘으면 true('창 더 보기…' 표시).</summary>
    public bool HasMore => _commands.OpenDocuments.Count > MaxSlots;

    /// <summary>칸 선택 = 그 문서 활성화. 최소화 상태면 복원된다(WindowCommands.Activate).</summary>
    public RelayCommand<OpenWindowSlot> SelectCommand { get; }

    // ── '창 더 보기…' 대화상자(Views/MoreWindowsView) ──

    /// <summary>대화상자의 전체 목록(열린 순서, 접근 키 없는 '번호. 제목').</summary>
    public ObservableCollection<OpenWindowSlot> MoreEntries { get; } = new();

    /// <summary>대화상자 열림 여부(MainTitleBar 의 Popup 이 양방향 바인딩 — 바깥 클릭으로 닫혀도 반영).</summary>
    public bool IsMoreOpen
    {
        get => _isMoreOpen;
        set => SetProperty(ref _isMoreOpen, value);
    }

    /// <summary>대화상자에서 고른 항목(처음엔 활성 창).</summary>
    public OpenWindowSlot? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public RelayCommand OpenMoreCommand { get; }

    /// <summary>확인 또는 목록 더블클릭 — 고른 창을 활성화(최소화면 복원)하고 닫는다.</summary>
    public RelayCommand ConfirmMoreCommand { get; }

    public RelayCommand CancelMoreCommand { get; }

    /// <summary>
    /// 칸을 다시 채운다. 문서 추가·닫기·활성 변경 때 자동으로 부르고, 이름 변경·언어 전환 반영을 위해 메뉴가 열릴 때도 부른다(WindowMenu 코드 비하인드).
    /// </summary>
    public void Refresh()
    {
        var docs = _commands.OpenDocuments;
        var active = _commands.ActiveDocument;
        for (var i = 0; i < MaxSlots; i++)
        {
            var doc = i < docs.Count ? docs[i] : null;
            var slot = Slots[i];
            slot.Document = doc;
            slot.Header = doc is null ? "" : Loc.Tr("menu.window.listItem", i + 1, EscapeAccessKey(doc.Title));
            slot.IsActive = doc is not null && ReferenceEquals(doc, active);
        }

        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(HasMore));
    }

    /// <summary>대화상자를 연다 — 지금 열린 창 전체로 목록을 새로 만들고 활성 창을 선택해 둔다.</summary>
    public void OpenMore()
    {
        MoreEntries.Clear();
        var docs = _commands.OpenDocuments;
        for (var i = 0; i < docs.Count; i++)
        {
            MoreEntries.Add(new OpenWindowSlot
            {
                Document = docs[i],
                Header = Loc.Tr("moreWindows.item", i + 1, docs[i].Title),
                IsActive = ReferenceEquals(docs[i], _commands.ActiveDocument),
            });
        }

        SelectedEntry = MoreEntries.FirstOrDefault(e => e.IsActive) ?? MoreEntries.FirstOrDefault();
        IsMoreOpen = true;
    }

    /// <summary>항목 확정(확인 버튼·더블클릭 공통). 항목이 없으면 아무것도 하지 않고 대화상자를 연 채로 둔다.</summary>
    public void ConfirmMore(OpenWindowSlot? entry)
    {
        if (entry?.Document is not { } doc)
        {
            return;
        }

        IsMoreOpen = false;
        _commands.Activate(doc, ActivationOrigin.WindowList);
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void Select(OpenWindowSlot? slot)
    {
        if (slot?.Document is { } doc)
        {
            // 출처는 명령이 넘긴다 — MenuItem 은 Click 의 클래스 처리기에서 명령을 먼저 실행해 상위 메뉴의 표식이 늦다(21 §3.2)
            _commands.Activate(doc, ActivationOrigin.WindowList);
        }
    }

    // 제목 안의 '_' 가 접근 키로 해석되지 않게 두 번 쓴다(번호만 접근 키)
    private static string EscapeAccessKey(string? title) => (title ?? "").Replace("_", "__");
}

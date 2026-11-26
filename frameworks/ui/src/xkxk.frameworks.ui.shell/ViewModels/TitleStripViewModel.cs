// 제목 줄 뷰모델 — 떠 있지 않은 문서는 제목 탭으로, 독립 창(SDI)은 창 단위 아이콘으로 나누고 제목 줄·제목 탭 표시 조건을 계산한다
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>제목 줄 상태. 바인딩 경로는 MainViewModel.TitleStrip.X. 규칙 정본은 plan/09_화면설계/01_화면설계_공통원칙.md §2-1.</summary>
public sealed class TitleStripViewModel : ObservableObject
{
    private readonly ShellContext _ctx;
    private readonly HashSet<PocDocument> _watched = new();
    private bool _refreshPosted;
    private bool _showTitleTabs;
    private bool _showTitleStrip;

    public TitleStripViewModel(ShellContext ctx)
    {
        _ctx = ctx;
        var factory = ctx.Factory;
        ctx.Commands.OpenDocuments.CollectionChanged += (_, _) => RequestRefresh();
        ctx.Commands.ActiveChanged += _ => RequestRefresh();
        factory.DockableMoved += (_, _) => RequestRefresh();
        factory.DockableDocked += (_, _) => RequestRefresh();
        factory.DockableUndocked += (_, _) => RequestRefresh();
        factory.WindowOpened += (_, _) => RequestRefresh();
        factory.WindowClosed += (_, _) => RequestRefresh();
        // 아이콘 활성 표시는 OS 창 활성을 따른다. 활성 문서가 그대로여도 전경이 바뀌므로 받는다(이벤트 뒤에 IsActive 가 바뀌어도 게시 처리라 새 값을 읽는다)
        factory.WindowActivated += (_, _) => RequestRefresh();
        factory.WindowDeactivated += (_, _) => RequestRefresh();
        XkxkUi.Current.Localization.LanguageChanged += RequestRefresh;
        ctx.Main.Compact.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CompactModeViewModel.IsWide)) { UpdateVisibility(); } };
        ctx.Main.FavoriteBar.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FavoriteBarViewModel.IsShown)) { UpdateVisibility(); } }; // 즐겨찾기만 있어도 제목 줄을 보인다
        if (factory.Documents is INotifyPropertyChanged documents)
        {
            documents.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IDocumentDock.LayoutMode)) { UpdateVisibility(); } };
        }
        Refresh();
    }

    /// <summary>제목 탭 목록. 떠 있지 않은 열린 문서를 열린 순서로 담는다.</summary>
    public ObservableCollection<PocDocument> TitleTabs { get; } = new();

    /// <summary>독립 창 아이콘 목록. 창 하나에 하나이므로 문서를 합친 창도 아이콘은 1개다.</summary>
    public ObservableCollection<SdiItem> SdiItems { get; } = new();

    /// <summary>제목 탭 표시 여부. MDI 이면서 넓은 화면일 때만 true 다(DOCK 은 문서 탭이 목록을 맡는다).</summary>
    public bool ShowTitleTabs { get => _showTitleTabs; private set => SetProperty(ref _showTitleTabs, value); }

    /// <summary>제목 줄 표시 여부. 넓은 화면에서 제목 탭을 보이거나 독립 창이 1개 이상일 때 true 다.</summary>
    public bool ShowTitleStrip { get => _showTitleStrip; private set => SetProperty(ref _showTitleStrip, value); }

    /// <summary>
    /// 제목 탭 선택 바인딩(TwoWay) 대상. 활성 문서가 독립 창이면 null 이다. MainViewModel.ActiveDocument 를 쓰면 목록에 없는 값을
    /// 목록 컨트롤이 null 로 되돌리고 그 값이 다시 강제 동기화되므로, 제목 탭 목록에 맞춘 이 속성을 쓴다.
    /// </summary>
    public PocDocument? SelectedTitleTab
    {
        get => _ctx.Commands.ActiveDocument is { } active && TitleTabs.Contains(active) ? active : null;
        set
        {
            if (value is not null && !ReferenceEquals(value, _ctx.Commands.ActiveDocument))
            {
                _ctx.Commands.Activate(value); // ActiveChanged 가 새로 고침을 부른다
            }
            else if (value is null && SelectedTitleTab is not null)
            {
                RequestRefresh(); // 목록 변경 중 선택이 일시로 풀렸다. 활성 탭 선택을 되돌린다
            }
        }
    }

    // 이벤트 여러 개가 한 번의 조작에서 이어지고 그 사이 Dock 모델은 중간 상태다. 조작이 끝난 뒤 1회만 다시 계산한다.
    private void RequestRefresh()
    {
        if (_refreshPosted) { return; }

        _refreshPosted = true;
        Dispatcher.UIThread.Post(() => { _refreshPosted = false; Refresh(); });
    }

    private void Refresh()
    {
        var open = _ctx.Commands.OpenDocuments;
        Watch(open);
        SyncOrder(TitleTabs, open.Where(d => !_ctx.Commands.IsFloating(d)).ToList());
        SyncSdiItems(open.Where(_ctx.Commands.IsFloating).ToList());
        UpdateVisibility();
        OnPropertyChanged(nameof(SelectedTitleTab)); // 값이 같아도 알려 목록 변경 뒤 선택을 맞춘다
    }

    private void SyncSdiItems(List<PocDocument> floating)
    {
        var commands = _ctx.Commands;
        var items = new List<SdiItem>();
        foreach (var group in floating.GroupBy(d => commands.SdiPlacement.WindowOf(d)!))
        {
            var docs = group.ToList();
            var item = SdiItems.FirstOrDefault(i => ReferenceEquals(i.Window, group.Key)) ?? new SdiItem(group.Key, Activate);
            var active = docs.FirstOrDefault(d => ReferenceEquals(d, commands.ActiveDocument));
            var shown = active ?? docs.FirstOrDefault(d => ReferenceEquals((d.Owner as IDock)?.ActiveDockable, d)) ?? docs[0];
            var tip = docs.Count > 1 ? Loc.Tr("titlebar.sdiIcon.merged", shown.Title, docs.Count - 1) : shown.Title;
            item.Update(docs[0].Id, shown, tip, commands.ChromeService.IsSdiActive(group.Key, active is not null), WindowCommands.ChromeOf(shown).IsMinimized);
            items.Add(item);
        }

        SyncOrder(SdiItems, items);
    }

    private void UpdateVisibility()
    {
        var wide = _ctx.Main.Compact.IsWide;
        ShowTitleTabs = wide && _ctx.Factory.Documents?.LayoutMode == DocumentLayoutMode.Mdi;
        ShowTitleStrip = wide && (ShowTitleTabs || SdiItems.Count > 0 || _ctx.Main.FavoriteBar.IsShown);
    }

    // 아이콘 클릭: 최소화 상태는 OS 최소화 버튼으로 들어간 경우도 있어 창 상태 기준으로 먼저 복원한 뒤 활성화한다.
    private void Activate(SdiItem item)
    {
        if (item.Document is not { } doc || !_ctx.Commands.OpenDocuments.Contains(doc)) { return; }
        if (item.IsMinimized) { _ctx.Commands.SetState(doc, MdiWindowState.Normal); }
        _ctx.Commands.Activate(doc);
    }

    // 제목 변경·창 최소화는 Dock 이벤트로 오지 않아 문서별로 구독한다. 목록에서 빠진 문서는 해제한다.
    private void Watch(IReadOnlyCollection<PocDocument> open)
    {
        foreach (var doc in _watched.Except(open).ToList())
        {
            _watched.Remove(doc);
            doc.PropertyChanged -= OnWatchedChanged;
            WindowCommands.ChromeOf(doc).PropertyChanged -= OnWatchedChanged;
        }

        foreach (var doc in open.Where(_watched.Add))
        {
            EnsureIdentity(doc);
            doc.PropertyChanged += OnWatchedChanged;
            WindowCommands.ChromeOf(doc).PropertyChanged += OnWatchedChanged;
        }
    }

    // 창 아이콘 식별(22 §2): 화면 ID·메뉴 ID·아이콘 키가 빈 문서(새 열기·레이아웃 복원 모두)는 PoC 종류의 대표 화면으로 채운다. 이미 값이 있으면 그대로 둔다
    private static void EnsureIdentity(PocDocument doc)
    {
        var kind = doc.Kind.ToString();
        doc.ScreenId ??= xkxk.frameworks.ui.data.Navigation.MenuIconCatalog.ScreenIdOfPocKind(kind);
        doc.MenuId ??= xkxk.frameworks.ui.data.Navigation.MenuIconCatalog.MenuIdFor(doc.ScreenId, kind);
        doc.IconKey ??= xkxk.frameworks.ui.data.Navigation.MenuIconCatalog.IconKeyFor(doc.ScreenId, kind);
    }

    private void OnWatchedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PocDocument.Title) or nameof(DocumentChromeState.IsMinimized)) { RequestRefresh(); }
    }

    // 선택·스크롤 위치가 풀리지 않게 전체를 지우지 않고 달라진 칸만 옮기거나 넣고 뺀다.
    private static void SyncOrder<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) { continue; }
            var at = target.IndexOf(desired[i]);
            if (at >= 0) { target.Move(at, i); } else { target.Insert(i, desired[i]); }
        }
        while (target.Count > desired.Count) { target.RemoveAt(target.Count - 1); }
    }
}

/// <summary>
/// 제목 줄 오른쪽 끝 독립 창 아이콘 하나(창 단위). 목록 항목 전용이라 MobileNavItem·MonitorItemViewModel 처럼 소유 뷰모델 파일에 함께 둔다.
/// Id 는 그 창 첫 문서 Id(AutomationId shell.sdiIcon.{Id}), Title·Document 는 창에 보이는 문서(Document 는 우클릭 창 메뉴 대상),
/// ToolTip 은 문서가 2개 이상이면 "활성 제목 외 N", IsActive 는 이 창이 활성(판정은 DocumentChromeService.IsSdiActive), ActivateCommand 는 활성화(최소화면 복원)다.
/// </summary>
public sealed class SdiItem : ObservableObject
{
    private string _id = "", _title = "", _toolTip = "";
    private bool _isActive, _isMinimized;
    private PocDocument? _document;

    internal SdiItem(IDockWindow window, Action<SdiItem> activate)
    {
        Window = window;
        ActivateCommand = new RelayCommand(() => activate(this));
    }

    public string Id { get => _id; private set => SetProperty(ref _id, value); }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string ToolTip { get => _toolTip; private set => SetProperty(ref _toolTip, value); }
    public bool IsActive { get => _isActive; private set => SetProperty(ref _isActive, value); }
    public bool IsMinimized { get => _isMinimized; private set => SetProperty(ref _isMinimized, value); }
    public PocDocument? Document { get => _document; private set => SetProperty(ref _document, value); }
    public RelayCommand ActivateCommand { get; }
    internal IDockWindow Window { get; }

    internal void Update(string? id, PocDocument shown, string? toolTip, bool isActive, bool isMinimized)
    {
        Id = id ?? "";
        Title = shown.Title ?? "";
        ToolTip = toolTip ?? "";
        IsActive = isActive;
        IsMinimized = isMinimized;
        Document = shown;
    }
}

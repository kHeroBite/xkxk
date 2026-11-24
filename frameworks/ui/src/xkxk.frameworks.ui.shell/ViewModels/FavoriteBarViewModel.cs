// 제목 줄 즐겨찾기 영역 뷰모델 — 개인 즐겨찾기(화면) 목록 구독·넣기/빼기·순서·열기/활성화와 넘침(») 분할. 정본 plan/09_화면설계/22_메뉴아이콘_제목탭_즐겨찾기_설계.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>
/// 데이터는 REQ-111 ① plt.personal_favorite(대상 종류 screen, 대상 ID = 화면 ID)이고 저장소 계약은 IPersonalFavoriteStore 다(서버 구현은 같은 계약으로 교체).
/// Items 는 저장소 순서(넣은 순서·끌어 바꾼 순서) 그대로이고, 화면은 오른쪽부터 채운다(첫 항목이 오른쪽 끝). 자리가 모자라면 뒤쪽 항목을 OverflowItems(») 로 보낸다.
/// </summary>
public sealed class FavoriteBarViewModel : ObservableObject
{
    /// <summary>제목줄 즐겨찾기의 대상 종류.</summary>
    public const string TargetType = "screen";

    private readonly ShellContext _ctx;
    private readonly IPersonalFavoriteStore _store;
    private int _capacity = int.MaxValue;
    private int _reloadVersion;

    public FavoriteBarViewModel(ShellContext ctx, IPersonalFavoriteStore store)
    {
        _ctx = ctx;
        _store = store;
        store.Changed += () => Dispatcher.UIThread.Post(Reload); // 저장소는 어느 스레드에서든 알린다
        Loc.Instance.LanguageChanged += Reload; // 이름(종류 표시명)이 언어를 따른다
        UiSettings.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UiSettings.FavoriteBarVisible) or nameof(UiSettings.FavoriteBarPosition))
            {
                OnPropertyChanged(nameof(IsShown));
                OnPropertyChanged(nameof(Position));
            }
        };
        OpenCommand = new RelayCommand<FavoriteItem>(Open);
        RemoveCommand = new AsyncRelayCommand<FavoriteItem>(i => i is null ? Task.CompletedTask : _store.RemoveAsync(TargetType, i.ScreenId));
        ToggleCommand = new AsyncRelayCommand<PocDocument>(d => ToggleAsync(d ?? _ctx.Commands.ActiveDocument));
        Reload();
    }

    /// <summary>전체 즐겨찾기(저장소 순서).</summary>
    public ObservableCollection<FavoriteItem> Items { get; } = new();

    /// <summary>제목 줄에 보이는 항목(앞에서부터 Capacity 개 — 화면은 오른쪽부터 놓는다).</summary>
    public ObservableCollection<FavoriteItem> VisibleItems { get; } = new();

    /// <summary>자리가 모자라 » 팝업으로 간 항목.</summary>
    public ObservableCollection<FavoriteItem> OverflowItems { get; } = new();

    public bool HasOverflow => OverflowItems.Count > 0;

    /// <summary>1개 이상 있는가 — 삼선 메뉴·탐색 패널의 세로 구역 표시(제목 줄 영역 설정과 무관, 22 §4.4).</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>영역 표시 여부 — 설정 ui.shell.favorite_bar.visible 이 켜져 있고 항목이 1개 이상(빈 영역·구분선은 그리지 않는다).</summary>
    public bool IsShown => UiSettings.Current.FavoriteBarVisible && Items.Count > 0;

    /// <summary>자리(ui.shell.favorite_bar.position) — End: 독립 창 아이콘 오른쪽, BeforeSdi: 독립 창 아이콘 왼쪽.</summary>
    public FavoriteBarPosition Position => UiSettings.Current.FavoriteBarPosition;

    public RelayCommand<FavoriteItem> OpenCommand { get; }

    public AsyncRelayCommand<FavoriteItem> RemoveCommand { get; }

    /// <summary>문서 화면을 즐겨찾기에 넣거나 뺀다(제목 탭 우클릭·창 메뉴·탐색기 별 단추). 인자 없으면 활성 문서.</summary>
    public AsyncRelayCommand<PocDocument> ToggleCommand { get; }

    /// <summary>문서 화면이 즐겨찾기에 있는가(메뉴 문구 '추가/빼기' 판정, 화면 캐시 기준).</summary>
    public bool IsFavorite(PocDocument? doc) => ScreenIdOf(doc) is { } id && Items.Any(i => i.ScreenId == id);

    /// <summary>보일 개수(화면이 폭으로 계산해 넘긴다). 같으면 아무 일도 하지 않는다.</summary>
    public void SetCapacity(int capacity)
    {
        capacity = Math.Max(0, capacity);
        if (capacity != _capacity)
        {
            _capacity = capacity;
            Split();
        }
    }

    /// <summary>끌어서 순서 바꾸기(저장소 순서 기준 새 자리).</summary>
    public Task MoveAsync(FavoriteItem item, int newIndex) => _store.MoveAsync(TargetType, item.ScreenId, newIndex);

    /// <summary>화면 ID → 문서 종류(PoC 대표 화면 표의 역방향). 모르면 null.</summary>
    internal static DocumentKind? KindOf(string screenId) =>
        Enum.GetValues<DocumentKind>().Cast<DocumentKind?>().FirstOrDefault(k => MenuIconCatalog.ScreenIdOfPocKind(k.ToString()) == screenId);

    /// <summary>문서의 화면 ID(문서에 없으면 PoC 종류의 대표 화면).</summary>
    internal static string? ScreenIdOf(PocDocument? doc) =>
        doc is null ? null : doc.ScreenId ?? MenuIconCatalog.ScreenIdOfPocKind(doc.Kind.ToString());

    private Task ToggleAsync(PocDocument? doc) => ToggleScreenAsync(ScreenIdOf(doc));

    /// <summary>화면 ID 를 즐겨찾기에 넣거나 뺀다(탐색기 별 단추는 종류의 대표 화면 ID 로 부른다). 상한이면 로그만 남긴다.</summary>
    public async Task ToggleScreenAsync(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (await _store.ContainsAsync(TargetType, id))
        {
            await _store.RemoveAsync(TargetType, id);
            return;
        }

        var result = await _store.AddAsync(TargetType, id);
        if (result == FavoriteAddResult.LimitReached)
        {
            _ctx.AddLog(Loc.Tr("favorite.limitReached", _store.MaxItems));
        }
    }

    // ui.shell.favorite_bar.click: 기본 activate_or_open(같은 화면이 열려 있으면 그 창 활성, 없으면 연다), always_new(늘 새 창)
    private void Open(FavoriteItem? item)
    {
        if (item is null)
        {
            return;
        }

        var commands = _ctx.Commands;
        if (UiSettings.Current.FavoriteBarClick == FavoriteBarClick.ActivateOrOpen
            && commands.MostRecentOpen(d => ScreenIdOf(d) == item.ScreenId) is { } open)
        {
            commands.Activate(open, ActivationOrigin.WindowList); // 22 §4.3 — 여러 개면 최근 활성 창, 출처는 window_list
            return;
        }

        if (KindOf(item.ScreenId) is not { } kind)
        {
            _ctx.AddLog(Loc.Tr("favorite.unknownScreen", item.ScreenId)); // 이 PoC 에 아직 화면이 없는 즐겨찾기(서버 동기화로 온 항목 등)
            return;
        }

        var doc = commands.OpenDocument(kind, allowDuplicate: UiSettings.Current.FavoriteBarClick == FavoriteBarClick.AlwaysNew);
        doc.ScreenId ??= item.ScreenId;
    }

    private async void Reload()
    {
        var version = ++_reloadVersion;
        IReadOnlyList<PersonalFavorite> list;
        try
        {
            list = await _store.ListAsync(TargetType);
        }
        catch (Exception ex)
        {
            _ctx.AddLog(Loc.Tr("favorite.loadFailed", ex.Message)); // 저장소 읽기 실패는 빈 목록으로 두고 기록만 한다
            list = Array.Empty<PersonalFavorite>();
        }

        if (version != _reloadVersion)
        {
            return; // 더 최근 다시 읽기가 있다
        }

        Items.Clear();
        foreach (var f in list)
        {
            Items.Add(new FavoriteItem(f.TargetId, TitleOf(f.TargetId)));
        }

        Split();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsShown));
    }

    private void Split()
    {
        VisibleItems.Clear();
        OverflowItems.Clear();
        for (var i = 0; i < Items.Count; i++)
        {
            (i < _capacity ? VisibleItems : OverflowItems).Add(Items[i]);
        }

        OnPropertyChanged(nameof(HasOverflow));
    }

    private static string TitleOf(string screenId) => KindOf(screenId) is { } kind ? kind.DisplayName() : screenId;
}

/// <summary>즐겨찾기 아이콘 하나. 목록 항목 전용이라 소유 뷰모델 파일에 함께 둔다(SdiItem 과 같은 방식).</summary>
public sealed class FavoriteItem
{
    public FavoriteItem(string screenId, string title)
    {
        ScreenId = screenId;
        Title = title;
        MenuId = MenuIconCatalog.MenuIdOf(screenId);
        IconKey = MenuIconCatalog.IconKeyFor(screenId, null);
    }

    public string ScreenId { get; }

    public string? MenuId { get; }

    /// <summary>아이콘 리소스 키(메뉴 아이콘 카탈로그).</summary>
    public string IconKey { get; }

    /// <summary>표시 이름(툴팁·접근성 이름·키 없는 화면의 첫 글자 표지).</summary>
    public string Title { get; }

    /// <summary>원격·자동화 id(shell.favorite.{화면 ID}).</summary>
    public string AutomationId => "shell.favorite." + ScreenId;
}

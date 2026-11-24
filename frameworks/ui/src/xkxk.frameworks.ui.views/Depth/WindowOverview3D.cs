// 창 전체 보기 덮개(REQ-104, 18 문서 §전체 보기) — 열린 문서 창을 썸네일 카드로 평면 격자 또는 3D 회전목마로 펼치고 ←→·Enter·Esc·클릭으로 고른다
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.views.Snap;

namespace xkxk.frameworks.ui.views.Depth;

/// <summary>
/// 셸(원격·메뉴)은 Views 를 모르므로 시작 때 <see cref="Register"/> 가 WindowOverviewHost 대리자를 채운다(review H-1). 카드 = vm.OpenDocuments 순서, windowId = PocDocument.Id.
/// 방식: depth.overview 실효값이 carousel3d 이고 폴백 없음·창 수 ≤ depth.maxWindows·넓은 화면이면 회전목마, 아니면 평면 격자. 카드 크기 = theme.style.overview.cardSize.
/// 썸네일은 Snap Assist 의 SnapAssistThumbnail(정지 1회 찍기)을 재사용하고 닫을 때 해제한다. 기울어진 카드는 히트 대상이 아니고, 그 위의 회전 없는 평면 히트 사각형이 클릭을 받는다.
/// </summary>
public sealed partial class WindowOverview3D : Panel
{
    private const string Carousel = "carousel3d";
    private const string Flat = "flat";

    /// <summary>카드 사이 간격 = 카드 폭 × 이 비율(배치 비례값, 디자인 값 아님).</summary>
    private const double GapRatio = 0.08;

    private static WindowOverview3D? s_open;
    private static WeakReference<Control>? s_anchor;
    private static WeakReference<MainViewModel>? s_vm;

    private readonly AdornerLayer _adorners;
    private readonly MainViewModel _vm;
    private readonly List<PocDocument> _docs;
    private readonly List<Border> _cards = new();
    private readonly List<Border> _hits = new();
    private readonly string _mode;
    private readonly Size _cardSize;
    private Rect[] _hitRects = Array.Empty<Rect>();
    private TopLevel? _top;
    private IInputElement? _prevFocus;
    private int _selected;
    private bool _chosen;

    private WindowOverview3D(AdornerLayer adorners, MainViewModel vm, List<PocDocument> docs)
    {
        _adorners = adorners;
        _vm = vm;
        _docs = docs;
        _mode = ModeFor(vm, docs.Count);
        _cardSize = CardSize(DesignThemeResolver.Stored("overview.cardSize")) ?? CardSize(DesignThemes.Spec("overview.cardSize")?.Default) ?? default;
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Transparent; // 카드 밖 누름 = 닫기
        AutomationProperties.SetName(this, Loc.Tr("theme.style.overview"));
        var backdrop = new Border { IsHitTestVisible = false, Opacity = DesignThemeResolver.Number("glass.minOpacity") };
        backdrop.Bind(Border.BackgroundProperty, backdrop.GetResourceObservable("PdBrushSurface"));
        Children.Add(backdrop);
        for (var i = 0; i < docs.Count; i++)
        {
            AddCard(i, docs[i]);
            _selected = ReferenceEquals(docs[i], vm.ActiveDocument) ? i : _selected;
        }
    }

    /// <summary>시작 때 1회(MainView): WindowOverviewHost 의 Open·Select·State·SelectAt·DepthProbe 를 채우고 Requested(단축키·메뉴)를 받는다.</summary>
    public static void Register(MainViewModel vm, Control anchor)
    {
        s_vm = new WeakReference<MainViewModel>(vm);
        s_anchor = new WeakReference<Control>(anchor);
        WindowOverviewHost.Open = () => s_open is not null || OpenOn(anchor);
        WindowOverviewHost.Select = i => s_open?.Choose(i) ?? false;
        WindowOverviewHost.State = () => s_open?.StateJson() ?? Json(false, Vm() is { } v ? ModeFor(v, v.OpenDocuments.Count) : Flat, Array.Empty<string>());
        WindowOverviewHost.SelectAt = (x, y) => s_open?.ChooseAt(new Point(x, y));
        DepthProbeHost.Probe = kind => Task.FromResult(DepthTransitions.Probe(kind));
        DesignThemeResolver.NavLayout = () => vm.Compact.IsCompact; // NAV 이면 depth.overview 실효값 = flat(18 §6)
        vm.Compact.PropertyChanged -= OnCompactChanged; // 화면이 다시 붙어 Register 가 또 불려도 구독은 1개
        vm.Compact.PropertyChanged += OnCompactChanged;
        WindowOverviewHost.Requested -= OnRequested;
        WindowOverviewHost.Requested += OnRequested;
    }

    /// <summary>열려 있으면 닫고, 닫혀 있으면 anchor 위에 연다(계약 이름 고정).</summary>
    public static void Toggle(Control anchor) => _ = s_open?.Close() ?? OpenOn(anchor);

    private static void OnCompactChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompactModeViewModel.IsCompact))
        {
            DesignThemeResolver.Refresh();
        }
    }

    private static void OnRequested() => _ = s_anchor is not null && s_anchor.TryGetTarget(out var anchor) && (s_open?.Close() ?? OpenOn(anchor));

    private static MainViewModel? Vm() => s_vm is not null && s_vm.TryGetTarget(out var vm) ? vm : null;

    /// <summary>덮개를 연다. 창이 없거나 AdornerLayer 를 못 찾으면 false.</summary>
    private static bool OpenOn(Control anchor)
    {
        if (Vm() is not { } vm || vm.OpenDocuments.Count == 0 || AdornerLayer.GetAdornerLayer(anchor) is not { } adorners)
        {
            return false;
        }

        var overlay = new WindowOverview3D(adorners, vm, vm.OpenDocuments.ToList());
        AdornerLayer.SetAdornedElement(overlay, anchor);
        adorners.Children.Add(overlay);
        s_open = overlay;
        overlay._top = TopLevel.GetTopLevel(overlay);
        overlay._prevFocus = overlay._top?.FocusManager?.GetFocusedElement();
        overlay._top?.AddHandler(KeyDownEvent, overlay.OnTopKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        overlay.ApplySelection(); // 테마 브러시는 화면 트리에 붙은 뒤에 찾는다
        overlay.Focus();
        return true;
    }

    private static string ModeFor(MainViewModel vm, int count) =>
        DesignThemeResolver.Effective("depth.overview") == Carousel && DesignThemeResolver.FallbackReason is null
            && !vm.Compact.IsCompact && count <= DesignThemeResolver.Number("depth.maxWindows") ? Carousel : Flat;

    private static Size? CardSize(string? text)
    {
        var p = (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        return p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && w > 0 && h > 0 ? new Size(w, h) : null;
    }

    /// <summary>카드(썸네일 또는 제목, 아래 제목 줄 — 히트 대상 아님)와 그 위의 평면 히트 사각형을 더한다.</summary>
    private void AddCard(int index, PocDocument doc)
    {
        Control body = new TextBlock { Text = doc.Title, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (FindSource(doc) is { } source && SnapAssistThumbnail.TryCapture(source, _cardSize, out var image, out _) && image is not null)
        {
            body = new Image { Source = image, Stretch = Stretch.Uniform };
        }

        var title = new TextBlock { Text = doc.Title, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(title, 1);
        var card = new Border { Child = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { body, title } }, IsHitTestVisible = false, ClipToBounds = true, RenderTransformOrigin = RelativePoint.Center };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("PdBrushSurface"));
        var hit = new Border { Background = Brushes.Transparent, Tag = index, Cursor = new Cursor(StandardCursorType.Hand) };
        AutomationProperties.SetName(hit, doc.Title);
        hit.PointerPressed += (_, e) => e.Handled = Choose(index);
        _cards.Add(card);
        _hits.Add(hit);
        Children.Add(card);
        Children.Add(hit);
    }

    /// <summary>썸네일 원천: 같은 문서(또는 그 묶음)의 MDI·관리형 창 화면 요소, 없으면 그 문서를 담은 OS 독립 창. 못 찾으면 null(제목 카드).</summary>
    private object? FindSource(PocDocument doc)
    {
        var group = MdiTabGroup.WindowOf(doc);
        var root = (Visual?)TopLevel.GetTopLevel(_adorners) ?? _adorners;
        bool Shows(Control c) => ReferenceEquals(c.DataContext, group) || ReferenceEquals(WindowCommands.FindDocument(c.DataContext), doc);
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();
        return (object?)root.GetVisualDescendants().OfType<MdiDocumentWindow>().FirstOrDefault(Shows)
            ?? windows.FirstOrDefault(w => !ReferenceEquals(w, root) && w.GetVisualDescendants().OfType<Control>().Any(Shows));
    }

    /// <summary>덮개를 닫는다(항상 true — 식 안에서 '처리함' 으로 쓴다). 고르지 않고 닫으면 표시 전 포커스로 돌려준다.</summary>
    private bool Close()
    {
        if (ReferenceEquals(s_open, this))
        {
            s_open = null;
            _top?.RemoveHandler(KeyDownEvent, OnTopKeyDown);
            _adorners.Children.Remove(this);
            SnapAssistThumbnail.ReleaseAll();
            if (!_chosen && _prevFocus is Visual prev && TopLevel.GetTopLevel(prev) is not null)
            {
                _prevFocus.Focus();
            }
        }

        return true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = e.Handled || Close(); // 히트 사각형이 처리하지 않은 누름 = 카드 밖 → 닫기
    }

    private void OnTopKeyDown(object? sender, KeyEventArgs e)
    {
        var step = e.Key switch { Key.Left => -1, Key.Right => 1, _ => 0 };
        if (step != 0)
        {
            _selected = (_selected + step + _docs.Count) % _docs.Count;
            ApplySelection();
            InvalidateArrange();
        }

        e.Handled |= step != 0 || (e.Key == Key.Enter && Choose(_selected)) || (e.Key == Key.Escape && Close());
    }

    private void ApplySelection()
    {
        for (var i = 0; i < _cards.Count; i++)
        {
            var on = i == _selected;
            _cards[i].BorderBrush = this.TryFindResource(on ? "PdBrushAccent" : "PdBrushBorder", ActualThemeVariant, out var brush) ? brush as IBrush : null;
            _cards[i].BorderThickness = new Thickness(on ? 2 : 1); // 선택 강조 두께(화소, 디자인 값 아님)
            _cards[i].ZIndex = _docs.Count - Math.Abs(i - _selected);
            _hits[i].ZIndex = (_docs.Count * 2) - Math.Abs(i - _selected); // 히트 사각형은 항상 카드 위
        }
    }

    /// <summary>index 번째 창을 고른다(덮개를 닫고 그 문서를 활성화). 범위 밖이면 false.</summary>
    private bool Choose(int index)
    {
        if (index < 0 || index >= _docs.Count)
        {
            return false;
        }

        _chosen = true; // 고른 창이 활성이 되므로 표시 전 포커스로 돌려주지 않는다
        Close();
        _vm.Commands.Activate(_docs[index]);
        return true;
    }

    /// <summary>덮개 좌표에서 실제 히트 시험으로 평면 히트 사각형을 찾아 그 창을 고른다. 고른 windowId, 빈 곳이면 null.</summary>
    private string? ChooseAt(Point point)
    {
        UpdateLayout(); // 원격이 열자마자 부를 수 있다 — 배치를 먼저 끝낸다(이미 유효하면 바로 돌아온다)
        var hit = (this.InputHitTest(point) as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(_hits.Contains);
        return hit is { Tag: int index } && Choose(index) ? _docs[index].Id : null;
    }

    private string StateJson()
    {
        UpdateLayout();
        return Json(true, _mode, _docs.Select((d, i) => "{\"windowId\":\"" + JsonEncodedText.Encode(d.Id ?? string.Empty).Value + "\",\"x\":" + Num(_hitRects[i].Center.X)
            + ",\"y\":" + Num(_hitRects[i].Center.Y) + ",\"w\":" + Num(_hitRects[i].Width) + ",\"h\":" + Num(_hitRects[i].Height) + "}"));
    }

    /// <summary>{open, mode, cards[{windowId,x,y,w,h}]}(창 목록·활성 창은 원격 style.state 가 OpenDocuments·ActiveDocument 로 낸다). 닫혀 있으면 mode = 지금 열면 쓸 방식.</summary>
    private static string Json(bool open, string mode, IEnumerable<string> cards) =>
        "{\"open\":" + (open ? "true" : "false") + ",\"mode\":\"" + mode + "\",\"cards\":[" + string.Join(",", cards) + "]}";

    private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}

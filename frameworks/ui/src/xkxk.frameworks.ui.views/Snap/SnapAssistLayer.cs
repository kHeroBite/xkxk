// 남은 칸 창 고르기(Snap Assist) 덮개 — 남은 칸마다 흐린 판을 그리고 활성 칸에 후보 카드를 붙이며, 키·포인터·포커스 입력을 SnapAssistSession 으로 넘긴다(11 문서 §3.9, 계획 §5.4)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.views.Snap;

/// <summary>
/// 세션 하나에 덮개 하나(Hide 뒤 다시 쓰지 않는다). 좌표는 <see cref="SnapAssistView.Area"/> 와 같은 좌표계이고, 그릴 때 <see cref="Origin"/> 을 뺀다.
/// Mdi·Managed 는 <see cref="Attach"/> 로 anchor(배치 패널)의 AdornerLayer 에 붙고(패널 로컬 좌표, Origin=0),
/// Sdi 는 데스크톱이 등록한 <see cref="SdiHostFactory"/> 의 최상위 창 내용으로 쓰며 Origin=작업 영역 TopLeft(Show 가 Area 로 맞춘다).
/// 입력: 카드 누름·Enter·Space → Pick, 흐린 다른 칸 누름 → SelectZone, Tab·방향키 → MoveFocus, Esc → Cancel(esc),
/// 카드·흐린 칸 밖 누름(채운 칸·영역 밖 포함) → Cancel(outside), 포커스가 덮개 밖으로·창 비활성 → Cancel(focus).
/// Pick 이 활성 창을 바꿔 포커스를 빼앗으므로 Show·Update 마다 포커스를 다시 가져가고, 그 사이 포커스 이탈은 보지 않는다.
/// </summary>
public sealed partial class SnapAssistLayer : Panel, ISnapAssistOverlay
{
    private const string LogTag = "snap-assist";

    private readonly ZonesVisual _zonesVisual;
    private readonly Dictionary<string, IImage?> _thumbs = new();
    private readonly Dictionary<string, string> _thumbStatus = new(); // 후보 키 → bitmap | fallback:<사유> (SnapAssistSession.ReportThumb 로 알린다)
    private SnapAssistPicker? _picker;
    private SnapAssistView? _view;
    private Panel? _attachedTo;
    private TopLevel? _top;
    private IInputElement? _prevFocus;
    private Point _origin;
    private bool _shown;
    private bool _hidden;
    private bool _refocusPending;
    private bool _thumbsBroken;
    private bool _picked;

    public SnapAssistLayer()
    {
        Focusable = true;
        IsVisible = false;
        IsHitTestVisible = false;
        ClipToBounds = false;
        Background = null;
        AutomationProperties.SetAutomationId(this, "snap.assist");
        _zonesVisual = new ZonesVisual(this);
        Children.Add(_zonesVisual);
        LostFocus += OnLostFocus;
        // Tab 탐색이 덮개 밖(메뉴 등)으로 나가지 않게 덮개 안에서만 돈다(AC-015 — 실 Tab 이 FileMenu 로 포커스를 넘겨 세션이 focus 로 끝나던 결함)
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    /// <summary>
    /// SDI(데스크톱 독립 창) 덮개 생성(데스크톱 SdiSnapAssistHost 가 시작 때 등록 — 계획 §5.5). 인자는 배치한 창.
    /// 등록이 없으면 SDI 세션은 no-overlay 로 끝난다.
    /// </summary>
    public static Func<Window, ISnapAssistOverlay?>? SdiHostFactory { get; set; }

    /// <summary>덮개가 숨겨졌다(세션 끝). SDI 호스트 창이 이때 자기를 닫는다.</summary>
    public event Action? Hidden;

    /// <summary>판 좌표 → 이 덮개 좌표 변환에서 빼는 값. Mdi·Managed 는 (0,0), Sdi 는 작업 영역 TopLeft.</summary>
    public Point Origin => _origin;

    /// <summary>지금 그리는 장면(시험·진단용). 표시 전·숨긴 뒤에는 null.</summary>
    public SnapAssistView? View => _shown ? _view : null;

    /// <summary>카드 목록 컨트롤(처음 Show 전에는 null).</summary>
    public SnapAssistPicker? Picker => _picker;

    /// <summary>Shell 층 이음매 등록(Ui.Views 어셈블리 적재 때 1회). 이미 다른 곳이 등록했으면 덮어쓰지 않는다.</summary>
#pragma warning disable CA2255 // 등록 파일(App)이 배정 밖이라 어셈블리 적재 때 이음매를 채운다(SnapOverlayLayer 와 같은 방식)
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterHost()
    {
        SnapAssistHost.Factory ??= Create;
        SnapAssistSession.ThumbDumpHook ??= SnapAssistThumbnail.DumpDiagnostic; // 진단 전용(원격 snapassist.thumbdump)
    }

    /// <summary>범위별 덮개 생성. Sdi 는 데스크톱 호스트에 맡기고, Mdi·Managed 는 anchor 패널의 AdornerLayer 에 붙인다.</summary>
    internal static ISnapAssistOverlay? Create(SnapAssistScope scope, object anchor)
    {
        if (scope == SnapAssistScope.Sdi)
        {
            return anchor is Window w ? SdiHostFactory?.Invoke(w) : null;
        }

        return anchor is Control c ? Attach(c) : null;
    }

    /// <summary>
    /// 영역 위 AdornerLayer 에 새 덮개를 붙이고 돌려준다. 영역이 아직 화면 트리에 없어 AdornerLayer 를 못 찾으면 null.
    /// 덮개는 세션마다 새로 만들고 Hide 때 뗀다(끌기 미리보기 SnapOverlayLayer 와 같은 층에 놓이며, 나중에 붙으므로 그 위에 그려진다).
    /// </summary>
    public static SnapAssistLayer? Attach(Control area)
    {
        if (AdornerLayer.GetAdornerLayer(area) is not { } adorners)
        {
            return null;
        }

        var layer = new SnapAssistLayer();
        AdornerLayer.SetAdornedElement(layer, area);
        adorners.Children.Add(layer);
        layer._attachedTo = adorners;
        return layer;
    }

    // ── ISnapAssistOverlay ──

    /// <inheritdoc/>
    public void Show(SnapAssistView view)
    {
        if (_hidden)
        {
            return;
        }

        _top = TopLevel.GetTopLevel(this);
        _prevFocus = _top?.FocusManager?.GetFocusedElement();
        if (_top is not null)
        {
            _top.AddHandler(PointerPressedEvent, OnTopPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            _top.AddHandler(KeyDownEvent, OnTopKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            if (_top is Window w)
            {
                w.Deactivated += OnWindowDeactivated;
            }
        }

        AutomationProperties.SetName(this, Loc.Tr("snapAssist.region"));
        Background = Brushes.Transparent; // 빈 곳 누름도 덮개가 받아 바깥 클릭을 판정한다
        IsHitTestVisible = true;
        IsVisible = true;
        _shown = true;
        Apply(view);
        TakeFocus();
    }

    /// <inheritdoc/>
    public void Update(SnapAssistView view)
    {
        if (!_shown || _hidden)
        {
            return;
        }

        Apply(view);
        TakeFocus(); // Pick 의 배치가 활성 창을 바꿔 빼앗은 포커스를 되찾는다(세션은 그 사이 이탈 취소를 무시한다)
    }

    /// <inheritdoc/>
    public void Hide()
    {
        if (_hidden)
        {
            return;
        }

        _hidden = true;
        var hadFocus = IsKeyboardFocusWithin;
        _shown = false;
        if (_top is not null)
        {
            _top.RemoveHandler(PointerPressedEvent, OnTopPointerPressed);
            _top.RemoveHandler(KeyDownEvent, OnTopKeyDown);
            if (_top is Window w)
            {
                w.Deactivated -= OnWindowDeactivated;
            }
        }

        LostFocus -= OnLostFocus;
        if (_picker is not null)
        {
            _picker.CardPicked -= OnCardPicked;
        }

        IsHitTestVisible = false;
        IsVisible = false;
        _attachedTo?.Children.Remove(this);
        _attachedTo = null;
        _thumbs.Clear();
        _thumbStatus.Clear();
        try
        {
            SnapAssistThumbnail.ReleaseAll();
        }
        catch (Exception ex)
        {
            PdLog.Info(LogTag, $"thumb release failed: {ex.Message}");
        }

        // 하나도 고르지 않고 끝났고 덮개가 포커스를 갖고 있었으면 표시 전 포커스로 돌려준다(그 요소가 아직 화면에 있을 때만).
        // 골랐으면 돌려주지 않는다 — 마지막에 놓은 창이 ApplyCell 로 활성이 됐다(계획 §9)
        if (hadFocus && !_picked && _prevFocus is Visual prev && TopLevel.GetTopLevel(prev) is not null)
        {
            _prevFocus.Focus();
        }

        _prevFocus = null;
        _top = null;
        _view = null;
        Hidden?.Invoke();
    }

    // ── 장면 반영 ──

    private void Apply(SnapAssistView view)
    {
        if (_view is { } prevView && view.Zones.Count(z => z.Filled) > prevView.Zones.Count(z => z.Filled))
        {
            _picked = true; // 칸이 하나 더 찼다 = 고른 창이 놓였다(카드·키·원격 공통)
        }

        _view = view;
        _origin = view.Scope == SnapAssistScope.Sdi ? view.Area.TopLeft : default;
        if (_picker is null)
        {
            _picker = new SnapAssistPicker();
            _picker.CardPicked += OnCardPicked;
            Children.Add(_picker);
        }

        var old = _picker.CurrentCards;
        var same = old.Count == view.Candidates.Count
            && old.Select(c => c.Key).SequenceEqual(view.Candidates.Select(c => c.Key))
            && old.All(c => c.SlotHeight == (view.Thumbnails ? SnapAssistCard.ThumbHeight : SnapAssistCard.IconSlotHeight));
        IReadOnlyList<SnapAssistCard> cards = same ? old : BuildCards(view);
        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].IsFocused = i == view.FocusIndex;
        }

        _picker.SetCards(cards, view.ActiveCell);
        _picker.IsVisible = cards.Count > 0 && ActiveZone(view) is not null;
        InvalidateMeasure();
        InvalidateArrange();
        _zonesVisual.InvalidateVisual();
        if (view.FocusIndex >= 0)
        {
            var focus = view.FocusIndex;
            Dispatcher.UIThread.Post(() => _picker?.BringFocusedIntoView(focus), DispatcherPriority.Loaded);
        }
    }

    private static SnapAssistZone? ActiveZone(SnapAssistView view) =>
        view.Zones.FirstOrDefault(z => z.Cell == view.ActiveCell && !z.Filled);
}

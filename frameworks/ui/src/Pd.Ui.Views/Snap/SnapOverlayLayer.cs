// 창 배치(스냅) 덮개 층 — 기준 영역 위에 상단 막대와 놓을 위치 미리보기(강조색 0.25 + 1 DIP 테두리)를 그린다. 입력 통과, 판정은 하지 않는다(11 문서 §3.1·§4)
using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Pd.Poc.Shell.Docking;

namespace Pd.Ui.Views.Snap;

/// <summary>
/// 기준 영역(MDI 영역·웹 관리형 창 영역·SDI 막대 창 내용) 위 덮개. 좌표는 판정 문맥(SnapZoneContext)과 같은 좌표계이고, 그릴 때 <see cref="Origin"/> 을 뺀다.
/// MDI·웹은 <see cref="Attach"/> 로 영역의 AdornerLayer 에 붙이고(영역 로컬 좌표, Origin=0), SDI 최상위 창은 창 내용에 두고 Origin=작업 영역 TopLeft 로 쓴다.
/// Shell 층 끌기 처리기는 Ui.Views 를 모르므로 <see cref="ISnapOverlay"/>·<see cref="SnapOverlayHost"/> 로 부른다(등록은 이 파일의 모듈 초기화).
/// 막대 자식은 처음 막대를 띄울 때 만든다(BusyOverlay 의 스피너와 같은 방식). 모든 호출은 UI 스레드에서 한다.
/// </summary>
public sealed class SnapOverlayLayer : Panel, ISnapOverlay
{
    /// <summary>미리보기 채움 불투명도(11 문서 §4).</summary>
    public const double PreviewOpacity = 0.25;

    /// <summary>미리보기 테두리 두께(DIP, 11 문서 §4).</summary>
    public const double PreviewBorder = 1;

    /// <summary>미리보기 모서리 반지름(DIP, 초기 제시값).</summary>
    public const double PreviewRadius = 4;

    private const string AccentKey = "PdBrushAccent";

    private static readonly ConditionalWeakTable<Control, SnapOverlayLayer> Layers = new();

    private readonly PreviewVisual _previewVisual;
    private SnapLayoutBar? _bar;
    private Rect _preview;
    private Point _origin;

    public SnapOverlayLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        IsVisible = false;
        ClipToBounds = false;
        _previewVisual = new PreviewVisual(this);
        Children.Add(_previewVisual);
    }

    /// <summary>Shell 층 이음매 등록(Ui.Views 어셈블리 적재 때 1회). 이미 다른 곳이 등록했으면 덮어쓰지 않는다.</summary>
#pragma warning disable CA2255 // 등록 파일(App)이 배정 밖이라 어셈블리 적재 때 이음매를 채운다(SdiTitleBar.TabJoin·TourInputGate 와 같은 방식)
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterHost()
    {
        SnapOverlayHost.Factory ??= area => Attach(area);
    }

    /// <summary>문맥 좌표 → 이 층 좌표 변환에서 빼는 값. 기본 (0,0). SDI 막대 창은 모니터 작업 영역의 TopLeft.</summary>
    public Point Origin
    {
        get => _origin;
        set
        {
            if (_origin == value)
            {
                return;
            }

            _origin = value;
            InvalidateArrange();
            _previewVisual.InvalidateVisual();
        }
    }

    /// <summary>막대가 지금 보이는가(Resolve 의 barVisible 인자).</summary>
    public bool IsBarVisible => _bar is { IsVisible: true };

    /// <summary>지금 보이는 미리보기 사각형(문맥 좌표). 없으면 빈 사각형(시험·원격 상태용).</summary>
    public Rect PreviewRect => _preview;

    /// <summary>막대 컨트롤(처음 ShowBar 전에는 null).</summary>
    public SnapLayoutBar? Bar => _bar;

    /// <summary>
    /// 영역 위 AdornerLayer 에 덮개를 붙이고 돌려준다. 이미 붙어 있으면 같은 덮개를 돌려준다(끌기 시작마다 불러도 된다).
    /// 영역이 아직 화면 트리에 없어 AdornerLayer 를 못 찾으면 null. 영역의 다른 어도너(AdornerLayer.Adorner 스타일)는 건드리지 않는다.
    /// </summary>
    public static SnapOverlayLayer? Attach(Control area)
    {
        var adorners = AdornerLayer.GetAdornerLayer(area);
        if (adorners is null)
        {
            return null;
        }

        var layer = Layers.GetValue(area, _ => new SnapOverlayLayer());
        if (!ReferenceEquals(layer.Parent, adorners))
        {
            // 영역이 다른 최상위(창 분리·SDI 전환)로 옮겨졌으면 옛 층에서 떼고 새 층에 붙인다
            (layer.Parent as Panel)?.Children.Remove(layer);
            AdornerLayer.SetAdornedElement(layer, area);
            adorners.Children.Add(layer);
        }

        return layer;
    }

    /// <summary>판정 문맥의 막대를 띄운다(BarRect·Tiles 그대로). 막대를 띄울 수 없는 문맥이면 막대를 숨긴다.</summary>
    public void ShowBar(SnapZoneContext ctx)
    {
        if (!ctx.HasBar)
        {
            HideBar();
            return;
        }

        if (_bar is null)
        {
            _bar = new SnapLayoutBar();
            Children.Add(_bar);
        }

        if (!ReferenceEquals(_bar.Context, ctx))
        {
            _bar.SetContext(ctx);
            InvalidateArrange();
        }

        _bar.IsVisible = true;
        UpdateVisible();
    }

    /// <summary>막대를 숨긴다(강조도 지운다).</summary>
    public void HideBar()
    {
        if (_bar is { IsVisible: true })
        {
            _bar.Highlight(null, -1);
            _bar.IsVisible = false;
        }

        UpdateVisible();
    }

    /// <summary>막대의 칸 하나를 강조한다. null·-1 이면 강조를 지운다. 막대가 없으면 아무것도 하지 않는다.</summary>
    public void Highlight(string? layoutId, int cell) => _bar?.Highlight(layoutId, cell);

    /// <summary>놓을 위치 미리보기를 보인다(문맥 좌표). 빈 사각형이면 숨긴다.</summary>
    public void ShowPreview(Rect target)
    {
        if (target.Width <= 0 || target.Height <= 0)
        {
            HidePreview();
            return;
        }

        if (_preview == target)
        {
            return;
        }

        _preview = target;
        UpdateVisible();
        _previewVisual.InvalidateVisual();
    }

    /// <summary>미리보기를 숨긴다.</summary>
    public void HidePreview()
    {
        if (_preview == default)
        {
            return;
        }

        _preview = default;
        UpdateVisible();
        _previewVisual.InvalidateVisual();
    }

    /// <summary>
    /// 판정 결과 하나로 미리보기·칸 강조를 맞춘다. 탭 합치기면 막대·미리보기를 모두 숨긴다(탭 합류 우선, 11 문서 §5).
    /// 막대 표시 여부(WantsBar + SnapBarDelayMs 머무름)는 호출자가 ShowBar/HideBar 로 정한다.
    /// </summary>
    public void Show(SnapZone zone)
    {
        if (zone.Kind == SnapZoneKind.TabJoin)
        {
            Hide();
            return;
        }

        Highlight(zone.LayoutId, zone.LayoutId is null ? -1 : zone.Cell);
        if (zone.HasTarget)
        {
            ShowPreview(zone.Target);
        }
        else
        {
            HidePreview();
        }
    }

    /// <summary>막대·미리보기를 모두 숨긴다(놓기·Esc·캡처 잃음).</summary>
    public void Hide()
    {
        HideBar();
        HidePreview();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        // 어도너 층·창 내용이 주는 크기를 그대로 쓴다(무한이면 0)
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (child is SnapLayoutBar { Context: { } ctx })
            {
                var r = ctx.BarRect;
                child.Arrange(new Rect(r.X - _origin.X, r.Y - _origin.Y, r.Width, r.Height));
            }
            else
            {
                child.Arrange(new Rect(finalSize));
            }
        }

        return finalSize;
    }

    private void UpdateVisible() => IsVisible = IsBarVisible || _preview.Width > 0;

    /// <summary>미리보기 그리기 자식(Panel.Render 는 봉인이라 따로 둔다). 첫 자식이라 막대보다 아래에 그려진다.</summary>
    private sealed class PreviewVisual : Control
    {
        private readonly SnapOverlayLayer _owner;

        public PreviewVisual(SnapOverlayLayer owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            Focusable = false;
        }

        public override void Render(DrawingContext context)
        {
            var preview = _owner._preview;
            if (preview.Width <= 0 || preview.Height <= 0)
            {
                return;
            }

            // 테마 강조색 토큰(PdBrushAccent). 끌기 중에만 잠깐 보이므로 그릴 때마다 찾는다(테마 전환 즉시 반영)
            var accent = this.TryFindResource(AccentKey, ActualThemeVariant, out var res) && res is IBrush b ? b : Brushes.DodgerBlue;
            var o = _owner._origin;
            var r = preview.Translate(new Vector(-o.X, -o.Y)).Deflate(PreviewBorder / 2);
            using (context.PushOpacity(PreviewOpacity))
            {
                context.DrawRectangle(accent, null, r, PreviewRadius, PreviewRadius);
            }

            context.DrawRectangle(null, new Pen(accent, PreviewBorder), r, PreviewRadius, PreviewRadius);
        }
    }
}

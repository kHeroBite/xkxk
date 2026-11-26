// 창·메뉴 아이콘 공용 컨트롤 — 아이콘, 불러오는 중 스피너·정지 글리프, 지연·오류 표지(plan/09_화면설계/22 §2.3, 23 §3·§4)
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 표시 자리는 모두 이 컨트롤을 쓴다. <see cref="Document"/> 가 있으면 그 문서의 아이콘·상태를 따르고, 없으면 <see cref="IconKey"/> 만 그린다.
/// 문서·설정 구독은 시각 트리에 붙어 있는 동안만 유지한다.
/// </summary>
public sealed partial class WindowIcon : Control
{
    private const string BusyStaticKey = "PdIconBusyStatic";
    private const double Grid24 = 24;
    private const double DefaultSize = 16;

    public static readonly StyledProperty<string?> IconKeyProperty =
        AvaloniaProperty.Register<WindowIcon, string?>(nameof(IconKey));

    public static readonly StyledProperty<PocDocument?> DocumentProperty =
        AvaloniaProperty.Register<WindowIcon, PocDocument?>(nameof(Document));

    public static readonly StyledProperty<string?> FallbackTextProperty =
        AvaloniaProperty.Register<WindowIcon, string?>(nameof(FallbackText));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<WindowIcon>();

    /// <summary>
    /// 불러오는 중 표시가 켜져 있는가 — show_delay_ms·min_visible_ms 를 반영한 값이다(23 §3). 시간 판정은 이 컨트롤 한 곳에만 두고,
    /// 제목만 탭의 inline·underline 같은 대체 표시는 이 값을 읽는다(IsVisible 이 꺼져 있어도 계산한다).
    /// </summary>
    public static readonly DirectProperty<WindowIcon, bool> IsBusyShownProperty =
        AvaloniaProperty.RegisterDirect<WindowIcon, bool>(nameof(IsBusyShown), o => o.IsBusyShown);

    private readonly IconSpinner _spinner = new() { IsVisible = false };
    private readonly BadgeLayer _badge = new();
    private PocDocument? _watched;
    private IDisposable? _showTimer;
    private IDisposable? _hideTimer;
    private IDisposable? _errorTimer;
    private long _shownAt;
    private bool _busy;
    private bool _attached;

    static WindowIcon()
    {
        AffectsRender<WindowIcon>(IconKeyProperty, FallbackTextProperty, ForegroundProperty);
    }

    public WindowIcon()
    {
        Focusable = false;
        VisualChildren.Add(_spinner);
        VisualChildren.Add(_badge); // 표지는 스피너 위에 그린다
        LogicalChildren.Add(_spinner);
        LogicalChildren.Add(_badge);
        ResourcesChanged += (_, _) => RefreshResources();
        ActualThemeVariantChanged += (_, _) => RefreshResources();
    }

    /// <summary>아이콘 리소스 키. Document 가 있으면 문서 값이 앞선다.</summary>
    public string? IconKey { get => GetValue(IconKeyProperty); set => SetValue(IconKeyProperty, value); }

    /// <summary>아이콘·불러오는 중 상태를 따를 문서.</summary>
    public PocDocument? Document { get => GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }

    /// <summary>글리프를 못 찾을 때 첫 글자 원형 표지로 쓸 글.</summary>
    public string? FallbackText { get => GetValue(FallbackTextProperty); set => SetValue(FallbackTextProperty, value); }

    /// <summary>아이콘 색(글자색 상속).</summary>
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>불러오는 중 표시가 켜져 있는가(읽기 전용, 바뀌면 통지).</summary>
    public bool IsBusyShown => _busy;

    /// <summary>실제로 그리는 아이콘 키(진단·판정).</summary>
    public string? EffectiveIconKey => Document is { } doc
        ? doc.IconKey ?? MenuIconCatalog.IconKeyFor(doc.ScreenId, doc.Kind.ToString())
        : IconKey;

    /// <summary>지금 보이는 상태 icon·spinner·static(진단·판정).</summary>
    public string DisplayState => !_busy ? "icon" : _spinner.IsVisible ? "spinner" : "static";

    // RTL 에서 통째로 뒤집지 않는다(22 §2.2 — 방향 아이콘만 뒤집는다)
    protected override bool BypassFlowDirectionPolicies => true;

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        return new Size(Math.Min(DefaultSize, availableSize.Width), Math.Min(DefaultSize, availableSize.Height));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        XkxkUi.Current.Settings.PropertyChanged += OnSettingsChanged;
        DesignThemeResolver.Changed += Apply;
        Watch(Document);
        RefreshResources();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        XkxkUi.Current.Settings.PropertyChanged -= OnSettingsChanged;
        DesignThemeResolver.Changed -= Apply;
        Watch(null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            if (_attached)
            {
                Watch(Document);
            }

            InvalidateVisual();
        }
        else if (change.Property == IsVisibleProperty)
        {
            Apply();
        }
        else if (change.Property == ForegroundProperty)
        {
            RefreshResources(); // 활성·비활성 전환으로 글자색이 바뀌면 스피너도 따라간다
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds); // 툴팁 히트 영역
        double side = Math.Min(bounds.Width, bounds.Height);
        if (side <= 0 || _spinner.IsVisible || Foreground is not { } brush)
        {
            return;
        }

        var origin = new Point((bounds.Width - side) / 2, (bounds.Height - side) / 2);
        var glyph = _busy
            ? FindGeometry(BusyStaticKey)
            : FindGeometry(EffectiveIconKey) ?? FindGeometry(MenuIconCatalog.DefaultDocumentIconKey);
        if (glyph is not null)
        {
            using (context.PushTransform(Matrix.CreateScale(side / Grid24, side / Grid24) * Matrix.CreateTranslation(origin.X, origin.Y)))
            {
                context.DrawGeometry(brush, null, glyph);
            }

            return;
        }

        DrawLetter(context, brush, new Rect(origin, new Size(side, side)));
    }

    private void DrawLetter(DrawingContext context, IBrush brush, Rect box)
    {
        context.DrawEllipse(null, new Pen(brush, 1), box.Center, box.Width / 2 - 0.5, box.Height / 2 - 0.5);
        var source = FallbackText ?? Document?.Title;
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var text = new FormattedText(
            StringInfo.GetNextTextElement(source.Trim()).ToUpperInvariant(), CultureInfo.CurrentUICulture,
            Avalonia.Media.FlowDirection.LeftToRight, new Typeface(TextElement.GetFontFamily(this)), box.Width * 0.6, brush);
        context.DrawText(text, new Point(box.Center.X - text.Width / 2, box.Center.Y - text.Height / 2));
    }

    private Geometry? FindGeometry(string? key) =>
        key is not null && this.TryFindResource(key, ActualThemeVariant, out var value) ? value as Geometry : null;

    private IBrush? FindBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    private void RefreshResources()
    {
        if (!_attached)
        {
            return;
        }

        _spinner.Brush = Foreground ?? FindBrush("PdBrushAccent"); // 아이콘과 같은 글자색 — 강조색은 활성 제목줄·탭 바탕과 같아 안 보인다
        _badge.SlowBrush = FindBrush("PdBrushWarning");
        _badge.ErrorBrush = FindBrush("PdBrushError");
        _badge.InvalidateVisual();
        InvalidateVisual();
    }
}

// 설정 창 뼈대 코드비하인드(요건 7) — 범주·페이지·검색어 속성과 좁은 폭 배치 전환. 값·명령은 상위 층이 바인딩으로 넣는다
using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace Pd.Ui.Controls;

public partial class SettingsShell : UserControl
{
    public static readonly StyledProperty<IEnumerable?> CategoriesProperty =
        AvaloniaProperty.Register<SettingsShell, IEnumerable?>(nameof(Categories));

    public static readonly StyledProperty<object?> SelectedCategoryProperty =
        AvaloniaProperty.Register<SettingsShell, object?>(nameof(SelectedCategory), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IDataTemplate?> CategoryTemplateProperty =
        AvaloniaProperty.Register<SettingsShell, IDataTemplate?>(nameof(CategoryTemplate));

    public static readonly StyledProperty<object?> PageContentProperty =
        AvaloniaProperty.Register<SettingsShell, object?>(nameof(PageContent));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<SettingsShell, object?>(nameof(Footer));

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<SettingsShell, string?>(nameof(SearchText), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> SearchWatermarkProperty =
        AvaloniaProperty.Register<SettingsShell, string?>(nameof(SearchWatermark));

    public static readonly StyledProperty<string?> EmptyTextProperty =
        AvaloniaProperty.Register<SettingsShell, string?>(nameof(EmptyText));

    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<SettingsShell, bool>(nameof(IsEmpty));

    /// <summary>이 폭보다 좁으면 범주 목록을 페이지 위 가로 줄로 옮긴다(스마트폰).</summary>
    private const double NarrowWidth = 560;

    private const double CategoryWidth = 168;

    private bool? _narrow;

    public SettingsShell()
    {
        InitializeComponent();
    }

    /// <summary>검색어가 바뀐 직후 알림. 페이지 안 SettingsRow 가 구독한다.</summary>
    public event Action? SearchTextChanged;

    public IEnumerable? Categories { get => GetValue(CategoriesProperty); set => SetValue(CategoriesProperty, value); }

    public object? SelectedCategory { get => GetValue(SelectedCategoryProperty); set => SetValue(SelectedCategoryProperty, value); }

    public IDataTemplate? CategoryTemplate { get => GetValue(CategoryTemplateProperty); set => SetValue(CategoryTemplateProperty, value); }

    /// <summary>오른쪽 페이지 내용. 뷰는 상위 층의 DataTemplates 가 고른다.</summary>
    public object? PageContent { get => GetValue(PageContentProperty); set => SetValue(PageContentProperty, value); }

    /// <summary>아래 버튼 칸(기본값·닫기).</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    public string? SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }

    public string? SearchWatermark { get => GetValue(SearchWatermarkProperty); set => SetValue(SearchWatermarkProperty, value); }

    /// <summary>검색 결과가 없을 때 보일 문구.</summary>
    public string? EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    public bool IsEmpty { get => GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SearchTextProperty)
        {
            SearchTextChanged?.Invoke();
        }
        else if (change.Property == BoundsProperty)
        {
            ApplyLayout(Bounds.Width);
        }
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var narrow = width < NarrowWidth;
        if (narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        DockPanel.SetDock(CategoryList, narrow ? Dock.Top : Dock.Left);
        CategoryList.Width = narrow ? double.NaN : CategoryWidth;
        CategoryList.Margin = narrow ? new Thickness(12, 0, 12, 4) : new Thickness(12, 0, 0, 0);
        CategoryList.BorderThickness = narrow ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(CategoryList, narrow ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        CategoryList.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical });
    }
}

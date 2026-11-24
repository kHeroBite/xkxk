// 제목줄 즐겨찾기 영역 코드 비하인드 — 허용 폭(제목 줄 폭 × max_width_pct)에서 보일 개수 계산, 넘침·빼기 메뉴 처리, 끌어서 순서 바꾸기
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

/// <summary>즐겨찾기 영역. 배치·폭 상한은 MainView(MainView.FavoriteBar.cs)가 정하고, 이 컨트롤은 받은 폭으로 보일 개수만 계산해 뷰모델에 넘긴다.</summary>
public partial class FavoriteBarView : UserControl
{
    /// <summary>아이콘 칸 폭(DIP).</summary>
    public const double ItemWidth = 24;

    /// <summary>아이콘 칸 사이 간격(DIP).</summary>
    public const double ItemSpacing = 2;

    private const double DragThreshold = 6;

    /// <summary>영역이 쓸 수 있는 최대 폭(DIP, ui.shell.favorite_bar.max_width_pct 로 계산한 값). 바뀌면 보일 개수를 다시 정한다.</summary>
    public static readonly StyledProperty<double> MaxAllowedWidthProperty =
        AvaloniaProperty.Register<FavoriteBarView, double>(nameof(MaxAllowedWidth), double.PositiveInfinity);

    private FavoriteItem? _dragItem;
    private Point _dragStart;
    private bool _dragging;

    public FavoriteBarView()
    {
        InitializeComponent();
        FavoriteItems.AddHandler(PointerPressedEvent, OnItemPressed, RoutingStrategies.Tunnel);
        FavoriteItems.AddHandler(PointerMovedEvent, OnItemMoved, RoutingStrategies.Tunnel);
        FavoriteItems.AddHandler(PointerReleasedEvent, OnItemReleased, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.Items.CollectionChanged += (_, _) => UpdateCapacity(); // 개수가 바뀌면 들어갈 수 있는지 다시 본다(뷰모델은 앱 수명 동안 하나)
            }

            UpdateCapacity();
        };
    }

    public double MaxAllowedWidth { get => GetValue(MaxAllowedWidthProperty); set => SetValue(MaxAllowedWidthProperty, value); }

    private FavoriteBarViewModel? Vm => DataContext as FavoriteBarViewModel;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MaxAllowedWidthProperty)
        {
            UpdateCapacity();
        }
    }

    /// <summary>모두 들어가면 전부, 아니면 » 칸을 뺀 자리만큼 보인다.</summary>
    private void UpdateCapacity()
    {
        if (Vm is not { } vm)
        {
            return;
        }

        var max = MaxAllowedWidth;
        var count = vm.Items.Count;
        if (double.IsInfinity(max) || count * (ItemWidth + ItemSpacing) <= max)
        {
            vm.SetCapacity(int.MaxValue);
            return;
        }

        vm.SetCapacity((int)Math.Floor((max - ItemWidth) / (ItemWidth + ItemSpacing)));
    }

    // 넘침 팝업 항목 — 팝업은 영역 시각 트리 밖이라 명령 바인딩 대신 여기서 뷰모델로 넘긴다
    private void OnOverflowItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FavoriteItem item } && Vm is { } vm)
        {
            OverflowButton.Flyout?.Hide();
            vm.OpenCommand.Execute(item);
        }
    }

    // ReflectionBinding $parent[views:…] 는 실행 중 접두 붙은 형 이름을 못 풀어 웹에서 예외가 났다 — 넘침 항목과 같이 여기서 넘긴다
    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FavoriteItem item } && Vm is { } vm)
        {
            vm.OpenCommand.Execute(item);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FavoriteItem item } && Vm is { } vm)
        {
            vm.RemoveCommand.Execute(item);
        }
    }

    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragItem = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? ItemAt(e.Source) : null;
        _dragStart = e.GetPosition(this);
        _dragging = false;
    }

    private void OnItemMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null || _dragging)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y) >= DragThreshold)
        {
            _dragging = true;
        }
    }

    // 놓은 자리의 항목 앞으로 옮긴다. 끌기였으면 클릭(열기)으로 넘어가지 않게 처리한다
    private void OnItemReleased(object? sender, PointerReleasedEventArgs e)
    {
        var item = _dragItem;
        var dragging = _dragging;
        _dragItem = null;
        _dragging = false;
        if (!dragging || item is null || Vm is not { } vm)
        {
            return;
        }

        e.Handled = true;
        var target = FavoriteItems.InputHitTest(e.GetPosition(FavoriteItems)) is { } hit ? ItemAt(hit) : null;
        if (target is not null && !ReferenceEquals(target, item))
        {
            _ = vm.MoveAsync(item, vm.Items.IndexOf(target));
        }
    }

    private static FavoriteItem? ItemAt(object? source) =>
        source is Visual v && (v as Button ?? v.FindAncestorOfType<Button>()) is { DataContext: FavoriteItem item } ? item : null;
}

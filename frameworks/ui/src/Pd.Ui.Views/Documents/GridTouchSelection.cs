// 그리드 문서의 터치 입력 처리 — 터치 끌기·핀치는 스크롤·글자 크기만 하고, 행 선택은 손가락을 떼는 탭에서만 한다(D-W1)
using Pd.Poc.Shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Pd.Ui.Views.Documents;

/// <summary>
/// DataGrid 12.1.2 의 셀은 PointerPressed 에서 바로 행을 고른다(DataGridCell_PointerPressed → UpdateStateOnMouseLeftButtonDown).
/// 터치는 스크롤 제스처를 시작하려고 Handled 만 남기지 않을 뿐 선택은 그대로 하므로, 끌기·핀치를 시작한 손가락 아래 행이 선택됐다(i1505 Fold5 301→306).
/// 셀 위 터치 누름을 그리드 Tunnel 에서 처리됨으로 표시해 셀의 즉시 선택을 막고, 놓을 때 탭이면 그 행을 고른다.
/// 스크롤(ScrollGestureRecognizer)·핀치(PinchGestureRecognizer) 인식기는 InputElement 클래스 처리기(handledEventsToo)라 그대로 동작한다.
/// 놓기·이동은 TopLevel 에서 받는다 — 핀치 인식기가 두 포인터를 TopLevel 로 캡처하면 놓기가 그리드를 지나지 않는다. 마우스·펜은 건드리지 않는다.
/// 생성하면 곧바로 처리기를 건다(GridDocumentView 생성자에서 1회).
/// </summary>
internal sealed class GridTouchSelection
{
    private readonly Control _view;
    private readonly DataGrid _grid;

    // 탭 판정 중인 터치(셀 위에서 누른 첫 손가락). 다른 손가락이 닿거나 탭 범위를 넘게 움직이면 탭이 아니다
    private IPointer? _tapPointer;
    private Point _tapStart;
    private object? _tapItem;
    private bool _tapCanceled;
    private TopLevel? _tapTop;

    public GridTouchSelection(Control view, DataGrid grid)
    {
        _view = view;
        _grid = grid;
        grid.AddHandler(InputElement.PointerPressedEvent, OnTouchPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        view.AttachedToVisualTree += (_, _) =>
        {
            _tapTop = TopLevel.GetTopLevel(_view);
            _tapTop?.AddHandler(InputElement.PointerMovedEvent, OnTouchMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            _tapTop?.AddHandler(InputElement.PointerReleasedEvent, OnTouchReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        };
        view.DetachedFromVisualTree += (_, _) =>
        {
            _tapTop?.RemoveHandler(InputElement.PointerMovedEvent, OnTouchMoved);
            _tapTop?.RemoveHandler(InputElement.PointerReleasedEvent, OnTouchReleased);
            _tapTop = null;
            ResetTap();
        };
    }

    /// <summary>진단용: 탭으로 선택한 횟수·탭이 아니라서(끌기·핀치) 선택하지 않은 터치 수.</summary>
    internal int TouchTapSelects { get; private set; }

    internal int TouchNonTaps { get; private set; }

    private void OnTouchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch)
        {
            return;
        }

        if (!e.Pointer.IsPrimary)
        {
            _tapCanceled = true; // 두 번째 손가락(핀치) — 진행 중인 터치는 탭이 아니다
        }

        // 머리글(정렬)·스크롤바 등 셀 밖은 기존 동작 그대로
        var cell = (e.Source as Visual)?.FindAncestorOfType<DataGridCell>(includeSelf: true);
        if (cell is null)
        {
            return;
        }

        e.Handled = true; // 셀의 즉시 선택을 막는다
        if (e.Pointer.IsPrimary)
        {
            // 첫 손가락(다른 접점 없음) — 이전 터치가 놓기 없이 끝났어도(취소) 여기서 새로 시작한다
            _tapPointer = e.Pointer;
            _tapStart = e.GetPosition(_grid);
            _tapItem = cell.DataContext;
            _tapCanceled = false;
        }
    }

    private void OnTouchMoved(object? sender, PointerEventArgs e)
    {
        if (_tapPointer is not null && e.Pointer == _tapPointer && !_tapCanceled && Moved(e.GetPosition(_grid)))
        {
            _tapCanceled = true; // 끌기(스크롤) — 선택하지 않는다
        }
    }

    private void OnTouchReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_tapPointer is null || e.Pointer != _tapPointer)
        {
            return;
        }

        var tap = !_tapCanceled && !Moved(e.GetPosition(_grid)) && _tapItem is not null;
        var item = _tapItem;
        ResetTap();
        if (!tap)
        {
            TouchNonTaps++;
            return;
        }

        _grid.SelectedItem = item;
        TouchTapSelects++;
    }

    private void ResetTap()
    {
        _tapPointer = null;
        _tapItem = null;
        _tapCanceled = false;
    }

    /// <summary>탭 허용 반경(논리 px). 12.1.4 에서 TopLevel.PlatformSettings 가 공개되지 않아(i1700rc CS1061) 고정값을 쓴다 — 가로·세로 각 ±5px(10px 사각형).</summary>
    private const double TapSlop = 5;

    /// <summary>탭 허용 범위를 넘게 움직였는가.</summary>
    private bool Moved(Point p) => Math.Abs(p.X - _tapStart.X) > TapSlop || Math.Abs(p.Y - _tapStart.Y) > TapSlop;
}

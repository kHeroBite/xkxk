// 목록(ListBox)에 XAML 한 줄로 수식키+휠 조작을 붙이는 부착 속성과 행 두께·글자 배율 처리
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Pd.Ui.Input;

/// <summary>
/// 사용: input:DenseList.Enable="True". 목록은 머리글·열이 없어 Alt(가로 배율)는 할 일이 없고, Alt+Shift 는 항목 위아래 여백으로 두께를 바꾼다.
/// 여백을 쓰는 이유는 여러 줄 항목(알림)도 줄 수와 무관하게 같은 비율로 두꺼워지기 때문이다. 값은 저장하지 않는다(간트 Alt 줌과 같음).
/// </summary>
public sealed class DenseList : AvaloniaObject
{
    private DenseList()
    {
    }

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<DenseList, ListBox, bool>("Enable");

    /// <summary>문서 창 밖 독립 창의 목록에서 Ctrl+휠로 이 목록 글자만 키운다. 메인 창 패널은 크롬 고정 원칙에 따라 켜지 않는다.</summary>
    public static readonly AttachedProperty<bool> LocalFontProperty =
        AvaloniaProperty.RegisterAttached<DenseList, ListBox, bool>("LocalFont");

    static DenseList() => EnableProperty.Changed.AddClassHandler<ListBox>((list, e) =>
    {
        if (e.NewValue is true)
        {
            DenseWheel.Attach(list, new ListDenseTarget(list));
        }
    });

    public static bool GetEnable(ListBox list) => list.GetValue(EnableProperty);

    public static void SetEnable(ListBox list, bool value) => list.SetValue(EnableProperty, value);

    public static bool GetLocalFont(ListBox list) => list.GetValue(LocalFontProperty);

    public static void SetLocalFont(ListBox list, bool value) => list.SetValue(LocalFontProperty, value);
}

internal sealed class ListDenseTarget : IDenseViewTarget
{
    private const double FontRatio = 1.1;
    private const int FontMinLevel = -3;
    private const int FontMaxLevel = 8;

    private readonly ListBox _list;
    private Thickness? _basePadding; // 첫 조절 때 실현된 항목의 테마 값(로컬 값이 없을 때)을 읽는다
    private double? _baseFont;
    private int _fontLevel;

    public ListDenseTarget(ListBox list)
    {
        _list = list;
        list.ContainerPrepared += (_, e) => ApplyPadding(e.Container); // 재활용·새 항목도 같은 두께로
    }

    public string Kind => "list";

    public int RowLevel { get; private set; }

    public int ColumnLevel => 0;

    public void StepRows(int direction, Point at)
    {
        var next = DenseScale.StepRow(RowLevel, direction);
        var containers = _list.GetRealizedContainers().ToList();
        if (next == RowLevel || containers.Count == 0)
        {
            return;
        }

        _basePadding ??= (containers[0] as ListBoxItem)?.Padding;
        var anchor = ItemAt(at);
        var oldTop = anchor?.TranslatePoint(default, _list)?.Y;
        RowLevel = next;
        foreach (var c in containers) { ApplyPadding(c); }
        _list.UpdateLayout();
        if (anchor is not null && oldTop is { } before && anchor.TranslatePoint(default, _list)?.Y is { } after
            && _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } sv)
        {
            sv.Offset = new Vector(sv.Offset.X, sv.Offset.Y + (after - before)); // 커서 아래 항목을 제자리에
        }
    }

    public void StepColumns(int direction, Point at)
    {
    }

    public bool StepLocalFont(int direction)
    {
        if (!DenseList.GetLocalFont(_list))
        {
            return false;
        }

        _baseFont ??= _list.FontSize;
        _fontLevel = Math.Clamp(_fontLevel + Math.Sign(direction), FontMinLevel, FontMaxLevel);
        _list.FontSize = Math.Round(_baseFont.Value * Math.Pow(FontRatio, _fontLevel), 1);
        return true;
    }

    public int? RowIndexAt(Point at) => ItemAt(at) is { } item ? _list.IndexFromContainer(item) : null;

    public IReadOnlyDictionary<string, double> Snapshot()
    {
        var containers = _list.GetRealizedContainers().ToList();
        return new Dictionary<string, double>
        {
            ["realized"] = containers.Count,
            ["rowHeight"] = containers.Count > 0 ? containers[0].Bounds.Height : 0,
            ["fontSize"] = _list.FontSize,
        };
    }

    private Control? ItemAt(Point at) =>
        (_list.InputHitTest(at) as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();

    private void ApplyPadding(Control container)
    {
        if (container is not ListBoxItem item)
        {
            return;
        }

        if (RowLevel == 0 || _basePadding is not { } b)
        {
            item.ClearValue(TemplatedControl.PaddingProperty);
            return;
        }

        var f = DenseScale.Factor(RowLevel);
        item.Padding = new Thickness(b.Left, b.Top * f, b.Right, b.Bottom * f);
    }
}

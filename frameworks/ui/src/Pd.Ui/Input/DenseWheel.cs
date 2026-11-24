// 표·목록·간트 같은 조밀 화면의 수식키+휠 조작을 한곳에서 판정해 화면별 대상에 넘기는 공통 입력 부품
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Pd.Ui.Input;

/// <summary>휠 조작 분류. 수식키 없음·Shift(가로 스크롤)는 컨트롤의 기본 처리에 맡기므로 PassThrough 다.</summary>
public enum DenseWheelAction
{
    PassThrough,

    /// <summary>Ctrl(macOS Cmd) — 전체 크기. 문서 창이면 전역 입력 처리기가 먼저 받는다.</summary>
    PageScale,

    /// <summary>Alt — 가로 배율(간트 날짜 폭·표 열 폭).</summary>
    ColumnScale,

    /// <summary>Alt+Shift — 세로 배율(행 높이).</summary>
    RowScale,
}

/// <summary>화면별 배율 대상. 조작 판정은 DenseWheel 이 하고 대상은 값만 바꾼다.</summary>
public interface IDenseViewTarget
{
    /// <summary>진단 표시용 종류(gantt·grid·list).</summary>
    string Kind { get; }

    int RowLevel { get; }

    int ColumnLevel { get; }

    /// <summary>행 높이 한 단계. at 은 부착 컨트롤 좌표이며 그 아래 행을 기준으로 유지한다.</summary>
    void StepRows(int direction, Point at);

    /// <summary>가로 배율 한 단계. 가로 축이 없는 화면은 아무것도 하지 않는다.</summary>
    void StepColumns(int direction, Point at);

    /// <summary>문서 창 밖 Ctrl+휠 처리. 처리했으면 true, 아니면 컨트롤 기본 처리로 넘긴다.</summary>
    bool StepLocalFont(int direction);

    /// <summary>그 점 아래 행 번호(진단·판정용). 행이 없으면 null.</summary>
    int? RowIndexAt(Point at);

    /// <summary>판정용 실제 값(행 높이·열 폭·스크롤 위치 등).</summary>
    IReadOnlyDictionary<string, double> Snapshot();
}

/// <summary>배율 단계 공통 규칙. 곱셈을 누적하지 않고 단계 번호로 계산해 왕복하면 정확히 처음 값으로 돌아온다.</summary>
public static class DenseScale
{
    /// <summary>Alt+휠 간트 줌과 같은 비율.</summary>
    public const double Ratio = 1.25;

    /// <summary>행 높이 하한. 1.25 배 한 단계 아래(0.8 배)까지가 글자가 잘리지 않는 범위다(간트 행 1.36×글자 ≥ 글자 줄 높이 1.35×글자).</summary>
    public const int RowMinLevel = -1;

    /// <summary>행 높이 상한(약 3.8 배).</summary>
    public const int RowMaxLevel = 6;

    /// <summary>열 폭 하한(약 0.5 배). 머리글이 잘리지 않는 폭 아래로는 표가 따로 막는다.</summary>
    public const int ColumnMinLevel = -3;

    /// <summary>열 폭 상한(약 3 배).</summary>
    public const int ColumnMaxLevel = 5;

    public static double Factor(int level) => Math.Pow(Ratio, level);

    public static int StepRow(int level, int direction) => Math.Clamp(level + Math.Sign(direction), RowMinLevel, RowMaxLevel);

    public static int StepColumn(int level, int direction) => Math.Clamp(level + Math.Sign(direction), ColumnMinLevel, ColumnMaxLevel);
}

/// <summary>컨트롤에 터널 처리기를 걸어 수식키+휠을 대상으로 보낸다. 화면마다 휠 코드를 두지 않게 하는 단일 입구다.</summary>
public static class DenseWheel
{
    private static readonly AttachedProperty<IDenseViewTarget?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, IDenseViewTarget?>("DenseTarget");

    /// <summary>수식키 조합 → 조작. 다른 수식키가 섞이면 넘긴다. Shift 단독은 DataGrid·ScrollViewer 가 가로로 스크롤한다.</summary>
    public static DenseWheelAction Classify(KeyModifiers modifiers) => modifiers switch
    {
        KeyModifiers.Alt | KeyModifiers.Shift => DenseWheelAction.RowScale,
        KeyModifiers.Alt => DenseWheelAction.ColumnScale,
        KeyModifiers.Control or KeyModifiers.Meta => DenseWheelAction.PageScale,
        _ => DenseWheelAction.PassThrough,
    };

    /// <summary>휠 방향 +1/-1/0. Shift 를 누르면 가로 delta 로 오는 플랫폼이 있어 세로가 0 이면 가로 값을 쓴다.</summary>
    public static int Direction(Vector delta) => Math.Sign(delta.Y != 0 ? delta.Y : delta.X);

    /// <summary>host 에 대상을 연결한다. 전역 Ctrl+휠 처리(TopLevel 터널) 다음, 컨트롤 자체 처리(버블) 전에 받는다.</summary>
    public static void Attach(Control host, IDenseViewTarget target)
    {
        host.SetValue(TargetProperty, target);
        host.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    /// <summary>진단용 — 대상 자신, 조상, 자손 순으로 연결된 배율 대상을 찾는다.</summary>
    public static IDenseViewTarget? FindTarget(Control control) =>
        control.GetSelfAndVisualAncestors().OfType<Control>().Concat(control.GetVisualDescendants().OfType<Control>())
            .Select(c => c.GetValue(TargetProperty)).FirstOrDefault(t => t is not null);

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Control host || host.GetValue(TargetProperty) is not { } target)
        {
            return;
        }

        var direction = Direction(e.Delta);
        if (direction == 0)
        {
            return;
        }

        switch (Classify(e.KeyModifiers))
        {
            case DenseWheelAction.RowScale:
                target.StepRows(direction, e.GetPosition(host));
                e.Handled = true;
                break;
            case DenseWheelAction.ColumnScale:
                target.StepColumns(direction, e.GetPosition(host));
                e.Handled = true; // 가로 축이 없는 화면도 세로로 흐르지 않게 삼킨다
                break;
            case DenseWheelAction.PageScale:
                e.Handled = target.StepLocalFont(direction);
                break;
        }
    }
}

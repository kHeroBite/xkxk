// 셀마다 TextBlock 하나만 만드는 경량 읽기 전용 텍스트 열 — 템플릿·바인딩 없이 미리 계산한 문자열을 접근자로 넣는다(G1, 그리드 첫 레이아웃 비용 감소)
using System;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pd.Ui.Views.Documents;

/// <summary>
/// 읽기 전용 텍스트 열. DataGrid 12.1.2 의 확장점 DataGridBoundColumn(public abstract, 소스 태그 12.1.2 확인)을 파생한다.
/// 셀 내용은 TextBlock(Classes="cell") 하나이며 Text 는 <see cref="TextOf"/> 접근자로 직접 넣는다 — DataTemplate 빌드·컴파일 바인딩 식·
/// 리플렉션 바인딩이 모두 없어 웹 AOT·트리밍에서도 깨질 경로가 없다.
/// 행 재활용: DataGrid 는 재활용 행의 셀을 다시 만들지 않고 행 DataContext 만 바꾼다(DataGridRows.cs·DataGrid.AddNewCellPrivate 확인).
/// 그래서 셀 TextBlock 의 DataContextChanged(상속 값 변경)에서 글자를 다시 넣는다.
/// 정렬은 SortMemberPath(숫자·날짜 원값 속성), 글자 크기·색·말줄임은 지금처럼 셀 상속과 TextBlock.cell 스타일(PdDataGridStyles.axaml)이 맡는다.
/// </summary>
public class PdTextColumn : DataGridBoundColumn
{
    /// <summary>행 항목 → 표시 문자열. 화면(View)이 정한다 — 데이터·셸 레이어는 이 열을 모른다.</summary>
    public Func<object, string?>? TextOf { get; set; }

    /// <summary>셀 글자 전체를 툴팁으로도 보인다(긴 문구 열 — 작업명).</summary>
    public bool ShowToolTip { get; set; }

    /// <summary>템플릿 열(편집 템플릿 없음)과 같이 항상 읽기 전용이다.</summary>
    public override bool IsReadOnly
    {
        get => true;
        set => base.IsReadOnly = value;
    }

    // 내용을 미룬 셀 → 그 셀의 열(셀이 처음 화면에 들어와 PdCellTemplate 이 적용될 때 RealizeDeferred 가 꺼낸다)
    private static readonly ConditionalWeakTable<DataGridCell, PdTextColumn> s_deferred = new();

    // 그리는 글자 셀 → 그 셀의 열(PdCellChrome.AttachCell 이 꺼내 글자를 받는다)
    private static readonly ConditionalWeakTable<DataGridCell, PdTextColumn> s_drawn = new();

    /// <summary>그리는 글자 셀이면 글자를 주는 열, 아니면 null.</summary>
    internal static PdTextColumn? DrawnColumnOf(DataGridCell cell) => s_drawn.TryGetValue(cell, out var c) ? c : null;

    /// <summary>그리는 글자 셀의 글자 모양 값(표가 PdDataGridStyles 에서 읽은 값).</summary>
    internal PdCellTextLook? DrawnLook => (OwningGrid as PdDataGrid)?.CellTextLook;

    /// <summary>행 항목의 표시 문자열(그리는 글자 셀이 쓴다 — TextBlock 판의 Fill 과 같은 값).</summary>
    internal string? TextFor(object? item) => item is null || TextOf is null ? null : TextOf(item);

    /// <summary>그리는 글자 셀의 셀 쪽 값: 자동화 이름(TextBlock 자동화 피어가 내던 글자 대신)과 툴팁 열의 툴팁. 값이 같으면 다시 쓰지 않는다.</summary>
    internal void ApplyCellText(DataGridCell cell, string? text)
    {
        if (AutomationProperties.GetName(cell) != text) { AutomationProperties.SetName(cell, text); }
        if (ShowToolTip && !Equals(ToolTip.GetTip(cell), text)) { ToolTip.SetTip(cell, text); }
    }

#if PD_DIAGNOSTICS
    /// <summary>진단 대조 grid-open=notext: 셀 내용을 만들지 않는다(셀 글자 요소 비용 상한 측정 — 글자 없음, GridColumnDrafts).</summary>
    internal static bool DiagNoText;
#endif

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
#if PD_DIAGNOSTICS
        if (DiagNoText) { return null!; }
#endif
        // K15 그리는 셀 글자(옵션 GridDrawnCellText, 01 §5.4): 내용을 만들지 않고 셀 뿌리(PdCellChrome)가 TextFor 로 글자를 그린다
        if (OwningGrid is PdDataGrid { DrawnCellText: true })
        {
            s_drawn.AddOrUpdate(cell, this);
            return null!;
        }

        // A16 화면 밖 열 셀 지연 실현(01 §5.4, 옵션 GridDeferOffscreenCells): 새로 만드는 셀(아직 행에 넣기 전)만 미룬다.
        // DataGrid 는 화면 밖 셀을 숨김으로 두어 템플릿을 적용하지 않으므로, 템플릿 적용 = 처음 화면에 들어옴이다. 내용 null 은 DataGrid 가 검사한다(12.1.2 디컴파일 확인)
        if (cell.Parent is null && OwningGrid is PdDataGrid { DeferOffscreenCells: true })
        {
            s_deferred.AddOrUpdate(cell, this);
            return null!;
        }

        return CreateText(dataItem);
    }

    /// <summary>내용을 미룬 셀이면 지금 만든다(PdCellTemplate.Build 가 부른다 — 셀이 처음 화면에 들어온 때). 글자는 셀의 지금 행 항목으로 채운다.</summary>
    internal static void RealizeDeferred(DataGridCell cell)
    {
        if (cell.Content is null && s_deferred.TryGetValue(cell, out var column))
        {
            s_deferred.Remove(cell);
            cell.Content = column.CreateText(cell.DataContext);
        }
    }

    private TextBlock CreateText(object? dataItem)
    {
#if PD_DIAGNOSTICS
        var t = GridOpenBreakdown.Start(); // A16 첫 측정 분해 — 진단 빌드는 측정 시간을 재는 CellText(스타일 키 TextBlock)를 쓴다
        var text = new CellText();
#else
        var text = new TextBlock();
#endif
        text.Classes.Add("cell");
        Fill(text, dataItem);
        text.DataContextChanged += OnCellDataContextChanged; // 처리기는 열 인스턴스 메서드 하나 — 셀마다 클로저를 만들지 않는다
#if PD_DIAGNOSTICS
        GridOpenBreakdown.Add(GridOpenBreakdown.Part.Content, t);
#endif
        return text;
    }

    /// <summary>읽기 전용이라 호출되지 않는다(DataGridBoundColumn 의 추상 멤버 구현). 그래도 부르면 표시용 TextBlock 을 돌려준다.</summary>
    protected override Control GenerateEditingElementDirect(DataGridCell cell, object dataItem) => CreateText(dataItem);

    protected override object? PrepareCellForEdit(Control editingElement, RoutedEventArgs editingEventArgs) => null;

    private void OnCellDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock text) { Fill(text, text.DataContext); }
    }

    private void Fill(TextBlock text, object? item)
    {
        var value = TextFor(item);
        text.Text = value;
        if (ShowToolTip) { ToolTip.SetTip(text, value); }
    }
}

/// <summary>GridRow 의 표시 문자열 필드(XAML 에서 열마다 고른다). 이름은 GridRow 속성 이름과 같다.</summary>
public enum GridRowField
{
    NoText,
    Code,
    Name,
    Owner,
    Status,
    StartText,
    EndText,
    ProgressText,
    HoursText,
    Note,
}

/// <summary>그리드 문서(GridDocumentView) 전용 열 — <see cref="Field"/> 로 GridRow 의 미리 계산한 문자열 속성을 고른다(정적 접근자, 리플렉션 없음).</summary>
public sealed class GridRowTextColumn : PdTextColumn
{
    private GridRowField _field;

    public GridRowTextColumn() => Field = GridRowField.NoText;

    public GridRowField Field
    {
        get => _field;
        set
        {
            _field = value;
            TextOf = value switch
            {
                GridRowField.NoText => static o => (o as GridRow)?.NoText,
                GridRowField.Code => static o => (o as GridRow)?.Code,
                GridRowField.Name => static o => (o as GridRow)?.Name,
                GridRowField.Owner => static o => (o as GridRow)?.Owner,
                GridRowField.Status => static o => (o as GridRow)?.Status,
                GridRowField.StartText => static o => (o as GridRow)?.StartText,
                GridRowField.EndText => static o => (o as GridRow)?.EndText,
                GridRowField.ProgressText => static o => (o as GridRow)?.ProgressText,
                GridRowField.HoursText => static o => (o as GridRow)?.HoursText,
                GridRowField.Note => static o => (o as GridRow)?.Note,
                _ => null,
            };
        }
    }
}

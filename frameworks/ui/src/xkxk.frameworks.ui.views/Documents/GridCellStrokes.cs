// 그리드 셀 템플릿의 숨은 테두리(포커스·현재 셀·오류) Stroke 를 테마 토큰 브러시 인스턴스로 고정해 테마 전환 때 셀 재측정을 없애는 처리(Q4)와 헤더 아래선 색 연결(Q11 A1)
using xkxk.frameworks.ui.shell;
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>GridDocumentView 생성자가 쓰는 셀·헤더 선 스타일 적용. 상태는 진단 스위치 UseStableCellStrokes 하나뿐이다. 시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GridCellStrokes
{
    /// <summary>
    /// 켜면 셀 템플릿의 CurrencyVisual·InvalidVisualElement·FocusVisual 안 Rectangle 의 Stroke 를 PdBrushSelectionLine 인스턴스로 고정한다.
    /// 근거: Fluent DataGridCell 템플릿은 이들 Stroke 를 Fluent 팔레트 DynamicResource 로 받아, 테마를 바꿀 때마다 숨은 Rectangle 의 Stroke 가 바뀌고
    /// 셀마다 측정이 무효화됐다(census who_measure — Rectangle#-@DataGridCell 212, #InvalidVisualElement 106).
    /// ThemeApplier 는 토큰 브러시 인스턴스를 유지하고 Color 만 바꾸므로 고정해도 보이는 색은 테마를 따라간다.
    /// 지금은 진단 스위치(--grid-open staticstroke)로만 켠다. 검증 뒤 제품 기본으로 바꾸려면 이 값의 기본을 true 로 하면 된다.
    /// </summary>
    internal static bool UseStableCellStrokes { get; set; }

    /// <summary>
    /// 헤더 아래선(PART_ColumnHeadersAndRowsSeparator) 색을 PdBrushGridHeaderLine 토큰으로 지정한다(Q11 A1, Mars #646464).
    /// Fluent 템플릿이 이 사각형의 Fill 을 템플릿 값으로 주기 때문에 스타일로는 덮을 수 없어, 템플릿 적용 때 로컬 바인딩(템플릿보다 우선)으로 건다.
    /// 토큰을 못 찾으면(진단 --theme-new-tokens off 등) 템플릿 기본색이 그대로 남는다.
    /// </summary>
    internal static void HookHeaderSeparator(Control view, DataGrid grid) =>
        grid.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Rectangle>("PART_ColumnHeadersAndRowsSeparator") is { } sep)
            {
                sep.Bind(Shape.FillProperty, view.GetResourceObservable("PdBrushGridHeaderLine"));
            }
        };

    /// <summary>시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보). 뷰 생성자에서 1회. UseStableCellStrokes 가 꺼져 있거나 토큰을 못 찾으면 아무것도 하지 않는다. 적용했으면 true.</summary>
    public static bool ApplyStableCellStrokes(Control view)
    {
        // 생성자 시점에는 아직 시각 트리에 붙지 않아 view 에서 앱 리소스까지 올라가지 못한다(i0944 실측 grid_stable_strokes=0) → 앱 리소스를 직접 본다.
        // ThemeApplier 가 토큰 브러시를 app.Resources 에 넣으므로 같은 인스턴스를 얻는다.
        object? res = null;
        if (!UseStableCellStrokes
            || !(view.TryFindResource("PdBrushSelectionLine", out res) || (Avalonia.Application.Current?.TryFindResource("PdBrushSelectionLine", out res) ?? false))
            || res is not IBrush line)
        {
            return false;
        }

        Style Stroke(Func<Selector?, Selector> selector) => new(selector) { Setters = { new Setter(Shape.StrokeProperty, line) } };
        view.Styles.Add(Stroke(x => x.OfType<DataGridCell>().Template().Name("CurrencyVisual")));
        view.Styles.Add(Stroke(x => x.OfType<DataGridCell>().Template().Name("InvalidVisualElement")));
        view.Styles.Add(Stroke(x => x.OfType<DataGridCell>().Template().Name("FocusVisual").Child().OfType<Rectangle>()));
        return true;
    }
}

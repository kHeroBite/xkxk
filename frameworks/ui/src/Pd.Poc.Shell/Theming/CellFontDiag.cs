// 진단 전용 셀 글꼴 셀렉터 스위치(Q4 R1_sel) — App.axaml 의 공용 글꼴 셀렉터에서 DataGridCell 을 빼 글꼴 전환 비용 몫을 잰다(제품 빌드에는 없음)
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Pd.Poc.Shell.Diagnostics;

namespace Pd.Poc.Shell.Theming;

/// <summary>
/// 인자 --cell-font family|both|default (또는 --cell-font=값). 소비하고 셸 인자에서 뺀다.
/// family: `:is(TemplatedControl)` FontFamily 스타일을 `:is(TemplatedControl):not(DataGridCell)` 로 바꾼다(셀은 DataGrid 에서 상속).
/// both: 위 + FontSize 목록에서 DataGridCell 을 뺀다 — Fluent 셀 테마가 FontSize=15 를 직접 넣어 셀 글자는 15 로 고정된다(측정 전용, 표시 정확성 아님).
/// 적용 결과는 perf text cell_font=default|family|both 로 남긴다. Android 는 인자가 초기화 뒤에 들어오므로 여러 번 불려도 한 번만 바꾼다.
/// </summary>
internal static class CellFontDiag
{
    public const string ArgName = "--cell-font";
    private static bool s_applied;

    public static IReadOnlyList<string> Take(IReadOnlyList<string> args)
    {
        string? mode = null;
        var rest = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith(ArgName + "=", StringComparison.OrdinalIgnoreCase))
            {
                mode = a[(ArgName.Length + 1)..];
            }
            else if (a.Equals(ArgName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                mode = args[++i];
            }
            else
            {
                rest.Add(a);
            }
        }

        if (mode is not null)
        {
            Apply(mode.Trim().ToLowerInvariant());
        }

        return rest;
    }

    private static void Apply(string mode)
    {
        if (s_applied || Application.Current is not { } app)
        {
            return;
        }

        s_applied = true;
        if (mode is not ("family" or "both"))
        {
            PerfProbe.Current.SetText("cell_font", "default");
            return;
        }

        var styles = app.Styles;
        var replaced = 0;
        for (var i = 0; i < styles.Count; i++)
        {
            if (styles[i] is not Style s || s.Selector?.ToString() is not { } text)
            {
                continue;
            }

            if (text.Contains("TemplatedControl", StringComparison.Ordinal) && !text.Contains("DataGridCell", StringComparison.Ordinal))
            {
                styles[i] = Clone(s, x => x.Is<TemplatedControl>().Not(n => n.OfType<DataGridCell>()));
                replaced++;
            }
            else if (mode == "both" && text.Contains("DataGridCell", StringComparison.Ordinal))
            {
                // App.axaml 의 FontSize 목록(MenuItem, TabItem, ListBoxItem, ComboBoxItem, Button, TextBox, ComboBox, DataGridCell, DataGridColumnHeader)에서 셀만 뺀다
                styles[i] = Clone(s, x => Selectors.Or(x.OfType<MenuItem>(), x.OfType<TabItem>(), x.OfType<ListBoxItem>(), x.OfType<ComboBoxItem>(),
                    x.OfType<Button>(), x.OfType<TextBox>(), x.OfType<ComboBox>(), x.OfType<DataGridColumnHeader>()));
                replaced++;
            }
        }

        PerfProbe.Current.SetText("cell_font", mode + (replaced == (mode == "both" ? 2 : 1) ? string.Empty : ":replaced=" + replaced));
    }

    private static Style Clone(Style source, Func<Selector?, Selector> selector)
    {
        var copy = new Style(selector);
        foreach (var setter in source.Setters)
        {
            copy.Setters.Add(setter is Setter s ? new Setter(s.Property!, s.Value) : setter);
        }

        return copy;
    }
}
#endif

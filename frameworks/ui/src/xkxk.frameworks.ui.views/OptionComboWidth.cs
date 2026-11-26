// 선택형 콤보 최소 폭 맞춤(Q17) — 현재 언어 선택지 중 가장 긴 문구가 잘리지 않는 폭을 실제 글꼴로 재서 MinWidth 로 둔다(기본 폭보다 좁아지지 않음)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 폭 = max(기본 폭, 가장 긴 선택지 문구 폭 + 콤보 장식 폭).
/// · 문구 폭: 콤보의 현재 글꼴·크기로 TextLayout 측정(추정 아님). 대상은 현재 언어의 모든 선택지라 선택을 바꿔도 폭이 출렁이지 않는다.
/// · 장식 폭(안쪽 여백·펼침 화살표·테두리): 배치된 콤보에서 '콤보 폭 − 내용 표시 영역 폭 + 내용 영역 안쪽 여백'으로 잰다(테마 값 하드코딩 없음).
/// 기본 폭은 한국어 기준 기존 폭이라 한국어 배치는 그대로이고, 영어처럼 긴 언어에서만 넓어진다. 언어·글꼴이 바뀌면 다시 잰다.
/// </summary>
public static class OptionComboWidth
{
    /// <summary>keys = 선택지 사전 키(표시 순서와 무관).</summary>
    public static void Attach(ComboBox combo, double baseWidth, IReadOnlyList<string> keys)
    {
        void Fit()
        {
            if (Measure(combo, keys) is { } needed)
            {
                combo.MinWidth = Math.Max(baseWidth, Math.Ceiling(needed));
            }
        }

        void FitLater() => Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded); // 배치가 끝난 뒤 장식 폭을 잰다

        combo.MinWidth = baseWidth;
        combo.AttachedToVisualTree += (_, _) => FitLater();
        XkxkUi.Current.Localization.LanguageChanged += FitLater;
        combo.PropertyChanged += (_, e) =>
        {
            if (e.Property == TemplatedControl.FontFamilyProperty || e.Property == TemplatedControl.FontSizeProperty)
            {
                FitLater();
            }
        };
    }

    /// <summary>필요 폭(px). 콤보가 아직 배치 전이라 장식 폭을 잴 수 없으면 null.</summary>
    private static double? Measure(ComboBox combo, IReadOnlyList<string> keys)
    {
        var presenter = combo.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(p => p.Name == "ContentPresenter");
        if (presenter is null || combo.Bounds.Width <= 0)
        {
            return null;
        }

        var chrome = combo.Bounds.Width - presenter.Bounds.Width + presenter.Padding.Left + presenter.Padding.Right;
        var typeface = new Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight);
        var widest = 0.0;
        foreach (var key in keys)
        {
            using var layout = new TextLayout(Loc.Tr(key), typeface, combo.FontSize, null);
            widest = Math.Max(widest, layout.WidthIncludingTrailingWhitespace);
        }

        return widest + chrome;
    }
}

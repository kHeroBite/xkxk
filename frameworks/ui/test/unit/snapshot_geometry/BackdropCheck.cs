// 덮개 바탕 단위 시험 — 반투명(glass) 표면 아래 탭 본문을 찍어 조상 바탕을 깐 그림이 '닫기 전 화면'(같은 트리 전체를 찍은 그 자리)과 같고 불투명한지 본다(21 §4.1)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.views.Depth;

namespace Pd.Test.SnapshotScale;

/// <summary>
/// 사례마다 루트(작업 영역)·탭 본문(view)을 만들고 세 가지를 잰다.
///  · new: SnapshotBackdrop.Collect + Compose 그림 ↔ 루트 전체를 찍은 그림의 view 자리. 최소 알파 255, 평균 채널 차 ≤ MeanTol, 최대 채널 차 ≤ MaxTol.
///  · old(음성 대조): 옛 규칙(처음 만나는 투명하지 않은 조상 한 겹을 view 사각형에 칠함). glass 사례에서 화면과 어긋나고(최소 알파 &lt; 255 또는 평균 차 &gt; MeanTol)
///    새 그림과도 달라야 한다(평균 차 &gt; MeanTol 또는 최소 알파가 다름). 탭 본문이 트리에 붙지 않았으면 new·old 모두 FAIL 로 센다.
///  · layers: 기대 층 수(중복 브러시 1회·불투명에서 멈춤).
/// 위치·크기는 8 의 배수 — 시험 배율(1·1.5·2.625)에서 화소 경계가 맞아 재표본 차가 생기지 않는다.
/// </summary>
internal static class BackdropCheck
{
    public static readonly double[] Scales = { 1, 1.5, 2.625 };
    private const double MeanTol = 1.0;
    private const int MaxTol = 3;

    private static readonly Color MdiBg = Color.FromRgb(0x77, 0x88, 0x99);      // ChromeResources PdBrushMdiBackground 와 같은 값(시험 고정값)
    private static readonly Color Fallback = Color.FromRgb(0xF0, 0xF0, 0xF0);   // PdBrushBackground 역할(시험 고정값)

    // ScreenOnFallback: 루트 밖이 투명이라 화면 쪽도 테마 바탕 위에 놓고 비교.
    // Hosted: 템플릿 창 대리가 있는 트리는 헤드리스 Window 에 붙여 보인다 — ContentPresenter 는 논리 트리에 붙어야 내용을 자식으로 만든다(10차 r1·r2: 창 없이 attached=False).
    //         이 사례들은 루트가 불투명이라 바탕 순회가 루트에서 멈추고 Window 자체 바탕까지 가지 않는다.
    private sealed record Case(string Name, Func<(Control Root, Control View)> Build, int Layers, bool OldMustFail, bool ScreenOnFallback = false, bool Hosted = false);

    private static IBrush Glass(double alpha) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb((byte)(255 * alpha), 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb((byte)(255 * alpha), 0xC0, 0xD0, 0xE0), 1) },
    };

    /// <summary>탭 본문: 바탕 없는 패널에 글자 대신 빨강·파랑 표식(내용 대리).</summary>
    private static Panel Body() => new()
    {
        Width = 160, Height = 96,
        Children =
        {
            new Border { Background = Brushes.Red, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) },
            new Border { Background = Brushes.Blue, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8) },
        },
    };

    /// <summary>MDI 창 템플릿 대리 — 주인 Background 를 칠하지 않는다(실제 MdiDocumentWindowTheme 는 PART_ContentBorder 만 TemplateBinding Background).</summary>
    /// 표시기 이름은 PART_ContentPresenter 여야 ContentControl 이 내용을 그 표시기에 붙인다(이름이 다르면 내용이 시각 트리에 들어가지 않는다 — 10차 1회 실행에서 화면 크기 0 으로 확인).
    private static readonly IControlTemplate BareTemplate = new FuncControlTemplate<ContentControl>((cc, scope) =>
        new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = cc[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));

    /// <summary>MDI 작업 영역(불투명) &gt; 무늬 층(반투명 타일 아님 — 단색 반투명) &gt; 창(TemplatedControl 대리: 같은 브러시를 안쪽 Border 에 넘김) &gt; glass 본문 Border &gt; 탭 본문.</summary>
    private static (Control, Control) GlassGroup(double alpha, bool decor)
    {
        var glass = Glass(alpha);
        var view = Body();
        var content = new Border { Background = glass, Child = view, Padding = new Thickness(0, 24, 0, 0) }; // 위 24 = 탭 줄 자리 — 그라데이션이 view 가 아니라 주인 높이에 맞는지 본다
        var window = new ContentControl { Background = glass, Content = content, Template = BareTemplate, Margin = new Thickness(40, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        window.ApplyTemplate(); // 템플릿을 먼저 세운다(로컬 Template 이라 창에 붙은 뒤 테마 스타일이 덮지 않는다). 내용은 창에 붙어 논리 트리에 들어간 뒤 표시기가 붙인다(Run 의 Hosted)
        Control area = window;
        if (decor)
        {
            area = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x00, 0xFF)), Children = { window } };
        }

        var root = new Border { Background = new SolidColorBrush(MdiBg), Width = 256, Height = 176, Child = area, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        return (root, view);
    }

    private static readonly Case[] Cases =
    {
        new("glass_group", () => GlassGroup(0.55, decor: false), 2, true, Hosted: true),            // MdiBg + glass(창 Background 는 중복이라 1회)
        new("glass_group_decor", () => GlassGroup(0.55, decor: true), 3, true, Hosted: true),       // MdiBg + 무늬 + glass
        new("opaque_surface", () => GlassGroup(1.0, decor: false), 1, false, Hosted: true),          // dark·flat 처럼 불투명 그라데이션 — 한 겹에서 멈춤(옛 규칙과 같은 결과)
        new("no_opaque_ancestor", () =>                                               // 불투명 조상 없음 → 테마 바탕(맨 아래)
        {
            var view = Body();
            var root = new Border { Background = Glass(0.55), Width = 160, Height = 96, Child = view };
            return (root, view);
        }, 2, true, ScreenOnFallback: true),
        new("opaque_view", () =>                                                      // view 자기 바탕이 불투명 → 층 없음
        {
            var view = Body();
            view.Background = Brushes.White;
            var root = new Border { Background = Glass(0.55), Width = 160, Height = 96, Child = view };
            return (root, view);
        }, 0, false),
        new("group_window", () =>                                                     // MDI 안 탭 묶음 창 전체를 찍는 닫기(21 §4.1) — 둥근 glass 창 밖 모서리가 MDI 바탕·무늬로 채워져야 한다
        {
            var view = new Border { Background = Glass(0.55), CornerRadius = new CornerRadius(8), Child = Body(), Margin = new Thickness(40, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var decor = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x00, 0xFF)), Children = { view } };
            var root = new Border { Background = new SolidColorBrush(MdiBg), Width = 256, Height = 176, Child = decor };
            return (root, view);
        }, 2, true),                                                                  // MdiBg + 무늬(창 자기 glass 바탕은 내용 그림에 들어감)
    };

    public static List<Dictionary<string, object>> RunAll(out int newFail, out int controlMiss)
    {
        var rows = new List<Dictionary<string, object>>();
        newFail = 0;
        controlMiss = 0;
        foreach (var c in Cases)
        {
            foreach (var scale in Scales)
            {
                var row = Run(c, scale);
                rows.Add(row);
                newFail += (bool)row["new_ok"] ? 0 : 1;
                controlMiss += (bool)row["control_ok"] ? 0 : 1;
            }
        }

        return rows;
    }

    private static Dictionary<string, object> Run(Case c, double scale)
    {
        var (root, view) = c.Build();
        Trees.NoLayoutRounding(root);
        var size = new Size(root.Width, root.Height);
        Window? host = null;
        if (c.Hosted)
        {
            host = new Window { Width = size.Width, Height = size.Height, Content = root };
            host.Show();
            Dispatcher.UIThread.RunJobs();
        }
        else
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
        }

        try
        {
            return Compare(c, scale, root, view, size);
        }
        finally
        {
            host?.Close();
        }
    }

    /// <summary>배치가 끝난 트리에서 새 그림·옛 그림을 화면 그림과 비교한다.</summary>
    private static Dictionary<string, object> Compare(Case c, double scale, Control root, Control view, Size size)
    {
        // 트리 구성 확인: 탭 본문이 루트 아래 시각 트리에 붙어 크기를 받았는가(아니면 비교가 무의미하므로 FAIL 로 센다)
        var attached = view.GetVisualAncestors().Contains(root) && view.Bounds.Width > 0 && view.Bounds.Height > 0;
        var g = new SnapshotGeometry(view.Bounds.Size, scale);
        var at = view.TranslatePoint(default, root) ?? default;
        var whole = new SnapshotGeometry(size, scale);
        using var screen = c.ScreenOnFallback
            ? SnapshotRenderer.Compose(SnapshotRenderer.Capture(root, whole, out _), new SolidColorBrush(Fallback), whole)
            : SnapshotRenderer.Capture(root, whole, out _);
        var want = Crop(screen, new PixelRect((int)Math.Round(at.X * scale), (int)Math.Round(at.Y * scale), g.Pixels.Width, g.Pixels.Height));

        var layers = SnapshotBackdrop.Collect(view, new SolidColorBrush(Fallback), "fallback");
        using var composed = SnapshotRenderer.Compose(SnapshotRenderer.Capture(view, g, out _), layers, g);
        var (newMean, newMax, newAlpha) = Diff(Read(composed), want);

        // 옛 규칙: 처음 만나는 투명하지 않은 조상 한 겹(없으면 fallback)을 view 사각형에 칠한다
        IBrush? oldBrush = SnapshotBackdrop.IsOpaque(SnapshotBackdrop.BackgroundOf(view)) ? null
            : view.GetVisualAncestors().OfType<Control>().Select(SnapshotBackdrop.BackgroundOf).FirstOrDefault(b => b is not null && !SnapshotBackdrop.IsClear(b)) ?? new SolidColorBrush(Fallback);
        var oldShot = SnapshotRenderer.Capture(view, g, out _);
        using var old = oldBrush is null ? oldShot : SnapshotRenderer.Compose(oldShot, oldBrush, g);
        var (oldMean, _, oldAlpha) = Diff(Read(old), want);

        // 음성 대조는 '옛 그림이 화면과 어긋남'과 '옛 그림이 새 그림과 다름'을 함께 요구한다.
        // 둘이 같은 그림이면(10차 1회 실행: 트리가 안 붙어 새·옛 모두 빈 그림) 대조가 옛 규칙의 결함을 보여 주지 못한 것이다.
        var (oldVsNewMean, _, _) = Diff(Read(old), Read(composed));
        var newOk = attached && layers.Count == c.Layers && newAlpha == 255 && newMean <= MeanTol && newMax <= MaxTol;
        var oldDeviates = attached && (oldAlpha < 255 || oldMean > MeanTol) && (oldVsNewMean > MeanTol || oldAlpha != newAlpha);
        return new Dictionary<string, object>
        {
            ["case"] = c.Name, ["scale"] = scale, ["attached"] = attached, ["layers"] = layers.Count, ["layers_want"] = c.Layers,
            ["sources"] = string.Join("+", layers.Select(l => l.Source)),
            ["new_mean"] = Math.Round(newMean, 3), ["new_max"] = newMax, ["new_min_alpha"] = newAlpha,
            ["old_mean"] = Math.Round(oldMean, 3), ["old_min_alpha"] = oldAlpha, ["old_vs_new_mean"] = Math.Round(oldVsNewMean, 3),
            ["new_ok"] = newOk, ["control_ok"] = !c.OldMustFail || oldDeviates,
        };
    }

    private static (byte[] Data, PixelSize Size) Read(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var data = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), data.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        return (data, size);
    }

    private static (byte[] Data, PixelSize Size) Crop(Bitmap bitmap, PixelRect rect)
    {
        var (all, size) = Read(bitmap);
        var data = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++)
        {
            Array.Copy(all, ((rect.Y + y) * size.Width + rect.X) * 4, data, y * rect.Width * 4, rect.Width * 4);
        }

        return (data, new PixelSize(rect.Width, rect.Height));
    }

    /// <summary>평균·최대 채널 차(RGB, 사전곱 그대로)와 최소 알파. 화소 크기가 다르면 무한대.</summary>
    private static (double Mean, int Max, int MinAlpha) Diff((byte[] Data, PixelSize Size) a, (byte[] Data, PixelSize Size) b)
    {
        if (a.Size != b.Size)
        {
            return (double.PositiveInfinity, int.MaxValue, 0);
        }

        long sum = 0;
        int max = 0, minAlpha = 255;
        for (var i = 0; i < a.Data.Length; i += 4)
        {
            for (var k = 0; k < 3; k++)
            {
                var d = Math.Abs(a.Data[i + k] - b.Data[i + k]);
                sum += d;
                max = Math.Max(max, d);
            }

            minAlpha = Math.Min(minAlpha, a.Data[i + 3]);
        }

        return (sum / (a.Data.Length / 4 * 3.0), max, minAlpha);
    }
}

// 덮개 그림의 글자 재현 시험 — 한글(임베드 Noto Sans KR·대체 글꼴)·굵게·입력칸 안내 문구를 화면(합성기) 그림과 옛·새 찍기로 비교한다
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using xkxk.frameworks.ui.views.Depth;

namespace Pd.Test.SnapshotScale;

internal static class TextCheck
{
    public static readonly double[] Scales = { 1, 1.5, 2.25, 2.625 };

    // 화면 그림과 '다른 화소'(밝기 차 > 48)의 비율 한계. 같은 글리프를 같은 변환으로 그리면 차이는 경계 보간뿐이라 0 에 가깝다.
    // 글자가 네모로 깨지거나 굵어지면 글자 넓이 몫(수 %)만큼 늘어난다. 한계는 그 사이 0.5% 로 둔다(판정은 옛 경로와의 대조로도 본다).
    private const double MismatchLimit = 0.005;
    private const int LumaDelta = 48;

    public static Dictionary<string, object> Run(double scale)
    {
        var root = Build();
        var window = new Window { Width = 360, Height = 240, Content = root, Background = Brushes.White };
        window.Show();
        window.SetRenderScaling(scale); // 보인 뒤에 배율을 바꾸고 다시 배치·그리기를 돌린다
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame(); // 합성기(실제 화면과 같은 경로) 그림 — 기준
        var size = root.Bounds.Size;
        var g = new xkxk.frameworks.ui.views.Depth.SnapshotGeometry(size, scale);
        var px = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
        using var old = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
        old.Render(root);
        using var shot = SnapshotRenderer.Capture(root, g, out var mode);
        var at = root.TranslatePoint(default, window) ?? default;
        var origin = new PixelPoint((int)Math.Round(at.X * scale), (int)Math.Round(at.Y * scale));
        var oldRatio = frame is null ? double.NaN : Mismatch(frame, origin, old);
        var newRatio = frame is null ? double.NaN : Mismatch(frame, origin, shot);
        window.Close();
        return new Dictionary<string, object>
        {
            ["scale"] = scale, ["mode"] = mode,
            ["old_mismatch"] = Finite(oldRatio), ["new_mismatch"] = Finite(newRatio),
            ["old_ok"] = oldRatio <= MismatchLimit, ["new_ok"] = newRatio <= MismatchLimit,
        };
    }

    private static object Finite(double v) => double.IsFinite(v) ? Math.Round(v, 5) : "측정 실패(화면 그림 없음)";

    private static Control Build()
    {
        var family = new FontFamily(Program.KoreanFont);
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 6, Background = Brushes.White };
        panel.Children.Add(new TextBlock { Text = "작업 입력", FontSize = 18, FontWeight = FontWeight.Bold, FontFamily = family });
        panel.Children.Add(new TextBlock { Text = "한글 조합 입력 확인용 — 저장은 메모리에만 합니다.", FontSize = 12, Foreground = Brushes.DimGray, FontFamily = family });
        panel.Children.Add(new TextBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, PlaceholderText = "예: 로그인 화면 설계", FontFamily = family });
        panel.Children.Add(new TextBox { Width = 260, Height = 60, HorizontalAlignment = HorizontalAlignment.Left, AcceptsReturn = true, PlaceholderText = "여러 줄 입력(한글 조합 확인)", FontFamily = family });
        panel.Children.Add(new TextBlock { Text = "작업명 담당자 Ab 123", FontSize = 14, FontFamily = family });
        return new Border { Background = Brushes.White, Child = panel };
    }

    /// <summary>찍은 그림을 화면 그림의 같은 자리와 비교해 다른 화소 비율.</summary>
    private static double Mismatch(Bitmap frame, PixelPoint origin, Bitmap shot)
    {
        var a = Luma(frame, out var fw, out var fh);
        var b = Luma(shot, out var sw, out var sh);
        long diff = 0, n = 0;
        for (var y = 0; y < sh && y + origin.Y < fh; y++)
        {
            for (var x = 0; x < sw && x + origin.X < fw; x++)
            {
                n++;
                if (Math.Abs(a[((y + origin.Y) * fw) + x + origin.X] - b[(y * sw) + x]) > LumaDelta)
                {
                    diff++;
                }
            }
        }

        return n == 0 ? double.NaN : (double)diff / n;
    }

    private static byte[] Luma(Bitmap bitmap, out int w, out int h)
    {
        var size = bitmap.PixelSize;
        w = size.Width;
        h = size.Height;
        var stride = w * 4;
        var data = new byte[stride * h];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), data.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var rgba = bitmap.Format == PixelFormats.Rgba8888;
        var luma = new byte[w * h];
        for (var i = 0; i < luma.Length; i++)
        {
            int r = data[(i * 4) + (rgba ? 0 : 2)], g = data[(i * 4) + 1], bl = data[(i * 4) + (rgba ? 2 : 0)], al = data[(i * 4) + 3];
            var lum = ((r * 299) + (g * 587) + (bl * 114)) / 1000; // 미리 곱한 알파 — 투명은 흰 바탕 위로 본다
            luma[i] = (byte)Math.Min(255, lum + (255 - al));
        }

        return luma;
    }
}

// 사례 1건 계산 — 찍기(옛 dpi·새 제품)·합성·표시 단계마다 표식 무게중심(px)을 기대 위치와 비교한다
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using xkxk.frameworks.ui.views.Depth;

namespace Pd.Test.SnapshotScale;

internal sealed record Result(string Tree, double Scale, Size Size, Dictionary<string, double> Errors, bool NewOk, bool ControlAsExpected, double OldFactor)
{
    public const string Missing = "측정 실패(표식 없음)"; // 무한대·NaN 은 JSON 에 못 쓰고, 숨기지 않고 이 문자열로 남긴다(집계에서는 이미 FAIL 쪽으로 셈)

    /// <summary>표식을 못 찾은 단계 이름(오차가 무한대·NaN).</summary>
    public IEnumerable<string> MissingSteps()
    {
        foreach (var (step, v) in Errors)
        {
            if (!double.IsFinite(v))
            {
                yield return step;
            }
        }

        if (!double.IsFinite(OldFactor))
        {
            yield return "old_capture_factor";
        }
    }

    public Dictionary<string, object> ToRow()
    {
        var err = new Dictionary<string, object>();
        foreach (var (step, v) in Errors)
        {
            err[step] = double.IsFinite(v) ? v : Missing;
        }

        return new()
        {
            ["tree"] = Tree, ["scale"] = Scale, ["w"] = Size.Width, ["h"] = Size.Height,
            ["new_ok"] = NewOk, ["control_as_expected"] = ControlAsExpected, ["err_px"] = err,
            ["missing"] = new List<string>(MissingSteps()),
            // 옛 찍기 배수 추정: 빨강 표식(원점 근처)까지 거리의 비. 1 = 정상, 배율 = 원인 B
            ["old_capture_factor"] = double.IsFinite(OldFactor) ? OldFactor : Missing,
        };
    }
}

internal static class Checks
{
    // 표식 무게중심 허용 오차(px), 결과를 보고 고르지 않고 아래에서 유도한다.
    // ① 찍기: 표식 변 L·R(px, 소수)에서 문턱(채널 > 200 ≈ 덮임 78% 이상)을 넘는 화소만 센다. 양 끝 화소가 들거나 빠지는 오차는 각각 [0,1) 이라
    //    무게중심 오차 = (끝 오차 합)/2 < 0.5px.
    // ② 표시·합성: 찍힌 그림을 소수 자리(Offset·소수 크기)에 쌍선형으로 다시 놓으면 양 끝 덮임이 다시 바뀌어 같은 이유로 < 0.5px 이 더해질 수 있다.
    // ⇒ 합 < 1.0px. 트리는 레이아웃 반올림을 끈다(Trees.cs — 화면 밖 트리는 배율 1 로 DIP 반올림해 기대 위치와 어긋난다).
    // 검출력: 배율이 한 번 더 곱해지면 빨강 표식 중심(DIP 13 이상, Trees.RedAt + Mark/2)이 13 × s × (s−1) px 밀린다. 최소 배율 1.25 에서 4.06px ≫ 1.0 이라 ×s 는 반드시 FAIL 이다.
    private const double Tolerance = 1.0;
    private const double Deviates = 2.0;    // 옛 식이 '어긋남'으로 볼 최소 오차(px) — 허용 오차 1.0 의 2배, 예상 최소 어긋남 4.06px 보다 작다
    private static readonly Point Offset = new(10.3, 7); // 화면 안 그림 자리(DIP) — 소수 자리도 본다

    public static string RendererMode { get; private set; } = "";

    public static Result Run(Tree tree, Size size, double scale)
    {
        var root = tree.Build(size);
        Trees.NoLayoutRounding(root);
        root.Measure(size);
        root.Arrange(new Rect(size));
        var g = new xkxk.frameworks.ui.views.Depth.SnapshotGeometry(size, scale);
        var err = new Dictionary<string, double>();
        double oldFactor;

        using (var old = OldCapture(root, size, scale))
        {
            err["old_capture"] = Error(old, tree, size, scale, default);
            oldFactor = Centroid(old, true) is { } red ? Math.Round(red.Y / ((Trees.RedAt(size).Y + (Trees.Mark / 2)) * scale), 3) : double.NaN;
            err["old_display"] = DisplayError(tree, size, scale, (c, at) => { using (c.PushTransform(Matrix.CreateTranslation(at.X, at.Y))) { c.DrawImage(old, new Rect(old.Size), new Rect(size)); } });
        }

        using (var shot = SnapshotRenderer.Capture(root, g, out var mode))
        {
            RendererMode = mode;
            err["new_capture"] = Error(shot, tree, size, scale, default);
            var picture = new SnapshotPicture(shot, g);
            err["new_display"] = DisplayError(tree, size, scale, (c, at) => { using (c.PushTransform(Matrix.CreateTranslation(at.X, at.Y))) { picture.Render(c); } });
        }

        using (var composed = SnapshotRenderer.Compose(SnapshotRenderer.Capture(root, g, out _), Brushes.Yellow, g))
        {
            err["new_compose"] = Error(composed, tree, size, scale, default);
        }

        var newOk = err["new_capture"] <= Tolerance && err["new_display"] <= Tolerance && err["new_compose"] <= Tolerance;
        // 원인 가설의 음성 대조: 배율 ≠ 1 이면 옛 표시는 항상 어긋나고, 옛 찍기는 그림자가 있는 MDI 트리에서만 어긋나야 한다
        var control = scale == 1 || (err["old_display"] >= Deviates && (tree.ShadowBug ? err["old_capture"] >= Deviates : err["old_capture"] <= Tolerance));
        return new Result(tree.Name, scale, size, err, newOk, control, oldFactor);
    }

    public static int PureGeometry(IEnumerable<double> scales, IEnumerable<Size> sizes)
    {
        var fail = 0;
        foreach (var s in scales)
        {
            foreach (var size in sizes)
            {
                var g = new xkxk.frameworks.ui.views.Depth.SnapshotGeometry(size, s);
                var spareW = g.Pixels.Width - g.Source.Width;
                var spareH = g.Pixels.Height - g.Source.Height;
                var k = g.Dest.Width * s / g.Source.Width;
                fail += spareW is >= 0 and < 1 && spareH is >= 0 and < 1 && Math.Abs(k - 1) < 1e-9 ? 0 : 1;
            }
        }

        return fail;
    }

    /// <summary>옛 식(5·6차): dpi 배율 RenderTargetBitmap 에 반올림 화소로 찍는다.</summary>
    private static RenderTargetBitmap OldCapture(Visual root, Size size, double scale)
    {
        var px = new PixelSize(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
        var bitmap = new RenderTargetBitmap(px, new Vector(96 * scale, 96 * scale));
        bitmap.Render(root);
        return bitmap;
    }

    /// <summary>화면처럼 dpi 배율을 건 비트맵에 그림을 Offset 자리에 그린 뒤 표식 오차.</summary>
    private static double DisplayError(Tree tree, Size size, double scale, Action<DrawingContext, Point> draw)
    {
        var screen = new Size(size.Width + 30, size.Height + 30);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(screen.Width * scale), (int)Math.Ceiling(screen.Height * scale)), new Vector(96 * scale, 96 * scale));
        using (var context = bitmap.CreateDrawingContext())
        {
            draw(context, Offset);
        }

        return Error(bitmap, tree, size, scale, Offset);
    }

    /// <summary>빨강·파랑 표식 무게중심과 기대 위치((offset + 표식 중심) × 배율) 차 중 큰 값(px). 표식이 없으면 무한대.</summary>
    private static double Error(Bitmap bitmap, Tree tree, Size size, double scale, Point offset)
    {
        var half = Trees.Mark / 2;
        var red = Centroid(bitmap, true);
        var blue = Centroid(bitmap, false);
        var wantRed = new Point((offset.X + Trees.RedAt(size).X + half) * scale, (offset.Y + Trees.RedAt(size).Y + half) * scale);
        var wantBlue = new Point((offset.X + Trees.BlueAt(size).X + half) * scale, (offset.Y + Trees.BlueAt(size).Y + half) * scale);
        return Math.Round(Math.Max(Distance(red, wantRed), Distance(blue, wantBlue)), 3);
    }

    private static double Distance(Point? a, Point b) => a is { } p ? Math.Max(Math.Abs(p.X - b.X), Math.Abs(p.Y - b.Y)) : double.PositiveInfinity;

    private static Point? Centroid(Bitmap bitmap, bool red)
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

        var rgba = bitmap.Format == PixelFormats.Rgba8888;
        double sx = 0, sy = 0, n = 0;
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var i = (y * stride) + (x * 4);
                var r = data[i + (rgba ? 0 : 2)];
                var gch = data[i + 1];
                var b = data[i + (rgba ? 2 : 0)];
                var hit = red ? r > 200 && gch < 60 && b < 60 : b > 200 && r < 60 && gch < 60;
                if (hit)
                {
                    sx += x + 0.5;
                    sy += y + 0.5;
                    n++;
                }
            }
        }

        return n > 0 ? new Point(sx / n, sy / n) : null;
    }
}

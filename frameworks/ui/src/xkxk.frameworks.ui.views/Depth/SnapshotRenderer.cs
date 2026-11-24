// 화면을 화소 비트맵으로 찍는 곳 — dpi 배율 대신 그리기 변환으로 배율을 주고 글자는 회색조로 그린다(Avalonia 12.1.4 Skia 의 배율 이중·투명 비트맵 LCD 글자 회피, 단위 시험 test/unit/snapshot_geometry 가 링크한다)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace xkxk.frameworks.ui.views.Depth;

/// <summary>
/// RenderTargetBitmap(dpi ≠ 96).Render 는 문맥에 '뒤 배율(postTransform)'을 건다. Skia 문맥은 클립·그림자를 되돌린 뒤 현재 변환을 버리고
/// 다음 PushTransform 에서 캔버스 전체 행렬(뒤 배율 포함)을 다시 읽어 뒤 배율을 한 번 더 곱한다(DrawingContextImpl.RestoreCanvas·Transform, 12.1.4 디컴파일).
/// 그래서 BoxShadow 가 있는 MDI 창은 내용이 배율만큼 더 커진다. dpi 96 비트맵에 배율 변환을 먼저 걸고 같은 즉시 그리기(ImmediateRenderer)를 부르면 뒤 배율이 없어 이 일이 없다.
/// 글자: RTB 문맥의 글자 기본값(Unspecified)은 Skia SubpixelAntialias(LCD)다(GlyphRunImpl.CreateSKFont). 투명 비트맵 위 LCD 글자는 Android 에서 한글이 꽉 찬 네모·굵은 획으로 나왔다
/// (8차 Fold5 진단: scaled·dpi·배율 1 모두 깨짐, 회색조만 정상). 그래서 회색조(Antialias)만 지정하고 힌팅·기준선은 Unspecified 로 둬 화면 글자와 같은 값(힌팅 Full)을 쓴다.
/// ImmediateRenderer 는 공개 형이 아니라 이름으로 찾는다. 못 찾으면 예비 경로(dpi 배율, 글자 기본값)로 찍고 Mode 로 알린다.
/// </summary>
internal static class SnapshotRenderer
{
    public const string ScaledMode = "scaled"; // 배율 변환 + 회색조 글자
    public const string DpiMode = "dpi";       // 예비 경로(그림자 뒤 배율 이중·LCD 글자 가능)

    private const string RendererType = "Avalonia.Rendering.ImmediateRenderer";

    private static readonly Action<DrawingContext, Visual>? s_render = FindRenderer();

    /// <summary>덮개 찍기 글자 설정 — 회색조 안티에일리어스만 정하고 나머지는 화면과 같은 기본값.</summary>
    private static readonly TextOptions s_text = new() { TextRenderingMode = TextRenderingMode.Antialias };

    [DynamicDependency("Render(Avalonia.Media.DrawingContext,Avalonia.Visual)", RendererType, "Avalonia.Base")] // 트리밍이 지우지 않게
    private static Action<DrawingContext, Visual>? FindRenderer()
    {
        var method = typeof(Visual).Assembly.GetType(RendererType)?
            .GetMethod("Render", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(DrawingContext), typeof(Visual) }, null);
        return method?.CreateDelegate<Action<DrawingContext, Visual>>();
    }

    /// <summary>view 를 geometry 의 화소 크기로 찍는다. mode = ScaledMode 또는 DpiMode.</summary>
    public static RenderTargetBitmap Capture(Visual view, SnapshotGeometry geometry, out string mode)
    {
        if (s_render is { } render)
        {
            var bitmap = new RenderTargetBitmap(geometry.Pixels); // dpi 96 — 뒤 배율 없음
            try
            {
                using (var context = bitmap.CreateDrawingContext())
                using (context.PushTransform(Matrix.CreateScale(geometry.Scale, geometry.Scale)))
                using (context.PushTextOptions(s_text))
                {
                    render(context, view);
                }

                mode = ScaledMode;
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        var fallback = new RenderTargetBitmap(geometry.Pixels, geometry.ScaledDpi);
        fallback.Render(view);
        mode = DpiMode;
        return fallback;
    }

    /// <summary>
    /// 바탕 층(아래→위)을 칠하고 내용을 얹은 그림 1장(불투명도가 한 번만 먹도록 컨트롤을 겹치지 않는다). content 는 여기서 버린다.
    /// 층 자리는 화면 DIP 이고 그림 사각형으로 잘라낸다. 2인자 DrawImage 는 배율만큼 키우므로 원본 사각형을 화소로 명시한다. dpi 96 비트맵은 문맥 좌표가 화소, 예비 경로 비트맵은 DIP 다.
    /// </summary>
    public static RenderTargetBitmap Compose(RenderTargetBitmap content, IReadOnlyList<BackdropLayer> layers, SnapshotGeometry geometry)
    {
        using (content)
        {
            var composed = new RenderTargetBitmap(content.PixelSize, content.Dpi);
            using (var context = composed.CreateDrawingContext())
            {
                var dpiPath = Math.Abs(content.Dpi.X - 96) > 0.01;
                var target = dpiPath ? geometry.Dest : geometry.Source;
                var k = dpiPath ? 1 : geometry.Scale; // 층 자리(DIP) → 문맥 좌표
                using (context.PushClip(target))
                {
                    foreach (var layer in layers)
                    {
                        var a = layer.Area;
                        context.FillRectangle(layer.Brush, new Rect(a.X * k, a.Y * k, a.Width * k, a.Height * k), (float)(layer.Radius * k));
                    }
                }

                context.DrawImage(content, geometry.Source, target);
            }

            return composed;
        }
    }

    /// <summary>단색 한 겹(그림 사각형 전체) — 단위 시험의 옛 비교용.</summary>
    public static RenderTargetBitmap Compose(RenderTargetBitmap content, IBrush brush, SnapshotGeometry geometry) =>
        Compose(content, new[] { new BackdropLayer(brush, geometry.Dest, 0, string.Empty) }, geometry);
}

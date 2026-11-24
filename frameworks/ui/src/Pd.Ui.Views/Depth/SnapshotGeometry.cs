// 화면 그림(덮개·짝 나감)의 화소·DIP 규약 — 찍기·합성·표시가 어느 배율에서나 1:1 이 되게 한다(앱 없는 단위 시험 test/unit/snapshot_geometry 가 이 파일을 그대로 링크한다)
using System;
using Avalonia;
using Avalonia.Media;

namespace Pd.Ui.Views.Depth;

/// <summary>
/// Avalonia 12.1.4 의 DrawImage 는 원본 사각형을 화소로 쓰는데 2인자 DrawImage·Image 컨트롤은 Bitmap.Size(DIP)를 넘긴다.
/// 그래서 그림은 언제나 화소 원본 사각형(Source)을 명시해서 그린다. 비트맵은 dpi 96 · 화소 크기 Pixels 로 만들고 배율은 그릴 때의 변환으로 준다(SnapshotRenderer).
/// </summary>
/// <param name="Size">화면의 크기(DIP).</param>
/// <param name="Scale">찍을 때의 화면 배율.</param>
internal readonly record struct SnapshotGeometry(Size Size, double Scale)
{
    private const double BaseDpi = 96;   // 장치 독립 화소 기준 DPI(Avalonia 규약)
    private const double Epsilon = 1e-6; // 곱셈 오차로 한 줄이 더 생기지 않게

    /// <summary>비트맵 화소 크기. 반올림하면 끝줄이 잘리므로 올린다.</summary>
    public PixelSize Pixels => new(
        Math.Max(1, (int)Math.Ceiling((Size.Width * Scale) - Epsilon)),
        Math.Max(1, (int)Math.Ceiling((Size.Height * Scale) - Epsilon)));

    /// <summary>예비 경로(dpi 로 배율을 주는 RenderTargetBitmap)에서만 쓰는 DPI.</summary>
    public Vector ScaledDpi => new(BaseDpi * Scale, BaseDpi * Scale);

    /// <summary>그림 안 실제 내용의 자리(화소). 올림으로 생긴 빈 끝은 넣지 않는다.</summary>
    public Rect Source => new(0, 0, Size.Width * Scale, Size.Height * Scale);

    /// <summary>화면에 그릴 자리(DIP).</summary>
    public Rect Dest => new(Size);

    /// <summary>화면에 그린다(DIP 문맥).</summary>
    public void Draw(DrawingContext context, IImage image) => context.DrawImage(image, Source, Dest);
}

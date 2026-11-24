// Snap Assist 썸네일 진단 전용 부분 — 원격 snapassist.thumbdump 가 예전/지금 찍기 방법의 비트맵·불투명 범위를 저장·보고한다(SnapAssistThumbnail 의 partial, 300줄 규칙 분할)
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Pd.Ui.Views.Snap;

public static partial class SnapAssistThumbnail
{
    /// <summary>
    /// 진단 전용(원격 snapassist.thumbdump): 같은 원본을 예전 방법(DPI 축소 Render)과 지금 방법(원래 크기 → 축소)으로 찍어
    /// prefix_legacy.png·prefix_full.png·prefix_thumb.png 로 저장하고(웹은 저장 안 함), 크기와 불투명 내용 범위를 1줄로 돌려준다.
    /// </summary>
    public static string DumpDiagnostic(object source, string prefix)
    {
        if (source is not Control c)
        {
            return "no-source";
        }

        var pre = Precheck(c);
        if (pre.Length > 0)
        {
            return "precheck=" + pre;
        }

        var w = c.Bounds.Width;
        var h = c.Bounds.Height;
        var max = new Size(SnapAssistCard.ThumbWidth, SnapAssistCard.ThumbHeight);
        var scale = Math.Max(MinScale, Math.Min(1.0, Math.Min(max.Width / w, max.Height / h)));
        var render = TopLevel.GetTopLevel(c)?.RenderScaling ?? 1.0;
        var save = !OperatingSystem.IsBrowser();

        // 예전 방법 그대로(비교용)
        var legacyPx = new PixelSize(Math.Max(1, (int)Math.Round(w * scale * render)), Math.Max(1, (int)Math.Round(h * scale * render)));
        using var legacy = new RenderTargetBitmap(legacyPx, new Vector(96 * scale * render, 96 * scale * render));
        legacy.Render(c);
        using var full = RenderFull(c, render, out var fullPx);
        if (save)
        {
            SavePng(legacy, prefix + "_legacy.png"); // 줄이기 전에 저장(줄이기가 실패해도 원인 비교본은 남긴다)
            SavePng(full, prefix + "_full.png");
        }

        var thumbPx = new PixelSize(Math.Max(1, (int)Math.Round(w * scale * render)), Math.Max(1, (int)Math.Round(h * scale * render)));
        using var thumb = Downscale(full, thumbPx);
        if (save)
        {
            SavePng(thumb, prefix + "_thumb.png");
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"src={c.GetType().Name} bounds={c.Bounds.X:0},{c.Bounds.Y:0},{w:0}x{h:0} render={render:0.##} scale={scale:0.###} "
            + $"legacy={legacyPx.Width}x{legacyPx.Height} legacyContent={ContentBox(legacy)} full={fullPx.Width}x{fullPx.Height} fullContent={ContentBox(full)} "
            + $"thumb={thumb.PixelSize.Width}x{thumb.PixelSize.Height} saved={(save ? prefix + "_*.png" : "no")}");
    }

    /// <summary>진단 저장: PNG 파일로 쓴다(파일 경로 Save 는 폐기 예정 API 라 스트림 + PNG 옵션을 쓴다).</summary>
    private static void SavePng(Bitmap bmp, string path)
    {
        using var fs = System.IO.File.Create(path);
        bmp.Save(fs, PngBitmapEncoderOptions.Default);
    }

    /// <summary>진단: 알파가 0 이 아닌 화소의 바깥 사각형 "x,y,w,h"(없으면 none, 복사 미지원이면 n/a). 예전 방법이 창 일부만 담는지 수치로 본다.</summary>
    private static string ContentBox(Bitmap bmp)
    {
        var size = bmp.PixelSize;
        var stride = size.Width * 4;
        var length = stride * size.Height;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            bmp.CopyPixels(new PixelRect(size), buffer, length, stride);
            int x0 = size.Width, y0 = size.Height, x1 = -1, y1 = -1;
            for (var y = 0; y < size.Height; y++)
            {
                for (var x = 0; x < size.Width; x++)
                {
                    if (Marshal.ReadByte(buffer, (y * stride) + (x * 4) + 3) == 0)
                    {
                        continue;
                    }

                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
            }

            return x1 < 0 ? "none" : $"{x0},{y0},{x1 - x0 + 1}x{y1 - y0 + 1}";
        }
        catch (Exception ex) when (ex is NotSupportedException or NotImplementedException or InvalidOperationException)
        {
            return "n/a";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

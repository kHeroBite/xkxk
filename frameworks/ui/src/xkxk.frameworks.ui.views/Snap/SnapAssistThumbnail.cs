// Snap Assist 후보 카드 썸네일 — 후보 창(Control·Window) 전체를 원래 크기로 정지 1회 찍은 뒤 비율 유지로 줄이고 세션 끝에 한꺼번에 해제한다(11 문서 §3.9, 계획 §5.4 R1)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.views.Snap;

/// <summary>
/// 썸네일은 실시간 갱신하지 않는다(YAGNI — 계획 §9). 찍지 못하면 false + 사유를 돌려주고, 호출자(SnapAssistPicker)는 대체 카드(큰 아이콘 + 제목 2줄)로 보인다.
/// 실패 사유: off(옵션 SnapAssistThumbnails 끔) · web-fallback(웹에서 이번 세션에 한 번 실패한 뒤) · no-source · detached · hidden · minimized · empty · error · transparent · 진단 강제 사유.
/// 찍는 방법: 원본을 화면과 같은 배율(RenderScaling)로 원래 크기 그대로 찍고, 그 비트맵을 CreateScaledBitmap 으로 줄인다.
/// 예전 방법(비트맵 DPI 를 96×축소배율로 두고 바로 Render)은 축소가 두 번 걸려 창의 일부(축소² 크기 조각)만 담겼다(2026-11-12 thumbdump 진단).
/// 모든 호출은 UI 스레드. 찍은 비트맵은 ReleaseAll(오버레이 Hide 때)까지 들고 있다. 진단 전용 부분은 SnapAssistThumbnail.Diag.cs.
/// </summary>
public static partial class SnapAssistThumbnail
{
    /// <summary>원본 대비 최소 배율. 이보다 작게 줄여야 하면 이 배율로 줄인다(계획 §5.4).</summary>
    public const double MinScale = 0.1;

    /// <summary>원래 크기로 찍을 때 긴 변 화소 상한(메모리 보호 — 이보다 크면 이 크기에 맞춰 찍는다). 초기 제시값.</summary>
    public const int MaxFullPixels = 4096;

    /// <summary>투명 판정에 쓰는 표본 화소 수(가로 4 × 세로 4).</summary>
    private const int SampleGrid = 4;

    private static readonly List<Bitmap> s_held = new();
    private static bool s_webFailed;

    /// <summary>마지막 TryCapture 의 진단 1줄(찍은 대상 형식·논리 크기·배율·화소 크기·방법). 실패 때도 남긴다.</summary>
    public static string LastDetail { get; private set; } = string.Empty;

    /// <summary>
    /// source(Control 또는 Window) 전체를 max 안에 비율 유지로 줄여 1회 찍는다.
    /// 성공이면 image 에 비트맵, reason 은 "ok". 실패면 image = null, reason = 사유.
    /// </summary>
    public static bool TryCapture(object source, Size max, out IImage? image, out string reason)
    {
        image = null;
        LastDetail = $"src={source?.GetType().Name ?? "null"}";
        if (!UiSettings.Current.EffectiveSnapAssistThumbnails)
        {
            reason = "off";
            return false;
        }

        if (s_webFailed)
        {
            reason = "web-fallback";
            return false;
        }

        if (SnapAssistSession.DiagThumbFail is { Length: > 0 } forced)
        {
            reason = forced;
            return Fail(ref reason);
        }

        if (source is not Control c)
        {
            reason = "no-source";
            return false;
        }

        reason = Precheck(c);
        if (reason.Length > 0)
        {
            return Fail(ref reason);
        }

        var w = c.Bounds.Width;
        var h = c.Bounds.Height;
        var scale = Math.Min(1.0, Math.Min(max.Width / w, max.Height / h));
        if (double.IsNaN(scale) || scale < MinScale)
        {
            scale = MinScale;
        }

        var render = TopLevel.GetTopLevel(c)?.RenderScaling ?? 1.0;
        Bitmap? result = null;
        try
        {
            using var full = RenderFull(c, render, out var fullPx);
            var thumbPx = new PixelSize(Math.Max(1, (int)Math.Round(w * scale * render)), Math.Max(1, (int)Math.Round(h * scale * render)));
            LastDetail = string.Create(CultureInfo.InvariantCulture,
                $"src={c.GetType().Name} size={w:0}x{h:0} render={render:0.##} scale={scale:0.###} full={fullPx.Width}x{fullPx.Height} thumb={thumbPx.Width}x{thumbPx.Height}");
            if (IsAllTransparent(full))
            {
                reason = "transparent";
                return Fail(ref reason);
            }

            result = Downscale(full, thumbPx);
        }
        catch (Exception ex)
        {
            result?.Dispose();
            PdLog.Info("snap-assist", $"thumb error type={c.GetType().Name} {ex.GetType().Name}: {ex.Message}");
            reason = "error";
            return Fail(ref reason);
        }

        s_held.Add(result);
        image = result;
        reason = "ok";
        return true;
    }

    /// <summary>세션 끝(오버레이 Hide)에 찍은 비트맵을 모두 해제하고 웹 폴백 표시를 지운다. 여러 번 불러도 된다.</summary>
    public static void ReleaseAll()
    {
        foreach (var b in s_held)
        {
            b.Dispose();
        }

        s_held.Clear();
        s_webFailed = false;
    }

    /// <summary>원본을 화면 배율 그대로(축소 없이) 찍는다. 긴 변이 MaxFullPixels 를 넘으면 그 크기에 맞게 배율을 낮춘다.</summary>
    private static RenderTargetBitmap RenderFull(Control c, double render, out PixelSize px)
    {
        var w = c.Bounds.Width;
        var h = c.Bounds.Height;
        var dpiScale = render;
        var longest = Math.Max(w, h) * dpiScale;
        if (longest > MaxFullPixels)
        {
            dpiScale *= MaxFullPixels / longest;
        }

        px = new PixelSize(Math.Max(1, (int)Math.Round(w * dpiScale)), Math.Max(1, (int)Math.Round(h * dpiScale)));
        var bmp = new RenderTargetBitmap(px, new Vector(96 * dpiScale, 96 * dpiScale));
        try
        {
            bmp.Render(c);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 원래 크기 비트맵을 target 화소 크기로 줄인다(고품질 보간). RenderTargetBitmap 은 CreateScaledBitmap 원본이 될 수 없어("Invalid source bitmap type", 2026-11-12 실측)
    /// 화소를 일반 Bitmap 으로 복사한 뒤 줄인다. 줄이기가 실패하면 복사본(원래 크기)을 넘긴다(표시는 Image Stretch 가 줄인다).
    /// </summary>
    private static Bitmap Downscale(RenderTargetBitmap full, PixelSize target)
    {
        var copy = Copy(full);
        if (target.Width >= copy.PixelSize.Width && target.Height >= copy.PixelSize.Height)
        {
            return copy;
        }

        try
        {
            var scaled = copy.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
            copy.Dispose();
            return scaled;
        }
        catch (Exception ex)
        {
            PdLog.Info("snap-assist", $"thumb scale failed: {ex.GetType().Name}: {ex.Message} — 원래 크기 사용");
            return copy;
        }
    }

    /// <summary>화소를 복사해 원래 크기 비트맵을 새로 만든다(찍은 RenderTargetBitmap 은 호출자가 바로 해제한다).</summary>
    private static Bitmap Copy(RenderTargetBitmap src)
    {
        var size = src.PixelSize;
        var stride = size.Width * 4;
        var length = stride * size.Height;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            src.CopyPixels(new PixelRect(size), buffer, length, stride);
            return new Bitmap(Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul, buffer, size, src.Dpi, stride);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>찍기 전 확인. 문제가 없으면 빈 문자열.</summary>
    private static string Precheck(Control c)
    {
        if (TopLevel.GetTopLevel(c) is null)
        {
            return "detached";
        }

        if (c is Window { WindowState: WindowState.Minimized })
        {
            return "minimized";
        }

        if (!c.IsEffectivelyVisible)
        {
            return "hidden";
        }

        return c.Bounds.Width < 1 || c.Bounds.Height < 1 ? "empty" : string.Empty;
    }

    /// <summary>실패 기록. 웹은 첫 실패 뒤 이번 세션 나머지를 대체 카드로 보인다(계획 §5.4 — 같은 실패를 후보마다 반복하지 않게).</summary>
    private static bool Fail(ref string reason)
    {
        if (OperatingSystem.IsBrowser() && reason is not ("minimized" or "hidden" or "detached" or "empty"))
        {
            s_webFailed = true;
        }

        return false;
    }

    /// <summary>
    /// 표본 16화소의 알파가 모두 0 이면 true(렌더가 비어 나온 경우 — 웹 WASM 등). 화소 복사를 지원하지 않는 백엔드면 판정하지 않고 false.
    /// 알파는 Bgra8888·Rgba8888 모두 4바이트 중 마지막 바이트다.
    /// </summary>
    private static bool IsAllTransparent(RenderTargetBitmap bmp)
    {
        var size = bmp.PixelSize;
        var stride = size.Width * 4;
        var length = stride * size.Height;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            bmp.CopyPixels(new PixelRect(size), buffer, length, stride);
            for (var gy = 0; gy < SampleGrid; gy++)
            {
                for (var gx = 0; gx < SampleGrid; gx++)
                {
                    var x = (int)((gx + 0.5) * size.Width / SampleGrid);
                    var y = (int)((gy + 0.5) * size.Height / SampleGrid);
                    if (Marshal.ReadByte(buffer, (y * stride) + (x * 4) + 3) != 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or NotImplementedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

// 장식 decor=image 의 사용자 배경 이미지 브러시(09/21 §6.2) — 배경 스레드에서 max_dim_px 로 줄여 디코드하고 캐시한다. 실패면 none(투명)·알림 1회·저장값 보존
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Theming;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Theming;

public static partial class DesignEffectsApplier
{
    private static string? _imageKey;      // 지금 캐시가 가리키는 '파일|축소 폭'
    private static Bitmap? _imageBitmap;   // 디코드 끝난 이미지(null = 아직·실패)
    private static bool _imageLoading;
    private static readonly HashSet<string> s_imageFailed = new(StringComparer.Ordinal); // 실패한 키 — 같은 키는 다시 읽지 않고 알림도 1회만

    /// <summary>배경 이미지 저장 뒷단. 데스크톱·Android = LocalBlobStore, 웹 = 브라우저 IndexedDB(Program.Storage). null 이면 이미지는 none.</summary>
    public static IBlobStore? BlobStore { get; set; }

    /// <summary>배경 이미지를 못 보여 none 으로 낮췄을 때(사유 키 background.fail.*). 같은 파일·사유는 1회만 알린다.</summary>
    public static event Action<string>? BackgroundFallback;

    /// <summary>마지막 낮춤 사유 키(진단·편집기 표시용). 성공하면 null 로 돌아간다.</summary>
    public static string? LastBackgroundFallback { get; private set; }

    /// <summary>이미지 브러시. 파일이 없거나 아직 읽는 중이거나 실패했으면 투명(none)이고, 읽기가 끝나면 다시 적용한다.</summary>
    private static IBrush ImageOf()
    {
        var s = UiSettings.Current;
        if (s.BackgroundImageFile is not { } file)
        {
            return Brushes.Transparent; // 아직 고르지 않음 — 실패가 아니므로 알리지 않는다
        }

        var key = file + "|" + s.BackgroundImageMaxDimPx;
        if (!string.Equals(_imageKey, key, StringComparison.Ordinal))
        {
            _imageKey = key;
            _imageBitmap = null; // 옛 비트맵은 옛 브러시가 놓으면 GC 가 거둔다(그리는 중 해제 방지)
            _imageLoading = false;
        }

        if (_imageBitmap is { } bmp)
        {
            return ImageBrushOf(bmp, s.BackgroundImageFit, s.BackgroundImageOpacity, s.BackgroundImageDim);
        }

        if (!_imageLoading && !s_imageFailed.Contains(key))
        {
            _imageLoading = true;
            MarkImageLoadStarted();
            _ = LoadImageAsync(file, s.BackgroundImageMaxDimPx, s.BackgroundImageLimits, key);
        }

        return Brushes.Transparent;
    }

    /// <summary>서명에 넣을 이미지 상태(설정 4값 + 준비 여부). 읽기가 끝나면 서명이 바뀌어 다시 쓴다.</summary>
    private static string ImageSignature()
    {
        var s = UiSettings.Current;
        return string.Join(",", s.BackgroundImageFile ?? "-", s.BackgroundImageFit, s.BackgroundImageOpacity, s.BackgroundImageDim, s.BackgroundImageMaxDimPx, _imageBitmap is null ? 0 : 1);
    }

    /// <summary>배경 이미지 설정이 바뀌면 효과를 다시 적용한다. 파일이 바뀌면 실패 기록을 지워 같은 파일을 다시 골라도 다시 읽는다(웹 할당량 정리 뒤 등).</summary>
    private static void OnUiSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { } name || !name.StartsWith("BackgroundImage", StringComparison.Ordinal))
        {
            return;
        }

        var fileChanged = name == nameof(UiSettings.BackgroundImageFile);
        if (fileChanged)
        {
            s_imageFailed.Clear();
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (fileChanged)
            {
                SetDecorImageFailed(false); // 새 파일은 다시 읽는다 — 실효값을 image 로 되돌려야 ImageOf 가 불린다
            }

            if (Application.Current is { } app) { Apply(app); }
        });
    }

    private static async Task LoadImageAsync(string file, int maxDim, BackgroundImageLimits limits, string key)
    {
        string? fail = null;
        Bitmap? bmp = null;
        try
        {
            var bytes = BlobStore is { } store ? await new BackgroundImageStore(store, limits).LoadAsync(file).ConfigureAwait(false) : null;
            if (bytes is null)
            {
                fail = BlobStore is null ? "store" : "missing"; // 지워짐·웹 축출
            }
            else if (BackgroundImageStore.Validate(file, bytes, limits) is { Ok: false } bad)
            {
                fail = "reject_" + bad.Reason.ToString().ToLowerInvariant(); // 손상·크기·형식·가로세로 초과
            }
            else
            {
                bmp = await Task.Run(() => Decode(bytes, maxDim)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            fail = ex.Message.Contains("QuotaExceeded", StringComparison.Ordinal) ? "quota" : "decode";
            PdLog.Write(PdLogLevel.Warning, "theme", $"배경 이미지 읽기 실패 file={file} {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
        }

        Dispatcher.UIThread.Post(() => OnImageLoaded(key, file, bmp, fail));
    }

    private static void OnImageLoaded(string key, string file, Bitmap? bmp, string? fail)
    {
        if (!string.Equals(_imageKey, key, StringComparison.Ordinal))
        {
            bmp?.Dispose(); // 그사이 다른 이미지로 바뀜
            return;
        }

        _imageLoading = false;
        if (bmp is null)
        {
            s_imageFailed.Add(key);
            MarkImageLoadFailed();
            NotifyFallback(file, fail ?? "decode");
            SetDecorImageFailed(true);
        }
        else
        {
            _imageBitmap = bmp;
            LastBackgroundFallback = null;
            SetDecorImageFailed(false);
        }

        if (Application.Current is { } app)
        {
            Apply(app); // 서명(ImageSignature)이 바뀌어 장식 리소스를 다시 쓴다
            MarkImageApplied(app);
        }
    }

    // 적용 시간 계측 지점(본문은 DesignEffectsApplier.ImageTiming.cs — 제품 빌드는 본문이 없어 호출째 빠진다)
    static partial void MarkImageLoadStarted();

    static partial void MarkImageLoadFailed();

    static partial void MarkImageApplied(Application app);

    /// <summary>낮춤 알림 누적 횟수(진단 — '알림 1회' 판정용).</summary>
    public static int BackgroundFallbackCount { get; private set; }

    /// <summary>이미지 디코드가 끝나 브러시가 준비됐는가(진단).</summary>
    public static bool BackgroundImageReady => _imageBitmap is not null;

    /// <summary>진단·시험용: 캐시·실패 기록을 지우고 다시 읽게 한다(손상 파일 케이스 — 같은 이름의 내용을 바꾼 뒤 부른다).</summary>
    public static void ResetBackgroundImage()
    {
        _imageKey = null;
        _imageBitmap = null;
        _imageLoading = false;
        s_imageFailed.Clear();
        SetDecorImageFailed(false);
        if (Application.Current is { } app)
        {
            Invalidate();
            Apply(app);
        }
    }

    /// <summary>이미지 실패 여부를 해석기에 알린다. 바뀌면 Refresh 로 decor 실효값(image ↔ none)과 편집기 표시를 갱신한다.</summary>
    private static void SetDecorImageFailed(bool failed)
    {
        if (DesignThemeResolver.DecorImageFailed == failed)
        {
            return;
        }

        DesignThemeResolver.DecorImageFailed = failed;
        DesignThemeResolver.Refresh();
    }

    private static void NotifyFallback(string file, string reason)
    {
        var reasonKey = "background.fail." + reason;
        LastBackgroundFallback = reasonKey;
        BackgroundFallbackCount++;
        PdLog.WriteUser(PdLogLevel.Warning, Loc.Tr(reasonKey) + " (" + file + ")");
        BackgroundFallback?.Invoke(reasonKey);
    }

    /// <summary>긴 변을 max_dim_px 이하로 줄여 디코드한다(원본이 더 작으면 원래 크기).</summary>
    private static Bitmap Decode(byte[] bytes, int maxDim)
    {
        var dims = BackgroundImageStore.ReadDimensions(bytes, BackgroundImageStore.DetectFormat(bytes) ?? string.Empty);
        using var ms = new MemoryStream(bytes, writable: false);
        if (dims is { } d && d.Height > d.Width)
        {
            return Bitmap.DecodeToHeight(ms, Math.Min(d.Height, maxDim), BitmapInterpolationMode.MediumQuality);
        }

        return Bitmap.DecodeToWidth(ms, Math.Min(dims?.Width ?? maxDim, maxDim), BitmapInterpolationMode.MediumQuality);
    }

    /// <summary>
    /// 맞춤: fill = 비율 유지 채움(넘치면 잘림)·fit = 비율 유지 맞춤·stretch = 늘림·tile = 원래 크기 바둑판·center = 원래 크기 가운데.
    /// 어둡게(dim)는 이미지 위에 검정 반투명 사각형을 얹고, 불투명도는 둘을 묶은 그룹에 건다.
    /// </summary>
    private static IBrush ImageBrushOf(Bitmap bmp, BackgroundImageFit fit, double opacity, double dim)
    {
        var rect = new Rect(bmp.Size);
        var group = new DrawingGroup { Opacity = Math.Clamp(opacity, 0, 1) };
        group.Children.Add(new ImageDrawing { ImageSource = bmp, Rect = rect });
        if (dim > 0)
        {
            group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(dim, 0, 1)), 0, 0, 0)), Geometry = new RectangleGeometry(rect) });
        }

        var brush = new DrawingBrush(group)
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Stretch = fit switch
            {
                BackgroundImageFit.Fill => Stretch.UniformToFill,
                BackgroundImageFit.Fit => Stretch.Uniform,
                BackgroundImageFit.Stretch => Stretch.Fill,
                _ => Stretch.None,
            },
        };
        if (fit == BackgroundImageFit.Tile)
        {
            brush.TileMode = TileMode.Tile;
            brush.SourceRect = new RelativeRect(rect, RelativeUnit.Absolute);
            brush.DestinationRect = new RelativeRect(rect, RelativeUnit.Absolute);
        }

        return brush;
    }
}

// 메뉴·셸·명령 아이콘 키(StreamGeometry 리소스)를 OS 창 아이콘(WindowIcon)으로 그려 주는 공용 변환기 — 키·색별 캐시, 실패하면 null(앱 아이콘 유지)
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// 아이콘 키(예: PdShellIconSettings·PdCmdIconAbout·PdMenuIcon1009)를 32px 투명 바탕 PNG 로 그려 WindowIcon 을 만든다(22 설계 — 메뉴 아이콘과 창 아이콘 일치).
/// 글리프 색은 테마 강조색(PdBrushAccent, 없으면 #2B6CB0)이다. 작업표시줄 바탕은 앱 테마와 무관하므로 글자색이 아니라 강조색을 쓴다.
/// 캐시 키에 색을 넣어 테마가 바뀌면 새로 그린다(이미 열린 창의 아이콘은 다시 열 때 바뀐다).
/// 셸 층이라 views 의 WindowIcon 컨트롤을 참조하지 않고 Geometry 리소스만 읽는다(레이어 규칙).
/// </summary>
public static class MenuIconBitmap
{
    private const int IconPixels = 32;
    private const double GridSize = 24; // 생성기 글리프 격자(menu_icons.py — 두 모서리 점으로 24 격자 고정)
    private static readonly Color FallbackAccent = Color.Parse("#2B6CB0");
    private static readonly Dictionary<(string Key, uint Color), WindowIcon> Cache = new();

    /// <summary>창에 단 아이콘 키(진단 window.icon·SDI 활성 문서 키 공용). 키가 없으면 null.</summary>
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<Window, string?>("Key", typeof(MenuIconBitmap));

    public static string? GetKey(Window window) => window.GetValue(KeyProperty);

    public static void SetKey(Window window, string? value) => window.SetValue(KeyProperty, value);

    /// <summary>키의 글리프를 창 아이콘으로 만든다. 키가 비었거나 리소스가 없거나 그리기에 실패하면 null(호출자는 앱 아이콘을 그대로 둔다).</summary>
    public static WindowIcon? Create(string? key)
    {
        if (string.IsNullOrEmpty(key) || Application.Current is not { } app)
        {
            return null;
        }

        if (!app.TryFindResource(key, app.ActualThemeVariant, out var value) || value is not Geometry glyph)
        {
            System.Diagnostics.Debug.WriteLine($"[경고][아이콘] 창 아이콘 글리프 없음: {key}");
            return null;
        }

        var color = AccentColor(app);
        var cacheKey = (key, color.ToUInt32());
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var icon = Render(glyph, color);
            Cache[cacheKey] = icon;
            return icon;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[경고][아이콘] 창 아이콘 그리기 실패 {key}: {ex.Message}");
            return null;
        }
    }

    /// <summary>창에 키 글리프 아이콘을 달고 KeyProperty 를 기록한다. 글리프를 못 만들면 Icon 은 건드리지 않고 키만 남긴다. 아이콘을 달았으면 true.</summary>
    public static bool Apply(Window window, string? key)
    {
        SetKey(window, key);
        if (Create(key) is not { } icon)
        {
            return false;
        }

        window.Icon = icon;
        return true;
    }

    private static Color AccentColor(Application app) =>
        app.TryFindResource("PdBrushAccent", app.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : FallbackAccent;

    private static WindowIcon Render(Geometry glyph, Color color)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(IconPixels, IconPixels), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var scale = IconPixels / GridSize;
            using (ctx.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                ctx.DrawGeometry(new SolidColorBrush(color), null, glyph);
            }
        }

        var png = new MemoryStream();
        bitmap.Save(png);
        png.Position = 0;
        return new WindowIcon(png); // WindowIcon 이 스트림을 읽어 보관한다(ShellPlatform.LoadAppIcon 과 같은 방식)
    }
}

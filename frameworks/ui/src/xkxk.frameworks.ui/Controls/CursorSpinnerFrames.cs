// 커서 스피너 프레임 — 24px 간트 비트맵 커서 8장을 한 번만 만들어 재사용하고 Dispose 로 함께 해제한다
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace xkxk.frameworks.ui.Controls;

/// <summary>
/// 애니메이션 커서 API 가 없어 상위 층이 타이머로 <see cref="Frames"/> 를 차례로 TopLevel.Cursor 에 넣는다.
/// Dispose 전에 TopLevel.Cursor 를 원복해야 한다. 테마가 바뀌면 새 색으로 다시 만든다.
/// </summary>
public sealed class CursorSpinnerFrames : IDisposable
{
    /// <summary>프레임 수.</summary>
    public const int FrameCount = 8;

    /// <summary>커서 크기(px).</summary>
    public const int PixelSize = 24;

    private readonly List<Cursor> _cursors = new(FrameCount);
    private readonly List<Bitmap> _bitmaps = new(FrameCount);
    private bool _disposed;

    private CursorSpinnerFrames()
    {
    }

    /// <summary>프레임 커서 목록. 마지막 프레임이 막대 4개 완성이다.</summary>
    public IReadOnlyList<Cursor> Frames => _cursors;

    /// <summary>프레임을 만든다. 플랫폼이 비트맵 커서를 못 만들면 null 과 사유를 돌려주므로 상위 층이 다음 단계로 폴백한다.</summary>
    public static CursorSpinnerFrames? TryCreate(IBrush brush, out string? error)
    {
        var frames = new CursorSpinnerFrames();
        try
        {
            var hotSpot = new PixelPoint(PixelSize / 2, PixelSize / 2);
            for (int i = 0; i < FrameCount; i++)
            {
                var bitmap = GanttSpinner.RenderFrame((i + 1) / (double)FrameCount, PixelSize, brush);
                frames._bitmaps.Add(bitmap);
                frames._cursors.Add(new Cursor(bitmap, hotSpot));
            }

            error = null;
            return frames;
        }
        catch (Exception ex)
        {
            frames.Dispose();
            error = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var cursor in _cursors)
        {
            cursor.Dispose();
        }

        foreach (var bitmap in _bitmaps)
        {
            bitmap.Dispose();
        }

        _cursors.Clear();
        _bitmaps.Clear();
    }
}

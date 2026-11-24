// 커서 스피너 구동 — busy 창의 TopLevel.Cursor 를 비트맵 프레임으로 차례로 바꾸고 끝나면 원복한다. 실패하면 Wait 커서로 폴백하고, 브라우저·모바일은 포인터 추적 스피너를 쓴다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Pd.Ui.Controls;
using Pd.Ui.Motion;

namespace Pd.Poc.Shell.Busy;

/// <summary>
/// 타이머는 표시 중인 스코프가 있고 프레임 방식일 때만 돈다. 커서는 그 창(TopLevel) 단위로 적용하므로 창 2개 이상이 독립으로 동작한다.
/// MDI 문서가 busy 면 그 문서가 든 메인 창 전체의 커서가 바뀐다(자식이 자기 커서를 지정한 곳은 제외).
/// </summary>
internal sealed class BusyCursorDriver
{
    internal const string CursorModeKey = "cursorMode";
    internal const string ModeFrames = "frames";
    internal const string ModeWait = "wait";
    internal const string ModeFollower = "follower";
    internal const string ModeNone = "none";
    internal const string ModeIdle = "idle";

    private const int FrameIntervalMs = 200; // 8프레임 × 200ms = 스피너 한 주기 1.6초
    private const string AccentKey = "PdBrushAccent";

    private sealed class Applied
    {
        public bool WasSet;
        public Cursor? Original;
        public Cursor? LastSet;
    }

    private readonly Func<string, TopLevel?> _resolve;
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<TopLevel, Applied> _applied = new();
    private DispatcherTimer? _timer;
    private CursorSpinnerFrames? _frames;
    private Color? _framesColor;
    private Cursor? _wait;
    private int _frameIndex;

    internal BusyCursorDriver(Func<string, TopLevel?> resolve) => _resolve = resolve;

    /// <summary>브라우저·Android·iOS 는 움직이는 OS 커서를 쓸 수 없어 창 안 포인터 추적 스피너로 대체한다.</summary>
    internal static bool UsesFollower => OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    /// <summary>현재 커서 방식. 플랫폼 판정 또는 마지막 폴백 결과다.</summary>
    internal string Mode { get; private set; } = ModeIdle;

    /// <summary>프레임 생성 실패 사유. 없으면 null.</summary>
    internal string? FrameError { get; private set; }

    internal int FrameIndex => _frameIndex;

    /// <summary>프레임 교체 횟수(진단 카운터, 진행 확인용).</summary>
    internal long FrameAdvances { get; private set; }

    /// <summary>프레임 묶음 생성 횟수. 테마 색이 그대로면 1 에서 늘지 않아야 한다(핸들 재생성 없음).</summary>
    internal int FrameSetsBuilt { get; private set; }

    internal bool IsTimerRunning => _timer is not null;

    internal int AppliedWindowCount => _applied.Count;

    internal IReadOnlyCollection<TopLevel> AppliedWindows => _applied.Keys;

    /// <summary>시험용: 켜면 비트맵 프레임을 만들지 않은 것처럼 Wait 로 폴백한다.</summary>
    internal bool ForceBitmapFailure { get; set; }

    /// <summary>그 창의 지금 커서가 이 구동기가 넣은 프레임인가.</summary>
    internal bool IsDriven(TopLevel top) => _applied.TryGetValue(top, out var a) && ReferenceEquals(top.Cursor, a.LastSet);

    internal void SetScope(string scope, bool visible)
    {
        if (visible)
        {
            _active.Add(scope);
        }
        else
        {
            _active.Remove(scope);
        }

        Refresh();
    }

    /// <summary>방식을 다시 고른다(시험에서 폴백을 켠 뒤 다음 시작부터 반영).</summary>
    internal void ResetFrames()
    {
        RestoreAll();
        _frames?.Dispose();
        _frames = null;
        _framesColor = null;
        Mode = ModeIdle;
        Refresh();
    }

    private void Refresh()
    {
        if (_active.Count == 0)
        {
            RestoreAll();
            StopTimer();
            return;
        }

        var cursor = NextCursor();
        var wanted = new HashSet<TopLevel>();
        foreach (var scope in _active)
        {
            if (cursor is not null && _resolve(scope) is { } top)
            {
                wanted.Add(top); // SDI 로 옮겨 간 문서는 여기서 새 창으로 따라간다
            }
        }

        foreach (var top in _applied.Keys.Where(t => !wanted.Contains(t)).ToArray())
        {
            Restore(top);
        }

        foreach (var top in wanted)
        {
            Apply(top, cursor!);
        }

        if (Mode == ModeFrames && !MotionPreference.Reduced)
        {
            StartTimer();
        }
        else
        {
            StopTimer();
        }
    }

    private Cursor? NextCursor()
    {
        if (UsesFollower)
        {
            Mode = OperatingSystem.IsBrowser() ? ModeFollower : ModeNone;
            return null;
        }

        if (EnsureFrames() is { } frames)
        {
            Mode = ModeFrames;
            return frames.Frames[MotionPreference.Reduced ? CursorSpinnerFrames.FrameCount - 1 : _frameIndex]; // 움직임 줄이기: 완성 프레임에 멈춘다
        }

        try
        {
            _wait ??= new Cursor(StandardCursorType.Wait);
            Mode = ModeWait;
            return _wait;
        }
        catch (Exception ex)
        {
            FrameError = ex.GetType().Name + ": " + ex.Message;
            Mode = ModeNone;
            return null;
        }
    }

    private CursorSpinnerFrames? EnsureFrames()
    {
        if (ForceBitmapFailure)
        {
            FrameError = "forced";
            return null;
        }

        var brush = AccentBrush();
        if (brush is null)
        {
            FrameError = "resource missing: " + AccentKey;
            return null;
        }

        var color = (brush as ISolidColorBrush)?.Color;
        if (_frames is not null && (_framesColor == color || _applied.Count > 0))
        {
            return _frames; // 테마가 바뀌어도 커서를 쓰는 중에는 바꾸지 않고 다음 시작에 새 색으로 만든다
        }

        if (_frames is null && FrameError is not null && _framesColor == color)
        {
            return null; // 같은 조건에서 실패한 생성을 틱마다 되풀이하지 않는다
        }

        _frames?.Dispose();
        _frames = CursorSpinnerFrames.TryCreate(brush, out var error);
        _framesColor = color;
        _frameIndex = 0;
        FrameError = error;
        if (_frames is null)
        {
            Console.WriteLine($"[busy] cursor frames unavailable: {error}");
            return null;
        }

        FrameSetsBuilt++;
        return _frames;
    }

    private static IBrush? AccentBrush() =>
        Application.Current is { } app && app.TryGetResource(AccentKey, app.ActualThemeVariant, out var value) ? value as IBrush : null;

    private void Apply(TopLevel top, Cursor cursor)
    {
        if (!_applied.TryGetValue(top, out var applied))
        {
            applied = new Applied { WasSet = top.IsSet(InputElement.CursorProperty), Original = top.Cursor };
            _applied[top] = applied;
        }

        top.Cursor = cursor;
        applied.LastSet = cursor;
    }

    private void RestoreAll()
    {
        foreach (var top in _applied.Keys.ToArray())
        {
            Restore(top);
        }
    }

    private void Restore(TopLevel top)
    {
        if (_applied.Remove(top, out var applied) && ReferenceEquals(top.Cursor, applied.LastSet))
        {
            if (applied.WasSet)
            {
                top.Cursor = applied.Original;
            }
            else
            {
                top.ClearValue(InputElement.CursorProperty);
            }
        }
    }

    private void StartTimer()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameIntervalMs) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _frameIndex = (_frameIndex + 1) % CursorSpinnerFrames.FrameCount;
        FrameAdvances++;
        Refresh();
    }
}

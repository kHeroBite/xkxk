// 기동 터치 게이트의 받아 두기·버림 사유 로그·다시 보내기(B 로 옮겨 보내기) 부분 — StartupTouchGate.cs 와 한 클래스(partial)
using Android.OS;
using Android.Views;
using Avalonia.Controls.Primitives;

namespace xkxk.frameworks.ui.android;

internal sealed partial class StartupTouchGate
{
    private void Buffer(MotionEvent e)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                if (_current is not null) { LogDrop("superseded_down", final: false); } // 앞 동작이 끝나기 전 새 Down 이 와 앞 동작을 버린다
                Release(ref _current);
                var (w, h) = _windowSize();
                _current = new StartupGesture { DownX = e.GetX(), DownY = e.GetY(), DownTime = e.EventTime, Width = w, Height = h };
                Add(_current, e);
                Console.WriteLine($"[startup] touch buffered +{StartupMarks.ElapsedMs}ms");
                break;
            case MotionEventActions.PointerDown when _current is not null:
                Reject(_current, "multi_touch");
                break;
            case MotionEventActions.Move when _current is not null:
                if (Math.Abs(e.GetX() - _current.DownX) > _touchSlop || Math.Abs(e.GetY() - _current.DownY) > _touchSlop)
                {
                    Reject(_current, "moved");
                }

                Add(_current, e);
                break;
            case MotionEventActions.Up when _current is not null:
                Add(_current, e);
                _current.UpTime = e.EventTime;
                if (_current.Rejected is null && _current.UpTime - _current.DownTime > _longPressMs)
                {
                    Reject(_current, "long_press");
                }

                FinishGesture();
                break;
            case MotionEventActions.Cancel:
                if (_current is not null) { LogDrop("cancelled"); } // 시스템이 동작을 취소했다
                Release(ref _current);
                break;
            case MotionEventActions.Up:
                LogDrop("orphan_up"); // 받아 둔 Down 이 없는 뗌(Up when _current 분기에 안 걸린 경우)
                break;
            default:
                if (_current is not null)
                {
                    Add(_current, e);
                }

                break;
        }
    }

    private void FinishGesture()
    {
        if (_current!.Rejected is { } reason)
        {
            LogDrop(reason);
            Release(ref _current);
            return;
        }

        if (_lastTap is not null) { LogDrop("multi_tap_superseded", final: false); } // 연타는 마지막 탭만 남긴다(설계)
        Release(ref _lastTap);
        _lastTap = _current;
        _current = null;
    }

    /// <summary>복사본을 만들어 위치를 '화면 좌표 - 창 위치'(B)로 옮겨 보낸다. 받은 이벤트·버퍼 원본은 바꾸지 않는다.</summary>
    private void DispatchAt(MotionEvent e, float rawX, float rawY, (int X, int Y) o)
    {
        var c = MotionEvent.Obtain(e)!;
        c.SetLocation(rawX - o.X, rawY - o.Y);
        _dispatch(c);
        c.Recycle();
    }

    /// <summary>B 로 옮겨 보내는 동작의 나머지. 두 번째 손가락이 오면 취소를 보내고 남은 것은 삼킨다(탭만 보낸다는 원칙).</summary>
    private void FollowRetarget(MotionEvent e, (int X, int Y) o)
    {
        var action = e.ActionMasked;
        if (action == MotionEventActions.PointerDown)
        {
            var cancel = MotionEvent.Obtain(e)!;
            cancel.Action = MotionEventActions.Cancel;
            cancel.SetLocation(e.RawX - o.X, e.RawY - o.Y);
            _dispatch(cancel);
            cancel.Recycle();
            _retargetOrigin = null;
            _swallowUntilUp = true;
            Console.WriteLine("[startup] touch retarget_b cancelled reason=multi_touch");
            LogDrop("multi_touch_cancelled");
            return;
        }

        DispatchAt(e, e.RawX, e.RawY, o);
        if (action is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            _retargetOrigin = null;
        }
    }

    private string? DropReason(StartupGesture g, long age)
    {
        if (age > TapMaxAgeMs)
        {
            return "stale";
        }

        if (_windowSize() != (g.Width, g.Height))
        {
            return "resized";
        }

        // 사용자가 보지 못한 팝업을 대신 눌러 닫거나 확인하지 않게 한다
        var stable = OriginStable(out var info);
        return g.Decide(Decor, _openUptime, _hit, stable, info)
            ?? (_top is not null && OverlayLayer.GetOverlayLayer(_top)?.Children.Count > 0 ? "overlay" : null);
    }

    /// <summary>버림 사유 한 줄. 시험 도구가 replay result 의 drop 낱말로 ②(설계상 버림)를 판별하므로 모든 버림이 이 형식을 쓴다.</summary>
    private static void LogDrop(string reason, bool final = true) =>
        Console.WriteLine(final ? $"[startup] replay result=dropped reason={reason}" : $"[startup] touch discarded reason={reason}"); // 뒤에 최종 결과가 이어지는 중간 버림은 result 줄로 쓰지 않는다(도구가 첫 result 줄만 읽는다)

    private static void Add(StartupGesture g, MotionEvent e) => g.Add(e);

    private static void Reject(StartupGesture g, string reason) => g.Reject(reason);

    private static void Release(ref StartupGesture? g)
    {
        g?.Recycle();
        g = null;
    }
}

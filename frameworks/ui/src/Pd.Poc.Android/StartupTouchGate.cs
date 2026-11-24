// 기동 터치 게이트 — 앱 창에 도착했지만 화면이 준비되기 전인 터치를 받아 두었다가, 준비된 뒤 안전한 탭 하나만 다시 보낸다
using Android.Content;
using Android.OS;
using Android.Views;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Pd.Poc.Shell.Diagnostics.Remote;

namespace Pd.Poc.Droid;

/// <summary>
/// 범위: Activity.DispatchTouchEvent 에 도착한 터치만 다룬다. 앱 창이 화면에 올라오기 전(시스템 스플래시 구간) 터치는 앱에 오지 않아 받을 수 없다.
/// 좌표 판정: 다시 보낼 때 일반 입력 경로를 타므로 그 시점 화면에서 대상을 찾는다. 창 크기가 바뀌었거나 팝업·오버레이가 떠 있으면 버린다. 창 좌표 기준이 바뀌었으면 누를 곳(A)과 손가락이 정착한 화면에서 가리키는 곳(B)이 같은 입력 처리 컨트롤이면 그대로 보내고, 다르면 창 위치가 안정이고 B 에 입력 컨트롤이 있을 때만 B 로 옮겨 보낸다(StartupHitTest.Judge, retarget_b).
/// 탭만 보낸다: 끌기·여러 손가락·길게 누름은 보이지 않는 화면을 상대로 한 동작이라 버리고, 연타는 마지막 하나만 보내 두 번 실행을 막는다.
/// </summary>
internal sealed partial class StartupTouchGate
{
    private const int TapMaxAgeMs = 3000; // 기준 측정 최악 첫 프레임 2648ms 보다 넉넉하고, 이보다 오래된 탭은 사용자가 이미 다른 생각을 한 것으로 본다
    private const int OpenTimeoutMs = 8000; // 준비 신호가 끝내 오지 않아도 입력을 막지 않게 하는 상한
    private const int StableFrames = 2; // 조건이 2회 연속 맞으면 그 사이 프레임 하나가 렌더링된 뒤다
    private const int MaxDirectLogs = 3;
    private const int OriginWaitMs = 300; // 준비됐는데 창 위치가 움직이는 중이면 이만큼까지 열기를 미룬다. 넘으면 열되 B 로 옮겨 보내기는 하지 않는다(origin_unstable)

    private readonly Context _context;
    private readonly Func<MotionEvent, bool> _dispatch;
    private readonly Func<(int Width, int Height)> _windowSize;
    private readonly int _touchSlop;
    private readonly int _longPressMs;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _onReady;
    private readonly StartupHitTest _hit;
    private StartupGesture? _current;
    private StartupGesture? _lastTap;
    private TopLevel? _top;
    private Control? _view;
    private bool _open;
    private bool _swallowUntilUp;
    private bool _renderMarked;
    private int _stable;
    private int _directLogs;
    private long _openUptime;
    private long _unstableSince = -1;
    private (int X, int Y)? _prevOrigin; // 앞 프레임의 창 위치
    private (int X, int Y)? _frameOrigin; // 이번(마지막으로 잰) 프레임의 창 위치
    private (int X, int Y)? _retargetOrigin; // 이 창 위치 기준 B 로 옮겨 보내는 중인 동작(뗄 때까지)

    public StartupTouchGate(Context context, Func<MotionEvent, bool> dispatch, Func<(int Width, int Height)> windowSize, Action onReady)
    {
        _context = context;
        _dispatch = dispatch;
        _windowSize = windowSize;
        _onReady = onReady;
        _hit = new StartupHitTest(context as Android.App.Activity);
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16;
        _longPressMs = ViewConfiguration.LongPressTimeout;
    }

    /// <summary>화면 준비 감시를 시작한다. 뷰를 못 찾으면 받아 둘 근거가 없으므로 바로 연다.</summary>
    public void Start(Control? view)
    {
        if (view is null)
        {
            Open("no_view");
            return;
        }

        _view = view;
        _handler.PostDelayed(() => Open("timeout"), OpenTimeoutMs);
        if (TopLevel.GetTopLevel(view) is { } top)
        {
            Watch(top);
            return;
        }

        // 내용이 아직 시각 트리에 붙기 전이면 붙은 뒤 시작한다. 끝내 안 붙어도 위 상한이 게이트를 연다.
        EventHandler<Avalonia.VisualTreeAttachmentEventArgs>? attached = null;
        attached = (_, _) =>
        {
            view.AttachedToVisualTree -= attached;
            if (TopLevel.GetTopLevel(view) is { } t) { Watch(t); }
        };
        view.AttachedToVisualTree += attached;
    }

    private void Watch(TopLevel top)
    {
        _top = top;
        _hit.Top = top;
        top.RequestAnimationFrame(OnFrame);
    }

    /// <summary>터치를 게이트가 가져가면 true. false 면 호출한 쪽이 평소대로 보낸다.</summary>
    public bool Intercept(MotionEvent e)
    {
        if (!_open)
        {
            Buffer(e);
            return true;
        }

        if (_retargetOrigin is { } ro)
        {
            if (e.ActionMasked != MotionEventActions.Down)
            {
                FollowRetarget(e, ro);
                return true;
            }

            _retargetOrigin = null; // 앞 동작이 끝나지 않은 채 새 동작이 왔다. 새 동작은 평소대로 판정한다
        }

        if (!_swallowUntilUp)
        {
            // 열리기 전과 열린 뒤 3초(TapMaxAgeMs) 안의 Down 은 재전송과 같이 판정, 어긋나면 나머지까지 삼킨다. 기동 창 애니메이션이 열린 뒤까지 남는다(D-REPLAY)
            if (e.ActionMasked == MotionEventActions.Down && e.DownTime < _openUptime + TapMaxAgeMs)
            {
                var stable = OriginStable(out var info);
                if (StartupGesture.DirectDecision(e, Decor, _openUptime, _hit, stable, info, out var retarget) is { } directDrop)
                {
                    LogDrop("direct_" + directDrop); // 직접 도착한 Down 을 버린 사유도 replay result 한 줄로 남긴다
                    return _swallowUntilUp = true;
                }

                if (retarget is { } o)
                {
                    // 손가락이 정착한 화면에서 가리키는 곳(B)으로 옮겨 보낸다. 같은 동작의 나머지도 뗄 때까지 같은 기준으로 옮긴다
                    _directLogs++;
                    Console.WriteLine($"[startup] touch direct +{StartupMarks.ElapsedMs}ms target={_hit.Describe(e.RawX - o.X, e.RawY - o.Y)} retarget_b");
                    _retargetOrigin = o;
                    DispatchAt(e, e.RawX, e.RawY, o);
                    return true;
                }
            }

            // 게이트가 열린 뒤 처음 몇 번은 대상까지 남긴다. 열리기 직전 들어와 메인 스레드에서 밀린 탭과 구분하기 위해서다(기록 전용).
            if (_directLogs < MaxDirectLogs && e.ActionMasked == MotionEventActions.Down)
            {
                _directLogs++;
                Console.WriteLine($"[startup] touch direct +{StartupMarks.ElapsedMs}ms target={_hit.Describe(e.GetX(), e.GetY())}");
            }

            return false;
        }

        var action = e.ActionMasked;
        if (action == MotionEventActions.Down)
        {
            _swallowUntilUp = false;
            return false;
        }

        _swallowUntilUp = action is not (MotionEventActions.Up or MotionEventActions.Cancel);
        return true;
    }

    /// <summary>화면이 내려가면 받아 둔 터치는 의미가 없어 버린다. 게이트 상태는 유지한다.</summary>
    public void DiscardPending()
    {
        if (_lastTap is not null || _current is not null)
        {
            LogDrop("stopped");
        }

        Release(ref _lastTap);
        Release(ref _current);
        _retargetOrigin = null;
    }

    private Android.Views.View? Decor => (_context as Android.App.Activity)?.Window?.DecorView;

    private void OnFrame(TimeSpan _)
    {
        if (_open || _top is null || _view is null)
        {
            return;
        }

        SampleOrigin();

        // 표면이 생기기 전 크기는 1 이하다. 복원이 끝나기 전 화면은 곧 바뀌므로 준비로 보지 않는다.
        var hasSurface = _view.Bounds.Width > 1 && _view.Bounds.Height > 1;
        if (hasSurface && !_renderMarked)
        {
            _renderMarked = true;
            StartupMarks.Mark("first_layout_frame");
        }

        var ready = hasSurface && _view.IsArrangeValid && RemoteCommands.StartupRestoreDone == true;
        if (ready && _prevOrigin is { } before && _frameOrigin != before)
        {
            // 창 위치가 프레임 사이에 바뀌는 중이면 짧게 기다린다(잰 값이 하나뿐인 첫 프레임은 기다리지 않아 열리는 시점이 늦어지지 않는다). 상한을 넘으면 열고, 옮겨 보내기는 판정에서 origin_unstable 로 막힌다
            var now = SystemClock.UptimeMillis();
            _unstableSince = _unstableSince < 0 ? now : _unstableSince;
            ready = now - _unstableSince >= OriginWaitMs;
        }

        _stable = ready ? _stable + 1 : 0;
        if (_stable >= StableFrames)
        {
            _onReady();
            Open("ready");
            return;
        }

        _top.RequestAnimationFrame(OnFrame);
    }

    private void Open(string reason)
    {
        if (_open)
        {
            return;
        }

        _open = true;
        _openUptime = SystemClock.UptimeMillis(); // 터치 DownTime 과 같은 시계
        _handler.RemoveCallbacksAndMessages(null);
        Console.WriteLine($"[startup] gate open reason={reason} +{StartupMarks.ElapsedMs}ms");

        // 누르고 있는 손가락이 가장 최근 의도다. 있으면 앞선 탭은 보내지 않는다.
        var pressed = _current is not null;
        if (pressed && _lastTap is not null) { LogDrop("multi_tap_superseded", final: false); } // 누르고 있으면 앞선 탭은 보내지 않는다(설계)
        var g = _current ?? _lastTap;
        if (g is not null)
        {
            var age = SystemClock.UptimeMillis() - (pressed ? g.DownTime : g.UpTime);
            if ((g.Rejected ?? DropReason(g, age)) is { } drop)
            {
                _swallowUntilUp = pressed; // 시작을 보내지 않은 동작의 나머지가 뷰로 가지 않게 한다
                Console.WriteLine($"[startup] replay result=dropped reason={drop} age={age}ms");
            }
            else if (g.RetargetOrigin is { } o)
            {
                // 받은 창 좌표(A)가 아니라 정착한 화면에서 손가락이 가리키는 곳(B)으로 옮겨 보낸다. 누르고 있으면 나머지도 뗄 때까지 옮긴다
                Console.WriteLine($"[startup] replay target={_hit.Describe(g.Raws[0].X - o.X, g.Raws[0].Y - o.Y)} retarget_b");
                for (var i = 0; i < g.Events.Count; i++)
                {
                    DispatchAt(g.Events[i], g.Raws[i].X, g.Raws[i].Y, o);
                }

                _retargetOrigin = pressed ? o : null;
                Console.WriteLine($"[startup] replay result={(pressed ? "sent_pressed_retarget_b" : "sent_retarget_b")} age={age}ms origin=({o.X},{o.Y})");
            }
            else
            {
                Console.WriteLine($"[startup] replay target={_hit.Describe(g.DownX, g.DownY)}");
                g.Events.ForEach(e => _dispatch(e));
                Console.WriteLine($"[startup] replay result={(pressed ? "sent_pressed" : "sent")} age={age}ms");
            }
        }

        Release(ref _lastTap);
        Release(ref _current);
        if (_top is not null)
        {
            _top.RequestAnimationFrame(OnOriginFrame); // 열린 뒤 3초 동안의 직접 Down 판정에 쓸 창 위치를 계속 잰다
        }
    }

    /// <summary>열린 뒤에도 직접 Down 판정 기간(TapMaxAgeMs) 동안 프레임마다 창 위치를 잰다.</summary>
    private void OnOriginFrame(TimeSpan _)
    {
        if (_top is null || SystemClock.UptimeMillis() > _openUptime + TapMaxAgeMs)
        {
            return;
        }

        SampleOrigin();
        _top.RequestAnimationFrame(OnOriginFrame);
    }

    private void SampleOrigin()
    {
        var o = new int[2];
        Decor?.GetLocationOnScreen(o);
        _prevOrigin = _frameOrigin;
        _frameOrigin = (o[0], o[1]);
    }

    /// <summary>창 위치가 앞 프레임·이번 프레임·지금 모두 같으면 안정이다. 잰 값이 모자라면(게이트를 상한으로 연 경우 등) 안정이 아니다. info 는 coord 줄 뒤에 붙일 값이다.</summary>
    private bool OriginStable(out string info)
    {
        var o = new int[2];
        Decor?.GetLocationOnScreen(o);
        var now = (o[0], o[1]);
        var stable = Decor is not null && _prevOrigin is { } p && _frameOrigin is { } f && p == f && f == now;
        info = $" origin_prev={(_prevOrigin is { } q ? $"({q.Item1},{q.Item2})" : "none")} origin_stable={(stable ? 1 : 0)}";
        return stable;
    }
}

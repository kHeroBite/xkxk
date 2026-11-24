// 기동 터치 게이트가 받아 둔 동작 하나 — 복사한 터치 이벤트와 탭 판정에 쓰는 시작 위치·시각·창 크기·화면 좌표 차이를 담는다
using Android.Views;

namespace Pd.Poc.Droid;

internal sealed class StartupGesture
{
    private const int MaxEvents = 64;
    private const float OffsetTolerancePx = 1f; // 화면 좌표와 창 좌표의 차이는 정수 창 위치라 소수 오차만 허용한다

    public readonly List<MotionEvent> Events = new();

    /// <summary>각 이벤트를 받을 때의 화면 좌표. B 로 옮겨 보낼 때 이벤트마다 '화면 좌표 - 지금 창 위치' 를 쓴다(복사본의 Raw 값에 기대지 않는다).</summary>
    public readonly List<(float X, float Y)> Raws = new();

    public float DownX;
    public float DownY;
    public long DownTime;
    public long UpTime;
    public int Width;
    public int Height;
    public string? Rejected;

    /// <summary>받을 때의 '화면 좌표 - 창 좌표'. 이벤트의 창 좌표는 그 순간 창 상태로 굳어 있어, 지금 창 위치와 다르면 다시 보낼 때 다른 곳을 누른다.</summary>
    public float OffsetX { get; private set; }

    public float OffsetY { get; private set; }

    /// <summary>한 동작 안에서 창 좌표 기준이 바뀌었다. 바로 버리지 않고 게이트가 열린 뒤 첫·마지막 이벤트로 판정한다.</summary>
    public bool Shifted { get; private set; }

    /// <summary>판정 결과 B 로 옮겨 보내야 하면 그때 쓴 창 위치. null 이면 받은 그대로 보낸다.</summary>
    public (int X, int Y)? RetargetOrigin { get; private set; }

    private float _lastX;
    private float _lastY;
    private float _lastOffsetX;
    private float _lastOffsetY;

    public void Reject(string reason) => Rejected ??= reason;

    public void Add(MotionEvent e)
    {
        if (Events.Count >= MaxEvents)
        {
            Reject("too_many_events");
            return;
        }

        var (dx, dy) = OffsetOf(e);
        if (Events.Count == 0)
        {
            OffsetX = dx;
            OffsetY = dy;
        }
        else if (Rejected is null && !Shifted && !Matches(OffsetX, OffsetY, dx, dy))
        {
            // 한 동작 안에서 창 좌표 기준이 바뀌었다. origin 자리에 바뀐 뒤 값을 남긴다(게이트 전이라 gate=-1)
            LogCoord("shift", DownX, DownY, OffsetX, OffsetY, dx, dy, DownTime, -1, "");
            Shifted = true;
        }

        (_lastX, _lastY, _lastOffsetX, _lastOffsetY) = (e.GetX(), e.GetY(), dx, dy);
        Events.Add(MotionEvent.Obtain(e)!); // 원본은 시스템이 재사용하므로 복사해 둔다
        Raws.Add((e.RawX, e.RawY));
    }

    /// <summary>
    /// 다시 보낼지 판정한다. null 이면 보낸다(RetargetOrigin 이 있으면 B 로 옮겨서). 받을 때의 창 좌표 기준이 지금 창 위치와 다르거나(Fold5 펼침 D-REPLAY)
    /// 한 동작 안에서 기준이 바뀌었으면 A(받은 창 좌표)·B(화면 좌표 - 지금 창 위치)를 누름·뗌 두 쌍으로 StartupHitTest.Judge 에 맡긴다.
    /// originInfo 는 게이트가 잰 창 위치 안정 정보(coord 줄 뒤에 붙는다)다.
    /// </summary>
    public string? Decide(View? decor, long gate, StartupHitTest? hit, bool originStable, string originInfo)
    {
        RetargetOrigin = null;
        var origin = new int[2];
        decor?.GetLocationOnScreen(origin);
        if (decor is null)
        {
            LogCoord("replay", DownX, DownY, OffsetX, OffsetY, origin[0], origin[1], DownTime, gate, "");
            return "no_window";
        }

        var moved = !Matches(OffsetX, OffsetY, origin[0], origin[1]);
        if (!moved && !Shifted)
        {
            LogCoord("replay", DownX, DownY, OffsetX, OffsetY, origin[0], origin[1], DownTime, gate, "");
            return null;
        }

        var aDown = (DownX, DownY);
        var bDown = (DownX + OffsetX - origin[0], DownY + OffsetY - origin[1]);
        var aLast = (_lastX, _lastY);
        var bLast = (_lastX + _lastOffsetX - origin[0], _lastY + _lastOffsetY - origin[1]);
        var marks = "";
        var retarget = false;
        // 화면이 없어 판정할 수 없으면 예전 기준: 창이 움직였으면 window_moved, 한 동작 안에서만 바뀌었으면 coord_shift
        var drop = hit is null
            ? (moved ? "window_moved" : "coord_shift")
            : hit.Judge(aDown, bDown, aLast, bLast, originStable, moved ? "window_moved" : "coord_shift", out marks, out retarget);
        if (Shifted)
        {
            // 기준이 바뀐 동작은 예전처럼 shift 줄을 남긴다(origin 자리는 마지막 이벤트 값)
            LogCoord("shift", DownX, DownY, OffsetX, OffsetY, _lastOffsetX, _lastOffsetY, DownTime, gate, "");
        }

        LogCoord("replay", DownX, DownY, OffsetX, OffsetY, origin[0], origin[1], DownTime, gate, marks + originInfo);
        if (drop is null && retarget)
        {
            RetargetOrigin = (origin[0], origin[1]);
        }

        return drop;
    }

    /// <summary>
    /// 기동 검사 기간(게이트 전 ~ 열린 뒤 3초)에 직접 도착한 Down 의 판정. 재전송과 같은 기준이다.
    /// null 이면 보낸다. retargetOrigin 이 있으면 이 Down 과 같은 동작의 나머지를 B 로 옮겨 보내야 한다. 어긋나면 이유를 남기고 돌려준다.
    /// </summary>
    public static string? DirectDecision(MotionEvent e, View? decor, long gate, StartupHitTest? hit, bool originStable, string originInfo, out (int X, int Y)? retargetOrigin)
    {
        retargetOrigin = null;
        var (dx, dy) = OffsetOf(e);
        var origin = new int[2];
        decor?.GetLocationOnScreen(origin);
        string? drop;
        var marks = "";
        if (decor is null)
        {
            drop = "no_window";
        }
        else if (Matches(dx, dy, origin[0], origin[1]))
        {
            drop = null;
        }
        else
        {
            var a = (e.GetX(), e.GetY());
            var b = (e.RawX - origin[0], e.RawY - origin[1]);
            var retarget = false;
            drop = hit is null ? "window_moved" : hit.Judge(a, b, a, b, originStable, "window_moved", out marks, out retarget);
            marks += originInfo;
            if (drop is null && retarget)
            {
                retargetOrigin = (origin[0], origin[1]);
            }
        }

        LogCoord("direct", e.GetX(), e.GetY(), dx, dy, origin[0], origin[1], e.DownTime, gate, marks);
        if (drop is not null)
        {
            Console.WriteLine($"[startup] touch direct dropped reason={drop}");
        }

        return drop;
    }

    private static (float X, float Y) OffsetOf(MotionEvent e) => (e.RawX - e.GetX(), e.RawY - e.GetY());

    /// <summary>앞 세 값(down·offset·origin)은 이전 형식 그대로 두고 뒤에 경로와 시각(둘 다 uptime ms, gate=-1 은 게이트 전), 같은 컨트롤 판정을 했으면 target_a·target_b·decision·origin_prev·origin_stable 을 붙인다.</summary>
    private static void LogCoord(string path, float x, float y, float dx, float dy, float ox, float oy, long downTime, long gate, string marks) =>
        Console.WriteLine($"[startup] touch coord down=({x:F0},{y:F0}) offset=({dx:F0},{dy:F0}) origin=({ox:F0},{oy:F0}) path={path} down_time={downTime} gate={gate}{marks}");

    private static bool Matches(float ax, float ay, float bx, float by) =>
        Math.Abs(ax - bx) <= OffsetTolerancePx && Math.Abs(ay - by) <= OffsetTolerancePx;

    public void Recycle()
    {
        foreach (var e in Events)
        {
            e.Recycle();
        }
    }
}

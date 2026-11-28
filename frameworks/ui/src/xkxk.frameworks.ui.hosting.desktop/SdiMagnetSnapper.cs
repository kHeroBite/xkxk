// SDI 자석 끌기 1회분 계산 — 끌기 시작 때 캐시한 후보(작업영역별로 옮긴 다른 창 사각형)와 이력(붙기 = 범위, 떼기 = 범위×2)으로 원래 위치(raw)를 붙인 위치로 바꾼다(순수 계산, Win32 의존 없음)
using Avalonia;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// SdiMagnetHook 이 WM_ENTERSIZEMOVE 때 만들고 WM_MOVING·WM_SIZING 마다 Apply 를 부른다. 프레임마다 OS 조회·할당이 없다(Q8 maxMs).
/// 이력 규칙(B-2): 가장자리(왼·위·오른·아래)별로 '붙음' 상태를 둔다. 떨어져 있을 때는 범위 안에 들어와야 붙고,
/// 붙어 있을 때는 raw 와 붙은 위치의 차이가 떼기 임계(범위×ReleaseFactor)를 넘어야 떨어진다. 떨어진 가장자리는 다시 범위 안으로 들어와야 붙는다.
/// 계산 자체는 MDI 와 같은 MagnetSnap(간격 0)을 쓴다.
/// </summary>
internal sealed class SdiMagnetSnapper
{
    /// <summary>떼기 임계 배수. 붙기 범위와 같으면 경계에서 붙었다 떨어졌다를 반복하므로 2배로 둔다(team-lead 지시 — 기본 24px 이면 48px 넘게 끌 때 떨어짐).</summary>
    public const double ReleaseFactor = 2.0;

    private readonly Rect[] _works;
    private readonly Rect[][] _others; // 작업영역별로 원점을 옮긴 후보 사각형
    private readonly double _range;
    private readonly bool[] _held = new bool[4]; // 0 왼 · 1 위 · 2 오른 · 3 아래

    /// <param name="works">모니터 작업영역(화면 픽셀).</param>
    /// <param name="others">다른 SDI·메인 창의 보이는 프레임(화면 픽셀).</param>
    /// <param name="range">붙기 범위(화면 픽셀).</param>
    public SdiMagnetSnapper(IReadOnlyList<Rect> works, IReadOnlyList<Rect> others, double range)
    {
        _works = works.ToArray();
        _range = range;
        _others = new Rect[_works.Length][];
        for (var i = 0; i < _works.Length; i++)
        {
            var origin = _works[i].Position;
            var shifted = new Rect[others.Count];
            for (var j = 0; j < others.Count; j++)
            {
                shifted[j] = others[j].Translate(-origin);
            }

            _others[i] = shifted;
        }
    }

    /// <summary>후보 수(로그용).</summary>
    public int TargetCount => _others.Length == 0 ? 0 : _others[0].Length;

    /// <summary>이력 상태를 비운다(예열 계산 뒤).</summary>
    public void Reset() => Array.Clear(_held);

    /// <summary>raw(보이는 프레임, 화면 픽셀) → 붙인 사각형. edge 0 = 이동(창 전체), 1~8 = WMSZ_* 크기 조절 가장자리(끄는 변만).</summary>
    public Rect Apply(Rect raw, int edge)
    {
        var i = NearestWork(raw);
        if (i < 0 || _range <= 0)
        {
            return raw;
        }

        var work = _works[i];
        var r = raw.Translate(-work.Position);
        var area = work.Size;
        var hold = _range * ReleaseFactor;
        Rect s, h;
        if (edge == 0)
        {
            s = MagnetSnap.SnapMove(r, _range, area, _others[i]);
            h = MagnetSnap.SnapMove(r, hold, area, _others[i]);
        }
        else
        {
            bool left = edge is 1 or 4 or 7, top = edge is 3 or 4 or 5, right = edge is 2 or 5 or 8, bottom = edge is 6 or 7 or 8;
            s = MagnetSnap.SnapResize(r, _range, area, _others[i], left, top, right, bottom);
            h = MagnetSnap.SnapResize(r, hold, area, _others[i], left, top, right, bottom);
        }

        var dl = Pick(0, s.Left - r.Left, h.Left - r.Left);
        var dt = Pick(1, s.Top - r.Top, h.Top - r.Top);
        var dr = Pick(2, s.Right - r.Right, h.Right - r.Right);
        var db = Pick(3, s.Bottom - r.Bottom, h.Bottom - r.Bottom);
        if (edge == 0)
        {
            // 이동은 크기를 지킨다: 왼·오른(위·아래)은 같은 보정이지만 상태가 갈리면 붙어 있는 쪽을 따른다.
            dl = dr = dl != 0 ? dl : dr;
            dt = db = dt != 0 ? dt : db;
        }

        return new Rect(raw.Left + dl, raw.Top + dt, Math.Max(0, raw.Width + dr - dl), Math.Max(0, raw.Height + db - dt));
    }

    // 붙어 있으면 떼기 임계(hold) 결과, 떨어져 있으면 붙기 범위(snap) 결과를 쓴다. 보정 0 = 떨어짐(정확히 맞닿아 있으면 보정이 없어 결과는 같다).
    private double Pick(int index, double snap, double hold)
    {
        var d = _held[index] ? hold : snap;
        _held[index] = d != 0;
        return d;
    }

    // 사각형과 가장 많이 겹치는 작업영역(겹침이 없으면 중심이 가장 가까운 곳).
    private int NearestWork(Rect r)
    {
        var best = -1;
        var bestScore = double.NegativeInfinity;
        for (var i = 0; i < _works.Length; i++)
        {
            var w = _works[i];
            var ix = Math.Min(r.Right, w.Right) - Math.Max(r.Left, w.Left);
            var iy = Math.Min(r.Bottom, w.Bottom) - Math.Max(r.Top, w.Top);
            var cx = r.Center.X - w.Center.X;
            var cy = r.Center.Y - w.Center.Y;
            var score = ix > 0 && iy > 0 ? ix * iy : -Math.Sqrt(cx * cx + cy * cy);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }
}

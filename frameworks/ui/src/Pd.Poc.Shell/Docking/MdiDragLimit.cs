// MDI 창 끌기·크기 조절 경계와 복구 판정의 순수 계산 — 창 전체가 아니라 제목 줄의 끌 수 있는 구간이 캔버스에 남게 한다(화면·창 조작 없음)
using System;
using Avalonia;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 창 안에서 제목 줄을 끌 수 있는 구간(DIP). Left = 창 왼쪽에서 구간 시작까지, RightInset = 구간 끝에서 창 오른쪽까지, TitleBottom = 창 위에서 제목 줄 아래까지.
/// 로고·캡션 버튼·기능 버튼은 눌러도 끌리지 않으므로 구간에서 뺀다. 값은 MdiGrabZoneReader 가 실제 배치에서 잰다.
/// Foldable = RightInset 중 기능 버튼 줄이 차지한 폭(접을 수 있는 폭). 기능 버튼 줄은 접힌 상태에서도 RightInset 에 포함한 값(접히지 않은 전체 폭)으로 준다. 기본 0 = 접지 않음.
/// </summary>
public readonly record struct MdiGrabZone(double Left, double RightInset, double TitleBottom, double Foldable = 0)
{
    /// <summary>템플릿 적용 전이거나 잴 수 없을 때. 제목 줄 전체를 끌 수 있다고 본다.</summary>
    public static MdiGrabZone Whole { get; } = new(0, 0, PlacementRecovery.TitleStrip);
}

/// <summary>
/// 사용자 결정(2026-11-08): 10%·55% 같은 고정 잔존 비율을 버리고 다시 잡을 곳(MinGrab)만 남긴다. 버튼만 남는 상태는 금지한다.
/// 끌기 한계: 가로는 끌 수 있는 구간 MinGrab 이 보이게 하고, 왼쪽으로 밀어 기능 버튼 줄이 잡을 곳을 가리면 그 줄을 접어(Foldable) 그만큼 더 밀 수 있다.
/// 세로는 제목 줄 전체가 보인다(위로 사라지지 않음, 아래는 제목 줄까지). 접기·펴기 판정은 ShouldFoldStrip 이 하고 화면 적용은 MdiStripFold 가 한다.
/// 2026-11-10 사용자 요청으로 잔존 비율(CurrentMinVisibleRatio)을 다시 두되 잡을 곳 규칙을 하한으로 쓴다(가로·아래만, 위는 0 정지).
/// 복구 판정(IsGrabbable)은 같은 구간을 재되 문턱을 MinRecoverGrab 으로 낮춘다. 접힌 창은 접힌 구간(캡션만 막힘)으로 판정한다.
/// </summary>
public static class MdiDragLimit
{
    // 공개 설정값 한 곳(초기 제시값, 옵션). 실제 값은 ConfigureMinGrab 공급자(UiSettings.MdiMinGrabDip)가 정한다.
    /// <summary>공급자가 없을 때 쓰는 기본 잡을 곳 폭(DIP). 좁은 창에서도 포인터로 잡을 수 있는 크기다.</summary>
    public static double MinGrab { get; set; } = 50;

    private static Func<double>? _minGrabProvider;
    private static double? _fixedMinGrab;

    /// <summary>잡을 곳 폭 공급자를 연결한다(null = 해제). Docking 은 설정 레이어를 직접 참조하지 않고 이 함수만 받는다.</summary>
    public static void ConfigureMinGrab(Func<double>? provider) => _minGrabProvider = provider;

    /// <summary>지금 적용되는 잡을 곳 폭(DIP). 고정값(UseFixedMinGrab) → 공급자 → MinGrab 기본값 순서다.</summary>
    public static double CurrentMinGrab => _fixedMinGrab ?? _minGrabProvider?.Invoke() ?? MinGrab;

    /// <summary>
    /// 시험용: 살아 있는 동안 공급자를 무시하고 CurrentMinGrab 이 value 를 돌려준다. Dispose 하면 원복한다.
    /// 이미 고정 중이면(중첩) 예외 없이 바깥 값을 유지하고, 안쪽 Dispose 는 아무것도 바꾸지 않는다.
    /// </summary>
    public static IDisposable UseFixedMinGrab(double value)
    {
        if (_fixedMinGrab.HasValue)
        {
            return new FixedMinGrabScope(owner: false);
        }

        _fixedMinGrab = value;
        return new FixedMinGrabScope(owner: true);
    }

    private sealed class FixedMinGrabScope : IDisposable
    {
        private readonly bool owner;
        private bool _disposed;

        public FixedMinGrabScope(bool owner) => this.owner = owner;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (owner)
            {
                _fixedMinGrab = null;
            }
        }
    }

    // 잔존 비율(2026-11-10 사용자 요청, 정본 01 §4.3): 축마다 max(ceil(크기 × 비율), 잡을 곳 규칙) 만큼 남긴다. 0 = 비율 끔(11-08 규칙 그대로).
    /// <summary>잔존 비율 상한(0.9 = 창을 최소 10% 는 밀 수 있게 한다).</summary>
    public const double MaxMinVisibleRatio = 0.9;

    private static Func<double>? _minVisibleRatioProvider;
    private static double? _fixedMinVisibleRatio;

    /// <summary>잔존 비율 공급자를 연결한다(0~1 소수, null = 해제). Docking 은 설정 레이어를 직접 참조하지 않고 이 함수만 받는다.</summary>
    public static void ConfigureMinVisibleRatio(Func<double>? provider) => _minVisibleRatioProvider = provider;

    /// <summary>지금 적용되는 잔존 비율(0~0.9). 고정값(UseFixedMinVisibleRatio) → 공급자 → 0(끔) 순서이며 범위 밖·NaN 은 잘라 맞춘다.</summary>
    public static double CurrentMinVisibleRatio
    {
        get
        {
            var p = _fixedMinVisibleRatio ?? _minVisibleRatioProvider?.Invoke() ?? 0;
            return double.IsNaN(p) ? 0 : Math.Clamp(p, 0, MaxMinVisibleRatio);
        }
    }

    /// <summary>시험용: 살아 있는 동안 CurrentMinVisibleRatio 가 value 를 돌려준다. 중첩·Dispose 동작은 UseFixedMinGrab 과 같다.</summary>
    public static IDisposable UseFixedMinVisibleRatio(double value)
    {
        if (_fixedMinVisibleRatio.HasValue)
        {
            return new FixedRatioScope(owner: false);
        }

        _fixedMinVisibleRatio = value;
        return new FixedRatioScope(owner: true);
    }

    private sealed class FixedRatioScope : IDisposable
    {
        private readonly bool owner;
        private bool _disposed;

        public FixedRatioScope(bool owner) => this.owner = owner;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (owner)
            {
                _fixedMinVisibleRatio = null;
            }
        }
    }

    /// <summary>크기(DIP)에 잔존 비율을 곱해 올림한 값. 비율 0 이면 0. 부동소수 오차(0.07×100=7.000…1)로 1 커지지 않게 1e-9 를 뺀다.</summary>
    private static double RatioKeep(double size)
    {
        var p = CurrentMinVisibleRatio;
        return p > 0 ? Math.Ceiling(size * p - 1e-9) : 0;
    }

    /// <summary>왼쪽으로 밀어 기능 버튼 줄이 잡을 곳을 가리면 그 줄을 접는가.</summary>
    public static bool FoldStrip { get; set; } = true;

    /// <summary>접힌 줄을 다시 펴려면 접는 문턱보다 이만큼(DIP) 더 보여야 한다(경계에서 깜박이지 않게).</summary>
    public static double FoldHysteresis { get; set; } = 8;

    /// <summary>복구 판정 문턱: 끌 수 있는 구간이 이보다 적게 보이면 잡을 수 없다고 본다(DIP, 세로 PlacementRecovery.MinVisibleHeight 와 같은 값).</summary>
    public const double MinRecoverGrab = 16;

    /// <summary>창 왼쪽이 캔버스 밖으로 나갈 때 오른쪽에 남길 폭(구간은 오른쪽 끝부터 보인다).</summary>
    private static double KeepWhenLeftOut(double width, MdiGrabZone zone) =>
        Math.Min(width, Math.Max(RatioKeep(width), KeepFolded(width, zone)));

    private static double KeepFolded(double width, MdiGrabZone zone)
    {
        var f = Folded(zone);
        return Keep(width, f.RightInset, GrabWidth(width, f));
    }

    /// <summary>창 오른쪽이 캔버스 밖으로 나갈 때 왼쪽에 남길 폭(구간은 왼쪽 끝부터 보인다).</summary>
    private static double KeepWhenRightOut(double width, MdiGrabZone zone) =>
        Math.Min(width, Math.Max(RatioKeep(width), Keep(width, zone.Left, GrabWidth(width, zone))));

    /// <summary>창 아래가 캔버스 밖으로 나갈 때 위에 남길 높이. 제목 줄 전체가 하한이고(비율 0 이면 지금 규칙 그대로), 비율이 더 크면 비율만큼 남긴다.</summary>
    private static double KeepWhenBottomOut(double height, MdiGrabZone zone) =>
        Math.Max(zone.TitleBottom, Math.Min(height, RatioKeep(height)));

    private static double GrabWidth(double width, MdiGrabZone zone) => Math.Max(0, width - zone.Left - zone.RightInset);

    private static double Keep(double width, double blocked, double grab) =>
        Math.Min(width, blocked + Math.Min(CurrentMinGrab, grab));

    /// <summary>접힌 상태의 구간: 기능 버튼 줄 폭을 오른쪽 막힌 폭에서 뺀다. 접지 않으면 그대로다.</summary>
    private static MdiGrabZone Folded(MdiGrabZone zone) =>
        FoldStrip && zone.Foldable > 0 ? zone with { RightInset = Math.Max(0, zone.RightInset - zone.Foldable), Foldable = 0 } : zone;

    /// <summary>
    /// 기능 버튼 줄을 접어야 하는가. 왼쪽이 캔버스 밖일 때만 대상이고, 화면에 보이는 오른쪽 부분이 (막힌 폭 + MinGrab) 보다 적으면 접는다.
    /// 이미 접혀 있으면 FoldHysteresis 만큼 더 보일 때까지 접은 채 둔다.
    /// </summary>
    public static bool ShouldFoldStrip(Rect r, Size area, MdiGrabZone zone, bool folded)
    {
        if (!FoldStrip || zone.Foldable <= 0 || r.X >= 0)
        {
            return false;
        }

        var need = zone.RightInset + CurrentMinGrab;
        return r.Right < (folded ? need + FoldHysteresis : need);
    }

    /// <summary>이동 결과를 끌기 한계 안으로 넣는다. 크기는 그대로다.</summary>
    public static Rect ClampMove(Rect r, Size area, MdiGrabZone zone) =>
        new(ClampX(r.X, r.Width, area, zone), ClampY(r.Y, r.Height, area, zone), r.Width, r.Height);

    /// <summary>
    /// 크기 조절 결과를 같은 한계 안으로 넣는다. 끄는 가장자리만 고치고 반대쪽 가장자리는 그대로 둔다.
    /// 오른쪽·아래쪽 가장자리는 캔버스 밖으로 늘어날 수 있다. 위쪽은 제목 줄이 캔버스 안에 남는 범위에서만 움직인다.
    /// </summary>
    public static Rect ClampResize(Rect r, Size area, MdiGrabZone zone, bool left, bool top, bool right)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        if (left)
        {
            var maxX = area.Width - KeepWhenRightOut(w, zone);
            if (x > maxX)
            {
                w += x - maxX;
                x = maxX;
            }
        }
        else if (right && x < 0)
        {
            w = Math.Max(w, KeepWhenLeftOut(w, zone) - x); // 왼쪽이 밖이면 오른쪽 가장자리를 끌 수 있는 구간 앞까지만 당긴다
        }

        if (top)
        {
            var bottom = y + h;
            y = ClampY(y, h, area, zone);
            h = Math.Max(0, bottom - y);
        }

        return new Rect(x, y, w, h);
    }

    /// <summary>제목 줄의 끌 수 있는 구간이 캔버스에 복구 문턱 이상 보이는가. 복구와 배치(Arrange)가 같은 값으로 판단한다.</summary>
    public static bool IsGrabbable(Rect r, Size area, MdiGrabZone zone)
    {
        if (ShouldFoldStrip(r, area, zone, true))
        {
            zone = Folded(zone); // 이 자리에서는 기능 버튼 줄이 접히므로 접힌 구간으로 판단한다
        }

        var from = r.X + zone.Left;
        var to = r.Right - zone.RightInset;
        var needW = Math.Min(MinRecoverGrab, Math.Max(0, to - from));
        var hitW = Math.Min(to, area.Width) - Math.Max(from, 0);
        var titleTop = r.Y;
        var titleBottom = r.Y + zone.TitleBottom;
        var needH = Math.Min(PlacementRecovery.MinVisibleHeight, zone.TitleBottom);
        var hitH = Math.Min(titleBottom, area.Height) - Math.Max(titleTop, 0);
        return hitW > 0 && hitW >= needW && hitH > 0 && hitH >= needH;
    }

    private static double ClampX(double x, double width, Size area, MdiGrabZone zone)
    {
        var min = KeepWhenLeftOut(width, zone) - width;
        var max = area.Width - KeepWhenRightOut(width, zone);
        return min <= max ? Math.Clamp(x, min, max) : max; // 캔버스가 남길 폭보다 좁으면 왼쪽 구간을 우선 보인다
    }

    // 위쪽은 비율과 무관하게 0 에서 멈춘다(제목 줄이 유일한 잡을 곳). 아래쪽만 잔존 비율을 따른다.
    private static double ClampY(double y, double height, Size area, MdiGrabZone zone) =>
        Math.Clamp(y, 0, Math.Max(0, area.Height - KeepWhenBottomOut(height, zone)));
}

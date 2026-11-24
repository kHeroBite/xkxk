// 창 움직임 효과 카탈로그(REQ-112) — 들어옴·나감 효과 데이터 표(시작·끝 상태·잘라냄·3D·가속 곡선·짝 나감·플랫폼 낮춤)와 낮춤 규칙
using System;
using System.Collections.Generic;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>효과 방향. Enter = 창이 나타날 때(열기·전환으로 들어오는 창), Exit = 창이 사라질 때(닫기·전환으로 나가는 창).</summary>
public enum MotionDirection { Enter, Exit }

/// <summary>잘라냄(Clip) 종류. None 이 아니면 잘라냄 효과다(21 문서 §2 — 플랫폼 실측 뒤 낮춤 표시).</summary>
public enum MotionClip { None, WipeLeft, WipeRight, WipeUp, WipeDown, Split, Circle }

/// <summary>배치 상태(21 문서 §4 표의 행).</summary>
public enum MotionPlacement { MdiOverlap, MdiMaximized, DockTab, Nav, Sdi }

/// <summary>플랫폼 낮춤 표시(잘라냄 애니메이션 실측 실패 플랫폼). 여러 개를 함께 켤 수 있다.</summary>
[Flags]
public enum MotionPlatform { None = 0, Desktop = 1, Web = 2, Android = 4 }

/// <summary>재생 상태 한 점. Opacity·Scale = 절대값, X·Y = 창 크기 대비 비율(오른쪽·아래가 +).</summary>
public readonly record struct MotionState(double Opacity, double Scale, double X, double Y)
{
    public static readonly MotionState Rest = new(1, 1, 0, 0);
}

/// <summary>
/// 효과 1행. From → To 를 재생하고, Clip 은 잘라냄 모양, Tilt 는 3D 기울기(각도·원근은 theme.style.depth.tiltDeg·perspective 키),
/// Easing 은 재생기(화면 층)가 해석하는 가속 곡선 이름, PartnerExit 는 최대화·DOCK 전환에서 함께 재생할 나감 효과,
/// NavAllowed = NAV(폰)에서 그대로 쓰는가, LowerOn = 잘라냄 실측에 실패해 fade 로 낮추는 플랫폼.
/// </summary>
public sealed record MotionEffect(
    string Id, MotionDirection Direction, MotionState From, MotionState To, MotionClip Clip, bool Tilt, string Easing,
    string? PartnerExit, bool NavAllowed, MotionPlatform LowerOn = MotionPlatform.None)
{
    /// <summary>위치가 이동하는 효과인가(시작·끝의 X·Y 가 다름).</summary>
    public bool Moves => From.X != To.X || From.Y != To.Y;

    /// <summary>잘라냄·밀어내기·3D 중 하나인가(움직임 줄이기에서 fade 로 낮추는 대상).</summary>
    public bool Heavy => Moves || Clip != MotionClip.None || Tilt;
}

/// <summary>
/// 움직임 효과 카탈로그. 효과 추가·값 조정은 아래 표에 행만 더하거나 고친다(코드 분기에 효과 ID 를 박지 않는다, REQ-99).
/// 정본 문서는 plan/09_화면설계/21_창_움직임효과_배경_설계.md §2·§4·§7. 시간은 theme.style.motion.&lt;수준&gt;.ms 를 쓴다.
/// </summary>
public static class MotionEffects
{
    public const string None = "none";
    public const string Fade = "fade";

    /// <summary>MDI 겹침 전환에서 이동·잘라냄 효과를 낮출 대상(21 §4).</summary>
    public const string OverlapSwitchFallback = "fadeZoom";

    /// <summary>motion.switch 의 '열기와 같음' 값.</summary>
    public const string SameAsOpen = "same_as_open";

    private static readonly MotionState Rest = MotionState.Rest;
    private static readonly MotionState Clear = new(0, 1, 0, 0);

    private static readonly MotionEffect[] Table =
    {
        // 들어옴(시작 → 끝 = 제자리). 배율·이동 비율은 초기 제시값
        new(None, MotionDirection.Enter, Rest, Rest, MotionClip.None, false, "Linear", None, true),
        new(Fade, MotionDirection.Enter, Clear, Rest, MotionClip.None, false, "CubicEaseOut", Fade, true),
        new("fadeZoom", MotionDirection.Enter, new(0, 0.92, 0, 0), Rest, MotionClip.None, false, "CubicEaseOut", Fade, false),
        new("zoomOut", MotionDirection.Enter, new(0, 1.08, 0, 0), Rest, MotionClip.None, false, "CubicEaseOut", Fade, false),
        new("pushLeft", MotionDirection.Enter, new(1, 1, 0.3, 0), Rest, MotionClip.None, false, "CubicEaseOut", "pushLeft", true),
        new("pushRight", MotionDirection.Enter, new(1, 1, -0.3, 0), Rest, MotionClip.None, false, "CubicEaseOut", "pushRight", true),
        new("pushUp", MotionDirection.Enter, new(1, 1, 0, 0.3), Rest, MotionClip.None, false, "CubicEaseOut", Fade, true),
        new("pushDown", MotionDirection.Enter, new(1, 1, 0, -0.3), Rest, MotionClip.None, false, "CubicEaseOut", "pushDown", true),
        new("wipeLeft", MotionDirection.Enter, Rest, Rest, MotionClip.WipeLeft, false, "QuadraticEaseOut", Fade, false),
        new("wipeRight", MotionDirection.Enter, Rest, Rest, MotionClip.WipeRight, false, "QuadraticEaseOut", Fade, false),
        new("wipeUp", MotionDirection.Enter, Rest, Rest, MotionClip.WipeUp, false, "QuadraticEaseOut", Fade, false),
        new("wipeDown", MotionDirection.Enter, Rest, Rest, MotionClip.WipeDown, false, "QuadraticEaseOut", Fade, false),
        new("flyUp", MotionDirection.Enter, new(0, 1, 0, 0.15), Rest, MotionClip.None, false, "BackEaseOut", Fade, false),
        new("split", MotionDirection.Enter, Rest, Rest, MotionClip.Split, false, "QuadraticEaseOut", Fade, false),
        new("circle", MotionDirection.Enter, Rest, Rest, MotionClip.Circle, false, "QuadraticEaseOut", Fade, false),
        new("tilt3d", MotionDirection.Enter, Clear, Rest, MotionClip.None, true, "CubicEaseOut", Fade, false),

        // 나감(시작 = 제자리 → 끝)
        new(None, MotionDirection.Exit, Rest, Rest, MotionClip.None, false, "Linear", null, true),
        new(Fade, MotionDirection.Exit, Rest, Clear, MotionClip.None, false, "CubicEaseIn", null, true),
        new("fadeZoomOut", MotionDirection.Exit, Rest, new(0, 0.92, 0, 0), MotionClip.None, false, "CubicEaseIn", null, false),
        new("zoomIn", MotionDirection.Exit, Rest, new(0, 1.08, 0, 0), MotionClip.None, false, "CubicEaseIn", null, false),
        new("pushLeft", MotionDirection.Exit, Rest, new(1, 1, -0.3, 0), MotionClip.None, false, "CubicEaseIn", null, true),
        new("pushRight", MotionDirection.Exit, Rest, new(1, 1, 0.3, 0), MotionClip.None, false, "CubicEaseIn", null, true),
        new("pushDown", MotionDirection.Exit, Rest, new(1, 1, 0, 0.3), MotionClip.None, false, "CubicEaseIn", null, true),
        new("flyDown", MotionDirection.Exit, Rest, new(0, 1, 0, 0.15), MotionClip.None, false, "CubicEaseIn", null, false),
        new("tilt3d", MotionDirection.Exit, Rest, Clear, MotionClip.None, true, "CubicEaseIn", null, false),
    };

    /// <summary>들어옴 효과 ID(표 순서).</summary>
    public static string[] EnterIds { get; } = Ids(MotionDirection.Enter);

    /// <summary>나감 효과 ID(표 순서).</summary>
    public static string[] ExitIds { get; } = Ids(MotionDirection.Exit);

    /// <summary>motion.switch 허용 값(none·same_as_open·들어옴 효과 ID).</summary>
    public static string[] SwitchIds { get; } = new[] { None, SameAsOpen }.Concat(EnterIds.Where(i => i != None)).ToArray();

    /// <summary>전체 표.</summary>
    public static IReadOnlyList<MotionEffect> All => Table;

    /// <summary>방향·ID 로 찾기(대소문자 무시). 모르면 null.</summary>
    public static MotionEffect? Find(MotionDirection direction, string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Table.FirstOrDefault(e => e.Direction == direction && string.Equals(e.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>움직임 줄이기(reduced) 낮춤 — 잘라냄·밀어내기·3D 는 fade(21 §7). 모르는 ID·none 은 그대로.</summary>
    public static string LowerReduced(MotionDirection direction, string id) => Find(direction, id) is { Heavy: true } ? Fade : id;

    /// <summary>MDI 겹침 전환 낮춤 — 위치 이동·잘라냄 효과는 fadeZoom(3D 는 유지, 21 §4).</summary>
    public static string LowerOverlapSwitch(string id) =>
        Find(MotionDirection.Enter, id) is { } e && (e.Moves || e.Clip != MotionClip.None) ? OverlapSwitchFallback : id;

    /// <summary>NAV(폰) 낮춤 — fade·push* 만 그대로, 나머지는 fade(none 은 none, 21 §4).</summary>
    public static string LowerNav(MotionDirection direction, string id) =>
        id == None || Find(direction, id) is { NavAllowed: true } ? id : Fade;

    /// <summary>플랫폼 낮춤 — 표의 LowerOn 에 그 플랫폼이 있으면 fade.</summary>
    public static string LowerPlatform(MotionDirection direction, string id, MotionPlatform platform) =>
        Find(direction, id) is { } e && (e.LowerOn & platform) != 0 ? Fade : id;

    /// <summary>최대화·DOCK 전환에서 들어오는 효과와 짝으로 나가는 창의 효과(표의 PartnerExit, 없으면 none).</summary>
    public static string PartnerExit(string enterId) => Find(MotionDirection.Enter, enterId)?.PartnerExit ?? None;

    private static string[] Ids(MotionDirection direction) => Table.Where(e => e.Direction == direction).Select(e => e.Id).ToArray();
}

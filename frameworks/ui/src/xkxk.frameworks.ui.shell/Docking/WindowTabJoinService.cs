// SDI 창 탭 합류 공통 서비스 — 끄는 SDI 창의 포인터로 다른 SDI 창의 로고 칸·탭 줄 머무름을 판정하고, 놓으면 문서를 대상 창 탭 줄 끝에 붙인 뒤 빈 원천 창을 닫는다(정본 01 §4.4, SDI→SDI)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 탭 합류 표적을 내는 SDI 창 1개(데스크톱 = SdiTitleBar, 웹·Android 관리형 창 = MdiDocumentWindow 어댑터). 뷰 레이어가 구현한다.
/// Key = 창 식별자(TabJoinTarget.WindowKey), Root = 창 루트(문서 찾기·빈 창 닫기), Frame = 창 전체 요소(가림 판정 — 데스크톱 OS 창, 관리형 창 요소).
/// TargetElements = 표적 요소와 그 요소 좌표 사각형(아이콘 칸, 이미 있으면 탭 줄 — 숨은 요소 제외).
/// ShowTabJoin = 강조(armed 끄는 동안 다른 창, hover 머무는 표적 종류, source 끄는 창이 표적 위).
/// </summary>
public interface IWindowTabJoinSurface
{
    string Key { get; }
    IRootDock? Root { get; }
    Visual? Frame { get; }
    IEnumerable<(TabJoinKind Kind, Visual Element, Rect Local)> TargetElements();
    void ShowTabJoin(bool armed, TabJoinKind? hover, bool source);
}

/// <summary>
/// SDI→SDI 탭 합류. 원천이 Begin·Update·End 로 포인터를 넘긴다(데스크톱 = OS 이동 루프의 커서, 웹 = 관리형 창 포인터 이벤트).
/// 좌표: screenScale &gt; 0 이면 화면 물리 px ÷ screenScale(데스크톱 화면 DIP — 커서와 표적이 같은 나눗수라 포함 판정이 정확), 0 이면 원천 TopLevel DIP(웹).
/// 겹침만으로는 합치지 않는다 — 표적 위 TabJoinRules.DwellMs 머문 뒤 같은 표적에서 놓을 때만 합류(TabJoinRules·TabJoinDwellGate).
/// 표적은 끌기로 판정된 순간 1회만 모은다(끌기 중 트리 훑기 없음). MDI↔SDI 교차 합류는 범위 밖이다.
/// </summary>
public static partial class WindowTabJoinService
{
    private static readonly List<IWindowTabJoinSurface> s_surfaces = new();
    private static readonly TabJoinDwellGate s_gate = new();
    private static readonly Dictionary<string, (IWindowTabJoinSurface Surface, Rect Rect, int Z)> s_windows = new(StringComparer.Ordinal);
    private static List<TabJoinTarget> s_targets = new();
    private static IReadOnlyList<IWindowTabJoinSurface>? s_candidates;
    private static Func<IWindowTabJoinSurface, int>? s_zOf;
    private static IWindowTabJoinSurface? s_source;
    private static IWindowTabJoinSurface? s_hot;
    private static Point s_start;
    private static double s_scale;
    private static bool s_dragging;

    /// <summary>끄는 중이고 합치기 표적 위에 머무는가(스냅 이음매 SdiSnapHook.OverTabJoinTarget 용).</summary>
    public static bool IsOverTarget => s_dragging && s_gate.Current is not null;

    /// <summary>합류했다(원천 창 키, 대상 창 키) — 로그·시험 관측용.</summary>
    public static event Action<string, string>? Joined;

    /// <summary>마지막 놓기 판정 한 줄(진단·시험 관측용).</summary>
    public static string LastOutcome { get; private set; } = "none";

    /// <summary>지금 끌기 중인가(원천이 Begin 했고 아직 End 전).</summary>
    public static bool IsActive => s_source is not null;

    /// <summary>상시 표면(데스크톱 SDI 창)을 등록한다. 멱등.</summary>
    public static void Register(IWindowTabJoinSurface surface)
    {
        if (!s_surfaces.Contains(surface)) { s_surfaces.Add(surface); SurfaceRegistered?.Invoke(surface); }
    }

    /// <summary>상시 표면이 처음 등록됐다 — 데스크톱 호스트가 그 OS 창에 이동 루프 훅을 붙인다(등록 지점이 배정 밖이라 알림으로 잇는다).</summary>
    public static event Action<IWindowTabJoinSurface>? SurfaceRegistered;

    /// <summary>등록된 상시 표면(데스크톱 OS Z 순위 계산용) + 관리형 창 표면(웹·Android — ManagedSurfaces 제공자, 진단 window.tabJoinTargets 용).</summary>
    public static IReadOnlyList<IWindowTabJoinSurface> Surfaces => ManagedSurfaces is { } m ? s_surfaces.Concat(m()).ToList() : s_surfaces;
    public static Func<IEnumerable<IWindowTabJoinSurface>>? ManagedSurfaces { get; set; }

    public static void Unregister(IWindowTabJoinSurface surface)
    {
        s_surfaces.Remove(surface);
        if (ReferenceEquals(surface, s_source))
        {
            Reset();
        }
    }

    /// <summary>등록된 표면 중 Frame 이 top 인 것(데스크톱 훅이 OS 창으로 원천을 찾는다).</summary>
    public static IWindowTabJoinSurface? SurfaceOf(Visual top) => s_surfaces.FirstOrDefault(s => ReferenceEquals(s.Frame, top));

    /// <summary>
    /// 끌기 시작(누름). candidates 가 null 이면 등록된 표면 전체. zOf 가 null 이면 후보 순서(뒤가 위). 아직 표적은 모으지 않는다.
    /// </summary>
    public static void Begin(IWindowTabJoinSurface source, Point start, double screenScale,
        IReadOnlyList<IWindowTabJoinSurface>? candidates = null, Func<IWindowTabJoinSurface, int>? zOf = null)
    {
        Reset();
        if (source.Root is null) { return; }

        s_source = source;
        s_dragRoot = source.Root; // Dock 기본 창 도킹 차단 표시(DockGuard)
        s_start = start;
        s_scale = screenScale;
        s_candidates = candidates;
        s_zOf = zOf;
    }

    /// <summary>포인터 이동. 8 DIP 를 넘는 첫 이동에서 표적을 1회 모으고, 이후 머무는 표적과 강조를 갱신한다.</summary>
    public static void Update(Point point)
    {
        if (s_source is not { } source) { return; }

        if (!s_dragging)
        {
            if (!TabJoinRules.IsDrag(s_start, point)) { return; }

            s_dragging = true;
            Collect(source);
            foreach (var (s, _, _) in s_windows.Values)
            {
                s.ShowTabJoin(armed: true, hover: null, source: false);
            }
        }

        if (s_gate.Update(PickVisible(point)))
        {
            ShowHover();
        }
    }

    /// <summary>놓기. point 가 있으면 그 점에서 표적을 다시 확인한다. 머무름이 찼고 같은 표적이면 합류를 예약한다(Post — 이동 루프·Dock 놓기 처리가 끝난 뒤).</summary>
    public static void End(Point? point, bool commit = true)
    {
        ClearDragRootLater();
        if (s_source is not { } source) { return; }

        var dragging = s_dragging;
        var current = s_gate.Current;
        var armed = s_gate.IsArmed;
        var atRelease = dragging ? (point is { } p ? PickVisible(p) : current) : null;
        var accepted = commit && dragging && armed && current is not null && atRelease?.Id == current.Id
            && s_windows.TryGetValue(current.WindowKey, out _);
        var target = accepted ? s_windows[current!.WindowKey].Surface : null;
        LastOutcome = $"source={source.Key}|dragging={dragging}|current={current?.Id ?? "none"}|release={atRelease?.Id ?? "none"}|armed={armed}|accepted={accepted}";
        Reset();
        if (target is null || source.Root is not { } sourceRoot || target.Root is not { } targetRoot)
        {
            return;
        }

        var dragged = WindowCommands.FindDocument(sourceRoot); // 원천 창이 보여 주던(활성 쪽) 문서 — 합류 뒤 활성 탭
        Dispatcher.UIThread.Post(() =>
        {
            var ok = Join(sourceRoot, targetRoot, dragged);
            LastOutcome += ok ? "|join=done" : "|join=skipped";
            if (ok)
            {
                Joined?.Invoke(source.Key, target.Key);
            }
        });
    }

    private static void Collect(IWindowTabJoinSurface source)
    {
        s_windows.Clear();
        s_targets = new List<TabJoinTarget>();
        var list = s_candidates ?? s_surfaces;
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (ReferenceEquals(s, source) || s.Root is not { } root || WindowCommands.FindDocument(root) is null
                || s.Frame is not { IsEffectivelyVisible: true } frame || ToSpace(frame, new Rect(frame.Bounds.Size)) is not { } rect)
            {
                continue;
            }

            var z = s_zOf?.Invoke(s) ?? i;
            s_windows[s.Key] = (s, rect, z);
            foreach (var (kind, element, local) in s.TargetElements())
            {
                if (ToSpace(element, local) is { } r)
                {
                    s_targets.Add(new TabJoinTarget(s.Key + (kind == TabJoinKind.Icon ? "#icon" : "#strip"), kind, r, z, s.Key));
                }
            }
        }
    }

    /// <summary>표적 고르기 + 가림 판정 — 표적보다 Z 가 높은 다른 창(끄는 창 제외)이 그 점을 덮으면 고르지 않는다.</summary>
    private static TabJoinTarget? PickVisible(Point point)
    {
        var sourceKey = s_source?.Key;
        if (TabJoinRules.Pick(point, s_targets, sourceKey) is not { } pick)
        {
            return null;
        }

        foreach (var (key, w) in s_windows)
        {
            if (key != sourceKey && key != pick.WindowKey && w.Z > pick.Z && w.Rect.Contains(point))
            {
                return null;
            }
        }

        return pick;
    }

    private static void ShowHover()
    {
        s_hot?.ShowTabJoin(armed: true, hover: null, source: false);
        s_hot = null;
        var current = s_gate.Current;
        if (current is not null && s_windows.TryGetValue(current.WindowKey, out var w))
        {
            s_hot = w.Surface;
            s_hot.ShowTabJoin(armed: true, hover: current.Kind, source: false);
        }

        s_source?.ShowTabJoin(armed: false, hover: null, source: current is not null);
    }

    /// <summary>요소 좌표 사각형 → 합류 좌표계. 화면 밖·크기 0·트리 밖이면 null.</summary>
    private static Rect? ToSpace(Visual element, Rect local)
    {
        if (local.Width <= 0 || local.Height <= 0) { return null; }

        if (s_scale > 0)
        {
            var tl = element.PointToScreen(local.TopLeft);
            var br = element.PointToScreen(local.BottomRight);
            return new Rect(tl.X / s_scale, tl.Y / s_scale, (br.X - tl.X) / s_scale, (br.Y - tl.Y) / s_scale);
        }

        return s_source?.Frame is { } frame && TopLevel.GetTopLevel(frame) is { } top && element.TranslatePoint(local.TopLeft, top) is { } p
            ? new Rect(p, local.Size)
            : null;
    }

    private static void Reset()
    {
        foreach (var (s, _, _) in s_windows.Values)
        {
            s.ShowTabJoin(armed: false, hover: null, source: false);
        }

        s_source?.ShowTabJoin(armed: false, hover: null, source: false);
        s_windows.Clear();
        s_targets = new List<TabJoinTarget>();
        s_gate.Reset();
        s_candidates = null;
        s_zOf = null;
        s_source = null;
        s_hot = null;
        s_dragging = false;
    }
}

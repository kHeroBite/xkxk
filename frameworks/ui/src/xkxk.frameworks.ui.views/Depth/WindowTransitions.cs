// 창 움직임 효과 재생기(REQ-112, 21 설계 §2~§4) — 열기·닫기·전환을 배치 상태(MDI 겹침·최대화·DOCK 탭·NAV)에 맞춰 효과 표(MotionEffects)로 재생한다
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.views.Depth;

/// <summary>
/// 효과 ID 는 해석기(DesignThemeResolver.OpenEffect·CloseEffect·SwitchEffect·SwitchExitEffect)가 폴백·배치·플랫폼 낮춤까지 반영해 준다. 재생기는 받은 ID 를 표대로 그리기만 한다(REQ-99).
/// 열기 = MDI 창이 처음 붙을 때(DepthTransitions.Open 이 넘김)와 DOCK·NAV 호스트에 문서가 더해질 때, 닫기 = DockableClosing 직전 모습을 DockableClosed 뒤 덮개에서 재생(WindowTransitions.Close·CloseCover, 실제 닫기는 늦추지 않음),
/// 전환 = 셸의 ActivatedFrom(활성 문서가 바뀌고 출처가 Program 이 아닐 때). 호스트 표식은 화면 쪽 클래스 pdMotionHostDock·pdMotionHostNav 다.
/// 시간은 theme.style.motion.&lt;수준&gt;.ms(DesignThemeResolver.MotionMs). 끝·중단 뒤 RenderTransform·Opacity·Clip 은 전 값(같은 객체)이다.
/// </summary>
public static partial class WindowTransitions
{
    private const string DockHostClass = "pdMotionHostDock";
    private const string NavHostClass = "pdMotionHostNav";
    private const string VisibleStartKey = "motion.switch.visibleStartOpacity";

    private static readonly ConditionalWeakTable<object, object> s_openedInHost = new();
    private static readonly object s_marker = new();
    private static WeakReference<Visual>? s_root;
    private static IFactory? s_factory;
    private static WindowCommands? s_shell;
    private static WeakReference<PocDocument>? s_current;
    private static WeakReference<PocDocument>? s_previous;

    /// <summary>시작 때 1회(DepthTransitions.Attach 에서): 문서 추가 알림과 셸 전환 알림을 건다. 닫기는 DepthTransitions 가 넘긴다.</summary>
    public static void Attach(IFactory factory, Visual root)
    {
        s_root = new WeakReference<Visual>(root);
        if (s_factory is not null)
        {
            s_factory.DockableAdded -= OnDockableAdded;
            s_factory.DockableClosed -= OnDockableClosed;
        }

        s_factory = factory;
        factory.DockableAdded += OnDockableAdded;
        factory.DockableClosed += OnDockableClosed; // 닫기 덮개는 실제로 닫힌 뒤에만(21 §4.1)
        DocumentTabKeepAlive.SlotHiding -= OnSlotHiding;
        DocumentTabKeepAlive.SlotHiding += OnSlotHiding; // DOCK 짝 나감: 숨기기 직전 모습(.Switch.cs)
        HookShell();
        DepthProbeHost.MotionProbe = (effect, placement, hold) => Dispatcher.UIThread.InvokeAsync(() => Probe(effect, placement, hold)).GetTask(); // 진단 motion.probe(Shell 은 Views 를 참조하지 않는다)
    }

    /// <summary>MDI 문서 창이 처음 화면에 붙음(DepthTransitions.Open 의 처음-붙음 판정 뒤). 최대화면 최대화 행, 아니면 겹침 행의 열기 효과.</summary>
    internal static void OnWindowOpened(MdiDocumentWindow window) =>
        Play(window, MotionDirection.Enter, DesignThemeResolver.OpenEffect(MdiPlacement(window)), null, null);

    /// <summary>MDI 겹침 열기 효과가 3D 기울기인가(DepthPrewarm 이 예열 여부를 같은 판단으로 정한다).</summary>
    internal static bool TiltOpenAllowed() =>
        MotionEffects.Find(MotionDirection.Enter, DesignThemeResolver.OpenEffect(MotionPlacement.MdiOverlap)) is { Tilt: true }
        && DesignThemeResolver.MotionMs() > 0 && DesignThemeResolver.Number("depth.tiltDeg") > 0;

    private static void HookShell()
    {
        if (s_shell is not null || xkxk.frameworks.ui.shell.App.Shell is not WindowCommands shell)
        {
            return;
        }

        s_shell = shell;
        shell.ActiveChanged += OnActiveChanged;
        shell.ActivatedFrom += OnActivatedFrom;
        TrackNavShown(shell);
        OnActiveChanged(shell.ActiveDocument);
    }

    private static void OnActiveChanged(PocDocument? doc)
    {
        if (doc is null || (s_current is not null && s_current.TryGetTarget(out var cur) && ReferenceEquals(cur, doc)))
        {
            return;
        }

        s_previous = s_current;
        s_current = new WeakReference<PocDocument>(doc);
    }

    /// <summary>DOCK·NAV 호스트에 문서가 더해짐 → 배치 뒤·그리기 전(AfterLayout) 그 화면에 열기 효과. MDI 창은 Loaded 경로가 맡으므로 건너뛴다.</summary>
    private static void OnDockableAdded(object? sender, DockableAddedEventArgs e)
    {
        HookShell();
        if (e.Dockable is not { } dockable)
        {
            return;
        }

        AfterLayout(final =>
        {
            var at = Locate(dockable);
            if (at is null && !final)
            {
                return false; // 이 배치에서 아직 안 보임 — 예비 시점에 한 번 더
            }

            if (at is not null && at.View is not MdiDocumentWindow && !s_openedInHost.TryGetValue(dockable, out _))
            {
                s_openedInHost.Add(dockable, s_marker);
                Play(at.View, MotionDirection.Enter, DesignThemeResolver.OpenEffect(at.Placement), null, null);
            }

            return true;
        });
    }

    /// <summary>전환: 들어오는 화면에 전환 효과, 최대화·DOCK·NAV 는 이전 화면 모습을 덮개에서 짝 나감 효과로 함께 재생한다. 열기 재생 중이면 열기를 우선한다.</summary>
    private static void OnActivatedFrom(PocDocument doc, ActivationOrigin origin)
    {
        EndCovers("switch"); // 다른 전환이 시작되면 앞선 닫기 덮개를 끝낸다(21 §7)
        var key = origin.ToKey();
        var sources = SwitchSources();

        // 이전 화면은 바뀐 직후(배치 전) 지금 찍는다. DOCK 탭 본문이 이미 숨겨졌으면 숨기기 직전 그림을 쓴다(.Switch.cs). 둘 다 없으면 짝 나감을 생략한다
        Located? before = null;
        Snapshot? shot = null;
        var note = s_lastSwitch = new SwitchNote(++s_switchSeq);
        if (s_previous is not null && s_previous.TryGetTarget(out var prev) && !ReferenceEquals(prev, doc))
        {
            (before, shot) = TakeLeaving(prev, key, sources, note);
        }

        // 들어오는 화면이 이미 있으면(NAV 호스트·MDI 창 활성 변경) 지금 시작해 첫 그림부터 시작 상태로 그린다(r7 콤보 깜박임 수정).
        // DOCK 탭 내용은 활성 변경 뒤 배치에서 보이기 시작하므로 배치 뒤·그리기 전(AfterLayout)에 찾는다
        if (Locate(doc) is { Placement: not MotionPlacement.DockTab } now)
        {
            StartSwitch(now, key, sources, before, shot, note);
            return;
        }

        AfterLayout(final =>
        {
            var at = Locate(doc);
            if (at is null && !final)
            {
                return false;
            }

            StartSwitch(at, key, sources, before, shot, note);
            return true;
        });
    }

    private static string[] SwitchSources()
    {
        var flags = UiSettings.Current.MotionSwitchSources;
        return Enum.GetValues<MotionSwitchSources>().Where(f => f != MotionSwitchSources.None && flags.HasFlag(f)).Select(f => UiKeyNames.Of(f)).ToArray();
    }

    /// <summary>효과를 시작해 화면 프레임마다 진행한다. 효과 없음·시간 0·화면 밖·합성 불가면 onEnd 만 바로 부른다.</summary>
    private static void Play(Control target, MotionDirection direction, string id, double? startOpacity, Action? onEnd)
    {
        var effect = MotionEffects.Find(direction, id);
        var ms = (double)DesignThemeResolver.MotionMs();
        var top = TopLevel.GetTopLevel(target);
        if (effect is null || effect.Id == MotionEffects.None || ms <= 0 || top is null || Begin(target, effect, startOpacity) is not { } playback)
        {
            onEnd?.Invoke();
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Frame(TimeSpan _)
        {
            if (playback.Done)
            {
                onEnd?.Invoke();
                return;
            }

            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
            playback.Step(t);
            if (t >= 1)
            {
                playback.Finish();
                onEnd?.Invoke();
                return;
            }

            top.RequestAnimationFrame(Frame);
        }

        playback.MarkStart(); // 진단 구간 motion.play(PERF-13)
        top.RequestAnimationFrame(Frame);
    }

    /// <summary>
    /// 화면을 지금 모습대로(DOCK·NAV 와 MDI 안 탭 묶음 창은 불투명해질 때까지 조상 바탕을 겹쳐) 찍는다(호스트 기준 자리 포함). 보이지 않거나 찍기를 지원하지 않는 렌더러면 null.
    /// group = MDI 안 탭 묶음 창 전체를 찍는 닫기(GroupWindowOf, 21 §4.1).
    /// </summary>
    private static Snapshot? Shot(Control view, Control host, MotionPlacement placement, bool group = false)
    {
        if (!view.IsEffectivelyVisible || view.Bounds.Width <= 0 || view.Bounds.Height <= 0 || view.TranslatePoint(default, host) is not { } at)
        {
            return null;
        }

        // 화소·DIP 규약은 SnapshotGeometry, 배율 이중 적용 회피는 SnapshotRenderer(둘 다 단위 시험 대상)
        var geometry = new SnapshotGeometry(view.Bounds.Size, TopLevel.GetTopLevel(view)?.RenderScaling ?? 1.0);
        try
        {
            var bitmap = SnapshotRenderer.Capture(view, geometry, out var mode);
            var backdrop = BackdropOf(view, placement, group);
            var image = backdrop is null ? bitmap : WithBackdrop(bitmap, backdrop.Layers, geometry);
            return new Snapshot(image, host, new Rect(at, view.Bounds.Size), backdrop?.Source, geometry, mode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null; // 찍기를 지원하지 않는 렌더러 — 덮개 효과 생략
        }
    }

    /// <summary>
    /// 문서의 화면·호스트·배치 상태를 찾는다. MDI 창(같은 문서) → 겹침·최대화, DOCK 호스트 아래 같은 문서의 가장 바깥 보이는 화면(탭 줄 제외) → DOCK 탭,
    /// 보이는 NAV 호스트 → NAV. 어느 것도 아니면(SDI 창 등) null — SDI 는 효과가 없다(21 §4).
    /// </summary>
    private static Located? Locate(object doc)
    {
        if (Root() is not { } root)
        {
            return null;
        }

        var all = root.GetVisualDescendants().OfType<Control>().ToList();
        if (all.OfType<MdiDocumentWindow>().FirstOrDefault(w => ReferenceEquals(w.DataContext, doc) && w.IsEffectivelyVisible) is { } window)
        {
            return new Located(window, window.Parent as Control ?? window, MdiPlacement(window));
        }

        foreach (var host in all.Where(c => c.Classes.Contains(DockHostClass) && c.IsEffectivelyVisible))
        {
            var view = host.GetVisualDescendants().OfType<Control>().FirstOrDefault(c =>
                ReferenceEquals(c.DataContext, doc) && c.IsEffectivelyVisible && c is not DocumentTabStrip and not DocumentTabStripItem
                && c.FindAncestorOfType<DocumentTabStrip>() is null);
            if (view is not null)
            {
                return new Located(view, host, MotionPlacement.DockTab);
            }
        }

        return all.FirstOrDefault(c => c.Classes.Contains(NavHostClass) && c.IsEffectivelyVisible) is { } nav
            ? new Located(nav, nav, MotionPlacement.Nav)
            : null;
    }

    /// <summary>
    /// 닫기 대상이 MDI 창 안 탭 묶음의 탭이면 묶음 창 전체를 대상으로 바꾼다(21 §4.1). 탭 본문만 가리면 묶음 머리가 덮개 밖에서 먼저 바뀐다.
    /// 배치는 묶음 창의 겹침·최대화(MdiTabGroupDock 은 IMdiDocument)라 위치·앞 창 빼기·효과 키가 MDI 경로를 따른다. 아니면 null.
    /// </summary>
    private static Located? GroupWindowOf(Located at) =>
        at.Placement == MotionPlacement.DockTab
        && at.View.FindAncestorOfType<MdiDocumentWindow>() is { DataContext: MdiTabGroupDock, IsEffectivelyVisible: true } window
            ? new Located(window, window.Parent as Control ?? window, MdiPlacement(window), Group: true)
            : null;

    private static MotionPlacement MdiPlacement(MdiDocumentWindow window) =>
        window.DataContext is IMdiDocument { MdiState: MdiWindowState.Maximized } ? MotionPlacement.MdiMaximized : MotionPlacement.MdiOverlap;

    private static Visual? Root() => s_root is not null && s_root.TryGetTarget(out var r) ? (Visual?)TopLevel.GetTopLevel(r) ?? r : null;

    /// <param name="Group">MDI 안 탭 묶음 창 전체로 바꾼 닫기 대상(바탕을 깐다).</param>
    private sealed record Located(Control View, Control Host, MotionPlacement Placement, bool Group = false);

    private sealed record Snapshot(RenderTargetBitmap Image, Control Host, Rect Bounds, string? Base, SnapshotGeometry Geometry, string Mode);
}

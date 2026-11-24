// SDI 창 탭 합류 표면 — 데스크톱 SDI 제목줄의 로고 칸·탭 줄을 표적으로 등록·강조하고, 웹·Android 관리형 SDI 창(MdiDocumentWindow) 끌기를 서비스에 넘긴다(정본 01 §4.4)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Settings;
using Pd.Poc.Shell.Docking;
using Pd.Ui.Controls;

namespace Pd.Ui.Views;

/// <summary>
/// 데스크톱 SDI 창 표면. 끌기 원천은 OS 이동 루프라 앱 포인터가 오지 않으므로 Desktop 레이어(SdiTabJoinHook)가 커서를 서비스에 넘긴다.
/// 이 partial 은 표적(로고 칸 — 로고가 숨었으면 제목줄 왼쪽 정사각형, 보이는 탭 줄)과 강조 클래스만 맡는다. 본문(.axaml.cs)은 바꾸지 않는다.
/// 강조 클래스 이름은 MDI(MdiTabJoinDrag)와 같다: .pdTabJoinArmed·.pdTabJoinTarget·.pdTabJoinOnIcon|.pdTabJoinOnStrip·.pdTabJoinSource(이 제목줄), 머무는 탭 줄에도 .pdTabJoinTarget.
/// </summary>
public partial class SdiTitleBar : IWindowTabJoinSurface
{
    private static int s_tabJoinSeq;
    private readonly string _tabJoinKey = "sdi-" + System.Threading.Interlocked.Increment(ref s_tabJoinSeq);
    private DocumentTabStrip? _tabJoinStrip;

    // 등록 지점(SdiWindowHost·본문)이 배정 밖이라 클래스 처리기로 붙인다 — 창에 붙으면 등록, 떨어지면 해제
    static SdiTitleBar()
    {
        LoadedEvent.AddClassHandler<SdiTitleBar>((bar, _) => WindowTabJoinService.Register(bar), RoutingStrategies.Direct);
        UnloadedEvent.AddClassHandler<SdiTitleBar>((bar, _) => WindowTabJoinService.Unregister(bar), RoutingStrategies.Direct);
    }

    string IWindowTabJoinSurface.Key => _tabJoinKey;

    IRootDock? IWindowTabJoinSurface.Root => _root;

    Visual? IWindowTabJoinSurface.Frame => _window;

    IEnumerable<(TabJoinKind Kind, Visual Element, Rect Local)> IWindowTabJoinSurface.TargetElements()
    {
        var logo = this.GetVisualDescendants().OfType<ChromeLogo>().FirstOrDefault(l => l.IsEffectivelyVisible && l.Bounds.Width > 0);
        if (logo is not null)
        {
            yield return (TabJoinKind.Icon, logo, new Rect(logo.Bounds.Size).Inflate(2)); // 2 DIP 여유(MDI 아이콘과 같은 규칙)
        }
        else if (Bar.Bounds.Height > 0)
        {
            yield return (TabJoinKind.Icon, Bar, new Rect(0, 0, Bar.Bounds.Height, Bar.Bounds.Height));
        }

        _tabJoinStrip = _window is { } w ? ManagedSdiTabJoin.VisibleStrip(w) : null;
        if (_tabJoinStrip is { } strip)
        {
            yield return (TabJoinKind.TabStrip, strip, new Rect(strip.Bounds.Size));
        }
    }

    void IWindowTabJoinSurface.ShowTabJoin(bool armed, TabJoinKind? hover, bool source)
    {
        ManagedSdiTabJoin.SetClasses(this, armed, hover, source);
        _tabJoinStrip?.Classes.Set(MdiTabJoinDrag.TargetClass, hover == TabJoinKind.TabStrip);
        // 원천 반투명은 제목줄이 아니라 OS 창 전체여야 MDI(창 전체 0.55)와 같다 — 창에도 같은 클래스(스타일 Themes/DockJoinTarget.axaml :is(Window).pdTabJoinSource)
        _window?.Classes.Set(MdiTabJoinDrag.SourceClass, source);
    }
}

/// <summary>
/// 웹·Android 관리형 SDI 창 끌기 원천. 관리형 창은 MdiDocumentWindow(DataContext = ManagedDockWindowDocument)이고 제목줄 끌기는 창이 포인터를 캡처해
/// 앱 PointerMoved 로 처리한다(디컴파일 MdiDocumentWindow.BeginDrag·PointerMovedHandler). 그래서 같은 창 클래스 처리기(handledEventsToo)로 엿본다.
/// MDI 창(PocDocument·묶음)은 MdiTabJoinDrag 몫이라 여기서는 관리형 창만 다룬다. 좌표는 TopLevel DIP(서비스 screenScale=0).
/// </summary>
internal static class ManagedSdiTabJoin
{
    private static readonly ConditionalWeakTable<MdiDocumentWindow, string> s_keys = new();
    private static int s_keySeq;
    private static readonly HashSet<MdiDocumentWindow> s_live = new();
    private static MdiDocumentWindow? s_source;
    private static bool s_captureLost;

#pragma warning disable CA2255 // 등록 파일(테마·App)이 배정 밖이라 어셈블리 적재 때 처리기를 붙인다(MdiTabGroupDiag·TourInputGate 와 같은 방식)
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        // 진단 목록(window.tabJoinTargets)용 — 붙어 있는 관리형 창을 표면으로 낸다(끌기 판정은 누름 때 후보를 따로 모은다)
        Control.LoadedEvent.AddClassHandler<MdiDocumentWindow>((w, _) => s_live.Add(w), RoutingStrategies.Direct);
        Control.UnloadedEvent.AddClassHandler<MdiDocumentWindow>((w, _) => s_live.Remove(w), RoutingStrategies.Direct);
        WindowTabJoinService.ManagedSurfaces = () => DockSettings.UseManagedWindows
            ? s_live.Where(w => w.DataContext is ManagedDockWindowDocument && w.IsEffectivelyVisible).Select(w => (IWindowTabJoinSurface)new ManagedSurface(w)).ToList()
            : Enumerable.Empty<IWindowTabJoinSurface>();
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiDocumentWindow>((w, e) =>
        {
            if (ReferenceEquals(w, s_source) && TopLevel.GetTopLevel(w) is { } top)
            {
                WindowTabJoinService.Update(e.GetPosition(top));
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<MdiDocumentWindow>((w, e) => { if (ReferenceEquals(w, s_source)) { Release(w, e); } }, RoutingStrategies.Bubble, handledEventsToo: true);
        // 창이 캡처를 먼저 잃으면 놓기는 포인터 아래 요소로 간다(MdiTabJoinDrag·PanelJoinDrag 와 같은 경로) — TopLevel 에서 받는다
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, e) => { if (s_captureLost && s_source is { } w) { Release(w, e); } }, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerCaptureLostEvent.AddClassHandler<MdiDocumentWindow>((w, e) =>
        {
            if (ReferenceEquals(e.Source, w) && ReferenceEquals(w, s_source))
            {
                s_captureLost = true;
                DispatcherTimer.RunOnce(() =>
                {
                    if (s_captureLost && ReferenceEquals(s_source, w))
                    {
                        s_source = null;
                        s_captureLost = false;
                        WindowTabJoinService.End(null, commit: false); // 놓기 없이 캡처만 잃음 = 취소
                    }
                }, TimeSpan.FromMilliseconds(500));
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        ManagedSnapDrag.Install(); // 탭 합류 처리기 뒤에 등록(순서 유지)
    }

    private static void OnPressed(MdiDocumentWindow window, PointerPressedEventArgs e)
    {
        if (!DockSettings.UseManagedWindows || window.DataContext is not ManagedDockWindowDocument { MdiState: MdiWindowState.Normal }
            || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed || !IsTitlePress(e.Source as Visual, window)
            || TopLevel.GetTopLevel(window) is not { } top)
        {
            return;
        }

        // 같은 관리형 창 레이어의 보이는 관리형 창 전부를 후보로(아래→위 시각 순서: ZIndex, 같으면 자식 순번)
        var layer = window.GetVisualAncestors().OfType<ManagedWindowLayer>().FirstOrDefault();
        var windows = (layer is null ? Enumerable.Empty<Visual>() : layer.GetVisualDescendants()).OfType<MdiDocumentWindow>()
            .Where(w => w.DataContext is ManagedDockWindowDocument && w.IsEffectivelyVisible)
            .Select((w, i) => (w, i)).OrderBy(t => t.w.ZIndex).ThenBy(t => t.i).Select(t => t.w).ToList();
        if (!windows.Contains(window))
        {
            windows.Add(window);
        }

        var surfaces = windows.Select(w => (IWindowTabJoinSurface)new ManagedSurface(w)).ToList();
        var source = surfaces[windows.IndexOf(window)];
        s_source = window;
        s_captureLost = false;
        WindowTabJoinService.Begin(source, e.GetPosition(top), 0, surfaces, s => surfaces.IndexOf(s));
    }

    /// <summary>누른 곳이 창 제목줄(PART_Header)인가 — 단추·Dock 끌기 손잡이 제외(Shell PanelJoinTargets.IsTitlePress 와 같은 규칙, 그쪽이 internal 이라 같은 판정을 둔다).</summary>
    private static bool IsTitlePress(Visual? source, MdiDocumentWindow window)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, window); v = v.GetVisualParent())
        {
            if (v is Button || v is Control { Name: TemplateParts.DragHandle })
            {
                return false;
            }

            if (v is Control { Name: TemplateParts.Header })
            {
                return true;
            }
        }

        return false;
    }

    private static void Release(MdiDocumentWindow window, PointerReleasedEventArgs e)
    {
        s_source = null;
        s_captureLost = false;
        WindowTabJoinService.End(TopLevel.GetTopLevel(window) is { } top ? e.GetPosition(top) : null);
    }

    /// <summary>창 안 보이는 문서 탭 줄(숨은 탭 본문 칸 아래로는 내려가지 않는다). 없으면 null.</summary>
    internal static DocumentTabStrip? VisibleStrip(Visual root) =>
        TabSlotMarker.Descendants(root).OfType<DocumentTabStrip>().FirstOrDefault(s => s.IsEffectivelyVisible && s.Bounds.Width > 0 && s.Bounds.Height > 0);

    /// <summary>
    /// 강조 클래스(MdiTabJoinDrag 와 같은 이름). 관리형 창은 끄는 동안 전부에 armed 를 걸지 않는다(로고 자리에 합치기 아이콘이 겹친다) —
    /// 로고 칸에 머물 때만 armed 를 함께 걸어 테마의 아이콘 강조(.pdTabJoinTarget.pdTabJoinOnIcon → PART_TabJoinIcon)가 보이게 한다.
    /// </summary>
    internal static void SetClasses(StyledElement e, bool armed, TabJoinKind? hover, bool source)
    {
        e.Classes.Set(MdiTabJoinDrag.ArmedClass, e is SdiTitleBar ? armed : hover == TabJoinKind.Icon);
        e.Classes.Set(MdiTabJoinDrag.TargetClass, hover is not null);
        e.Classes.Set(MdiTabJoinDrag.OnIconClass, hover == TabJoinKind.Icon);
        e.Classes.Set(MdiTabJoinDrag.OnStripClass, hover == TabJoinKind.TabStrip);
        e.Classes.Set(MdiTabJoinDrag.SourceClass, source);
    }

    /// <summary>관리형 SDI 창 1개 어댑터. 표적 = 제목줄 로고(테마 PdAppLogo, 숨었으면 제목줄 왼쪽 정사각형)와 보이는 탭 줄.</summary>
    private sealed class ManagedSurface(MdiDocumentWindow window) : IWindowTabJoinSurface
    {
        private DocumentTabStrip? _strip;

        private ManagedDockWindowDocument? Managed => window.DataContext as ManagedDockWindowDocument;

        // 창마다 달라야 한다 — Managed.Id 는 관리형 창 전부 "IDockWindow" 로 같아 원천 키 제외(TabJoinRules.Pick)에 모든 표적이 걸렸다
        public string Key => s_keys.GetValue(window, _ => "managed-" + System.Threading.Interlocked.Increment(ref s_keySeq));

        public IRootDock? Root => Managed?.Window?.Layout;

        public Visual? Frame => window;

        public IEnumerable<(TabJoinKind Kind, Visual Element, Rect Local)> TargetElements()
        {
            var header = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == TemplateParts.Header);
            var logo = header?.GetVisualDescendants().OfType<Image>().FirstOrDefault(i => i.Name == "PdAppLogo" && i.IsEffectivelyVisible && i.Bounds.Width > 0);
            if (logo is not null)
            {
                yield return (TabJoinKind.Icon, logo, new Rect(logo.Bounds.Size).Inflate(2));
            }
            else if (header is { Bounds.Height: > 0 })
            {
                yield return (TabJoinKind.Icon, header, new Rect(0, 0, header.Bounds.Height, header.Bounds.Height));
            }

            _strip = VisibleStrip(window);
            if (_strip is { } strip)
            {
                yield return (TabJoinKind.TabStrip, strip, new Rect(strip.Bounds.Size));
            }
        }

        public void ShowTabJoin(bool armed, TabJoinKind? hover, bool source)
        {
            SetClasses(window, armed, hover, source);
            _strip?.Classes.Set(MdiTabJoinDrag.TargetClass, hover == TabJoinKind.TabStrip);
        }
    }
}

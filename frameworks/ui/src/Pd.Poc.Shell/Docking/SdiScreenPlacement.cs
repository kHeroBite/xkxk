// MDI↔SDI 전환 때 창의 화면상 보이는 사각형을 그대로 잇는 좌표 계산(MDI 문서 창 PART_OuterBorder ↔ SDI 보이는 프레임) — 플랫폼 무관, 보이는 프레임 측정은 App.VisibleFrameOf(데스크톱 DWM)가 공급
using Pd.Poc.Shell.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 사용자 요구(C2): MDI↔SDI 전환 때 창이 모니터 기준으로 같은 자리·같은 크기로 보인다.
/// - 분리(Float): 전환 직전 MDI 창의 보이는 사각형(PART_OuterBorder, 화면 물리 px)을 재 두고, 띄운 SDI 의 보이는 프레임이 그 사각형이 되게
///   위치·크기를 맞춘다. 보이는 프레임 = App.VisibleFrameOf(Win32 DWMWA_EXTENDED_FRAME_BOUNDS — 보이지 않는 크기 조절 테두리 제외),
///   공급자가 없으면 창 사각형(Position + FrameSize). Dock 이 준 x,y(MDI 클라이언트 기준)를 그대로 쓰던 어긋남(테두리·제목줄·DPI)을 실측으로 없앤다.
/// - 재결합(Redock): SDI 의 마지막 보이는 프레임을 MDI 배치 패널 좌표(DIP)로 바꿔 MdiBounds 에 넣는다. SdiRedockPolicy 가 Keep 이면(요건 4 — 50% 이상 보임) 그 자리·크기 그대로 두고, 아니면 MDI 영역 안으로 보정한다.
/// 최대화·최소화 창, 비전경 시험(--bg-pos), OS 창이 아닌 호스트(웹 관리형 창 — SdiRedockOutcome.FrameUnavailable, 기존 PreFloatBounds 경로), MDI 에 아직 그려지지 않은 문서(레이아웃 복원·새 문서)는
/// 건드리지 않는다(기존 경로 그대로). 화면 밖 SDI 는 SdiWindowHost 의 MDI-12 점검이 맡는다.
/// </summary>
internal static class SdiScreenPlacement
{
    private const int Retries = 2; // 표시(OnOpened 맞춤)·배치·DPI 변경이 뒤따르는 경우 낮은 우선순위로 다시 맞추는 횟수

    /// <summary>MDI 문서 창의 화면상 보이는 사각형(물리 px). 보통 상태가 아니거나 MDI 배치에 그려져 있지 않으면 null.</summary>
    public static PixelRect? MdiScreenRect(PocDocument doc) =>
        MdiTabGroup.WindowOf(doc)!.MdiState == MdiWindowState.Normal && Locate(doc, viaGroup: true) is { } at ? ScreenRect(at.Border) : null; // 묶음 안 문서는 묶음 창(비활성 탭 포함)

    /// <summary>SDI 의 보이는 프레임(물리 px). 보통 상태로 보이는 OS 창이 아니면 null.</summary>
    public static PixelRect? SdiScreenRect(IDockWindow? window) =>
        !PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode) && window?.Host is Window { IsVisible: true, WindowState: WindowState.Normal } w ? VisibleFrame(w) : null;

    /// <summary>띄운 SDI 의 보이는 프레임을 target 에 맞춘다(지금 한 번 + Background 재확인).</summary>
    public static void PlaceSdi(IDockWindow? window, PixelRect target, Action<string> log)
    {
        if (!PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode) && window?.Host is Window w)
        {
            FitSdi(w, target, log, Retries, null);
        }
    }

    /// <summary>
    /// 잴 MDI 사각형이 없는 분리(MDI 가 최대화됐거나 아직 그려지지 않은 새 문서): Dock 이 준 좌표는 메인 창 위치와 무관해 주 모니터에 뜬다(실측 메인 x=1600 에서 요구 482,907).
    /// 메인과 다른 모니터에 떴으면 같은 상대 위치로 메인 모니터에 옮긴다. 복원은 뒤따르는 ApplyWindow 가 저장 좌표로 덮으므로 다시 맞추지 않는다(retries 0).
    /// </summary>
    public static void PlaceOnMainMonitor(IDockWindow? window, Action<string> log)
    {
        if (PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode) || window?.Host is not Window { IsVisible: true } w
            || (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { IsVisible: true } main
            || ViewModels.MonitorMenuViewModel.Provider?.GetMonitors() is not { Count: > 1 } monitors)
        {
            return;
        }

        var frame = VisibleFrame(w);
        var mainMonitor = MonitorSelection.LargestOverlap(monitors, VisibleFrame(main));
        var current = MonitorSelection.LargestOverlap(monitors, frame);
        if (mainMonitor is not null && current is not null && mainMonitor.Id != current.Id)
        {
            FitSdi(w, MonitorSelection.Relocate(frame, current.WorkingArea, mainMonitor.WorkingArea), log, 0, null);
        }
    }

    /// <summary>포인터 좌표 없이 factory.FloatDockable 을 직접 부른 경로(탭 메뉴 '모두 분리'·패널 띄우기)용: 문서가 든 창을 찾아 같은 규칙을 적용한다.</summary>
    public static void PlaceOnMainMonitor(IRootDock? root, IDockable? item)
    {
        if (root is not null && item is not null)
        {
            PlaceOnMainMonitor(LayoutSerializer.FindWindowOf(root, item), Console.WriteLine);
        }
    }

    /// <summary>
    /// 보이는 프레임과 target 의 차이만큼 위치·클라이언트 크기를 옮긴다. expected = 직전에 이 함수가 놓은 위치 —
    /// 재확인 때 위치가 그와 다르면 다른 경로(레이아웃 복원 SetPosition·사용자 끌기)가 옮긴 것이라 손대지 않는다.
    /// </summary>
    private static void FitSdi(Window w, PixelRect target, Action<string> log, int retries, PixelPoint? expected)
    {
        if (expected is { } e && w.Position != e)
        {
            return;
        }

        if (w.IsVisible && w.WindowState == WindowState.Normal)
        {
            var frame = VisibleFrame(w);
            if (frame != target)
            {
                var s = w.RenderScaling;
                w.Position = new PixelPoint(w.Position.X + target.X - frame.X, w.Position.Y + target.Y - frame.Y);
                if (frame.Width != target.Width || frame.Height != target.Height)
                {
                    w.Width = Math.Max(1, w.ClientSize.Width + (target.Width - frame.Width) / s);
                    w.Height = Math.Max(1, w.ClientSize.Height + (target.Height - frame.Height) / s);
                }

                log($"[sdi] place frame {Str(frame)} -> target {Str(target)} scale={s} left={retries}");
            }

            expected = w.Position;
        }

        if (retries > 0)
        {
            Dispatcher.UIThread.Post(() => FitSdi(w, target, log, retries - 1, expected), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 재결합 직전 추정: 같은 문서 영역(docs)의 다른 MDI 창이 놓인 배치 패널로 frame 을 MdiBounds 로 바꾼다(처음부터 그 자리에 그려지게).
    /// 그런 창이 없으면 null(호출자는 기존 PreFloatBounds·계단 위치를 쓰고, PlaceMdi 가 배치 뒤 바로잡는다).
    /// </summary>
    public static DockRect? EstimateMdiBounds(IDock docs, PixelRect frame)
    {
        foreach (var (panel, child, border, doc) in Entries())
        {
            if (ReferenceEquals(doc.Owner, docs) && MdiBoundsFor(panel, child, border, frame) is { } b)
            {
                return b.Bounds;
            }
        }

        return null;
    }

    /// <summary>
    /// 재결합 직전 표시: 정책이 Keep 이고 document.MdiBounds 가 그 추정값이면 첫 배치부터 영역 안으로 당겨지지 않게 놓은 값으로 등록한다.
    /// 프레임을 못 쟀거나(FrameUnavailable) 조건 미달(Clamp)이면 아무것도 하지 않는다. 추정할 다른 창이 없으면 FitMdi 가 배치 뒤 등록한다.
    /// </summary>
    public static void KeepPlaced(PocDocument document, IDock docs, PixelRect? frame)
    {
        if (frame is not { } f)
        {
            return;
        }

        foreach (var (panel, child, border, doc) in Entries())
        {
            if (ReferenceEquals(doc.Owner, docs) && MdiBoundsFor(panel, child, border, f) is { Keep: true } r && r.Bounds.Equals(document.MdiBounds))
            {
                CenterDropTitleAlign.Mark(document, r.Bounds);
                return;
            }
        }
    }

    /// <summary>재결합한 MDI 창의 보이는 사각형이 frame(SDI 마지막 보이는 프레임) 자리에 오게 MdiBounds 를 맞춘다. 배치 전이면 Loaded 뒤 다시.</summary>
    public static void PlaceMdi(PocDocument doc, PixelRect frame, Action<string> log) => FitMdi(doc, frame, log, Retries);

    private static void FitMdi(PocDocument doc, PixelRect frame, Action<string> log, int retries)
    {
        if (doc.MdiState != MdiWindowState.Normal)
        {
            return;
        }

        if (Locate(doc) is { } at && MdiBoundsFor(at.Panel, at.Child, at.Border, frame) is { } placed)
        {
            var b = placed.Bounds;
            var o = doc.MdiBounds;
            var changed = Math.Abs(o.X - b.X) >= 0.5 || Math.Abs(o.Y - b.Y) >= 0.5 || Math.Abs(o.Width - b.Width) >= 0.5 || Math.Abs(o.Height - b.Height) >= 0.5;
            if (placed.Keep)
            {
                CenterDropTitleAlign.Mark(doc, changed ? b : o); // 정책 충족 — 이후 배치(Arrange)가 영역 안으로 되돌리지 않는다. 사용자가 끌면 값이 달라져 풀린다
            }

            if (changed)
            {
                doc.MdiBounds = WindowMinSize.AtLeast(b); // 최소 크기 미만으로 맞추지 않는다(D-26)
                log($"[sdi] redock frame {Str(frame)} -> mdi ({b.X:F1},{b.Y:F1},{b.Width:F1}x{b.Height:F1}) keep={placed.Keep} left={retries}");
            }
        }

        if (retries > 0)
        {
            Dispatcher.UIThread.Post(() => FitMdi(doc, frame, log, retries - 1), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 화면 물리 px 사각형 → 배치 패널 DIP 의 MdiBounds. 패널 자식(MdiBounds 로 배치되는 항목)과 PART_OuterBorder 사이 여백(그림자 여백 등)을 더하고,
    /// 정책이 Keep 이면 그대로 쓰고(Keep=true), 아니면 MDI 영역보다 크면 줄이고 영역 밖이면 안으로 옮긴다. 패널·항목이 아직 배치 전(크기 0)이면 null.
    /// </summary>
    private static (DockRect Bounds, bool Keep)? MdiBoundsFor(MdiLayoutPanel panel, Control child, Border border, PixelRect frame)
    {
        var area = panel.Bounds.Size;
        if (area.Width <= 0 || area.Height <= 0 || child.Bounds.Width <= 0 || border.Bounds.Width <= 0 || border.TranslatePoint(default, child) is not { } inset)
        {
            return null;
        }

        var s = TopLevel.GetTopLevel(panel)?.RenderScaling ?? 1.0;
        var origin = panel.PointToScreen(default);
        double extraW = child.Bounds.Width - border.Bounds.Width, extraH = child.Bounds.Height - border.Bounds.Height;
        double frameX = (frame.X - origin.X) / s, frameY = (frame.Y - origin.Y) / s, frameW = frame.Width / s, frameH = frame.Height / s;
        if (SdiRedockPolicy.Decide(new Rect(frameX, frameY, frameW, frameH), new Rect(area)) == SdiRedockOutcome.Keep)
        {
            return (new DockRect(frameX - inset.X, frameY - inset.Y, frameW + extraW, frameH + extraH), true);
        }

        var w = Math.Min(frameW + extraW, area.Width);
        var h = Math.Min(frameH + extraH, area.Height);
        var x = Math.Clamp(frameX - inset.X, 0, area.Width - w);
        var y = Math.Clamp(frameY - inset.Y, 0, area.Height - h);
        return (new DockRect(x, y, w, h), false);
    }

    /// <summary>보이는 프레임(물리 px). 데스크톱은 DWM 값, 공급자가 없거나 못 얻으면 창 사각형.</summary>
    private static PixelRect VisibleFrame(Window w) =>
        App.VisibleFrameOf?.Invoke(w) ?? new PixelRect(w.Position, PixelSize.FromSize(w.FrameSize ?? w.ClientSize, w.RenderScaling));

    private static PixelRect? ScreenRect(Visual v)
    {
        var tl = v.PointToScreen(default);
        var br = v.PointToScreen(new Point(v.Bounds.Width, v.Bounds.Height));
        return br.X > tl.X && br.Y > tl.Y ? new PixelRect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y) : null;
    }

    /// <summary>SDI 로 분리하기 전에 이 문서의 MDI 창에서 접어 둔 기능 버튼 줄을 원래대로 되돌린다(접힌 줄 없으면 아무 일 없음).</summary>
    public static void RestoreStrip(PocDocument doc)
    {
        if (Locate(doc) is { } at)
        {
            MdiStripFold.Restore(at.Child);
        }
    }

    /// <summary>viaGroup=true 면 묶음 안 문서는 같은 묶음 창 항목으로도 찾는다(Entries 의 Doc 은 묶음의 활성 탭이라 비활성 탭은 못 찾는다).</summary>
    private static (MdiLayoutPanel Panel, Control Child, Border Border, PocDocument Doc)? Locate(PocDocument doc, bool viaGroup = false)
    {
        var group = viaGroup ? MdiTabGroup.GroupOf(doc) : null;
        foreach (var e in Entries())
        {
            if (ReferenceEquals(e.Doc, doc) || (group is not null && ReferenceEquals(MdiTabGroup.GroupOf(e.Doc), group)))
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>주 창의 MDI 배치 패널마다 문서 항목(패널 자식)과 그 MDI 창 바깥 테두리(PART_OuterBorder, 템플릿 뿌리라 첫 번째로 만난다).</summary>
    private static IEnumerable<(MdiLayoutPanel Panel, Control Child, Border Border, PocDocument Doc)> Entries()
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } top)
        {
            yield break;
        }

        foreach (var panel in top.GetVisualDescendants().OfType<MdiLayoutPanel>())
        {
            foreach (var child in panel.Children.OfType<Control>())
            {
                if (WindowCommands.FindDocument(child.DataContext) is PocDocument doc
                    && child.GetSelfAndVisualDescendants().OfType<MdiDocumentWindow>().FirstOrDefault() is { } mdw
                    && mdw.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == TemplateParts.OuterBorder) is { } border)
                {
                    yield return (panel, child, border, doc);
                }
            }
        }
    }

    private static string Str(PixelRect r) => $"({r.X},{r.Y},{r.Width}x{r.Height})";
}

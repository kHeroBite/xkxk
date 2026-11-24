// 기울기 열기 첫 렌더 예열(K13) — depth.open=tilt 가 켜지면 유휴 때 기울인 문서 창 견본을 불투명한 창 바탕 아래에 한 프레임 그려 원근 렌더 첫 사용 비용을 앞당긴다
using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.views.Depth;

/// <summary>
/// 문제: 프로세스에서 처음으로 Rotate3DTransform 이 걸린 문서 창을 그리는 프레임이 60~80ms 더 든다. 같은 프로세스 두 번째 열기부터는 classic 과
/// 같은 수준이고, 화면 밖 비트맵(RenderTargetBitmap)에 그려 두는 예열로는 줄지 않았다(perf2 실측 — 창 합성기의 GPU 경로 첫 사용 비용으로 본다).
/// 그래서 실제 창 합성기가 그리게 하되 화면 결과는 같도록, 창 템플릿의 불투명 바탕(테두리) 바로 아래 층에 같은 구성(바탕·그림자·글자 + 같은 각도·원근의
/// 3D 회전)의 견본을 넣어 한 번 그려지게 한 뒤 뗀다. 바탕이 완전 불투명(단색 알파 255·불투명도 1)이 아니면 가려지지 않으므로 하지 않는다.
/// 시점: 효과가 켜지고 전환 움직임 시간(theme.style.motion.&lt;값&gt;.ms) 뒤 유휴 우선순위 — 전환 화면을 먼저 그리게 한다.
/// 켬/끔은 기존 뷰 예열 옵션(UiSettings.PrewarmViews)을 따르고, 웹은 설정 키 depth.webPrewarm 이 더 켜져야 한다. 프로세스당 1회, 실패해도 열기에는 영향이 없다.
/// 견본 글자는 지금 언어의 자동 문서 제목·숫자·날짜 형식에서 만든다(표 내용과 같은 글자 종류, 고정 문구 없음). 크기는 기준 화면 크기다.
/// </summary>
public static class DepthPrewarm
{
    private static bool s_hooked;
    private static bool s_scheduled;
    private static bool s_done;
    private static WeakReference<Visual>? s_root;

    /// <summary>시작 때 1회(DepthTransitions.Attach): 디자인 테마 실효값 변화를 듣고, 지금 켜져 있으면 바로 예약한다.</summary>
    public static void Attach(Visual root)
    {
        s_root = new WeakReference<Visual>(root);
        if (!s_hooked)
        {
            s_hooked = true;
            DesignThemeResolver.Changed += Schedule;
        }

        Schedule();
    }

    private static void Schedule()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Schedule);
            return;
        }

        if (s_done || s_scheduled || !Wanted())
        {
            return;
        }

        s_scheduled = true;
        DispatcherTimer.RunOnce(AfterFrames, TimeSpan.FromMilliseconds(DesignThemeResolver.MotionMs()), DispatcherPriority.Background);
    }

    /// <summary>
    /// 움직임 시간 뒤에도 전환 화면이 아직 그려지는 중일 수 있으므로(느린 전환 실측 352ms), 그 뒤 렌더된 두 프레임을 더 기다렸다가 유휴 우선순위로 그린다.
    /// 원격 전환 측정(처리 끝 뒤 두 프레임)보다 늦게 시작한 두 프레임이라 그 구간과 겹치지 않는다.
    /// </summary>
    private static void AfterFrames()
    {
        if (s_root is not null && s_root.TryGetTarget(out var root) && TopLevel.GetTopLevel(root) is { } top)
        {
            top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(Run, DispatcherPriority.Background)));
            return;
        }

        s_scheduled = false;
    }

    /// <summary>
    /// 데스크톱 창은 항상, 웹(브라우저 호스트)은 설정 키 depth.webPrewarm(DepthDiag.WebPrewarm, 기본 끔)이 켜졌을 때만 — dtheme6 K13 에서 웹 열기 비가 1.29 로 바뀌어 후보로 둔다.
    /// 모바일(Android·iOS 앱)은 K13 범위 밖이고 A16 회귀 위험이 있어 하지 않는다.
    /// </summary>
    private static bool Wanted() =>
        (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
         || (OperatingSystem.IsBrowser() && DepthDiag.WebPrewarm))
        && UiSettings.Current.PrewarmViews && DepthTransitions.OpenAllowed();

    private static void Run()
    {
        s_scheduled = false;
        if (s_done || !Wanted() || s_root is null || !s_root.TryGetTarget(out var root) || TopLevel.GetTopLevel(root) is not { } top)
        {
            return;
        }

        s_done = true; // 성공·실패와 무관하게 프로세스당 1회
        var t0 = Environment.TickCount64;
        try
        {
            if (CoverOf(top) is not { } cover || cover.Parent is not Panel layer)
            {
                Note(0, placed: false);
                return; // 가릴 불투명 바탕이 없음(웹·투명 창) — 화면 결과가 바뀔 수 있으므로 하지 않는다
            }

            var size = root.Bounds.Size;
            var sample = Sample(size);
            layer.Children.Insert(layer.Children.IndexOf(cover), sample); // 바탕 아래 층 — 바탕이 덮어 화면에는 보이지 않는다
            // 붙인 뒤 두 번째 프레임 콜백 = 견본이 든 프레임을 한 번 그린 뒤. 그때 떼고 소요(붙임~뗌)를 남긴다
            void Detach()
            {
#if PD_DIAGNOSTICS
                if (Environment.GetEnvironmentVariable("PD_DIAG_TILT_PREWARM_HOLD") == "1")
                {
                    PdLog.Info(PdLogCategory.System, "기울기 예열 견본 유지(진단 HOLD)"); // i18n-ok 진단 로그
                    return; // 진단 전용: 견본을 붙여 둔 채로 두어 화면 캡처가 예열 없는 상태와 같은지 비교한다(시각 동일성 근거)
                }
#endif
                if (layer.Children.Remove(sample))
                {
                    Note(Environment.TickCount64 - t0, placed: true);
                }
            }

            top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => Detach()));
            DispatcherTimer.RunOnce(Detach, TimeSpan.FromMilliseconds(DesignThemeResolver.MotionMs())); // 프레임이 오지 않는 창(최소화)에서도 반드시 뗀다
        }
        catch (Exception ex)
        {
            PdLog.Info(PdLogCategory.System, $"기울기 예열 실패: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
        }
    }

    /// <summary>창 템플릿 패널의 자식 중 패널 전체를 덮는 완전 불투명 단색 테두리(창 바탕). 없으면 null.</summary>
    private static Border? CoverOf(TopLevel top)
    {
        if (top.GetVisualChildren().FirstOrDefault() is not Panel panel)
        {
            return null;
        }

        return panel.Children.OfType<Border>().FirstOrDefault(b =>
            b.IsVisible && b.Opacity >= 1 && b.OpacityMask is null && b.Background is ISolidColorBrush { Opacity: >= 1 } s && s.Color.A == byte.MaxValue
            && b.Bounds.Width >= panel.Bounds.Width && b.Bounds.Height >= panel.Bounds.Height);
    }

    /// <summary>
    /// 견본: 문서 창 템플릿(MdiDocumentWindowTheme.axaml)과 같은 층과 리소스 — 바깥 둥근 테두리·그림자(PART_OuterBorder), 둥근 잘라내기(PdClip),
    /// 제목줄(문서 기호 PathIcon·제목 글자·닫기 Path), 둥근 본문 잘라내기(PART_ContentBorder, 바탕 PdStyleSurface) 안 글자 — 를
    /// 열기 전환 첫 프레임과 같은 각도·원근(DepthTransitions.OpenStartTransform)으로 기울인다.
    /// </summary>
    private static Control Sample(Size size)
    {
        var header = new Border
        {
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children = { R(new PathIcon(), PathIcon.DataProperty, "PdIconDocument"), new TextBlock { Text = WindowCommands.AutoTitle(DocumentKind.Grid, 1) }, R(new Avalonia.Controls.Shapes.Path(), Avalonia.Controls.Shapes.Path.DataProperty, "PdCapCloseGeometry") },
            },
        };
        R(header, Border.HeightProperty, "PdMdiTitleHeight");
        R(header, Border.BackgroundProperty, "PdChromeTitleActive");
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        var content = new xkxk.frameworks.ui.Controls.ClipBorder { Child = Body() };
        R(content, Border.BackgroundProperty, "PdStyleSurface");
        R(content, Border.CornerRadiusProperty, "PdChromeContentCorner");
        var clip = new xkxk.frameworks.ui.Controls.ClipBorder { ClipToBounds = true, Child = new DockPanel { Children = { header, content } } };
        R(clip, Border.CornerRadiusProperty, "PdChromeInnerCorner");
        R(clip, Border.PaddingProperty, "PdChromeFramePadding");
        R(clip, Border.BorderThicknessProperty, "PdChromeHighlightThickness");
        R(clip, Border.BorderBrushProperty, "PdBrushWindowHighlight");
        var outer = new Border { Child = clip, BorderThickness = new Thickness(1), RenderTransform = DepthTransitions.OpenStartTransform(), Width = size.Width, Height = size.Height };
        R(outer, Border.CornerRadiusProperty, "PdChromeCorner");
        R(outer, Border.BorderBrushProperty, "PdChromeBorderActive");
        R(outer, Border.BackgroundProperty, "PdChromeTitleActive");
        R(outer, Border.BoxShadowProperty, "PdMdiShadow");
        var root = new Panel { Children = { outer }, IsHitTestVisible = false, Focusable = false, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        Avalonia.Input.KeyboardNavigation.SetTabNavigation(root, Avalonia.Input.KeyboardNavigationMode.None); // 붙어 있는 두 프레임 동안 Tab 이 견본 단추로 들어가지 않게
        return root;
    }

    /// <summary>
    /// 본문: 표 문서 화면의 그림 종류 — 단추, 선 있는 칸(표 셀 테두리) 안 글자, 가로·세로 스크롤바. 칸 수는 견본 글자 조각 수만큼(고정 개수 없음).
    /// 테마 기본 스타일이 그대로 적용되도록 표준 컨트롤만 쓴다.
    /// </summary>
    private static Control Body()
    {
        var cells = new Avalonia.Controls.Primitives.UniformGrid();
        foreach (var part in SampleText().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var cell = new Border { BorderThickness = new Thickness(0, 0, 1, 1), Child = new TextBlock { Text = part } };
            R(cell, Border.BorderBrushProperty, "PdBrushGridLine");
            cells.Children.Add(cell);
        }

        var bars = new DockPanel { Children = { new Avalonia.Controls.Primitives.ScrollBar { Orientation = Avalonia.Layout.Orientation.Horizontal, AllowAutoHide = false }, new Avalonia.Controls.Primitives.ScrollBar { Orientation = Avalonia.Layout.Orientation.Vertical, AllowAutoHide = false }, cells } };
        DockPanel.SetDock(bars.Children[0], Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(bars.Children[1], Avalonia.Controls.Dock.Right);
        var button = new Button { Content = WindowCommands.AutoTitle(DocumentKind.Form, 2) };
        DockPanel.SetDock(button, Avalonia.Controls.Dock.Top);
        return new DockPanel { Children = { button, bars } };
    }

    /// <summary>속성을 앱·창 리소스에 묶는다(DynamicResource 와 같은 효과 — 테마 값이 그대로 들어간다).</summary>
    private static T R<T>(T c, AvaloniaProperty p, string key) where T : Control
    {
        c.Bind(p, c.GetResourceObservable(key));
        return c;
    }

    /// <summary>지금 언어의 자동 문서 제목(표·양식·간트)과 숫자·날짜 형식 — 표 셀에 나오는 글자 종류를 고정 문구 없이 모은다.</summary>
    private static string SampleText()
    {
        var c = CultureInfo.CurrentCulture;
        var now = DateTime.Now;
        return string.Join(" ",
            WindowCommands.AutoTitle(DocumentKind.Grid, 1), WindowCommands.AutoTitle(DocumentKind.Form, 2), WindowCommands.AutoTitle(DocumentKind.Gantt, 3),
            (1234567.89).ToString("N2", c), (-0.5).ToString("P0", c), now.ToString("d", c), now.ToString("t", c), now.ToString("MMMM dddd", c));
    }

    /// <summary>진단 빌드만: 로그 한 줄과 지표 prewarm_tilt_ms·prewarm_tilt_placed(perf.get metrics).</summary>
    private static void Note(long ms, bool placed)
    {
#if PD_DIAGNOSTICS
        PdLog.Info(PdLogCategory.System, $"기울기 예열 {ms}ms 그림={(placed ? 1 : 0)}"); // i18n-ok 진단 로그
        PerfProbe.Current.SetMetric("prewarm_tilt_ms", ms);
        PerfProbe.Current.SetMetric("prewarm_tilt_placed", placed ? 1 : 0);
#endif
    }
}

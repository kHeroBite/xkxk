// MDI 영역 크기가 바뀌어 제목줄이 닿지 않게 된 문서 창을 계단 기본 위치로 옮기는 감시자(요건 5) — 데스크톱·웹·태블릿 공용
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 메인 창이 줄면 밀어 둔 창·끌어 놓은 창·50% 규칙으로 재결합한 창은 제목 줄을 잡을 수 없게 될 수 있다. 그런 창만 옮긴다.
/// 잡을 수 없는 창은 Dock 기본 배치가 영역 안으로 당겨 그리므로 그려진 사각형이 아니라 저장값(MdiBounds)으로 판정한다.
/// 잡을 곳이 남은 창·최소화·최대화 창은 건드리지 않는다. 평상시에는 타이머가 없다. 비전경 시험(--bg-pos)에서는 끈다.
/// </summary>
public static class MdiVisibilityGuard
{
    /// <summary>연속 크기 변경을 1회로 합치는 대기 시간(ms).</summary>
    public const int DebounceMs = 400;

    private static readonly HashSet<MdiLayoutPanel> s_pending = new();
    private static readonly RecoveryDebounce s_debounce = new(DebounceMs, new DispatcherBusyClock(), Flush);
    private static bool s_installed;

    /// <summary>판정 횟수(진단).</summary>
    public static int EvaluationCount { get; private set; }

    /// <summary>옮긴 창 수 누계(진단).</summary>
    public static int MovedCount { get; private set; }

    /// <summary>셸 기동 때 1회. 모든 MDI 배치 패널의 크기 변경을 받는다.</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        Visual.BoundsProperty.Changed.AddClassHandler<MdiLayoutPanel>((panel, _) =>
        {
            s_pending.Add(panel);
            s_debounce.Trigger();
        });
    }

    private static void Flush()
    {
        var panels = s_pending.ToArray();
        s_pending.Clear();
        if (PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode))
        {
            Log("recovery off (background mode)");
            return;
        }

        foreach (var panel in panels)
        {
            Evaluate(panel);
        }
    }

    private static void Evaluate(MdiLayoutPanel panel)
    {
        EvaluationCount++;
        // 창 단위(탭 묶음이면 묶음): 위치·상태는 묶음에서 읽고 쓴다. 묶음 안 문서 자신의 값은 화면과 무관하다.
        var entries = new List<(Control Child, IMdiDocument Win, string Title, bool Locked)>();
        foreach (var child in panel.Children.OfType<Control>())
        {
            if (WindowCommands.FindDocument(child.DataContext) is PocDocument doc && MdiTabGroup.WindowOf(doc) is { } win)
            {
                var locked = win is MdiTabGroupDock g ? g.Documents.Any(d => WindowCommands.ChromeOf(d).IsLocked) : WindowCommands.ChromeOf(doc).IsLocked;
                entries.Add((child, win, (win as IDockable)?.Title ?? doc.Title, locked));
            }
        }

        var arranged = entries.Select(e => new RecoveryWindow(SavedRect(e.Win, e.Child), e.Locked,
            e.Win.MdiState == MdiWindowState.Minimized, e.Win.MdiState == MdiWindowState.Maximized, MdiGrabZoneReader.Read(e.Child))).ToArray();
        var bounds = entries.Select(e => e.Win.MdiBounds).ToArray();
        var targets = PlacementRecovery.EvaluateMdi(panel.Bounds.Size, arranged, bounds, PocDockFactory.NextMdiBounds);
        var moved = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i] is { } target)
            {
                Log($"recover '{entries[i].Title}' drawn={arranged[i].Bounds} -> ({target.X:F0},{target.Y:F0}) area={panel.Bounds.Size} locked={arranged[i].IsLocked}");
                entries[i].Win.MdiBounds = target;
                moved++;
            }
        }

        if (moved > 0)
        {
            MovedCount += moved;
            App.NotifyLayoutChanged(); // 끌기 없는 위치 변경 — 자동 저장 대상
        }
    }

    /// <summary>판정할 사각형. 저장값이 비어 있으면(아직 자리를 받지 않은 창) 그려진 사각형을 쓴다.</summary>
    private static Rect SavedRect(IMdiDocument win, Control child) =>
        win.MdiBounds is { Width: > 0, Height: > 0 } b ? new Rect(b.X, b.Y, b.Width, b.Height) : child.Bounds;

    private static void Log(string text)
    {
        PdLog.Info("mdi-guard", text);
#if PD_DIAGNOSTICS
        XkxkUi.Current.MainViewModel?.AddLog("[mdi-guard] " + text); // log.tail 에서도 보이게(진단 빌드 한정)
#endif
    }
}

#if PD_DIAGNOSTICS
/// <summary>진단 monitors.setVirtual 이 호스트(데스크톱)를 부르는 훅. 웹·Android 는 비어 있다.</summary>
public static class PlacementDiagHooks
{
    /// <summary>가상 모니터 수 변경. 오류 문구를 돌려주고, 성공하면 null.</summary>
    public static Func<int, string?>? SetVirtualMonitors { get; set; }

    /// <summary>변경을 실제 모니터 변경 이벤트처럼 감시자에게 알린다.</summary>
    public static Action? MonitorsApplied { get; set; }

    /// <summary>화면 밖 시험 창의 크기를 바꾼다(w, h = DIP). window.resize 는 OS 보정이 창을 화면 안으로 끌어와 화면 밖 가드에 막힌다.</summary>
    public static Func<double, double, bool>? ResizeMain { get; set; }
}
#endif

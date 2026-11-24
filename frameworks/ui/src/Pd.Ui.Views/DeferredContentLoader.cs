// 레이아웃 복원 때 가려진 문서 뷰의 생성을 뒤 프레임으로 미루는 진단 스위치(--layout-load deferred) — 보이는 문서는 같은 프레임에 실현
using Pd.Poc.Shell;
using Pd.Poc.Shell.Docking;
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Model.Core;
using Pd.Poc.Shell.Diagnostics;
#endif

namespace Pd.Ui.Views;

/// <summary>
/// DocumentViewLocator 가 부르는 문서 뷰 지연 실현 훅. 본문과 상태는 진단 빌드 전용이다.
/// DeferContent 는 Conditional 메서드라서 제품 빌드에서는 호출째 빠진다(예전 partial void 훅과 같은 성질).
/// </summary>
internal static class DeferredContentLoader
{
    /// <summary>
    /// 문서 뷰 실현을 미룰지 정한다. 미루면 deferred=true 로 두고 host.Child 에 자리표시를 넣은 뒤,
    /// 나중에 host.Child = App.DocumentViews.Create(doc) 로 바꾼다.
    /// </summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal static void DeferContent(PocDocument doc, ZoomDecorator host, ref bool deferred)
    {
#if PD_DIAGNOSTICS
        if (!Enabled || App.Shell is not { } shell || ShouldRealizeNow(doc, shell))
        {
            return;
        }

        deferred = true;
        var blank = new Border();
        blank.Bind(Border.BackgroundProperty, blank.GetResourceObservable("PdBrushBackground"));
        host.Child = blank;
        s_pending.Add((doc, host));
        s_deferredTotal++;
        if (s_firstDeferMs < 0) { s_firstDeferMs = PerfProbe.Current.NowMs; }
        if (!s_pumping)
        {
            s_pumping = true;
            Dispatcher.UIThread.Post(PumpOne, DispatcherPriority.Background); // 이번 프레임(보이는 문서)이 먼저 그려진 뒤 시작
        }
#endif
    }
#if PD_DIAGNOSTICS

    private const double VisibleThreshold = 0.25; // 내용 영역의 이 비율 이상이 위쪽 창에 가려지지 않으면 같은 프레임에 실현
    private const int Samples = 12; // 가림 비율 표본 격자(12×12)

    private static readonly List<(PocDocument Doc, ZoomDecorator Host)> s_pending = new();
    private static bool s_pumping;
    private static double s_firstDeferMs = -1;
    private static int s_deferredTotal;
    private static int s_blankFrames;
    private static bool? s_enabled;

    private static bool Enabled => s_enabled ??= ReadSwitch();

    private static bool ReadSwitch()
    {
        var a = App.StartupArgs;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Equals("--layout-load=deferred", StringComparison.OrdinalIgnoreCase)
                || (a[i].Equals("--layout-load", StringComparison.OrdinalIgnoreCase) && i + 1 < a.Count && a[i + 1].Equals("deferred", StringComparison.OrdinalIgnoreCase)))
            {
                PerfProbe.Current.SetText("layout_load_mode", "deferred");
                return true;
            }
        }

        return false;
    }

    /// <summary>즉시 실현할 문서: 활성, 창으로 뜬 문서(SDI), 위치를 모르는 문서(패널 탭 등), 보이는 비율이 문턱 이상인 문서.</summary>
    private static bool ShouldRealizeNow(PocDocument doc, IShellCommands shell)
    {
        if (ReferenceEquals(doc, shell.ActiveDocument)) { return true; }
        if (shell is WindowCommands wc && wc.IsFloating(doc)) { return true; }
        if (doc.MdiState == MdiWindowState.Minimized) { return false; }
        var r = doc.MdiBounds;
        if (r.Width <= 0 || r.Height <= 0) { return true; }
        var docs = shell.Documents;
        if (docs.Any(d => !ReferenceEquals(d, doc) && d.MdiState == MdiWindowState.Maximized && d.MdiZIndex > doc.MdiZIndex)) { return false; }
        var above = docs.Where(d => !ReferenceEquals(d, doc) && d.MdiZIndex > doc.MdiZIndex && d.MdiState == MdiWindowState.Normal
                                    && d.MdiBounds.Width > 0 && d.MdiBounds.Height > 0).Select(d => d.MdiBounds).ToList();
        if (above.Count == 0) { return true; }
        var visible = 0;
        for (var i = 0; i < Samples; i++)
        {
            for (var j = 0; j < Samples; j++)
            {
                var x = r.X + ((i + 0.5) * r.Width / Samples);
                var y = r.Y + ((j + 0.5) * r.Height / Samples);
                if (!above.Any(b => x >= b.X && x < b.X + b.Width && y >= b.Y && y < b.Y + b.Height)) { visible++; }
            }
        }

        return visible >= VisibleThreshold * Samples * Samples;
    }

    /// <summary>미룬 문서를 위쪽 창부터 한 프레임에 하나씩 실제 뷰로 바꾼다. 닫혔거나 재사용된 host 는 건너뛴다.</summary>
    private static void PumpOne()
    {
        var open = App.Shell?.Documents;
        s_pending.RemoveAll(p => !ReferenceEquals(p.Host.Tag, p.Doc) || open is null || !open.Contains(p.Doc));
        if (s_pending.Count == 0)
        {
            Finish();
            return;
        }

        var next = s_pending.OrderByDescending(p => p.Doc.MdiZIndex).First();
        s_pending.Remove(next);
        next.Host.Child = App.DocumentViews.Create(next.Doc);
        s_blankFrames++;
        if (TopLevel.GetTopLevel(next.Host) is { } top) { top.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(PumpOne, DispatcherPriority.Background)); }
        else { Dispatcher.UIThread.Post(PumpOne, DispatcherPriority.Background); }
    }

    private static void Finish()
    {
        s_pumping = false;
        if (s_firstDeferMs < 0) { return; }
        var all = PerfProbe.Current.NowMs - s_firstDeferMs;
        PerfProbe.Current.RecordOperation("ui.layout.realize.all", all, all); // 첫 지연부터 마지막 문서 실현까지
        PerfProbe.Current.SetMetric("layout_deferred_count", s_deferredTotal);
        PerfProbe.Current.SetMetric("layout_blank_frames", s_blankFrames); // 미룬 문서가 남아 있던 프레임 수(문서 1개 실현 = 1프레임)
        (s_firstDeferMs, s_deferredTotal, s_blankFrames) = (-1, 0, 0);
    }
#endif
}

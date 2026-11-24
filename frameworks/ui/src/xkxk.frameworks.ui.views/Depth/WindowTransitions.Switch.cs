// 전환 짝 나감의 '숨기기 직전' 촬영과 진단(WindowTransitions 의 partial, 21 설계 §4) — DOCK 탭 본문이 활성 알림보다 먼저 숨겨지는 플랫폼에서도 나가는 모습을 남긴다
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.views.Depth;

public static partial class WindowTransitions
{
    private static Hidden? s_hidden;                         // 숨기기 직전에 찍은 이전 탭 본문(활성 알림이 가져가거나 다음 유휴에 버린다)
    private static WeakReference<PocDocument>? s_shotAtSwitch; // 활성 알림에서 이미 찍은 문서(웹: 숨김이 한 프레임 뒤라 다시 찍지 않는다)
    private static SwitchNote? s_lastSwitch;
    private static int s_switchSeq;

    /// <summary>DOCK 탭 본문 칸이 숨기 직전(DocumentTabKeepAlive.SlotHiding). 활성 알림에서 이미 찍었거나 짝 나감이 꺼져 있으면 찍지 않는다(비용 0).</summary>
    private static void OnSlotHiding(Control view)
    {
        // 닫히는 문서는 닫기 덮개가 맡는다(s_closing)
        if (view.DataContext is not PocDocument doc || IsSame(s_shotAtSwitch, doc) || s_closing.ContainsKey(doc) || !DockExitPossible()
            || HostOf(view) is not { } host)
        {
            return;
        }

        var shot = Shot(view, host, MotionPlacement.DockTab);
        if (shot is null)
        {
            return;
        }

        s_hidden?.Shot.Image.Dispose();
        var hidden = s_hidden = new Hidden(new WeakReference<PocDocument>(doc), new Located(view, host, MotionPlacement.DockTab), shot);
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(s_hidden, hidden))
            {
                s_hidden = null; // 활성 알림이 오지 않았다(출처 없는 활성화 등)
                hidden.Shot.Image.Dispose();
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>OnActivatedFrom 이 부른다: 이전 문서를 지금 찾아 찍고, 이미 숨겨졌으면 숨기기 직전 그림을 쓴다.</summary>
    /// <summary>들어오는 화면에 전환 효과를 시작하고, 짝 나감 그림이 있으면 덮개에서 함께 재생한다(OnActivatedFrom 이 바로 또는 배치 뒤에 부른다).</summary>
    private static void StartSwitch(Located? at, string key, string[] sources, Located? before, Snapshot? shot, SwitchNote note)
    {
        var enterId = at is null ? MotionEffects.None : DesignThemeResolver.SwitchEffect(at.Placement, key, sources);
        if (at is null || enterId == MotionEffects.None || s_running.ContainsKey(at.View))
        {
            shot?.Image.Dispose(); // 열기 재생 중이면 열기를 우선한다
            return;
        }

        var exitId = DesignThemeResolver.SwitchExitEffect(at.Placement, enterId);
        if (shot is not null && exitId != MotionEffects.None && !ReferenceEquals(before!.View, at.View))
        {
            Ghost(shot, MotionDirection.Exit, exitId, note);
        }
        else
        {
            shot?.Image.Dispose(); // NAV 본문처럼 들어오는 화면과 같은 컨트롤이면 이전 모습을 따로 남길 수 없다
        }

        double? start = at.Placement == MotionPlacement.MdiOverlap ? DesignThemeResolver.Number(VisibleStartKey) : null;
        Play(at.View, MotionDirection.Enter, enterId, start, null);
    }

    private static (Located? Before, Snapshot? Shot) TakeLeaving(PocDocument prev, string key, string[] sources, SwitchNote note)
    {
        if (Locate(prev) is { } b)
        {
            note.PrevFound = true;
            if (ExitOf(b.Placement, key, sources) == MotionEffects.None)
            {
                return (null, null);
            }

            s_shotAtSwitch = new WeakReference<PocDocument>(prev);
            Dispatcher.UIThread.Post(() => s_shotAtSwitch = null, DispatcherPriority.Background);
            var shot = Shot(b.View, b.Host, b.Placement);
            note.Source = shot is null ? null : "activated";
            return (b, shot);
        }

        if (s_hidden is { } h && IsSame(h.Doc, prev) && ExitOf(MotionPlacement.DockTab, key, sources) != MotionEffects.None)
        {
            s_hidden = null;
            note.Source = "hidden";
            return (h.At, h.Shot);
        }

        return (null, null);
    }

    private static string ExitOf(MotionPlacement placement, string key, string[] sources) =>
        DesignThemeResolver.SwitchExitEffect(placement, DesignThemeResolver.SwitchEffect(placement, key, sources));

    private static bool DockExitPossible()
    {
        var sources = SwitchSources();
        return sources.Any(k => ExitOf(MotionPlacement.DockTab, k, sources) != MotionEffects.None);
    }

    private static Control? HostOf(Control view) =>
        view.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Classes.Contains(DockHostClass));

    private static bool IsSame(WeakReference<PocDocument>? weak, PocDocument doc) =>
        weak is not null && weak.TryGetTarget(out var d) && ReferenceEquals(d, doc);

    /// <summary>원격 motion.probe 의 lastSwitch(마지막 전환 1건).</summary>
    private static string LastSwitchJson()
    {
        static string B(bool v) => v ? "true" : "false";
        var n = s_lastSwitch;
        if (n is null)
        {
            return "null";
        }

        return new StringBuilder("{\"seq\":").Append(n.Seq.ToString(CultureInfo.InvariantCulture))
            .Append(",\"prevFound\":").Append(B(n.PrevFound))
            .Append(",\"source\":").Append(n.Source is null ? "null" : "\"" + n.Source + "\"")
            .Append(",\"ghost\":").Append(n.Ghost is null ? "null" : "\"" + n.Ghost + "\"")
            .Append(",\"tabHeaderFirst\":").Append(B(UiSettings.Current.TabHeaderFirst))
            .Append('}').ToString();
    }

    private sealed record Hidden(WeakReference<PocDocument> Doc, Located At, Snapshot Shot);

    /// <summary>전환 1건 진단. Source = activated(활성 알림에서 찍음)·hidden(숨기기 직전 그림)·null(짝 없음), Ghost = started·done·none(덮개 층 없음).</summary>
    private sealed class SwitchNote(int seq)
    {
        public int Seq { get; } = seq;

        public bool PrevFound { get; set; }

        public string? Source { get; set; }

        public string? Ghost { get; set; }
    }
}

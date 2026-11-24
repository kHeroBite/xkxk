// 창 닫기 효과의 시작(WindowTransitions 의 partial, 21 설계 §4.1) — DockableClosing 에서 찍고 DockableClosed 가 오면 덮개를 재생하며, 단계는 motion.probe lastClose 로 남긴다
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Pd.Poc.Shell.Busy;
using Pd.Poc.Shell.Theming;

namespace Pd.Ui.Views.Depth;

public static partial class WindowTransitions
{
    private static readonly Dictionary<IDockable, PendingClose> s_closing = new(ReferenceEqualityComparer.Instance);
    private static CloseNote? s_lastClose;
    private static int s_closeSeq;
    private static int s_closeStarted;
    private static int s_closeEnded;

    /// <summary>
    /// 닫히는 문서의 화면을 지금 찍어 두기만 한다(DepthTransitions 가 DockableClosing 을 넘긴다). 재생은 실제로 닫힌 뒤(OnDockableClosed)다.
    /// 화면이 남아 보이는지로 취소를 판정하지 않는다 — DOCK·NAV 는 본문 화면을 다시 쓴다(21 §4.1).
    /// </summary>
    internal static void OnDockableClosing(DockableClosingEventArgs e)
    {
        if (e.Dockable is not { } dockable)
        {
            return;
        }

        HookShell(); // NAV 현재 문서 판정에 셸이 있어야 한다
        var note = s_lastClose = new CloseNote(++s_closeSeq);
        EndCovers("close");
        if (s_closing.Remove(dockable, out var stale))
        {
            stale.Discard();
        }

        if (Locate(dockable) is not { } at)
        {
            note.Stage = "noTarget";
            return;
        }

        at = GroupWindowOf(at) ?? at; // MDI 안 탭 묶음의 탭이면 묶음 창 전체(21 §4.1)
        note.Placement = at.Placement.ToString();
        note.Group = at.Group;
        note.View = at.View.GetType().Name;
        if (s_running.TryGetValue(at.View, out var running))
        {
            running.Finish(); // 열기·전환 중 닫기 — 재생하지 않는 닫기여도 즉시 끝내고(21 §7), 있으면 제자리 모습을 찍는다
        }

#if PD_DIAGNOSTICS // 판정 전에 보던 값(원인 확인용). OldGap = 화면 DataContext 로 보던 옛 NAV 식
        note.ViewContext = at.View.DataContext?.GetType().Name;
        note.ClosingId = dockable.Id;
        note.ActiveId = s_shell?.ActiveDocument?.Id;
        note.ShownId = NavShown()?.Id;
        note.OldGap = at.Placement == MotionPlacement.Nav
            && !ReferenceEquals((at.View.DataContext as Pd.Poc.Shell.ViewModels.MainViewModel)?.ActiveDocument, dockable);
#endif
        if (e.Cancel)
        {
            note.Stage = "skipped";
            note.Reason = "canceled";
            return;
        }

        if (CloseGapOf(dockable, at) is { } gap)
        {
            note.Stage = "skipped";
            note.Reason = "gap";
            note.GapKind = gap;
            return;
        }

        var id = DesignThemeResolver.CloseEffect(at.Placement);
        note.Effect = id;
        if (id == MotionEffects.None)
        {
            note.Stage = "none";
            return;
        }

        if (IsMainBusy())
        {
            note.Stage = "busy"; // 진행 표시를 덮지 않는다(21 §4.1)
            return;
        }

        // 닫히면 사라질 창·탭 영역이 아니라 최상위 덮개 층 — 자리도 지금 그 층 좌표로 정한다
        if (TopLevel.GetTopLevel(at.View) is not { } top || AdornerLayer.GetAdornerLayer(top) is not { } layer)
        {
            note.Stage = "noLayer";
            return;
        }

        var shot = Shot(at.View, layer, at.Placement, at.Group);
        note.Shot = shot is not null;
        note.Base = shot?.Base;
        note.Cover = shot is null ? null : CoverJson(shot);
        if (shot is null)
        {
            note.Stage = "noShot";
            return;
        }

        var pending = new PendingClose(shot, id, CoverScope.Capture(at, layer), note);
        s_closing[dockable] = pending;
        note.Stage = "pending";
        Dispatcher.UIThread.Post(() => AfterClose(dockable, at, pending), DispatcherPriority.Background);
    }

    /// <summary>실제로 닫힘(취소면 오지 않는다). Closing 과 같은 호출 스택이라 닫기를 늦추지 않는다.</summary>
    private static void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable is not { } dockable || !s_closing.Remove(dockable, out var pending))
        {
            return;
        }

        if (TopLevel.GetTopLevel(pending.Shot.Host) is not { } top)
        {
            pending.Note.Stage = "detached";
            pending.Discard();
            return;
        }

        if (IsMainBusy())
        {
            pending.Note.Stage = "busy";
            pending.Discard();
            return;
        }

        pending.Cover = CloseCover.Start(pending, top);
    }

    private static void AfterClose(IDockable dockable, Located at, PendingClose pending)
    {
#if PD_DIAGNOSTICS // 원인 확인용 — 옛 판정이 같은 시점에 보던 값
        pending.Note.ViewVisible = TopLevel.GetTopLevel(at.View) is not null && at.View.IsEffectivelyVisible;
        pending.Note.HostLayerFound = AdornerLayer.GetAdornerLayer(at.Host) is not null;
#endif
        if (s_closing.TryGetValue(dockable, out var waiting) && ReferenceEquals(waiting, pending))
        {
            s_closing.Remove(dockable);
            pending.Note.Stage = "canceled"; // Closing 뒤 다른 처리기가 취소했다
            pending.Discard();
            return;
        }

        // 여기부터의 다음 닫기·전환이 덮개를 끊는다(닫기 자체의 활성 변경과 한 번의 '모두 닫기'는 끊지 않는다)
        pending.Cover?.Arm();
    }

    /// <summary>21 §4.1 정본 공백(현행 유지 = 효과 없음): NAV 의 현재가 아닌 문서, 관리형 SDI 창. DOCK 비활성 탭은 Locate 가 이미 null 이다.</summary>
    private static string? CloseGapOf(IDockable dockable, Located at)
    {
        if (at.View.FindAncestorOfType<ManagedWindowLayer>() is not null)
        {
            return "managed";
        }

        return at.Placement == MotionPlacement.Nav && !TakeNavCurrent(dockable) ? "nav" : null;
    }

    private static bool IsMainBusy() => BusyHooks.Service.IsVisible(BusyHooks.MainScope);

    /// <summary>원격 motion.probe 의 lastClose(마지막 닫기 1건 + 누적 수).</summary>
    private static string LastCloseJson()
    {
        static string S(string? v) => v is null ? "null" : "\"" + v + "\"";
        static string B(bool? v) => v is { } b ? (b ? "true" : "false") : "null";

        var n = s_lastClose;
        return new StringBuilder("{\"seq\":").Append((n?.Seq ?? 0).ToString(CultureInfo.InvariantCulture))
            .Append(",\"stage\":").Append(S(n?.Stage))
            .Append(",\"reason\":").Append(S(n?.Reason))
            .Append(",\"placement\":").Append(S(n?.Placement))
            .Append(",\"group\":").Append(B(n?.Group))
            .Append(",\"effect\":").Append(S(n?.Effect))
            .Append(",\"view\":").Append(S(n?.View))
            .Append(",\"shot\":").Append(B(n?.Shot))
            .Append(",\"viewVisible\":").Append(B(n?.ViewVisible))
            .Append(",\"hostLayerFound\":").Append(B(n?.HostLayerFound))
            .Append(",\"gapKind\":").Append(S(n?.GapKind))
            .Append(",\"base\":").Append(S(n?.Base))
            .Append(",\"viewContext\":").Append(S(n?.ViewContext))
            .Append(",\"closingId\":").Append(S(n?.ClosingId))
            .Append(",\"activeId\":").Append(S(n?.ActiveId))
            .Append(",\"shownId\":").Append(S(n?.ShownId))
            .Append(",\"oldGap\":").Append(B(n?.OldGap))
            .Append(",\"cover\":").Append(n?.Cover ?? "null")
            .Append(",\"started\":").Append(s_closeStarted.ToString(CultureInfo.InvariantCulture))
            .Append(",\"ended\":").Append(s_closeEnded.ToString(CultureInfo.InvariantCulture))
            .Append('}').ToString();
    }

    /// <summary>덮개 그림 진단(7차): 찍은 경로·배율·비트맵 화소·원본(화소)·대상(DIP). k = 대상×배율÷원본 이 1 이어야 1:1 이다.</summary>
    private static string CoverJson(Snapshot shot)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var g = shot.Geometry;
        return new StringBuilder("{\"mode\":\"").Append(shot.Mode).Append("\",\"scale\":").Append(N(g.Scale))
            .Append(",\"px\":[").Append(shot.Image.PixelSize.Width).Append(',').Append(shot.Image.PixelSize.Height)
            .Append("],\"dpi\":").Append(N(shot.Image.Dpi.X))
            .Append(",\"src\":[").Append(N(g.Source.Width)).Append(',').Append(N(g.Source.Height))
            .Append("],\"dest\":[").Append(N(g.Dest.Width)).Append(',').Append(N(g.Dest.Height))
            .Append("],\"k\":").Append(N(g.Dest.Width * g.Scale / g.Source.Width)).Append('}').ToString();
    }

    private sealed class PendingClose(Snapshot shot, string effect, CoverScope scope, CloseNote note)
    {
        public Snapshot Shot { get; } = shot;

        public string Effect { get; } = effect;

        public CoverScope Scope { get; } = scope;

        public CloseNote Note { get; } = note;

        public CloseCover? Cover { get; set; }

        public void Discard() => Shot.Image.Dispose();
    }

    private sealed class CloseNote(int seq)
    {
        public int Seq { get; } = seq;

        public string? Stage { get; set; }

        public string? Reason { get; set; }

        public string? Placement { get; set; }

        public bool? Group { get; set; } // MDI 안 탭 묶음 창 전체로 바꾼 닫기

        public string? Effect { get; set; }

        public string? View { get; set; }

        public bool? Shot { get; set; }

        public bool? ViewVisible { get; set; }

        public bool? HostLayerFound { get; set; }

        public string? GapKind { get; set; }

        public string? Base { get; set; }

        public string? ViewContext { get; set; }

        public string? ClosingId { get; set; }

        public string? ActiveId { get; set; }

        public string? ShownId { get; set; }

        public bool? OldGap { get; set; }

        public string? Cover { get; set; }
    }
}

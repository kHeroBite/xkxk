// Snap Assist 세션 흐름 — Pick·SelectZone·Skip·MoveFocus·Cancel, 다음 칸 이동(Advance·Refresh), 이벤트 감시(Watch)
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Docking;

public static partial class SnapAssistSession
{
    /// <summary>활성 칸에 후보 candidateIndex 번 창을 놓는다. 놓았으면 다음 남은 칸(칸 번호 순)으로 가고, 남은 칸이 없으면 끝낸다.</summary>
    public static bool Pick(int candidateIndex)
    {
        if (s_run is not { } r || candidateIndex < 0 || candidateIndex >= r.Current.Candidates.Count)
        {
            return false;
        }

        var c = r.Current.Candidates[candidateIndex];
        var cell = r.ActiveCell;
        bool done;
        s_applying = true;
        try
        {
            done = WindowMenuOps.ApplyCell(c.Target, r.LayoutId, cell); // assist 없음 — 세션을 다시 열지 않는다
        }
        finally
        {
            s_applying = false;
        }

        if (!ReferenceEquals(s_run, r))
        {
            return done; // 배치 중 세션이 끝났다(배치한 창 닫힘 등)
        }

        PdLog.Info("snap-assist", $"pick cell={cell} key={c.Key} done={done}");
        if (done)
        {
            r.Exclude.Add(c.Key);
            SetFilled(r, cell);
            Advance(r, preferAfter: cell);
        }
        else
        {
            Refresh(r); // 후보 창이 닫혔거나 놓을 수 없게 됐다 — 다시 계산
        }

        return done;
    }

    /// <summary>활성 칸을 cell 로 바꾼다(흐린 칸 클릭). 채운 칸·없는 칸·후보 0개 칸이면 false.</summary>
    public static bool SelectZone(int cell)
    {
        if (s_run is not { } r || r.Zones.FirstOrDefault(z => z.Cell == cell) is not { Filled: false })
        {
            return false;
        }

        var list = SnapAssistCandidates.For(r.Scope, r.Anchor, r.Placed, r.LayoutId, cell, r.Exclude);
        if (list.Candidates.Count == 0)
        {
            return false;
        }

        r.Skipped.Remove(cell);
        Activate(r, cell, list);
        r.Overlay.Update(ViewOf(r));
        return true;
    }

    /// <summary>활성 칸 건너뛰기(원격 전용). 다음 칸이 없으면 끝(no-candidates — 칸이 비어 남는다).</summary>
    public static bool Skip()
    {
        if (s_run is not { } r || r.ActiveCell < 0)
        {
            return false;
        }

        var cell = r.ActiveCell;
        r.Skipped.Add(cell);
        Advance(r, preferAfter: cell);
        return true;
    }

    /// <summary>후보 초점 이동(Tab·방향키). 끝에서 처음으로 돈다.</summary>
    public static void MoveFocus(int delta)
    {
        if (s_run is not { } r || r.Current.Candidates.Count == 0 || delta == 0)
        {
            return;
        }

        var n = r.Current.Candidates.Count;
        r.Focus = (((r.Focus < 0 ? 0 : r.Focus) + delta) % n + n) % n;
        r.Overlay.Update(ViewOf(r));
    }

    /// <summary>
    /// 세션을 끝낸다(SnapAssistEndReason). 고른 배치는 그대로 둔다. 세션이 없으면 아무 것도 하지 않는다.
    /// Pick 처리 중 들어온 outside·focus 는 무시한다(배치가 활성 창을 바꿔 생기는 가짜 이탈).
    /// </summary>
    public static void Cancel(string reason)
    {
        if (s_run is not { } r)
        {
            return;
        }

        if (s_applying && (reason == SnapAssistEndReason.Outside || reason == SnapAssistEndReason.Focus))
        {
            PdLog.Info("snap-assist", $"cancel ignored reason={reason} (applying)");
            return;
        }

        s_run = null;
        Unwatch(r);
        s_lastEnd = string.IsNullOrEmpty(reason) ? SnapAssistEndReason.Cancel : reason;
        try
        {
            r.Overlay.Hide();
        }
        catch (Exception ex)
        {
            PdLog.Info("snap-assist", $"hide failed: {ex.Message}");
        }

        PdLog.Info("snap-assist", $"end reason={s_lastEnd} filled={r.Zones.Count(z => z.Filled)}/{r.Zones.Count}");
        Ended?.Invoke(s_lastEnd);
    }

    private static void SetFilled(Run r, int cell)
    {
        var i = r.Zones.FindIndex(z => z.Cell == cell);
        if (i >= 0)
        {
            r.Zones[i] = r.Zones[i] with { Filled = true };
        }

        r.Skipped.Remove(cell);
    }

    /// <summary>
    /// 다음 칸으로. 후보가 있는 첫 남은 칸(채우지 않음·건너뛰지 않음)을 preferAfter 다음 번호부터 돌며 찾는다. 후보 0개 칸은 건너뛴다.
    /// 없으면 끝: 모든 칸이 찼으면 filled, 아니면 no-candidates.
    /// </summary>
    private static void Advance(Run r, int preferAfter)
    {
        var open = r.Zones.Where(z => !z.Filled && !r.Skipped.Contains(z.Cell)).Select(z => z.Cell).OrderBy(c => c).ToList();
        foreach (var cell in open.Where(c => c > preferAfter).Concat(open.Where(c => c <= preferAfter)))
        {
            var list = SnapAssistCandidates.For(r.Scope, r.Anchor, r.Placed, r.LayoutId, cell, r.Exclude);
            if (list.Candidates.Count > 0)
            {
                Activate(r, cell, list);
                r.Overlay.Update(ViewOf(r));
                return;
            }
        }

        Cancel(r.Zones.All(z => z.Filled) ? SnapAssistEndReason.Filled : SnapAssistEndReason.NoCandidates);
    }

    /// <summary>활성 칸 후보를 다시 계산한다. 0개가 되면 다음 칸으로 넘어간다.</summary>
    private static void Refresh(Run r)
    {
        if (r.ActiveCell < 0)
        {
            return;
        }

        var key = r.Focus >= 0 && r.Focus < r.Current.Candidates.Count ? r.Current.Candidates[r.Focus].Key : null;
        var list = SnapAssistCandidates.For(r.Scope, r.Anchor, r.Placed, r.LayoutId, r.ActiveCell, r.Exclude);
        if (list.Candidates.Count == 0)
        {
            Advance(r, preferAfter: r.ActiveCell);
            return;
        }

        r.Current = list;
        var keep = key is null ? -1 : list.Candidates.ToList().FindIndex(c => c.Key == key);
        r.Focus = keep >= 0 ? keep : Math.Min(Math.Max(r.Focus, 0), list.Candidates.Count - 1);
        r.Overlay.Update(ViewOf(r));
    }

    private static SnapAssistView ViewOf(Run r) =>
        new(r.Scope, r.Area, r.LayoutId, r.Zones.ToList(), r.ActiveCell, r.Current.Candidates, r.Focus, r.Thumbnails);
    // ── 감시(이벤트 구독, 세션 동안만) ──

    private static void Watch(Run r)
    {
        if (App.Shell is WindowCommands shell)
        {
            shell.OpenDocuments.CollectionChanged += OnDocumentsChanged;
        }

        UiSettings.Current.PropertyChanged += OnSettingsChanged;
        switch (r.Anchor)
        {
            case Window w:
                w.Closed += OnAnchorClosed;
                break;
            case Control c:
                c.SizeChanged += OnAnchorSizeChanged;
                break;
        }
    }

    private static void Unwatch(Run r)
    {
        if (App.Shell is WindowCommands shell)
        {
            shell.OpenDocuments.CollectionChanged -= OnDocumentsChanged;
        }

        UiSettings.Current.PropertyChanged -= OnSettingsChanged;
        switch (r.Anchor)
        {
            case Window w:
                w.Closed -= OnAnchorClosed;
                break;
            case Control c:
                c.SizeChanged -= OnAnchorSizeChanged;
                break;
        }
    }

    /// <summary>배치한 창이 닫히면 끝(closed). 다른 창이 열리고 닫히면 후보만 다시 계산한다.</summary>
    private static void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (s_run is not { } r || s_applying)
        {
            return;
        }

        if (r.Placed.Document is { } doc && sender is IList<PocDocument> open && !open.Contains(doc))
        {
            Cancel(SnapAssistEndReason.Closed);
            return;
        }

        Refresh(r);
    }

    /// <summary>UI 방식 전환(태블릿형·모바일형으로)이면 끝(mode). 스냅·Snap Assist 옵션을 끄면 끝(cancel).</summary>
    private static void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (s_run is null)
        {
            return;
        }

        var s = UiSettings.Current;
        if (e.PropertyName == nameof(UiSettings.EffectiveUiStyle) && s.EffectiveUiStyle is UiStyle.Tablet or UiStyle.Mobile)
        {
            Cancel(SnapAssistEndReason.Mode);
        }
        else if (e.PropertyName == nameof(UiSettings.EffectiveSnapAssistEnabled) && !s.EffectiveSnapAssistEnabled)
        {
            Cancel(SnapAssistEndReason.Cancel);
        }
    }

    /// <summary>SDI anchor(배치한 창)가 닫히면 끝(closed).</summary>
    private static void OnAnchorClosed(object? sender, EventArgs e) => Cancel(SnapAssistEndReason.Closed);

    /// <summary>MDI·관리형 기준 영역 크기가 바뀌면 끝(resize). 칸 사각형이 더는 맞지 않는다. 배치로 생긴 0.5 DIP 미만 흔들림은 무시.</summary>
    private static void OnAnchorSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (s_run is not { } r || s_applying)
        {
            return;
        }

        var area = SnapAssistCandidates.AreaOf(r.Scope, r.Anchor);
        if (Math.Abs(area.Width - r.Area.Width) >= 0.5 || Math.Abs(area.Height - r.Area.Height) >= 0.5)
        {
            Cancel(SnapAssistEndReason.Resize);
        }
    }
}

// 화면 밖 예열 호스트와 창 ID 별 준비된 뷰 1개 보관 — 뷰를 실제 크기로 붙여 데우고, 준비된 뷰를 다음 열기에 넘긴 뒤 유휴 때 다시 채운다(A16, 옵션 PrewarmViews·GridViewPool(기본 끔))
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.views.Windows;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 규칙 정본: plan/09_화면설계/01_화면설계_공통원칙.md §5.4(문서 뷰 예열과 준비된 뷰). 시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).
/// 화면 밖 호스트는 메인 화면 DockPanel 맨 앞에 넣는 높이 0·잘라냄·입력 제외 Canvas 다. 그 안에 뷰를 실제 문서 영역 크기의 칸(Border)으로 붙이면
/// 테마·스타일·템플릿·행/셀 생성·글자 측정이 실제 열기와 같은 경로로 돈다(붙지 않은 뷰는 테마가 적용되지 않는다).
/// 그림은 화면 밖 비트맵에 한 번 그려 글리프 캐시를 데운다. 준비된 뷰는 실제 열기 때 칸에서 떼어 문서 칸에 붙인다.
/// 만들 때는 PerfProbe.SuppressOpenTrack 로 열기 계측을 끄고, 넘길 때 IWindowOpenTracking.BeginOpenTracking 으로 켠다.
/// 풀은 창 ID(Prewarm=KeepReady 인 등록 창)마다 뷰 1개를 쥔다. 데모 표 형식 같은 특정 형식은 모른다.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class WindowViewPool
{
    /// <summary>다시 채우기 전 입력이 없어야 하는 시간(ms). 채우기는 UI 스레드를 수백 ms 막으므로 사용자가 조작 중이면 미룬다.</summary>
    private const int InputQuietMs = 2000;
    private const int IdleHoldMs = 1000;
    private const int PollMs = 250;
    private const string PoolIdPrefix = "pool:";

    private static readonly Dictionary<string, (Control View, Border? Slot)> s_ready = new();
    private static Canvas? s_host;
    private static Size s_lastSize;
    private static long s_lastInputMs;
    private static bool s_refillScheduled;
    private static bool s_building;

    /// <summary>해당 창의 준비된 뷰가 있는가(DocumentViewLocator 가 빈 칸 프레임 없이 바로 붙일지 판단).</summary>
    public static bool HasReady(string windowId) => s_ready.ContainsKey(windowId) && XkxkUi.Current.Settings.GridViewPool;

    /// <summary>화면 밖 호스트가 준비됐는가(예열이 붙여서 데울 수 있는가).</summary>
    internal static bool HasHost => s_host is not null;

    /// <summary>화면 밖 호스트를 메인 화면 패널 맨 앞에 넣는다(1회). 패널이 DockPanel 이면 위쪽에 높이 0 으로 붙어 배치에 영향이 없다.</summary>
    public static void AttachHost(Panel parent)
    {
        if (s_host is not null)
        {
            return;
        }

        var host = new Canvas { Height = 0, ClipToBounds = true, IsHitTestVisible = false, Focusable = false };
        DockPanel.SetDock(host, Avalonia.Controls.Dock.Top);
        KeyboardNavigation.SetTabNavigation(host, KeyboardNavigationMode.None); // Tab 이 화면 밖 뷰로 들어가지 않게
        XkxkUi.Current.Settings.ApplyDocumentScope(host); // 문서 범위 글자 토큰을 실제 문서 칸과 같게 — 옮겨 붙일 때 열 폭을 다시 계산하지 않게
        XkxkUi.Current.Settings.Changed += () =>
        {
            XkxkUi.Current.Settings.ApplyDocumentScope(host);
            if (!XkxkUi.Current.Settings.GridViewPool) { ReleaseAll(); }
        };
        parent.Children.Insert(0, host);
        s_host = host;
        HookInput();
    }

    /// <summary>실제 화면에 배치된 문서 크기를 기억한다(다음 준비 뷰 크기). 화면 밖 칸에 있는 뷰는 건너뛴다.</summary>
    public static void NoteSize(Control view, Size size)
    {
        if (!IsPooled(view) && size.Width >= 100 && size.Height >= 100)
        {
            s_lastSize = size;
        }
    }

    private static bool IsPooled(Control view)
    {
        foreach (var e in s_ready.Values)
        {
            if (ReferenceEquals(e.View, view))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 뷰를 화면 밖 칸에 붙여 측정·배치하고 비트맵에 한 번 그린다. keep=false 면 바로 떼어 버린다. 실패하면 false.
    /// 크기는 마지막 문서 크기, 아직 없으면 메인 화면 크기다(크게 잡으면 넘길 때 줄어드는 쪽이라 다시 측정이 싸다).
    /// </summary>
    internal static bool Warm(Control view, bool keep, out Border? slot)
    {
        slot = null;
        if (s_host is null)
        {
            return false;
        }

        var size = TargetSize();
        var s = new Border { Width = size.Width, Height = size.Height, Child = view };
        s_host.Children.Add(s);
        try
        {
            s.Measure(size);
            s.Arrange(new Rect(size));
            RenderOnce(s, size);
        }
        catch (Exception ex)
        {
            PdLog.Info(PdLogCategory.System, $"뷰 예열 그리기 실패: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
        }

        if (!keep)
        {
            s.Child = null;
            s_host.Children.Remove(s);
            return true;
        }

        slot = s;
        return true;
    }

    /// <summary>창의 준비된 뷰로 보관한다(예열 또는 다시 채우기에서). 같은 창의 옛 뷰는 버린다.</summary>
    public static void Keep(string windowId, Control view, Border slot)
    {
        Release(windowId);
        MaskAutomationIds(view, on: true);
        s_ready[windowId] = (view, slot);
    }

    /// <summary>
    /// 보관 중에는 뷰 안 AutomationId 앞에 'pool:' 을 붙인다. 원격 진단·UI 자동화가 id 로 찾거나 트리 안 개수를 셀 때
    /// 화면 밖 준비 뷰를 실제 문서로 잡지 않게 한다. 넘길 때 원래 id 로 되돌린다.
    /// </summary>
    private static void MaskAutomationIds(Control view, bool on)
    {
        foreach (var c in Avalonia.LogicalTree.LogicalExtensions.GetSelfAndLogicalDescendants(view))
        {
            if (c is not Control control || Avalonia.Automation.AutomationProperties.GetAutomationId(control) is not { Length: > 0 } id)
            {
                continue;
            }

            if (on && !id.StartsWith(PoolIdPrefix, StringComparison.Ordinal))
            {
                Avalonia.Automation.AutomationProperties.SetAutomationId(control, PoolIdPrefix + id);
            }
            else if (!on && id.StartsWith(PoolIdPrefix, StringComparison.Ordinal))
            {
                Avalonia.Automation.AutomationProperties.SetAutomationId(control, id[PoolIdPrefix.Length..]);
            }
        }
    }

    /// <summary>
    /// 해당 창의 준비된 뷰를 꺼낸다(창 내용 생성 함수가 부른다). 없거나 옵션이 꺼져 있거나 풀이 뷰를 만드는 중이면 null — 호출 쪽이 새로 만든다.
    /// 꺼낸 뒤 유휴 시간에 다음 1개를 다시 채우도록 예약한다(그 전 연속 열기는 일반 경로).
    /// </summary>
    public static Control? TryTake(string windowId)
    {
        if (s_building)
        {
            return null; // 풀이 만드는 중의 호출은 적중·실패로 세지 않는다
        }

        if (!s_ready.TryGetValue(windowId, out var entry))
        {
            NoteMiss(windowId);
            return null;
        }

        if (!XkxkUi.Current.Settings.GridViewPool)
        {
            ReleaseAll();
            NoteMiss(windowId);
            return null;
        }

        s_ready.Remove(windowId);
        if (entry.Slot is not null)
        {
            entry.Slot.Child = null;
            s_host?.Children.Remove(entry.Slot);
        }

        MaskAutomationIds(entry.View, on: false);
        (entry.View as IWindowOpenTracking)?.BeginOpenTracking();
        NoteTake(windowId, hit: true);
        ScheduleRefill();
        return entry.View;
    }

    /// <summary>보관 중인 준비 뷰를 버린다(다시 채우기 교체).</summary>
    private static void Release(string windowId)
    {
        if (s_ready.Remove(windowId, out var e) && e.Slot is not null)
        {
            e.Slot.Child = null;
            s_host?.Children.Remove(e.Slot);
        }
    }

    /// <summary>모든 준비 뷰를 버린다(옵션 끔).</summary>
    private static void ReleaseAll()
    {
        foreach (var id in new List<string>(s_ready.Keys))
        {
            Release(id);
        }
    }

    /// <summary>
    /// 창 내용 1개를 만들어 데운다(예열 단계 DocumentPrewarm 와 다시 채우기가 부른다). keep 이고 슬롯이 있으면 준비된 뷰로 보관한다.
    /// 내용은 등록 창의 내용 생성 함수로 만든다(WindowDisplay.InvokeContent). 만드는 동안 TryTake 는 null 이라 풀이 자기 뷰를 다시 꺼내지 않는다.
    /// 호스트가 없으면 붙이지 않고 측정만 한다(attached=false, 테마가 적용되지 않아 효과는 작다). 만들었으나 Control 이 아니거나 실패하면 false.
    /// </summary>
    internal static bool Build(string windowId, bool keep, out bool attached)
    {
        attached = false;
        Control? view;
        PerfProbe.SuppressOpenTrack = true;
        s_building = true;
        try
        {
            var doc = new PocDocument { WindowId = windowId, Id = PoolIdPrefix + windowId, Title = string.Empty };
            view = WindowDisplay.InvokeContent(doc, out _) as Control;
        }
        finally
        {
            s_building = false;
            PerfProbe.SuppressOpenTrack = false;
        }

        if (view is null)
        {
            return false;
        }

        attached = Warm(view, keep, out var slot);
        if (!attached)
        {
            view.Measure(new Size(640, 480));
        }
        else if (keep && slot is not null)
        {
            Keep(windowId, view, slot);
        }

        return true;
    }

    private static Size TargetSize()
    {
        if (s_lastSize.Width >= 100 && s_lastSize.Height >= 100)
        {
            return s_lastSize;
        }

        var b = s_host?.Parent is Visual p ? p.Bounds.Size : default;
        return b.Width >= 100 && b.Height >= 100 ? b : new Size(800, 600);
    }

    /// <summary>화면 밖 비트맵에 한 번 그린다(글자 모양·글리프 캐시). 화면에는 그리지 않는다.</summary>
    private static void RenderOnce(Control target, Size size)
    {
        var w = Math.Clamp((int)Math.Ceiling(size.Width), 1, 4096);
        var h = Math.Clamp((int)Math.Ceiling(size.Height), 1, 4096);
        using var bitmap = new RenderTargetBitmap(new PixelSize(w, h));
        bitmap.Render(target);
    }
}

/// <summary>
/// 옛 표 뷰 풀 이름. 같은 기능은 <see cref="WindowViewPool"/> 이 창 ID 키로 일반화했고 이 형식은 표 데모 창 ID 로 위임하는 얇은 껍데기다.
/// 시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GridViewPool
{
    /// <summary>표 데모 창의 준비된 뷰가 있는가.</summary>
    public static bool HasReady => WindowViewPool.HasReady(LegacyWindowKinds.Grid);

    /// <summary>화면 밖 호스트를 넣는다. <see cref="WindowViewPool.AttachHost"/> 로 위임한다.</summary>
    public static void AttachHost(Panel parent) => WindowViewPool.AttachHost(parent);

    /// <summary>표 데모 창의 준비된 뷰를 꺼낸다(반환형이 GridDocumentView? 에서 Control? 로 넓어졌다, UADR-0009 계획된 예외).</summary>
    public static Control? TryTake() => WindowViewPool.TryTake(LegacyWindowKinds.Grid);

    /// <summary>화면에 배치된 문서 크기를 기억한다. <see cref="WindowViewPool.NoteSize"/> 로 위임한다.</summary>
    public static void NoteSize(Control view, Size size) => WindowViewPool.NoteSize(view, size);

    /// <summary>표 데모 창의 준비된 뷰로 보관한다. <see cref="WindowViewPool.Keep"/> 로 위임한다.</summary>
    public static void Keep(Control view, Border slot) => WindowViewPool.Keep(LegacyWindowKinds.Grid, view, slot);
}

// 복원 문서 뷰 지연 생성(B5) — 레이아웃 복원 때 보이는 문서만 즉시 만들고, 나머지는 보게 될 때(활성화·가림 해제) 또는 유휴 시간에 1개씩 만든다
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 판정·순서(컨트롤 층)는 여기가 갖고, 뷰를 만드는 방법은 화면 층(DocumentViewLocator)이 넘긴 콜백이 갖는다(레이어 분리).
/// ① LayoutRestorer 가 수집 구간(BeginCollect)을 열면 AddDocument 는 그 문서를 '보류'로 표시하고 첫 열림 스코프·열기 계측 시작점을 찍지 않는다.
/// ② 화면 층 Build 가 TryDefer 를 물으면, 지금 보이는 문서는 그 자리에서 스코프·시작점을 찍고(기존 규칙) 즉시 만들게 하고, 안 보이는 문서는 콜백을 받아 빈 칸으로 둔다.
/// ③ 미룬 문서는 보게 될 때(활성화·위 창 이동/최소화/닫기) 그 문서에만 스코프를 걸어 만들고, 유휴 시간에는 스코프 없이 1개씩 만든다.
/// 설정 RestoreViewCreation=Immediate 면 수집 구간을 열지 않아 이전 동작 그대로다. 복원이 아닌 새 문서 열기는 보류 대상이 아니다.
/// </summary>
public static partial class RestoreViewDeferral
{
    private const int PollMs = 250;
    private const int IdleHoldMs = 1000; // DocumentPrewarm 과 같은 유휴 판정 시간
    private const int InputQuietMs = 1000; // 마지막 사용자 입력 뒤 이만큼 조용해야 선생성한다

    private static readonly HashSet<PocDocument> s_pending = new(); // 복원으로 추가됐고 아직 Build 판단 전
    private static readonly List<(PocDocument Doc, Action<bool> Realize)> s_deferred = new(); // Build 됐으나 미뤄 둔 문서
    private static readonly HashSet<PocDocument> s_watched = new();
    private static WindowSession? s_session;
    private static bool s_collecting;
    private static bool s_wired;
    private static bool s_reevalPosted;
    private static bool s_pumpScheduled;
    private static bool s_inputHooked;
    private static bool s_prewarmDone;
    private static long s_lastInputMs;
    private static long s_quietSinceMs;

    /// <summary>설정이 지연일 때만 복원 수집 구간을 연다(지연이 아니면 null). 반환값을 using 으로 닫는다.</summary>
    internal static IDisposable? BeginCollect(WindowSession session)
    {
        if (UiSettings.Current.RestoreViewCreation != RestoreViewCreation.Deferred)
        {
            return null;
        }

        s_session = session;
        s_collecting = true;
        return new CollectScope();
    }

    private sealed class CollectScope : IDisposable
    {
        public void Dispose()
        {
            s_collecting = false;
            if (s_pending.Count > 0)
            {
                Wire();
            }
        }
    }

    /// <summary>AddDocument 가 부른다. 수집 구간이면 보류로 표시하고 true(호출부는 스코프·계측 시작을 건너뛴다).</summary>
    internal static bool MarkIfRestoring(PocDocument doc) => s_collecting && s_pending.Add(doc);

    /// <summary>
    /// 화면 층 Build 가 뷰를 만들기 전에 묻는다. 복원 보류 문서가 아니면 false(기존 경로). 보류 문서가 지금 보이면 스코프·시작점을 찍고 false.
    /// 안 보이면 realize(busy) 를 맡아 true — 호출부는 빈 칸만 둔다. busy=true 는 보게 된 시점(스피너 뒤에 만들 것), false 는 유휴 선생성(바로 만들 것).
    /// </summary>
    public static bool TryDefer(PocDocument doc, Action<bool> realize)
    {
        if (!s_pending.Remove(doc))
        {
            return false;
        }

        if (IsVisible(doc))
        {
            BeginOpen(doc, busy: true);
            return false;
        }

        s_deferred.Add((doc, realize));
        Wire();
        SchedulePump();
        return true;
    }

    /// <summary>이 종류의 미룬 문서가 있는가. 있으면 그 문서 선생성이 템플릿 예열을 겸하므로 DocumentPrewarm 이 같은 종류 예열을 건너뛴다.</summary>
    public static bool HasDeferred(DocumentKind kind) => s_deferred.Any(d => d.Doc.Kind == kind);

    /// <summary>DocumentPrewarm 이 끝났다(꺼짐·실패 포함). 선생성은 예열 뒤에 시작한다(두 작업이 같은 유휴 시간을 다투지 않게).</summary>
    public static void NotifyPrewarmDone()
    {
        s_prewarmDone = true;
        if (s_deferred.Count > 0)
        {
            SchedulePump();
        }
    }

    /// <summary>
    /// 지금 사용자에게 보이는가. 활성 문서 → 보임. SDI 창 → 최소화가 아니면 보임. 탭형·패널·모바일형 → 그 영역의 선택 탭만 보임.
    /// MDI → 최소화면 안 보임, 위 창 중 최대화가 있으면 안 보임, 자신이 최대화면 보임, 위 창과 겹치는 부분이 있으면 안 보임. 위치를 모르면 보이는 것으로 본다(이전 동작 쪽).
    /// </summary>
    private static bool IsVisible(PocDocument doc)
    {
        if (s_session is not { } s || ReferenceEquals(s.Owner.ActiveDocument, doc) || ReferenceEquals(s.RestoreTarget, doc))
        {
            return true;
        }

        if (s.FindWindow(doc) is { } window)
        {
            return window.WindowState != DockWindowState.Minimized;
        }

        if (doc.Owner is not IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } dock || dock is PanelDocumentDock)
        {
            return doc.Owner is not IDock owner || ReferenceEquals(owner.ActiveDockable, doc);
        }

        if (doc.MdiState == MdiWindowState.Minimized)
        {
            return false;
        }

        var above = (dock.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<IMdiDocument>() // 창 단위 — 탭 묶음(MdiTabGroupDock)도 위 창 1개(안 문서의 옛 위치가 아니라 묶음 위치·Z·상태로 가린다)
            .Where(d => (d is PocDocument or MdiTabGroupDock) && !ReferenceEquals(d, doc) && d.MdiState != MdiWindowState.Minimized && d.MdiZIndex > doc.MdiZIndex).ToList();
        if (above.Any(d => d.MdiState == MdiWindowState.Maximized))
        {
            return false;
        }

        if (doc.MdiState == MdiWindowState.Maximized || doc.MdiBounds is not { Width: > 0, Height: > 0 } b)
        {
            return true;
        }

        return !above.Any(d => d.MdiBounds is { Width: > 0, Height: > 0 } o
            && o.X < b.X + b.Width && b.X < o.X + o.Width && o.Y < b.Y + b.Height && b.Y < o.Y + o.Height);
    }

    /// <summary>보게 될 시점을 알 수 있는 신호(활성 변경·문서 목록·창 상태/위치/쌓임 변경)와 사용자 입력을 받는다.</summary>
    private static void Wire()
    {
        if (s_wired || s_session is not { } s)
        {
            return;
        }

        s_wired = true;
        s.Owner.ActiveChanged += OnActiveChanged;
        s.Owner.OpenDocuments.CollectionChanged += OnDocumentsChanged;
        Watch(s.Owner.OpenDocuments);
        HookInput();
    }

    private static void Unwire()
    {
        if (!s_wired || s_session is not { } s)
        {
            return;
        }

        s_wired = false;
        s.Owner.ActiveChanged -= OnActiveChanged;
        s.Owner.OpenDocuments.CollectionChanged -= OnDocumentsChanged;
        foreach (var doc in s_watched)
        {
            doc.PropertyChanged -= OnDocumentChanged;
        }

        s_watched.Clear();
    }

    private static void Watch(IEnumerable<PocDocument> docs)
    {
        foreach (var doc in docs.Where(s_watched.Add))
        {
            doc.PropertyChanged += OnDocumentChanged;
        }
    }

    private static void OnActiveChanged(PocDocument? _) => QueueReevaluate();

    private static void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (s_session is { } s)
        {
            Watch(s.Owner.OpenDocuments);
        }

        QueueReevaluate();
    }

    private static void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IMdiDocument.MdiState) or nameof(IMdiDocument.MdiBounds) or nameof(IMdiDocument.MdiZIndex))
        {
            QueueReevaluate();
        }
    }

    /// <summary>신호가 한 틱에 여러 번 와도 한 번만 다시 판정한다(레이아웃 뒤 Background).</summary>
    private static void QueueReevaluate()
    {
        if (s_reevalPosted)
        {
            return;
        }

        s_reevalPosted = true;
        Dispatcher.UIThread.Post(Reevaluate, DispatcherPriority.Background);
    }

    private static void Reevaluate()
    {
        s_reevalPosted = false;
        if (s_session is not { } s)
        {
            return;
        }

        s_pending.RemoveWhere(d => !s.Owner.OpenDocuments.Contains(d));
        for (var i = s_deferred.Count - 1; i >= 0; i--)
        {
            if (!s.Owner.OpenDocuments.Contains(s_deferred[i].Doc))
            {
                s_deferred.RemoveAt(i); // 만들기 전에 닫혔다
            }
            else if (IsVisible(s_deferred[i].Doc))
            {
                Realize(i, busy: true);
            }
        }

        if (s_pending.Count == 0 && s_deferred.Count == 0)
        {
            Unwire();
        }
    }

    private static void Realize(int index, bool busy)
    {
        var (doc, realize) = s_deferred[index];
        s_deferred.RemoveAt(index);
        BeginOpen(doc, busy);
        try
        {
            realize(busy);
        }
        catch (Exception ex)
        {
            PdLog.Info("restore-view", $"지연 뷰 생성 실패 {doc.Id}: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
            OpenFailed(doc, ex); // 열기 토큰을 실패로 끝내 스코프·아이콘이 남지 않게(.Load.cs)
        }
    }
}

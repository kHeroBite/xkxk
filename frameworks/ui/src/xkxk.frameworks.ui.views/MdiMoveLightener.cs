// MDI 문서 창이 움직이는 동안만 크롬을 가볍게(.pdMoving) 하고 창 안쪽을 비트맵 캐시로 그리는 첨부 동작(테마 Setter 로 붙인다)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 창의 문서 모델(IMdiDocument.MdiBounds)이 크기 변화 없이 위치만 바뀌면 그 창에 .pdMoving 클래스를 걸고, 마지막 변화 뒤 MdiMoveLight.QuietMs 동안 변화가 없으면 뗀다.
/// 모델 값으로 판정하므로 끌기·자석 배치·전체 이동(MoveAll)이 같은 규칙을 탄다. 처음 판은 창 Bounds 변화로 판정했으나 b2_split4 실측에서
/// docs.moveAll 뒤 한 번도 걸리지 않았다(창 컨트롤 자신의 Bounds 가 아니라 바깥 배치 쪽이 움직이는 것으로 추정) — 그래서 위치의 정본인 모델 값을 본다.
/// 크기가 함께 바뀌는 변화(크기 조절·최대화·복원)는 이동으로 보지 않는다. 보통 상태가 아닌 창(최소·최대)도 걸지 않는다.
/// 이동 중에는 (1) 테마 .pdMoving 절이 그림자를 끄고 모서리·잘라냄 반경을 0 으로 하고, (2) 하위 옵션 MdiMoveLight.BitmapCache(기본 끔 — 웹 W4 실측 손해)가 켜져 있으면 창 안쪽(PdClip: 제목줄+내용)에 BitmapCache 를 걸어
/// 이동 프레임마다 내용을 다시 그리지 않고 캐시 비트맵만 옮겨 그리게 한다. 근거: 20창 이동 1회당 렌더 중 내용 몫 약 18~21ms(odebug-perf2 실측, 크롬과 겹쳐 더 커짐).
/// 원복 판정은 프레임을 강제하지 않는 DispatcherTimer 로 한다(매 프레임 무효화로 틱을 유지하던 처음 판은 이득 없이 렌더만 늘어 뺐다, b2_split3).
/// 테마 규칙은 Themes/MdiDocumentWindowTheme.axaml 의 .pdMoving 절, 규칙 정본은 plan/09_화면설계/01_화면설계_공통원칙.md §4.2.
/// 옵션 MdiMoveLight.Enabled=false 면 걸지 않고, 끄는 순간 걸려 있던 경량화를 바로 푼다(MdiMoveLight.Changed).
/// 별도 옵션 MdiMoveLight.StaticCache 가 켜져 있으면 같은 이동 판정으로 첫 위치 변화 때 정지 아래 창 캐시를 걸고(MdiStaticCache), 움직이는 창이 모두 원복되면 뗀다. 옵션 Siblings 는 같은 시점에 정지 창 가벼운 크롬(MdiStaticLight)을 건다.
/// </summary>
public static partial class MdiMoveLightener
{
    public const string MovingClass = "pdMoving";
    public const string SquareClass = "pdMoveSquare"; // 끌기 중 모서리 반경 0 — 옵션 MdiMoveLight.SquareCorners 일 때만 .pdMoving·.pdMovingSibling 과 함께(2026-11-11)

    public static readonly AttachedProperty<bool> TrackProperty =
        AvaloniaProperty.RegisterAttached<MdiDocumentWindow, bool>("Track", typeof(MdiMoveLightener));

    private static readonly Dictionary<MdiDocumentWindow, Watch> s_watches = new();
    private static readonly Dictionary<MdiDocumentWindow, State> s_moving = new();
    private static DispatcherTimer? s_timer;
    private static bool s_lastBitmapCache = MdiMoveLight.BitmapCache;
    private static bool s_lastEnabled = MdiMoveLight.Enabled;
    private static bool s_lastStaticCache = MdiMoveLight.StaticCache;
    private static bool s_lastSiblings = MdiMoveLight.Siblings;
    private static bool s_lastSquare = MdiMoveLight.SquareCorners;

    // 이동 판정을 할지 — 경량화(Enabled)·정지 창 캐시(StaticCache)·정지 창 가벼운 크롬(Siblings) 중 하나라도 켜져 있으면 판정한다(옵션은 따로 켜고 끈다, 01 §4.2)
    private static bool Tracking => MdiMoveLight.Enabled || MdiMoveLight.StaticCache || MdiMoveLight.Siblings;

    static MdiMoveLightener()
    {
        TrackProperty.Changed.AddClassHandler<MdiDocumentWindow>(OnTrackChanged);
        MdiMoveLight.Changed += () => Dispatcher.UIThread.Post(OnOptionChanged); // 원격 토글 등 어느 스레드에서 바뀌어도 UI 스레드에서 처리
    }

    public static bool GetTrack(MdiDocumentWindow w) => w.GetValue(TrackProperty);

    public static void SetTrack(MdiDocumentWindow w, bool value) => w.SetValue(TrackProperty, value);

    /// <summary>창 하나가 구독 중인 문서 모델과 직전 경계값.</summary>
    private sealed class Watch
    {
        public required MdiDocumentWindow Window;
        public IMdiDocument? Doc;
        public DockRect Last;

        public void OnDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IMdiDocument.MdiBounds) && Doc is { } d)
            {
                var now = d.MdiBounds;
                var old = Last;
                Last = now;
                if (Dispatcher.UIThread.CheckAccess())
                {
                    OnBoundsChanged(Window, d, old, now);
                }
            }
        }
    }

    private sealed class State
    {
        public long LastMoveTicks;
        public Visual? Cached; // BitmapCache 를 건 요소(원복 때 이 요소만 되돌린다)
    }

    private static void OnTrackChanged(MdiDocumentWindow w, AvaloniaPropertyChangedEventArgs e)
    {
        w.DataContextChanged -= OnDataContextChanged;
        w.AttachedToVisualTree -= OnAttached;
        w.DetachedFromVisualTree -= OnDetached;
        Unwatch(w);
        if (e.NewValue is true)
        {
            w.DataContextChanged += OnDataContextChanged;
            w.AttachedToVisualTree += OnAttached; // 닫힌 창이 정적 사전에 남아 문서·창을 붙잡지 않게 붙어 있는 동안만 구독한다
            w.DetachedFromVisualTree += OnDetached;
            if (TopLevel.GetTopLevel(w) is not null)
            {
                WatchDoc(w);
            }
        }
        else
        {
            End(w);
        }
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is MdiDocumentWindow w)
        {
            End(w);
            Unwatch(w);
            if (TopLevel.GetTopLevel(w) is not null)
            {
                WatchDoc(w);
            }
        }
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is MdiDocumentWindow w)
        {
            Unwatch(w);
            WatchDoc(w);
        }
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is MdiDocumentWindow w)
        {
            End(w);
            Unwatch(w);
        }
    }

    private static void WatchDoc(MdiDocumentWindow w)
    {
        if (w.DataContext is IMdiDocument doc and INotifyPropertyChanged npc)
        {
            var watch = new Watch { Window = w, Doc = doc, Last = doc.MdiBounds };
            npc.PropertyChanged += watch.OnDocPropertyChanged;
            s_watches[w] = watch;
        }
    }

    private static void Unwatch(MdiDocumentWindow w)
    {
        if (s_watches.Remove(w, out var watch) && watch.Doc is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= watch.OnDocPropertyChanged;
        }
    }

    private static void OnOptionChanged()
    {
        var cacheTurnedOff = s_lastBitmapCache && !MdiMoveLight.BitmapCache;
        var switched = s_lastEnabled != MdiMoveLight.Enabled || s_lastStaticCache != MdiMoveLight.StaticCache || s_lastSiblings != MdiMoveLight.Siblings || s_lastSquare != MdiMoveLight.SquareCorners;
        s_lastSquare = MdiMoveLight.SquareCorners;
        s_lastBitmapCache = MdiMoveLight.BitmapCache;
        s_lastEnabled = MdiMoveLight.Enabled;
        s_lastStaticCache = MdiMoveLight.StaticCache;
        s_lastSiblings = MdiMoveLight.Siblings;
        if (TakeShadowChange() | (!Tracking || cacheTurnedOff || switched))
        {
            EndAll(); // 경량화·캐시·정지 창 캐시를 바꾸면 걸려 있던 것을 바로 푼다(다음 이동에서 새 설정으로 다시 건다). QuietMs 변경은 다음 타이머 판정부터 반영된다
        }
    }

    private static void OnBoundsChanged(MdiDocumentWindow w, IMdiDocument doc, DockRect old, DockRect now)
    {
#if PD_DIAGNOSTICS
        using var probe = xkxk.frameworks.ui.shell.Diagnostics.MdiDragProbe.Chrome(); // 끌기 단계 '크롬 갱신' 할당(--drag-probe 일 때만)
#endif
        // 크기가 같고 위치만 바뀐 경우만 이동이다. 보통 상태 창만, 화면에 붙어 있는 창만 대상이다
        if (old.Width != now.Width || old.Height != now.Height || (old.X == now.X && old.Y == now.Y) || old.Width <= 0
            || doc.MdiState != MdiWindowState.Normal || !Tracking || TopLevel.GetTopLevel(w) is null)
        {
            return;
        }

        if (!s_moving.TryGetValue(w, out var st))
        {
            st = new State { Cached = MdiMoveLight.Enabled ? FindNamed(w, "PdClip") : null };
            s_moving[w] = st;
            if (MdiMoveLight.Enabled)
            {
                w.Classes.Add(MovingClass);
                SyncShadowOff(w);
                if (MdiMoveLight.SquareCorners) { w.Classes.Add(SquareClass); }
            }

            if (MdiMoveLight.Enabled && MdiMoveLight.BitmapCache && st.Cached is { CacheMode: null } c) // 하위 옵션(기본 끔 — 추가 측정 뒤 재결정)
            {
                c.CacheMode = new BitmapCache(); // 창마다 따로 만든다(캐시 객체는 요소 하나에만 붙는다)
            }
            else
            {
                st.Cached = null; // 다른 곳이 건 CacheMode 는 건드리지 않는다
            }

            // 정지 창 가벼운 크롬(Siblings 기본 켬, N2)·정지 아래 창 캐시(StaticCache 기본 끔) — 거는 시점 = 이 창의 첫 위치 변화(누름 직후가 아님).
            // 같은 차례에 움직이기 시작한 창을 모아 MDI 영역마다 한 번만 건다(MdiStaticLight.ScheduleMoveStarted, 전체 이동 첫 호출 비용)
            if (MdiMoveLight.Siblings || MdiMoveLight.StaticCache) { MdiStaticLight.ScheduleMoveStarted(w, s_moving.ContainsKey); }
        }

        st.LastMoveTicks = Environment.TickCount64;
        EnsureTimer();
    }

    private static void EnsureTimer()
    {
        s_timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(MdiMoveLight.MinQuietMs), DispatcherPriority.Background, OnTimer);
        if (!s_timer.IsEnabled)
        {
            s_timer.Start();
        }
    }

    /// <summary>조용해진 창을 원복한다. 이동 중인 창이 없으면 타이머를 멈춘다(유휴 비용 없음).</summary>
    private static void OnTimer(object? sender, EventArgs e)
    {
#if PD_DIAGNOSTICS
        using var probe = xkxk.frameworks.ui.shell.Diagnostics.MdiDragProbe.Chrome(); // 원복도 크롬 단계로 센다
#endif
        var now = Environment.TickCount64;
        List<MdiDocumentWindow>? done = null;
        foreach (var (w, st) in s_moving)
        {
            if (!Tracking || now - st.LastMoveTicks >= MdiMoveLight.QuietMs || TopLevel.GetTopLevel(w) is null)
            {
                (done ??= new()).Add(w);
            }
        }

        if (done is not null)
        {
            foreach (var w in done)
            {
                End(w);
            }
        }

        if (s_moving.Count == 0)
        {
            s_timer?.Stop();
        }
    }

    // 움직이는 창이 하나도 남지 않으면 정지 창 캐시를 뗀다(이동이 모두 끝남)
    private static void ReleaseStaticIfIdle()
    {
        if (s_moving.Count == 0)
        {
            MdiStaticLight.ReleaseAllWhenIdle(); // 캐시·가벼운 크롬 해제 — P2(RestorePerFrame>0)면 다음 프레임부터 나눠 뗀다(MdiStaticLight.Restore.cs)
        }
    }

    private static void EndAll()
    {
        foreach (var w in new List<MdiDocumentWindow>(s_moving.Keys))
        {
            End(w);
        }

        MdiStaticCache.ReleaseAll();
        MdiStaticLight.ReleaseAll();
        s_timer?.Stop();
    }

    private static void End(MdiDocumentWindow w)
    {
        MdiStaticCache.Release(w); // 창이 닫히거나 분리될 때 정지 창 캐시 목록에서도 뺀다(움직이지 않던 창 포함)
        MdiStaticLight.Release(w); // 정지 창 가벼운 크롬 목록도 같다
        if (!s_moving.Remove(w, out var st))
        {
            return;
        }

        w.Classes.Remove(MovingClass);
        w.Classes.Remove(SquareClass);
        SyncShadowOff(w);
        if (st.Cached is { CacheMode: BitmapCache })
        {
            st.Cached.CacheMode = null;
        }

        ReleaseStaticIfIdle();
    }
}

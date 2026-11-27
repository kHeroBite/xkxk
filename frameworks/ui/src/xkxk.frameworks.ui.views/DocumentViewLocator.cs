// 문서 뷰 DataTemplate(ZoomDecorator 래핑·host 재사용) — App.axaml.cs 에서 분리한 책임 단위
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// PocDocument 를 App.DocumentViews 로 그리는 DataTemplate(플로팅 창 포함 전역).
/// 뷰를 ZoomDecorator(창을 채운 채 Scale)로 감싸 창 단위 배율(UI-06)을 준다. Tag=문서는 Ctrl+휠 대상 찾기용.
/// </summary>
public sealed class DocumentViewLocator : IDataTemplate
{
    public bool Match(object? data) => data is PocDocument;

#if PD_DIAGNOSTICS
    private static bool? s_diagDirect;

    /// <summary>A16 대조(진단 인자 --doc-create direct): 표 문서를 빈 칸·스피너 프레임 없이 바로 만든다(준비된 표 뷰 경로와 같은 분기). 제품 빌드는 늘 false.</summary>
    private static bool DiagDirectGridCreate => s_diagDirect ??= ReadDirectSwitch();

    private static bool ReadDirectSwitch()
    {
        var a = App.StartupArgs;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Equals("--doc-create=direct", StringComparison.OrdinalIgnoreCase)
                || (a[i].Equals("--doc-create", StringComparison.OrdinalIgnoreCase) && i + 1 < a.Count && a[i + 1].Equals("direct", StringComparison.OrdinalIgnoreCase)))
            {
                PerfProbe.Current.SetText("grid_doc_create", "direct");
                return true;
            }
        }

        return false;
    }
#else
    private const bool DiagDirectGridCreate = false;
#endif

    /// <summary>문서별 마지막 host(F1). 키가 약한 참조라 문서가 닫혀 수거되면 항목도 사라진다.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PocDocument, Panel> Hosts = new();

    /// <summary>뷰를 만들고 그 첫 배치 프레임까지 그려진 문서(열기 효과 대기 판정, PERF-13). 약한 키.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PocDocument, object> Settled = new();

    /// <summary>뷰 정착을 기다리는 열기 효과 시작 콜백(문서별).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PocDocument, List<Action>> Waiters = new();

    /// <summary>정착 대기 상한(ms). 뷰 생성이 오지 않는 경로(닫힘·화면 밖·진단 지연 실현)에서도 효과가 이 시간 뒤에는 시작한다.</summary>
    internal const int SettleLimitMs = 400;

    /// <summary>
    /// 열기 효과 시작을 문서 뷰 정착 뒤로 미룬다(PERF-13). 뷰가 아직 정착하지 않은 문서면 뷰 생성 → 그 첫 배치 프레임 커밋과 뒤처리가 끝난 뒤(또는 SettleLimitMs 뒤) start 를 1회 부르고 true.
    /// 이미 정착했거나 문서가 아니면 false 이며 호출 쪽이 바로 시작한다. 폼·간트 뷰는 빈 칸·스피너를 먼저 그린 뒤 만들어지므로(요건 2), 재생을 창이 붙는 순간 시작하면 뷰 생성·첫 템플릿·배치 비용이 재생 2~3번째 프레임에 들어간다.
    /// </summary>
    internal static bool DeferUntilViewSettled(object? data, Action start)
    {
        if (data is not PocDocument doc || Settled.TryGetValue(doc, out _))
        {
            PerfProbe.Current.SetText(SettlePathKey, "immediate"); // 진단(K4 ①): 대기 없이 바로 재생
            return false;
        }

        var fired = false;
        void Once()
        {
            if (!fired)
            {
                fired = true;
                start();
            }
        }

        PerfProbe.Current.SetText(SettlePathKey, "waiting");
        Waiters.GetOrCreateValue(doc).Add(Once);
        DispatcherTimer.RunOnce(() =>
        {
            if (!fired)
            {
                PerfProbe.Current.SetText(SettlePathKey, "timer"); // 상한 폴백으로 시작
                if (Waiters.TryGetValue(doc, out var pending))
                {
                    pending.Remove(Once); // 뒤늦은 정착이 경로 표식을 덮어쓰지 않게
                }
            }

            Once();
        }, TimeSpan.FromMilliseconds(SettleLimitMs));
        return true;
    }

    /// <summary>진단 키(제품판 PerfProbe 는 아무것도 하지 않는다): 열기 효과 시작 경로 immediate|waiting|timer|idle|detached, 대기 시간, 시작 직전 배치 비우기 시간.</summary>
    private const string SettlePathKey = "motion_settle_path";

    /// <summary>
    /// 뷰를 붙인 직후: 새 뷰를 배치하는 프레임(첫 rAF)이 커밋되고, 뷰 실현이 걸어 둔 뒤처리(UiDeferred — 오버레이 종료·불러오는 중 해제 등)까지 끝난 뒤(ContextIdle)
    /// 그 뒤처리가 남긴 배치를 지금 비우고 정착으로 표시해 대기자를 부른다. 이렇게 해야 재생 시작 틱에 남은 배치가 들어가 첫 커밋이 vsync 를 놓치지 않는다(K4 누락).
    /// 화면에 안 붙어 있으면 바로 정착한다.
    /// </summary>
    private static void SettleAfterFirstFrame(PocDocument doc, Control anchor)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Fire(string path)
        {
            if (Settled.TryGetValue(doc, out _))
            {
                return;
            }

            Settled.AddOrUpdate(doc, doc);
            if (Waiters.TryGetValue(doc, out var list) && list.Count > 0)
            {
                Waiters.Remove(doc);
                PerfProbe.Current.SetText(SettlePathKey, path);
                PerfProbe.Current.SetMetric("motion_settle_ms", clock.Elapsed.TotalMilliseconds);
                foreach (var start in list)
                {
                    start();
                }
            }
        }

        if (TopLevel.GetTopLevel(anchor) is { } top)
        {
            top.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(() =>
            {
                var before = clock.Elapsed.TotalMilliseconds;
                top.UpdateLayout(); // 뒤처리가 무효화한 배치를 재생 시작 전에 끝낸다(이미 깨끗하면 비용 없음)
                PerfProbe.Current.SetMetric("motion_settle_flush_ms", clock.Elapsed.TotalMilliseconds - before);
                Fire("idle");
            }, DispatcherPriority.ContextIdle));
        }
        else
        {
            Fire("detached");
        }
    }

    /// <summary>
    /// host 를 지금 부모에서 떼어 낸다(공개 API 만 — Dock ControlRecycling 의 떼어 내기와 같은 규칙).
    /// 부모 없음 → true. ContentPresenter(Child 가 host) → Content 를 비우고 UpdateChild. Decorator/ContentControl/Panel → 해당 슬롯에서 제거.
    /// 그 밖이거나 떼어지지 않으면 false(호출 쪽이 새로 만든다).
    /// </summary>
    internal static bool TryDetach(Panel host)
    {
        var visualParent = Avalonia.VisualTree.VisualExtensions.GetVisualParent(host);
        if (visualParent is Avalonia.Controls.Presenters.ContentPresenter presenter)
        {
            if (!ReferenceEquals(presenter.Child, host))
            {
                return false;
            }

            presenter.SetCurrentValue(Avalonia.Controls.Presenters.ContentPresenter.ContentProperty, null);
            presenter.UpdateChild();
            return Avalonia.VisualTree.VisualExtensions.GetVisualParent(host) is null;
        }

        switch ((object?)host.Parent ?? visualParent)
        {
            case null:
                return true;
            case Panel panel:
                return panel.Children.Remove(host);
            case ContentControl cc when ReferenceEquals(cc.Content, host):
                cc.SetCurrentValue(ContentControl.ContentProperty, null);
                return true;
            case Decorator d when ReferenceEquals(d.Child, host):
                d.Child = null;
                return true;
            default:
                return false;
        }
    }

    public Control? Build(object? param)
    {
        if (param is not PocDocument doc)
        {
            return null;
        }

        // ③ SDI(F1): Dock 12.1.0.12 은 독립 창 DockControl 이 화면에 붙을 때 재초기화(DeInitialize→CleanupFactory→PruneControlRecycling)하며
        // 그 창 레이아웃의 문서 캐시 항목을 지운다 → Dock 캐시 미스로 이 템플릿이 다시 불린다(i1300 [sdi] float.laid hit=False).
        // 문서별로 만든 host 를 기억했다가 옛 부모에서 떼어 같은 인스턴스를 돌려준다(선택·스크롤 유지). 떼어 내지 못하면 아래처럼 새로 만든다.
        // Dock 캐시가 적중하는 경로(D-2 탭↔MDI, M4 폰/접힘, 테마)는 이 템플릿을 부르지 않으므로 동작이 바뀌지 않는다.
        // 같은 문서를 두 곳에 동시에 그리는 경로는 코드상 없다(odev-2r 확인 2026-10-29): DocumentDockMode 의 탭/MDI 래퍼는 LayoutModeGate 로 한쪽만,
        // DockHost 와 CompactPresenter 는 IsCompact 게이트로 한쪽만 실현한다. DragPreviewControl 은 ShowContent 기본 false 이고 이 앱은 켜지 않는다.
        if (Hosts.TryGetValue(doc, out var kept))
        {
            CrossRootLayoutFlush.Flush(kept, null); // A10: 다른 창으로 옮기기 전 옛 창 대기 측정·배치를 비운다(대상 창을 모르므로 붙어 있는 창을 비운다)
        }

        if (kept is not null && TryDetach(kept))
        {
            return kept;
        }

        // LayoutTransformControl 은 자식을 원하는 크기로만 배치·가운데 정렬해 그리드·간트가 창을 채우지 못했다(odev-3 제보) → ZoomDecorator
        var host = new ZoomDecorator { Tag = doc };
        // 진행 표시(요건 2): 문서와 함께 MDI·SDI 를 오가는 칸. 배율 영향을 받지 않게 ZoomDecorator 바깥에 둔다
        var frame = new Panel { Children = { host, new BusyOverlay { Scope = doc.Id } } };
        CrossRootLayoutFlush.Track(frame); // A10: 떨어질 때의 창을 기억해 다른 창에 붙이기 전 그 창 대기열을 비운다
        Hosts.AddOrUpdate(doc, frame); // F1: 약한 키 — 닫힌 문서는 함께 수거된다
        var deferred = false;
        DeferredContentLoader.DeferContent(doc, host, ref deferred); // 진단 --layout-load deferred(DeferredContentLoader.cs): 미루면 자리표시를 넣고 나중에 실제 뷰로 바꾼다. 제품 빌드는 Conditional 이라 호출이 빠진다
        if (!deferred)
        {
            // 진행 표시(요건 2): 빈 칸과 오버레이를 먼저 그린 뒤 뷰를 만든다. 뷰 생성이 UI 스레드를 막는 동안에도 스피너가 보이게 하려는 것이다
            var blank = new Border();
            blank.Bind(Border.BackgroundProperty, blank.GetResourceObservable("PdBrushBackground"));
            host.Child = blank;
            void Create()
            {
                // 그 사이 닫힌 문서는 건너뛴다(뷰를 만들면 수거되기 전까지 자원만 쓴다). 스피너는 아래에서 함께 내린다.
                // 셸이 없는 경로(갤러리·벤치)는 열린 문서 목록이 없으니 그대로 만든다.
                var closed = XkxkUi.Current.Shell is { } shell && !System.Linq.Enumerable.Contains(shell.Documents, doc);
                if (!closed && ReferenceEquals(host.Child, blank))
                {
                    host.Child = App.DocumentViews.Create(doc);
                    WindowLoadState.Default.AttachContent(doc, host.Child); // 보고 계약이 있으면 뷰 부착을 완료로 보지 않는다(23 §2.2) — DocumentRealized 보다 먼저
                }

                xkxk.frameworks.ui.shell.Busy.BusyHooks.DocumentRealized(doc.Id);
                SettleAfterFirstFrame(doc, frame); // 열기 효과 대기 해제(PERF-13) — 닫혀 건너뛴 경우도 대기자를 풀어 준다
            }

            // 복원 지연(B5): 안 보이는 복원 문서는 빈 칸으로 두고 만들 때를 컨트롤 층(RestoreViewDeferral)이 정한다.
            // busy=true(보게 된 때)는 기존처럼 스피너를 먼저 그린 뒤, false(유휴 선생성)는 그 자리에서 만든다. 복원이 아니면 기존 경로 그대로다
            if (doc.WindowId == LegacyWindowKinds.Grid && (GridViewPool.HasReady || DiagDirectGridCreate))
            {
                Create(); // A16 준비된 표 뷰: 만드는 비용이 없으므로 빈 칸·스피너 프레임을 먼저 그리지 않고 바로 붙인다(한 프레임 절약)
            }
            else if (!RestoreViewDeferral.TryDefer(doc, busy =>
                {
                    if (busy)
                    {
                        xkxk.frameworks.ui.shell.Busy.BusyHooks.AfterRender(frame, Create);
                    }
                    else
                    {
                        Create();
                    }
                }))
            {
                xkxk.frameworks.ui.shell.Busy.BusyHooks.AfterRender(frame, Create);
            }
        }

        host.Zoom = doc.Zoom;

        void OnChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PocDocument.Zoom))
            {
                host.Zoom = doc.Zoom;
            }
        }

        // 문서 글자 크기는 이 래퍼 범위에만 적용(메인 창 크롬은 고정, 사용자 지시 21:40)
        void OnSettings() => XkxkUi.Current.Settings.ApplyDocumentScope(host);
        XkxkUi.Current.Settings.ApplyDocumentScope(host);

        // 뷰가 화면에 붙어 있는 동안만 구독(재생성·닫기 시 누수 방지)
        host.AttachedToVisualTree += (_, _) =>
        {
            host.Zoom = doc.Zoom; // 떨어져 있던 동안 바뀐 배율·글자 크기 반영
            XkxkUi.Current.Settings.ApplyDocumentScope(host);
            doc.PropertyChanged += OnChanged;
            XkxkUi.Current.Settings.Changed += OnSettings;
            if (deferred)
            {
                xkxk.frameworks.ui.shell.Busy.BusyHooks.DocumentRealized(doc.Id); // 진단 지연 실현 경로는 뷰 생성 시점을 알 수 없어 부착으로 끝낸다
            }
        };
        host.DetachedFromVisualTree += (_, _) =>
        {
            doc.PropertyChanged -= OnChanged;
            XkxkUi.Current.Settings.Changed -= OnSettings;
        };
        return frame;
    }
}

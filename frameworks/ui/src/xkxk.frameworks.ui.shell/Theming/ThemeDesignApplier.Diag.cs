// 테마 요소 디자인 적용기 진단 분해(K13, C5-D-25) — 교체 직후 다음 프레임(애니메이션 프레임 대기·레이아웃·틱 끝)을 조작 계측으로 나눈다. 진단판 전용, 제품판은 빈 partial
using Avalonia;
using Avalonia.Controls;
#if PD_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Diagnostics;
#endif

namespace xkxk.frameworks.ui.shell.Theming;

public static partial class ThemeDesignApplier
{
    /// <summary>
    /// 디자인 Styles 교체 뒤 다음 렌더 틱을 나눠 잰다(측정만, 동작 무변경). 기준점 = 교체·재스타일이 끝난 시각.
    /// ui.apply.design.next.raf = 다음 애니메이션 프레임 콜백까지, .layout = 그 뒤 레이아웃 패스 끝(LayoutUpdated)까지, .tickend = 레이아웃 뒤 같은 틱의 남은 일(합성 커밋 등)이 끝나 기본 우선순위 작업이 돌 때까지.
    /// </summary>
    static partial void DiagNextFrame(Application app);

    /// <summary>새 TopLevel 의 Loaded(첫 렌더 뒤) 때 표식이 이미 맞았는지 남긴다 — ui.apply.design.firstframe.hit|miss(팝업 루트는 .popup.*). hit = 첫 렌더 전에 표식이 붙었다.</summary>
    static partial void DiagFirstFrame(TopLevel top, bool synced);

    /// <summary>새 TopLevel 의 TemplateApplied(첫 렌더 전) 시각을 적어 둔다 — Loaded 때 ui.apply.design.firstframe.ms(팝업 루트는 .popup.ms) 로 TemplateApplied→Loaded 경과를 남기는 기준점.</summary>
    static partial void DiagTemplated(TopLevel top);

#if PD_DIAGNOSTICS
    private static readonly ConditionalWeakTable<TopLevel, StrongBox<long>> TemplatedAt = new(); // 약한 참조 — 닫힌 창을 붙잡지 않음

    static partial void DiagTemplated(TopLevel top) => TemplatedAt.GetValue(top, _ => new StrongBox<long>(Stopwatch.GetTimestamp()));

    static partial void DiagFirstFrame(TopLevel top, bool synced)
    {
        var prefix = "ui.apply.design.firstframe." + (top is Avalonia.Controls.Primitives.PopupRoot ? "popup." : "");
        var name = prefix + (synced ? "hit" : "miss");
        Record(name, 0);
        if (TemplatedAt.TryGetValue(top, out var at))
        {
            TemplatedAt.Remove(top);
            Record(prefix + "ms", Ms(at.Value)); // TemplateApplied→Loaded 첫 프레임 ms(표식 적중 여부와 무관)
        }

        if (!synced)
        {
            // 어긋난 까닭 분해: on = 늘 켠 표식 없음, cur = 넣은 Styles 없음, seen = 훅이 템플릿 적용을 봄, root = Roots 에 이 창이 있음
            var detail = (top.Classes.Contains(DesignOnClass) ? "" : ".on") + (_current is null ? ".cur" : "")
                + (SeenTemplated.TryGetValue(top, out _) ? ".seen" : "")
                + (Application.Current is { } app && Roots(app).Contains(top) ? ".root" : "");
            Record(name + detail, 0);
        }
    }

    static partial void DiagNextFrame(Application app)
    {
        if (Roots(app).OfType<TopLevel>().FirstOrDefault() is not { } top)
        {
            return;
        }

        var t0 = Stopwatch.GetTimestamp();
        double? raf = null;
        top.RequestAnimationFrame(_ => raf ??= Ms(t0));
        EventHandler? onLayout = null;
        onLayout = (_, _) =>
        {
            top.LayoutUpdated -= onLayout;
            var layoutEnd = Ms(t0);
            var rafAt = raf ?? layoutEnd;
            Record("ui.apply.design.next.raf", rafAt);
            Record("ui.apply.design.next.layout", layoutEnd - rafAt);
            Dispatcher.UIThread.Post(() => Record("ui.apply.design.next.tickend", Ms(t0) - layoutEnd), DispatcherPriority.Default);
        };
        top.LayoutUpdated += onLayout;
    }

    private static double Ms(long t0) => Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

    private static void Record(string name, double ms) => PerfProbe.Current.RecordOperation(name, ms, ms);
#endif
}

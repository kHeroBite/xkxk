// MDI 창의 끌 수 있는 구간(MdiGrabZoneReader) 읽기 결과를 창별로 보관해 위치만 바뀐 창은 다시 재지 않게 하는 캐시
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.Controls;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// B2·W4(배치·끌기 프레임 비용): 구간 읽기는 창 시각 트리를 훑으므로 Arrange·접기 판정마다 하면 창 수에 비례해 비싸다.
/// 구간은 창 안쪽 배치에만 달려 있고 창 위치와는 무관하므로 키(창 폭·접힘 여부·템플릿 적용 세대)가 같으면 보관값을 돌려준다.
/// 세대가 오르는 경우: 창 템플릿 재적용(TemplateApplied), 기능 버튼 줄·머리줄 크기 변경(SizeChanged — 버튼 추가·제거·글꼴),
/// UI 설정 변경(UiSettings.PropertyChanged — 캡션 위치·스타일 등, 전체 창), <see cref="InvalidateAll"/>(끌기 스냅샷 무효화).
/// 창 하나만 버리는 경우: <see cref="Invalidate"/>(접힘 토글·SDI 분리 — MdiStripFold).
/// 저장하지 않는 값: RightInset&lt;0(안쪽 제목 칸이 새 폭으로 아직 배치되지 않은 낡은 값), Whole(템플릿 적용 전·배치 전).
/// 배치 항목이 템플릿 컨트롤이 아니면 바로 아래 자식 창의 템플릿 재적용을 쓴다(K1, <see cref="Bound"/>). 그것도 없으면 보관하지 않고 매번 읽는다. 끌기 잡을 곳 폭(MdiMinGrabDip)은 구간이 아니라
/// MdiDragLimit 판정에서 쓰이므로 이 캐시 값에 들어가지 않는다(설정 변경 때는 위 UI 설정 경로로 어차피 세대가 오른다). UI 스레드 전용.
/// </summary>
internal static class MdiGrabZoneCache
{
    private static readonly ConditionalWeakTable<Control, Entry> Entries = new();

    /// <summary>모든 창에 걸치는 세대(UI 설정 변경·InvalidateAll 때 오른다).</summary>
    private static int s_global;

    /// <summary>같은 구조 버전에서 '못 찾음'을 건너뛰는 상한(부품이 템플릿 적용·부착 없이 늦게 생기는 경우 대비, D-24).</summary>
    internal const int MissRetryEvery = 64;

    static MdiGrabZoneCache()
    {
        UiSettings.Current.PropertyChanged += (_, _) => s_global++;
    }

    private sealed class Entry
    {
        internal int Generation; // 창별 템플릿 적용 세대(템플릿 재적용·줄/머리줄 크기 변경 때 오른다)
        internal int Templates; // 템플릿 적용 횟수만(MdiStripFold 가 기능 버튼 줄을 다시 찾을지 판단)
        internal int Attached; // 시각 트리에 붙은 횟수(떨어졌다 다시 붙으면 구조가 바뀔 수 있어 '못 찾음'을 버린다)
        internal int MissVersion = -1; // 부품(제목 칸·머리줄)을 못 찾은 구조 버전(Templates+Attached). -1 = 없음
        internal int MissSkips; // 같은 구조 버전에서 건너뛴 횟수(상한에 닿으면 한 번 다시 찾는다)
        internal bool Has;
        internal int Unset = MdiGrabZoneDiag.First; // 진단: 보관값이 없는 이유(처음·무효화·음수·Whole·템플릿 재적용)
        internal double Width;
        internal bool Folded;
        internal int KeptGeneration;
        internal int KeptGlobal;
        internal MdiGrabZone Zone;
        internal Control? WatchedStrip;
        internal TemplatedControl? Templated; // 템플릿 재적용을 지켜보는 창(항목 자신 또는 담는 컨트롤 바로 아래 창, K1)
        internal object? BoundContext; // Templated 를 찾을 때의 항목 DataContext(바뀌면 다시 찾는다)

        internal void OnTemplateApplied(object? sender, TemplateAppliedEventArgs? args)
        {
            Generation++;
            Templates++;
            Has = false;
            Unset = MdiGrabZoneDiag.UnsetTemplate;
        }
    }

    /// <summary>창의 끌 수 있는 구간. 키가 같으면 보관값, 다르면 MdiGrabZoneReader 로 다시 읽어 보관한다.</summary>
    internal static MdiGrabZone Get(Control window)
    {
        if (Bound(window) is not { } e)
        {
            MdiGrabZoneDiag.Note(MdiGrabZoneDiag.NotTemplated);
            return MdiGrabZoneReader.Read(window);
        }

        var width = window.Bounds.Width;
        var folded = MdiStripFold.IsFolded(window);
        if (IsFresh(e, width, folded))
        {
            MdiGrabZoneDiag.Note(MdiGrabZoneDiag.Hit);
            return e.Zone;
        }

        var version = e.Templates + e.Attached;
        if (e.MissVersion == version && ++e.MissSkips < MissRetryEvery)
        {
            MdiGrabZoneDiag.Note(MdiGrabZoneDiag.MissSkip);
            return MdiGrabZone.Whole; // 이 구조에서 이미 부품이 없음을 확인했다 — 창 전체 시각 트리 순회를 반복하지 않는다(D-24)
        }

        MdiGrabZoneDiag.Note(!e.Has ? e.Unset : e.Width != width ? MdiGrabZoneDiag.Width : e.Folded != folded ? MdiGrabZoneDiag.Folded
            : e.KeptGeneration != e.Generation ? MdiGrabZoneDiag.Generation : MdiGrabZoneDiag.Global);
        e.MissSkips = 0;
        var zone = MdiGrabZoneReader.Read(window, out var partsMissing);
        e.MissVersion = partsMissing ? version : -1;
        if (zone.RightInset < 0 || zone == MdiGrabZone.Whole)
        {
            e.Has = false; // 낡았거나 아직 배치 전인 값은 저장하지 않는다(다음 판정이 다시 읽는다)
            e.Unset = zone.RightInset < 0 ? MdiGrabZoneDiag.UnsetNegative : MdiGrabZoneDiag.UnsetWhole;
            return zone;
        }

        e.Has = true;
        e.Width = width;
        e.Folded = folded;
        e.KeptGeneration = e.Generation;
        e.KeptGlobal = s_global;
        e.Zone = zone;
        return zone;
    }

    private static bool IsFresh(Entry e, double width, bool folded) =>
        e.Has && e.Width == width && e.Folded == folded && e.KeptGeneration == e.Generation && e.KeptGlobal == s_global;

    /// <summary>K2: 지금 Get 을 부르면 보관값을 그대로 돌려줄 상태인가(템플릿 컨트롤만). MdiStripFold 가 같은 입력의 접기 판정을 건너뛸 때 쓴다.</summary>
    internal static bool IsFresh(Control window) =>
        Bound(window) is { } e && IsFresh(e, window.Bounds.Width, MdiStripFold.IsFolded(window));

    /// <summary>창 하나의 보관값을 버린다(접힘 토글·SDI 분리).</summary>
    internal static void Invalidate(Control window)
    {
        if (Entries.TryGetValue(window, out var e))
        {
            e.Has = false;
            e.Unset = MdiGrabZoneDiag.UnsetInvalidate;
        }
    }

    /// <summary>모든 창의 보관값을 버린다(끌기 스냅샷 무효화 — 크기 조절·진단 구동).</summary>
    internal static void InvalidateAll() => s_global++;

    /// <summary>기능 버튼 줄과 그 머리줄의 크기 변경을 지켜 세대를 올린다(줄 폭 = 제목 칸 둘째 열 폭, 머리줄 높이 = 구간 아래 끝).</summary>
    internal static void WatchStrip(Control window, Control strip)
    {
        if (Bound(window) is not { } e || ReferenceEquals(e.WatchedStrip, strip))
        {
            return;
        }

        e.WatchedStrip = strip;
        strip.SizeChanged += (_, _) => e.Generation++;
        foreach (var v in strip.GetVisualAncestors())
        {
            if (v is Control { Name: TemplateParts.Header } header)
            {
                header.SizeChanged += (_, _) => e.Generation++;
                break;
            }

            if (ReferenceEquals(v, window))
            {
                break;
            }
        }
    }

    private static Entry Create(Control window)
    {
        var e = new Entry();
        window.AttachedToVisualTree += (_, _) => { e.Attached++; };
        return e;
    }

    /// <summary>
    /// K1(01a 2026-11-14): 항목 컨트롤의 보관 항목. 배치 항목(MdiLayoutEntry.Control)은 창 자신이 아니라 템플릿 컨트롤이 아닌 담는 컨트롤일 수 있다 —
    /// r9 전 진단 카운터에서 끌기 중 Get 이 전부 '템플릿 컨트롤 아님'으로 매번 읽었다(적중 0). 그래서 항목 자신 또는 바로 아래 시각 자식 중 템플릿 컨트롤(창)의
    /// 템플릿 재적용을 세대로 쓴다. 담는 컨트롤의 문서(DataContext)나 자식 창이 바뀌면 다시 찾고 세대를 올린다. 둘 다 아니면 null(보관하지 않고 매번 읽는다).
    /// </summary>
    private static Entry? Bound(Control window)
    {
        var e = Entries.GetValue(window, Create);
        if (e.Templated is { } t && ReferenceEquals(window.DataContext, e.BoundContext)
            && (ReferenceEquals(t, window) || ReferenceEquals(t.GetVisualParent(), window)))
        {
            return e;
        }

        var found = TemplatedOf(window);
        if (found is null)
        {
            return null;
        }

        if (!ReferenceEquals(found, e.Templated))
        {
            if (e.Templated is { } old)
            {
                old.TemplateApplied -= e.OnTemplateApplied;
                e.OnTemplateApplied(old, null); // 다른 창으로 바뀌었다 — 옛 구간·기능 버튼 줄을 버린다
            }

            e.Templated = found;
            found.TemplateApplied += e.OnTemplateApplied;
            MdiGrabZoneDiag.NoteTypes(window, found);
        }
        else
        {
            e.Generation++; // 같은 창에 다른 문서가 들어왔다 — 구간을 다시 잰다
            e.Has = false;
        }

        e.BoundContext = window.DataContext;
        return e;
    }

    private static TemplatedControl? TemplatedOf(Control window)
    {
        if (window is TemplatedControl self)
        {
            return self;
        }

        foreach (var v in window.GetVisualChildren())
        {
            if (v is TemplatedControl child)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>창 템플릿이 적용된 횟수(템플릿 컨트롤을 못 찾으면 0). 이 캐시가 처음 본 뒤의 재적용만 센다.</summary>
    internal static int TemplatesOf(Control window) => Bound(window)?.Templates ?? 0;

    /// <summary>구조 버전(템플릿 적용 횟수 + 시각 트리 부착 횟수). 같으면 같은 '못 찾음' 결과를 다시 내지 않는다(MdiStripFold 도 같은 값으로 FindHost 를 건너뛴다).</summary>
    internal static int StructureOf(Control window) => Bound(window) is { } e ? e.Templates + e.Attached : 0;
}

/// <summary>
/// K1·K2 진단 카운터(01a 2026-11-14): 구간 캐시 적중·실패 원인과 접기 판정 건너뜀 횟수. 제품 빌드는 Note 호출이 빠진다(Conditional).
/// 원격 chrome.moveLight 조회 응답 grabZone 에 실린다(reset 은 grabZoneReset=true). UI 스레드 전용.
/// </summary>
internal static class MdiGrabZoneDiag
{
    internal const int Hit = 0, NotTemplated = 1, MissSkip = 2, First = 3, UnsetInvalidate = 4, UnsetNegative = 5, UnsetWhole = 6,
        UnsetTemplate = 7, Width = 8, Folded = 9, Generation = 10, Global = 11, FoldSkip = 12, FoldRun = 13;

    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal static void Note(int kind)
    {
#if PD_DIAGNOSTICS
        Counts[kind]++;
#endif
    }

    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal static void NoteTypes(Control item, Control templated)
    {
#if PD_DIAGNOSTICS
        s_types = item.GetType().Name + ">" + templated.GetType().Name; // 마지막으로 묶은 배치 항목 > 템플릿 창 타입(원인 근거)
#endif
    }

#if PD_DIAGNOSTICS
    private static readonly string[] Names =
    {
        "hit", "readNotTemplated", "missSkip", "readFirst", "readInvalidated", "readNegativeNotKept", "readWholeNotKept",
        "readTemplate", "readWidth", "readFolded", "readGeneration", "readGlobal", "foldSkip", "foldRun",
    };

    private static readonly long[] Counts = new long[Names.Length];
    private static string? s_types;

    internal static void Reset() => System.Array.Clear(Counts);

    internal static System.Text.Json.Nodes.JsonObject ToJson()
    {
        var o = new System.Text.Json.Nodes.JsonObject { ["boundTypes"] = s_types };
        for (var i = 0; i < Names.Length; i++)
        {
            o[Names[i]] = Counts[i];
        }

        return o;
    }
#endif
}

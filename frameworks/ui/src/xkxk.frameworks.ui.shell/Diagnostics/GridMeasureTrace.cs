// 그리드 측정 호출 단위 기록(진단 빌드 전용) — Q4 원인 확정용: 호출마다 받은 원래 제약·레이아웃 패스 번호·호출 스택(Measure 계열 프레임)·조상 체인 이전 제약을 남긴다
#if PD_DIAGNOSTICS
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>모든 그리드 문서가 공유하는 측정 기록(UI 스레드 전용). 원격 grid.mtrace 가 읽는다.</summary>
public static class GridMeasureTrace
{
    /// <summary>측정 호출 1건. Raw = 받은 측정 제약.</summary>
    public sealed record MeasureCall(int Seq, int Pass, int InPass, double TimeMs, double RawW, double RawH, string Stack, string Chain);

    private const int MeasureTraceCapacity = 400; // 전환 1회 60~80건 × 5회를 담는 크기(넘으면 오래된 것부터 버린다)
    private static readonly Queue<MeasureCall> s_measureCalls = new();
    private static int s_measureSeq;
    private static int s_layoutPass; // 최상위 TopLevel.LayoutUpdated(레이아웃 패스 끝) 횟수
    private static int s_lastPassSeen = -1;
    private static int s_inPass;
    private static TopLevel? s_passTop;

    /// <summary>측정 기록 사본(오래된 것 먼저).</summary>
    public static IReadOnlyList<MeasureCall> Calls() => s_measureCalls.ToArray();

    public static void Clear() { s_measureCalls.Clear(); s_measureSeq = 0; }

    /// <summary>MeasureOverride 첫 줄에서 안정화 전 제약으로 부른다. 패스 번호는 최상위 창의 LayoutUpdated 로 센다.</summary>
    public static void Trace(Visual view, Size raw)
    {
        var top = TopLevel.GetTopLevel(view);
        if (top is not null && !ReferenceEquals(top, s_passTop))
        {
            if (s_passTop is not null) { s_passTop.LayoutUpdated -= OnPassEnded; }
            s_passTop = top;
            top.LayoutUpdated += OnPassEnded;
        }

        if (s_lastPassSeen != s_layoutPass) { s_lastPassSeen = s_layoutPass; s_inPass = 0; }
        var call = new MeasureCall(++s_measureSeq, s_layoutPass, ++s_inPass, PerfProbe.Current.NowMs,
            raw.Width, raw.Height, MeasureStack(), AncestorChain(view));
        s_measureCalls.Enqueue(call);
        while (s_measureCalls.Count > MeasureTraceCapacity) { s_measureCalls.Dequeue(); }
    }

    private static void OnPassEnded(object? sender, System.EventArgs e) => s_layoutPass++;

    /// <summary>호출 스택에서 측정 계열 프레임만 남긴다("형식.메서드" ← 순). 바깥 호출자가 오른쪽.</summary>
    private static string MeasureStack()
    {
        var sb = new StringBuilder();
        var frames = new System.Diagnostics.StackTrace(2, false).GetFrames();
        foreach (var f in frames)
        {
            var m = f.GetMethod();
            if (m is null) { continue; }
            var n = m.Name;
            var keep = n.Contains("Measure") || n.Contains("Layout") || n.Contains("Arrange") || n.StartsWith("Calculate") || n.Contains("Proportion");
            if (!keep) { continue; }
            if (sb.Length > 0) { sb.Append(" <- "); }
            sb.Append(m.DeclaringType?.Name).Append('.').Append(n);
            if (sb.Length > 1500) { sb.Append(" …"); break; }
        }

        return sb.ToString();
    }

    /// <summary>조상 체인(가까운 것부터 최대 14단): 형식#이름 = 그 조상이 직전에 받은 측정 제약 폭/현재 Bounds 폭.</summary>
    private static string AncestorChain(Visual self)
    {
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var v in self.GetVisualAncestors())
        {
            if (++depth > 14) { break; }
            if (v is not Layoutable l) { continue; }
            var prev = LayoutInformation.GetPreviousMeasureConstraint(l);
            if (sb.Length > 0) { sb.Append(" | "); }
            sb.Append(l.GetType().Name);
            if (l is Control { Name: { Length: > 0 } name }) { sb.Append('#').Append(name); }
            sb.Append('=').Append(prev is { } p ? F(p.Width) : "-").Append('/').Append(F(l.Bounds.Width));
        }

        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
#endif

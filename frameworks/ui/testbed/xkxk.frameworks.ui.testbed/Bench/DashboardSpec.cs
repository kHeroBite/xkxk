// 대시보드 벤치의 차트 정의(형식·한글 제목·시드 고정 데이터) — 모든 라이브러리가 같은 데이터를 그리게 한다(U4~U6)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.testbed.Bench;

public enum DashKind { Line, Bar, StackedBar, Pie, Donut, Area, Gauge, Combo }

/// <summary>차트 1개의 정의. Series[i] 는 시리즈 i 의 값(선·영역은 점 수 개, 막대는 범주 수 개, 파이·도넛은 조각 수 개, 게이지는 비움). Gauge 는 0~100 값.</summary>
public sealed record DashChartSpec(DashKind Kind, string Title, string[] Labels, string[] SeriesNames, double[][] Series, double Gauge = 0);

/// <summary>
/// 대시보드 구성(설계서 B1 3절). 6개 = 선·막대·파이·도넛·영역·게이지, 12개(관문) = 선3·막대2·누적1·파이1·도넛1·영역1·게이지2·콤보1, 24개 = 12개 구성을 시드만 바꿔 2벌.
/// </summary>
public static class DashboardSpec
{
    private static readonly string[] Months = { "1월", "2월", "3월", "4월", "5월", "6월", "7월", "8월", "9월", "10월", "11월", "12월" };
    private static readonly string[] Teams = { "기획", "설계", "개발", "시험", "배포", "운영" };
    private static readonly string[] Status = { "완료", "진행", "지연", "보류", "대기", "취소" };

    public static IReadOnlyList<DashChartSpec> Build(int count, int points)
    {
        if (count is not (6 or 12 or 24)) { throw new ArgumentException($"차트 수는 6|12|24 중 하나: {count}"); }
        var baseSet = Twelve(points, 0);
        if (count == 12) { return baseSet; }
        if (count == 6) { return new[] { baseSet[0], baseSet[3], baseSet[6], baseSet[7], baseSet[8], baseSet[9] }; }
        var all = new List<DashChartSpec>(baseSet);
        all.AddRange(Twelve(points, 100));
        return all;
    }

    private static List<DashChartSpec> Twelve(int points, int seedBase) => new()
    {
        Walk(DashKind.Line, "공정률 추이 (%)", points, seedBase + 1, "계획", "실적"),
        Walk(DashKind.Line, "번다운 (잔여 작업)", points, seedBase + 2, "이상선", "실제"),
        Walk(DashKind.Line, "비용 누적 추이", points, seedBase + 3, "예산", "집행"),
        Bars(DashKind.Bar, "월별 매출", Months, seedBase + 4, "매출"),
        Bars(DashKind.Bar, "부서별 인원", Teams, seedBase + 5, "인원"),
        Bars(DashKind.StackedBar, "단계별 공수 (계획·실적·예비·지연)", Teams, seedBase + 6, "계획", "실적", "예비", "지연"),
        Slices(DashKind.Pie, "작업 상태 비율", seedBase + 7),
        Slices(DashKind.Donut, "리스크 등급 비율", seedBase + 8),
        Walk(DashKind.Area, "자원 부하 (면적)", Math.Min(points, 500), seedBase + 9, "개발", "시험"),
        new DashChartSpec(DashKind.Gauge, "진척률 (%)", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<double[]>(), 72),
        new DashChartSpec(DashKind.Gauge, "비용 성과 지수 CPI (x100)", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<double[]>(), 94),
        Bars(DashKind.Combo, "매출(막대)·이익률(선)", Months, seedBase + 12, "매출", "이익률"),
    };

    private static DashChartSpec Walk(DashKind k, string title, int points, int seed, params string[] names)
    {
        var series = new double[names.Length][];
        for (var i = 0; i < names.Length; i++) { series[i] = BenchSignal.RandomWalk(points, seed * 10 + i); }
        return new DashChartSpec(k, title, Array.Empty<string>(), names, series);
    }

    private static DashChartSpec Bars(DashKind k, string title, string[] labels, int seed, params string[] names)
    {
        var series = new double[names.Length][];
        var rnd = new Random(seed);
        for (var i = 0; i < names.Length; i++)
        {
            series[i] = new double[labels.Length];
            for (var j = 0; j < labels.Length; j++) { series[i][j] = Math.Round(20 + rnd.NextDouble() * 60, 1); }
        }

        return new DashChartSpec(k, title, labels, names, series);
    }

    private static DashChartSpec Slices(DashKind k, string title, int seed)
    {
        var rnd = new Random(seed);
        var v = new double[Status.Length];
        for (var i = 0; i < v.Length; i++) { v[i] = Math.Round(5 + rnd.NextDouble() * 30, 1); }
        return new DashChartSpec(k, title, Status, new[] { "비율" }, new[] { v });
    }
}

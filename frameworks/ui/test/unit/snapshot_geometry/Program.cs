// 덮개 그림 배율 단위 시험 진입점 — 배율·크기·트리마다 옛 식(dpi 찍기·Image 표시)과 새 식(제품 Snapshot*)을 실제로 그려 표식 위치를 비교한다
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using xkxk.frameworks.ui.views.Depth;

namespace Pd.Test.SnapshotScale;

/// <summary>
/// 사용: dotnet run -c Release -- [--out bin/result/snapshot_geometry.json]
/// rc 0 = 새 식 전부 PASS 이고 음성 대조(옛 식)가 배율 ≠ 1 에서 기대대로 어긋남, 1 = 그 밖. 결과 폴더는 고정이며 덮어쓴다.
/// </summary>
internal sealed class TestApp : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
}

internal static class Program
{
    public const string KoreanFont = "avares://SnapshotGeometryCheck/Assets/Fonts#Noto Sans KR"; // Android 와 같은 파일(링크)
    private static readonly double[] Scales = { 1, 1.25, 1.5, 1.75, 2, 2.25, 2.625, 3 };
    private static readonly Size[] Sizes = { new(401.8, 258.7), new(333.333, 200.25), new(100, 100), new(57.1, 187.3), new(805.33, 87.3), new(1087.3, 58.7) };

    private static int Main(string[] args)
    {
        var outPath = Path.GetFullPath(args.Length >= 2 && args[0] == "--out" ? args[1] : Path.Combine("bin", "result", "snapshot_geometry.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        // 글꼴은 Android 판과 같게(임베드 Noto Sans KR 을 기본·대체 글꼴로). 입력칸 안내 문구는 Fluent 테마가 그린다
        AppBuilder.Configure<TestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(new FontManagerOptions { DefaultFamilyName = KoreanFont, FontFallbacks = [new FontFallback { FontFamily = new FontFamily(KoreanFont) }] })
            .SetupWithoutStarting();

        var rows = new List<Dictionary<string, object>>();
        var newFail = 0;
        var controlMiss = 0;
        var missingRows = 0; // 표식 없음·FAIL·대조 어긋남이 하나라도 있는 행(화면에 행마다 출력)
        foreach (var tree in Trees.All)
        {
            foreach (var scale in Scales)
            {
                foreach (var size in Sizes)
                {
                    var r = Checks.Run(tree, size, scale);
                    rows.Add(r.ToRow());
                    newFail += r.NewOk ? 0 : 1; // 새 식 단계의 표식 없음(무한대)은 허용 오차를 넘으므로 여기서 FAIL 로 셈
                    controlMiss += r.ControlAsExpected ? 0 : 1;
                    var missing = string.Join(",", r.MissingSteps());
                    if (missing.Length > 0 || !r.NewOk || !r.ControlAsExpected)
                    {
                        missingRows++;
                        Console.WriteLine($"  {tree.Name} s={scale} {size.Width}x{size.Height} new_ok={r.NewOk} control={r.ControlAsExpected} 표식없음=[{missing}] old_factor={r.OldFactor}");
                    }
                }
            }
        }

        var geometryFail = Checks.PureGeometry(Scales, Sizes);
        var text = new List<Dictionary<string, object>>();
        int textNewFail = 0, textOldFail = 0;
        foreach (var scale in TextCheck.Scales)
        {
            var t = TextCheck.Run(scale);
            text.Add(t);
            textNewFail += (bool)t["new_ok"] ? 0 : 1;
            textOldFail += (bool)t["old_ok"] ? 0 : 1;
            Console.WriteLine($"  text s={scale} old={t["old_mismatch"]} new={t["new_mismatch"]} mode={t["mode"]}");
        }

        var backdrop = BackdropCheck.RunAll(out var backdropFail, out var backdropControlMiss);
        foreach (var b in backdrop.Where(b => !(bool)b["new_ok"] || !(bool)b["control_ok"]))
        {
            Console.WriteLine($"  backdrop {b["case"]} s={b["scale"]} layers={b["layers"]}/{b["layers_want"]} new_mean={b["new_mean"]} alpha={b["new_min_alpha"]} old_mean={b["old_mean"]} old_alpha={b["old_min_alpha"]}");
        }

        var ok = newFail == 0 && controlMiss == 0 && geometryFail == 0 && textNewFail == 0 && backdropFail == 0 && backdropControlMiss == 0;
        var summary = new Dictionary<string, object>
        {
            ["verdict"] = ok ? "PASS" : "FAIL",
            ["renderer"] = Checks.RendererMode,
            ["cases"] = rows.Count,
            ["new_fail"] = newFail,
            ["control_not_as_expected"] = controlMiss,
            ["geometry_fail"] = geometryFail,
            ["flagged_rows"] = missingRows,
            ["text_new_fail"] = textNewFail, // 글자(한글·굵게·안내 문구)가 화면 그림과 다른 새 찍기 배율 수
            ["text_old_fail"] = textOldFail, // 같은 비교의 옛 찍기(dpi) — 참고(대조)
            ["text"] = text,
            ["backdrop_fail"] = backdropFail,                // 덮개 바탕 층(반투명 표면) 새 규칙 FAIL 수
            ["backdrop_control_miss"] = backdropControlMiss, // 옛 한 겹 규칙이 glass 사례에서 어긋나지 않은 수(음성 대조 실패)
            ["backdrop"] = backdrop,
            ["rows"] = rows,
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{summary["verdict"]} cases={rows.Count} new_fail={newFail} control_not_as_expected={controlMiss} geometry_fail={geometryFail} flagged_rows={missingRows} text_new_fail={textNewFail} text_old_fail={textOldFail} backdrop_fail={backdropFail} backdrop_control_miss={backdropControlMiss} renderer={Checks.RendererMode} → {outPath}");
        return ok ? 0 : 1;
    }
}

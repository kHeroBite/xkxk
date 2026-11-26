// 도움말 투어 단계 공급(요건 6) — 단계 데이터는 tour_catalog.json 이 정본이다. 이 파일은 소스 교체 지점과 창·방식·플랫폼·조건 적용을 맡는다
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Tour;

/// <summary>카탈로그 원문 공급자. 원격 갱신을 넣게 되면 이 인터페이스를 구현해 <see cref="TourCatalog.UseSource"/> 로 넘긴다.</summary>
public interface ITourCatalogSource
{
    /// <summary>출처 이름(로그·진단용).</summary>
    string Name { get; }

    /// <summary>JSON 원문. 읽지 못하면 null.</summary>
    string? ReadJson();
}

/// <summary>기본 소스: 어셈블리에 내장한 tour_catalog.json. 내용을 바꾸면 csproj·코드 수정 없이 빌드만으로 반영된다.</summary>
public sealed class EmbeddedTourCatalogSource : ITourCatalogSource
{
    public const string ResourceName = "xkxk.frameworks.ui.shell.Tour.tour_catalog.json";

    public string Name => "embedded";

    public string? ReadJson()
    {
        using var stream = typeof(TourCatalog).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>한 창에 적용할 단계와, 조건이 맞지 않아 뺀 단계(사유 포함).</summary>
public sealed record TourPlan(IReadOnlyList<TourStep> Steps, IReadOnlyList<(TourStep Step, string Reason)> Excluded);

public static class TourCatalog
{
    /// <summary>문서 본문 앵커. AutomationId 가 없는 문서 래퍼를 TourTargets 가 찾는다.</summary>
    public const string ContentAnchor = "@content";

    private static ITourCatalogSource s_source = new EmbeddedTourCatalogSource();
    private static TourCatalogData? s_current;

    /// <summary>지금 카탈로그(처음 쓸 때 읽는다). 읽기 실패도 앱을 멈추지 않고 빈 목록으로 간다. 그때 투어는 마지막 레이어만 보여 준다.</summary>
    public static TourCatalogData Current => s_current ??= Load(s_source);

    /// <summary>모든 단계(카탈로그 순서).</summary>
    public static IEnumerable<TourStep> All => Current.Steps;

    /// <summary>소스를 바꾼다. 진행 중 세션은 시작할 때 받은 단계를 그대로 쓰고, 다음 시작부터 적용된다.</summary>
    public static void UseSource(ITourCatalogSource? source)
    {
        s_source = source ?? new EmbeddedTourCatalogSource();
        s_current = null;
    }

    /// <summary>해당 창·UI 방식·플랫폼·조건에 적용되는 단계와 빠진 단계(대상 가시성 판정 전, 카탈로그 순서).</summary>
    public static TourPlan Plan(TourScope scope, TourModes mode, TourPlatforms platform)
    {
        var steps = new List<TourStep>();
        var excluded = new List<(TourStep, string)>();
        foreach (var step in All.Where(s => s.Scope == scope))
        {
            var reason = (step.Modes & mode) == 0 ? "mode"
                : (step.Platforms & platform) == 0 ? "platform"
                : !TourConditions.IsMet(step.Requires) ? "requires:" + step.Requires
                : null;
            if (reason is null)
            {
                steps.Add(step);
            }
            else
            {
                excluded.Add((step, reason));
            }
        }

        return new TourPlan(steps, excluded);
    }

    /// <summary>해당 창·UI 방식·플랫폼·조건에 적용되는 단계.</summary>
    public static IReadOnlyList<TourStep> For(TourScope scope, TourModes mode, TourPlatforms platform) => Plan(scope, mode, platform).Steps;

    /// <summary>문구: 카탈로그 소스가 가진 문구가 있으면 그것, 없으면 앱 사전. 저장소 카탈로그는 texts 를 갖지 않는다(검사기가 막는다).</summary>
    public static string Tr(string key) =>
        Current.Texts.TryGetValue(XkxkUi.Current.Localization.Language, out var texts) && texts.TryGetValue(key, out var text) ? text : Loc.Tr(key);

    private static TourCatalogData Load(ITourCatalogSource source)
    {
        var data = TourCatalogParser.Parse(source.ReadJson(), source.Name);
        foreach (var error in data.Errors)
        {
            PdLog.WriteSystem(PdLogLevel.Error, "tour catalog: " + error);
        }

        PdLog.WriteSystem(PdLogLevel.Information, $"tour catalog loaded source={source.Name} version={data.Version} steps={data.Steps.Count} shortcuts={data.Shortcuts.Count} features={data.Features.Count} errors={data.Errors.Count}");
        return data;
    }
}

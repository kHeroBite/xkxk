// 단축키·기능 레코드와 조회(요건 6) — 목록은 tour_catalog.json 이 정본이고, 메인 화면 키 바인딩과 투어 마지막 레이어가 같은 목록을 읽는다
using System.Collections.Generic;
using System.Linq;

namespace Pd.Poc.Shell.Tour;

/// <summary>단축키 한 줄. Keys 는 화면 표기이자 바인딩 제스처이고 검사기 대조 값이다.</summary>
/// <param name="Id">행 식별자(원격 시험용).</param>
/// <param name="Keys">키 조합 표기.</param>
/// <param name="DescKey">설명 사전 키.</param>
/// <param name="WebUnavailable">웹에서는 브라우저·OS 가 먼저 가로채 쓸 수 없다(04_웹 W-16).</param>
/// <param name="MacOnly">macOS 에서만 동작한다.</param>
/// <param name="Binding">메인 화면에 키 바인딩으로 걸 명령 이름. 없으면 전용 입력 처리기나 OS 가 처리한다.</param>
public sealed record ShortcutEntry(string Id, string Keys, string DescKey, bool WebUnavailable = false, bool MacOnly = false, string? Binding = null);

/// <summary>기능 설명 한 줄. 조건이 안 맞는 UI 방식·플랫폼에서는 보이지 않는다.</summary>
public sealed record FeatureEntry(string Id, string TitleKey, string BodyKey, TourModes Modes = TourModes.All, TourPlatforms Platforms = TourPlatforms.All, string? Requires = null);

public static class ShortcutCatalog
{
    /// <summary>등록된 전체 단축키.</summary>
    public static IReadOnlyList<ShortcutEntry> Shortcuts => TourCatalog.Current.Shortcuts;

    /// <summary>등록된 전체 기능 설명.</summary>
    public static IReadOnlyList<FeatureEntry> Features => TourCatalog.Current.Features;

    /// <summary>이 플랫폼에서 보여 줄 단축키. 웹 예약키(WebUnavailable)는 숨기지 않고 '웹에서는 사용 불가' 로 표기한다.</summary>
    public static IEnumerable<ShortcutEntry> For(TourPlatforms platform, bool isMac)
    {
        if (platform == TourPlatforms.Android)
        {
            return Enumerable.Empty<ShortcutEntry>(); // 물리 키보드를 전제하지 않는다
        }

        return Shortcuts.Where(s => !s.MacOnly || isMac || (platform == TourPlatforms.Web && s.WebUnavailable));
    }

    /// <summary>이 UI 방식·플랫폼에서 보여 줄 기능 설명.</summary>
    public static IEnumerable<FeatureEntry> FeaturesFor(TourModes mode, TourPlatforms platform) =>
        Features.Where(f => (f.Modes & mode) != 0 && (f.Platforms & platform) != 0 && TourConditions.IsMet(f.Requires));
}

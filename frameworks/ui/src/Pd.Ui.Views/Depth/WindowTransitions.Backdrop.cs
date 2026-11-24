// 닫기 덮개·전환 짝 나감 그림의 바탕(WindowTransitions 의 partial, 21 설계 §4.1) — 자기 바탕이 불투명하지 않은 DOCK·NAV 화면(MDI 창 안 탭 묶음 포함) 그림에 조상 바탕을 불투명해질 때까지 겹쳐 깔아 한 장으로 만든다
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pd.Poc.Shell.Theming;

namespace Pd.Ui.Views.Depth;

public static partial class WindowTransitions
{
    private const string FallbackBackdropKey = "PdBrushBackground"; // 조상에 불투명 바탕이 하나도 없을 때 맨 아래에 까는 테마 자원

    /// <summary>
    /// 그림 밑에 깔 바탕 층. 값을 저장해 두지 않고 찍을 때마다 읽는다(테마 전환·무늬 변경을 따른다). 층을 모으는 규칙은 단위 시험 대상인 SnapshotBackdrop 이다.
    /// MDI 창(겹침·최대화)은 자기 바탕(glass 면 반투명)을 그대로 쓴다 — 닫힌 뒤 창 뒤 모습이 닫기 전과 같다.
    /// 예외: MDI 안 탭 묶음 창(group)은 닫힌 뒤 같은 자리에 남은 문서 창이 그려지므로 조상 바탕을 깐다(21 §4.1).
    /// </summary>
    private static Backdrop? BackdropOf(Control view, MotionPlacement placement, bool group = false)
    {
        if (!group && (placement is MotionPlacement.MdiOverlap or MotionPlacement.MdiMaximized))
        {
            return null;
        }

        var fallback = view.TryFindResource(FallbackBackdropKey, view.ActualThemeVariant, out var found) ? found as IBrush : null;
        var layers = SnapshotBackdrop.Collect(view, fallback, FallbackBackdropKey);
        return layers.Count == 0 ? null : new Backdrop(layers, string.Join("+", layers.Select(l => l.Source)));
    }

    /// <summary>바탕 합성은 단위 시험 대상인 SnapshotRenderer.Compose 가 한다(화소 원본 사각형 규약).</summary>
    private static RenderTargetBitmap WithBackdrop(RenderTargetBitmap content, IReadOnlyList<BackdropLayer> layers, SnapshotGeometry geometry) =>
        SnapshotRenderer.Compose(content, layers, geometry);

    /// <param name="Layers">아래→위 순 바탕 층.</param>
    /// <param name="Source">진단 base — 층 주인 이름을 아래부터 '+' 로 잇는다.</param>
    private sealed record Backdrop(IReadOnlyList<BackdropLayer> Layers, string Source);
}

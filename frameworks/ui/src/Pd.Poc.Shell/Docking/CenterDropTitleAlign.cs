// 분할 밖 놓기로 MDI 창이 된 문서의 제목줄 중앙을 놓은 커서에 맞추는 실측 보정(C4)
using Pd.Client.Data.Logging;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 배경: CenterDropToMdi.Place 의 추정(창 폭/2, 제목줄 높이/2)은 그림자 여백·테두리 두께를 모른다.
/// 그래서 배치가 끝난 뒤 실제 PART_Header 의 중앙을 MDI 배치 패널 좌표로 재서 커서와의 차이만큼 MdiBounds 를 옮긴다(오차 &lt; 0.25 DIP).
/// 단위는 DIP 이다. 물리 px 오차는 DIP 오차 × RenderScaling 이라 배율 1.25~2 에서도 1px 안이다.
/// 보정 규칙(커서가 캔버스 밖일 때): 가로 중앙은 캔버스 폭 안, 세로 중앙은 제목줄 높이/2 이상 캔버스 높이-제목줄 높이/2 이하로 옮겨 제목줄이 캔버스 안에 남게 한다.
/// </summary>
internal static class CenterDropTitleAlign
{
    private const int MaxTries = 4;
    private const double Tolerance = 0.25;
    private const double DefaultTitleHeight = 32;

    /// <summary>MDI 제목줄 높이(DIP). 테마 자원 PdMdiTitleHeight, 못 읽으면 32.</summary>
    internal static double TitleHeight() =>
        Application.Current?.TryGetResource("PdMdiTitleHeight", null, out var v) == true && v is double d && d > 0 ? d : DefaultTitleHeight;

    /// <summary>
    /// 놓은 자리 표시: 드롭으로 배치한 창과 50% 규칙으로 재결합한 창(SdiScreenPlacement, 요건 4)은 Dock 기본 배치의 영역 안 clamp(ClassicMdiLayoutManager.Arrange)를 받지 않는다(D-C3-12 — 창 일부가 캔버스 밖이어도 제목줄 중앙=커서).
    /// MdiBounds 가 놓은 값 그대로일 때만 유효하다 — 사용자가 끌거나 크기를 바꾸면 값이 달라져 바로 일반 규칙(MdiDragLimit)으로 돌아간다.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IMdiDocument, DockRect[]> s_dropBounds = new();

    internal static void Mark(PocDocument doc, DockRect bounds) => s_dropBounds.AddOrUpdate(doc, [bounds]);

    /// <summary>드롭 배치 값 그대로(보통 상태·MdiBounds 불변)면 true — MagnetMdiLayoutManager.Arrange 가 clamp 없이 둔다.</summary>
    internal static bool IsDropPlaced(IMdiDocument doc) =>
        doc.MdiState == MdiWindowState.Normal && s_dropBounds.TryGetValue(doc, out var m) && m[0].Equals(doc.MdiBounds);

    /// <summary>커서(host 좌표)를 목표로 삼는다. 캔버스 안 유지 보정은 하지 않는다(커서는 항상 문서 칸 안이라 제목줄 중앙도 안에 있다). 칸 밖 좌표만 칸 경계로 맞춘다.</summary>
    internal static Point Target(Point cursor, Size area) =>
        new(Math.Clamp(cursor.X, 0, Math.Max(0, area.Width)), Math.Clamp(cursor.Y, 0, Math.Max(0, area.Height)));

    /// <summary>배치 뒤 실제 제목줄 중앙이 target 과 어긋나면 MdiBounds 를 그만큼 옮긴다(최대 MaxTries 회). 진단 로그 한 줄을 남긴다.</summary>
    internal static void Refine(PocDocument doc, Control host, Point cursor, Point target) => Step(doc, host, cursor, target, MaxTries);

    private static void Step(PocDocument doc, Control host, Point cursor, Point target, int left)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var header = HeaderOf(doc, host);
            if (header is null || header.Bounds.Width <= 0 || header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), host) is not { } center)
            {
                if (left > 1)
                {
                    Step(doc, host, cursor, target, left - 1);
                }

                return;
            }

            double dx = target.X - center.X, dy = target.Y - center.Y;
            var scale = TopLevel.GetTopLevel(host)?.RenderScaling ?? 1.0;
            var ok = Math.Abs(dx) < Tolerance && Math.Abs(dy) < Tolerance;
            var text = $"cursor=({cursor.X:0.##},{cursor.Y:0.##}) target=({target.X:0.##},{target.Y:0.##}) titleCenter=({center.X:0.##},{center.Y:0.##}) d=({dx:0.##},{dy:0.##}) scale={scale} left={left} {(ok ? "OK" : "adjust")}";
            PdLog.Info("mdi-drop", text);
#if PD_DIAGNOSTICS
            App.MainViewModel?.AddLog("[mdi-drop] " + text); // log.tail(앱 로그 패널)에서도 보이게(진단 빌드 한정)
#endif
            if (ok)
            {
                return;
            }

            var b = doc.MdiBounds;
            var moved = new DockRect(b.X + dx, b.Y + dy, b.Width, b.Height);
            Mark(doc, moved);
            doc.MdiBounds = moved;
            if (left > 1)
            {
                Step(doc, host, cursor, target, left - 1);
            }
        }, DispatcherPriority.Loaded);
    }

    private static Control? HeaderOf(PocDocument doc, Control host) =>
        host is Panel panel
            ? panel.Children.OfType<Control>().Where(c => ReferenceEquals(WindowCommands.FindDocument(c.DataContext), doc))
                .SelectMany(c => c.GetSelfAndVisualDescendants().OfType<Control>()).FirstOrDefault(c => c.Name == TemplateParts.Header)
            : null;
}

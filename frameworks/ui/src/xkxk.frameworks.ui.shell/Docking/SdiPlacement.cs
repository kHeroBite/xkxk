// 독립(SDI) 창 ⑦ 위치 초기화(웹 관리형 창 경로, 옛 WindowCommands.Sdi) — 공식은 SdiResetLayout(데스크톱 OS 창과 공용), 이 파일은 창 목록 수집과 적용만. ⑧⑨⑩·SdiDocuments 는 WindowStateOps
using System.Collections.Generic;
using Avalonia;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>SDI 문서 목록은 s.Owner.StateOps.SdiDocuments(), 창 조회는 s.FindWindow 를 쓴다.</summary>
[WindowCommandsPart]
internal sealed class SdiPlacement
{
    private readonly WindowSession _s;

    internal SdiPlacement(WindowSession s) => _s = s;

    /// <summary>
    /// ⑦ 기본 위치의 작업 영역 기준 오프셋. Mars 는 폼마다 WinPropDef X·Y(클래스 기본값 0,0 — class.Collection.cs:811~812)를 쓰지만
    /// PD 문서에는 폼별 기본 위치 정의가 없어 구현 상수로 둔다(작업 영역 좌상단에서 40,40 — 창 머리줄이 가장자리에 붙지 않게).
    /// </summary>
    public const double SdiCascadeOrigin = 40;

    /// <summary>작업 영역 좌상단 기준 ⑦ 기본 위치(데스크톱·웹 공통).</summary>
    public static Point SdiDefaultPosition(Rect workArea) => new(workArea.X + SdiCascadeOrigin, workArea.Y + SdiCascadeOrigin);

    /// <summary>
    /// ⑦ 독립 창 위치 초기화(웹 관리형 창): 보통 상태로 되돌린 뒤 SdiResetLayout 공식으로 위치만 옮긴다(크기 불변, 위치 고정 창 제외).
    /// layerArea 는 창 레이어 좌표의 사각형을 돌려준다(호출자 ManagedWindowHostCommands). 보통 상태로 되돌린 뒤에 읽어야
    /// 남은 최소화 창 줄(−32) 반영이 맞으므로 함수로 받는다. 위치는 SdiResetLayout.FitInLayer 로 레이어에 맞추고(큰 창은 크기도 줄임, ⑦-6)
    /// 그 값을 모델(Host → MdiBounds)에 그대로 써서 Dock 표시 클램프와 일치시킨다. 반환 = 옮긴 창 수.
    /// </summary>
    public int SdiResetPositions(System.Func<Rect> layerArea) => Reset(layerArea, null);

    /// <summary>창 메뉴 '기본 위치로 이동'(웹 관리형 창): 그 창 하나만 같은 공식으로 옮긴다. 다른 창은 겹침 기준으로만 쓴다.</summary>
    public bool MoveOne(System.Func<Rect> layerArea, IDockWindow only) => Reset(layerArea, only) > 0;

    /// <summary>문서가 떠 있는 SDI 창. 도킹 문서면 null.</summary>
    internal IDockWindow? WindowOf(PocDocument document) => _s.FindWindow(document);

    private int Reset(System.Func<Rect> layerArea, IDockWindow? only)
    {
        using var op = PerfProbe.Current.TimeOperation("ui.sdi.resetPositions");
        var targets = new List<IDockWindow>();
        var inputs = new List<SdiResetWindow>();
        foreach (var doc in _s.Owner.StateOps.SdiDocuments())
        {
            if (_s.FindWindow(doc) is not { } window)
            {
                continue;
            }

            var locked = only is null ? DocumentChromeService.ChromeOf(doc).IsLocked : !ReferenceEquals(window, only);
            if (!locked)
            {
                LayoutSerializer.SetHostState(window, DockWindowState.Normal); // 최대화 크기가 아니라 보통 크기로 공식을 적용
            }

            targets.Add(window);
            inputs.Add(new SdiResetWindow(new Point(window.X, window.Y), SizeOf(window), locked));
        }

        // 레이어 크기를 모르면(빈 사각형) 화면 밖 보정·레이어 맞춤은 하지 않는다 — 빈 영역 가운데(음수 좌표)로 옮기지 않게
        var workArea = layerArea();
        var areas = workArea.Width > 0 && workArea.Height > 0 ? new[] { workArea } : System.Array.Empty<Rect>();
        var placed = SdiResetLayout.Compute(areas, SdiDefaultPosition(workArea), inputs);
        var moved = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            if (placed[i] is not { } p)
            {
                continue;
            }

            var window = targets[i];
            var size = inputs[i].Size;
            var fit = areas.Length > 0
                ? SdiResetLayout.FitInLayer(p - workArea.TopLeft, size, workArea.Size).Translate(new Vector(workArea.X, workArea.Y))
                : new Rect(p, size);
            (window.X, window.Y) = (fit.X, fit.Y);
            window.Host?.SetPosition(fit.X, fit.Y); // ManagedHostWindow 가 MdiBounds 에 그대로 쓴다
            if (fit.Width != size.Width || fit.Height != size.Height)
            {
                // 레이어보다 큰 창만 줄인다(⑦-6). 들어가는 창은 크기 불변(⑦ 기준 — 사용자가 키운 창 유지)
                (window.Width, window.Height) = (fit.Width, fit.Height);
                window.Host?.SetSize(fit.Width, fit.Height);
            }

            moved++;
        }

        _s.Log(Loc.Tr("chrome.log.sdiReset", moved));
        if (moved > 0)
        {
            App.NotifyLayoutChanged(); // 끌기 없는 위치 변경이라 WindowMoveDragEnd 가 오지 않는다 — 자동 저장 dirty(D-5)
        }

        return moved;
    }

    /// <summary>화면 안 판정용 창 크기. 모델 값이 아직 없으면(NaN·0) 새 창 기본 크기로 본다 — 크기 자체는 바꾸지 않는다.</summary>
    private static Size SizeOf(IDockWindow window) => new(
        double.IsFinite(window.Width) && window.Width > 0 ? window.Width : PocDockFactory.DefaultMdiWidth,
        double.IsFinite(window.Height) && window.Height > 0 ? window.Height : PocDockFactory.DefaultMdiHeight);
}

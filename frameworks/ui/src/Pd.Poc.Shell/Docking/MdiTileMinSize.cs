// MDI 바둑판 배치의 최소 크기 보정(U9 ⑩) — Dock 바둑판 결과가 창 최소 크기(WindowMinSize) 아래면 열·행을 줄여 다시 놓고, 넘치는 창은 겹쳐 놓는다
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// Dock MdiLayoutHelper.TileDocuments* 는 창 수로 문서 영역을 나눌 뿐 최소 크기를 보지 않는다(웹 실측: 24개 세로 바둑판에서 폭 38px).
/// Win32 TileWindows 문서(learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-tilewindows)에는 최소 크기·넘침 처리 규칙이 적혀 있지 않다.
/// 그래서 아래는 공식 규칙이 아니라 이 앱의 규칙이다(추정 기반):
/// ① 모든 창이 최소 크기 이상이면 Dock 결과를 그대로 둔다.
/// ② 아니면 최소 크기가 들어가는 열 수·행 수 안에서 칸을 만들고(세로 바둑판 = 열 우선, 가로 바둑판 = 행 우선) 칸 크기는 영역을 고르게 나눈다.
/// ③ 칸보다 창이 많으면 남는 창은 앞 칸에 제목줄 높이만큼 오른쪽·아래로 어긋나게 겹쳐 놓아 제목이 보이게 한다.
/// </summary>
public static class MdiTileMinSize
{
    /// <summary>Dock 바둑판 직후 호출. vertical = 세로 바둑판(나란히 열).</summary>
    public static void Enforce(IDocumentDock dock, bool vertical)
    {
        var min = WindowMinSize.Current;
        var docs = (dock.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<IMdiDocument>() // 창 단위 — 탭 묶음(MdiTabGroupDock)도 창 1개, 묶음은 안 문서 중 하나라도 잠기면 잠긴 창
            .Where(d => d.MdiState == MdiWindowState.Normal && !(d is PocDocument p ? WindowCommands.ChromeOf(p).IsLocked : d is MdiTabGroupDock g && g.Documents.Any(x => WindowCommands.ChromeOf(x).IsLocked))).ToList();
        dock.GetVisibleBounds(out _, out _, out var w, out var h);
        const double x = 0, y = 0; // MdiBounds 는 MDI 패널 기준 — GetVisibleBounds 의 x,y(메인 창 기준)를 원점으로 쓰면 묶음이 밀려 넘쳤다(web_c2dev_n 실측)
        if (docs.Count == 0 || min.Width <= 0 || double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0
            || docs.All(d => d.MdiBounds.Width >= min.Width && d.MdiBounds.Height >= min.Height))
        {
            return;
        }

        var (cols, rows) = Grid(docs.Count, Math.Max(1, (int)(w / min.Width)), Math.Max(1, (int)(h / min.Height)), vertical);
        var cellW = Math.Max(min.Width, w / cols);
        var cellH = Math.Max(min.Height, h / rows);
        var step = min.Height; // 겹칠 때 어긋남 = 제목줄(+틀) 높이
        var cells = cols * rows;
        for (var i = 0; i < docs.Count; i++)
        {
            var cell = i % cells;
            var layer = i / cells;
            var (c, r) = vertical ? (cell / rows, cell % rows) : (cell % cols, cell / cols);
            docs[i].MdiBounds = new DockRect(x + c * cellW + layer * step, y + r * cellH + layer * step, cellW, cellH);
        }
    }

    /// <summary>창 n개를 놓을 열·행: 최대 열·행 안에서, 우선 축(세로 바둑판=열)을 먼저 채운다.</summary>
    /// <summary>계단식 직후 호출(D-26). 위치는 그대로 두고 창 크기만 최소 크기 이상으로 올린다. 잠긴 창·보통 상태가 아닌 창은 건드리지 않는다. 이미 최소 이상이면 아무것도 바꾸지 않는다.</summary>
    public static void EnforceMin(IDocumentDock dock)
    {
        foreach (var d in (dock.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<IMdiDocument>()
                     .Where(d => d.MdiState == MdiWindowState.Normal && !(d is PocDocument p ? WindowCommands.ChromeOf(p).IsLocked : d is MdiTabGroupDock g && g.Documents.Any(x => WindowCommands.ChromeOf(x).IsLocked)))
                     .ToList())
        {
            var fit = WindowMinSize.AtLeast(d.MdiBounds);
            if (!fit.Equals(d.MdiBounds))
            {
                d.MdiBounds = fit;
            }
        }
    }

    private static (int Cols, int Rows) Grid(int n, int maxCols, int maxRows, bool vertical)
    {
        if (vertical)
        {
            var cols = Math.Min(n, maxCols);
            return (cols, Math.Min(maxRows, (int)Math.Ceiling(n / (double)cols)));
        }

        var rows = Math.Min(n, maxRows);
        return (Math.Min(maxCols, (int)Math.Ceiling(n / (double)rows)), rows);
    }
}

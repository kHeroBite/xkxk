// 문서 영역 분할 해제·합류 규칙(U14) — 우클릭 '분할 해제'로 분할 칸 문서를 주 문서 영역(MDI·탭 모드)으로 합치고, 분할 칸 MDI 창을 다른 칸으로 끌어 놓으면 합류시킨 뒤 빈 칸을 정리한다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 분할 칸 = 중앙 문서 영역(Documents)이 아닌 문서 칸(Dock 가장자리 놓기·탭 메뉴 '새 문서 영역'이 만든 Documents.Split.N, DocumentSplitMode). MDI·탭 모드 모두다.
/// 탭 모드에서 탭을 주 칸 탭 줄로 끌어 놓는 합류는 Dock 기본 Fill 이동이 하고, 빈 칸은 Dock RemoveDockable(collapse) → CollapseDock 이 정리한다(디컴파일 FactoryBase).
/// 정리: 문서가 모두 빠진 분할 칸은 PocDockFactory.CollapseDock(→ DocumentSplitCollapse — 몫을 이웃 문서 칸에 돌려준다)으로 뺀다.
/// 문서 객체를 그대로 옮기므로 잠금·배율 등 문서 상태는 유지된다. 위치(MdiBounds)는 분할 칸 안 자리를 그대로 쓴다(주 영역 왼쪽 위 기준).
/// UI 는 Themes/MdiWindowMenu.axaml 의 '분할 해제' 항목(MDI 창 메뉴)과 PdUnsplitTabMenuItem 템플릿(탭 메뉴 — DocumentTabMenu.InstallUnsplit 이 붙인다)이 UnsplitCommand·UnsplitVisible 로 쓴다. 끌어 합류는 PanelJoinDrag 가 TryMergeAt 을 부른다.
/// </summary>
public static class SplitMergeService
{
    /// <summary>MDI 창 우클릭 '분할 해제' 명령. 인자는 메뉴를 연 문서(IDockable). 분할 칸이 없으면 아무것도 하지 않는다.</summary>
    public static ICommand UnsplitCommand { get; } = new RelayCommand<IDockable?>(d =>
    {
        if (d?.Owner?.Factory is PocDockFactory factory)
        {
            Unsplit(factory);
        }
    });

    /// <summary>
    /// '분할 해제' 항목 표시 여부 다중 변환기. values[0] = 메뉴를 연 문서의 Owner, 나머지(메뉴 IsOpen 등)는 다시 계산 신호다 —
    /// Owner 만 받으면 분할이 생겨도 다시 계산되지 않아 주 칸 창 메뉴에서 숨은 채 남았다(c2dev_n 실측). 분할이 하나라도 있는 문서 칸이면 true.
    /// </summary>
    public static IMultiValueConverter UnsplitVisible { get; } = new FuncMultiValueConverter<object?, bool>(values =>
        values.FirstOrDefault() is IDock { Factory: PocDockFactory factory } dock && IsDocumentArea(factory, dock) && SplitAreas(factory).Count > 0);

    /// <summary>문서 칸(중앙 문서 영역 또는 분할 칸 — MDI·탭 모드 모두)인가. 측면 패널·알림 서랍 탭 영역은 아니다.</summary>
    internal static bool IsDocumentArea(PocDockFactory factory, IDockable? dock) =>
        dock is IDocumentDock and not PanelDocumentDock
        && factory.Root is { } root && factory.FindDockable(root, d => ReferenceEquals(d, dock)) is not null;

    /// <summary>분할 해제 뒤(로그·시험 관측용). 인자는 옮긴 문서 수와 정리한 분할 칸 수.</summary>
    public static event Action<int, int>? Unsplitted;

    /// <summary>트리 안의 분할 칸(중앙 문서 영역 제외 MDI 문서 칸) 목록. 트리 순서다.</summary>
    internal static IReadOnlyList<IDocumentDock> SplitAreas(PocDockFactory factory)
    {
        var found = new List<IDocumentDock>();
        if (factory.Root is { } root)
        {
            Walk(root, factory, found);
        }

        return found;
    }

    private static void Walk(IDockable node, PocDockFactory factory, List<IDocumentDock> found)
    {
        if (node is IDocumentDock dock)
        {
            if (!ReferenceEquals(dock, factory.Documents) && IsDocumentArea(factory, dock))
            {
                found.Add(dock);
            }

            return; // 문서 칸 안에 칸은 없다
        }

        if (node is IDock { VisibleDockables: { } children })
        {
            foreach (var child in children)
            {
                Walk(child, factory, found);
            }
        }
    }

    /// <summary>
    /// 모든 분할 칸의 문서를 주 문서 영역(Documents)으로 옮기고 빈 분할 칸을 정리한다. 옮긴 문서 수를 돌려준다.
    /// 주 문서 영역이 없거나 탭 모드(프리셋)면 Dock 기본 이동만 한다(MDI 위치는 탭 모드에서 쓰이지 않는다).
    /// </summary>
    internal static int Unsplit(PocDockFactory factory)
    {
        if (factory.Documents is not IDock center)
        {
            return 0;
        }

        var areas = SplitAreas(factory);
        var moved = 0;
        IDockable? last = null;
        foreach (var area in areas)
        {
            foreach (var doc in area.VisibleDockables?.ToList() ?? new List<IDockable>())
            {
                var bounds = (doc as IMdiDocument)?.MdiBounds;
                factory.MoveDockable(area, center, doc, null);
                if (!ReferenceEquals(doc.Owner, center))
                {
                    continue;
                }

                if (center is IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } && doc is IMdiDocument mdi && bounds is { } b)
                {
                    mdi.MdiBounds = b; // 분할 칸 안 자리를 주 영역에서도 그대로 쓴다
                }

                moved++;
                last = doc;
            }

            Tidy(factory, area);
        }

        if (last is not null)
        {
            factory.SetActiveDockable(last);
        }

        var tidied = areas.Count(a => factory.Root is { } root && factory.FindDockable(root, d => ReferenceEquals(d, a)) is null);
        factory.RaiseSplitChanged();
        Unsplitted?.Invoke(moved, tidied);
        return moved;
    }

    /// <summary>
    /// MDI 창 제목줄 끌기를 놓은 뒤(PanelJoinDrag.Commit — 패널 합류가 아닐 때) 부른다. 마지막 포인터가 문서의 칸이 아닌 다른 MDI 문서 칸 위면
    /// 그 칸으로 합류시키고(커서 자리·기본 크기) 빈 분할 칸을 정리한다. 합류하면 true. Dock 의 놓기 처리가 끝난 뒤 트리를 바꾸도록 Post 한다.
    /// </summary>
    internal static bool TryMergeAt(PocDockFactory factory, PocDocument doc)
    {
        if (doc.Owner is not IDock owner || !CenterDropToMdi.IsMdiArea(factory, owner) || CenterDropToMdi.AreaAtPointer(factory) is not { } hit
            || ReferenceEquals(hit.Dock, owner))
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(doc.Owner, owner))
            {
                return; // 그 사이 다른 경로가 옮겼다
            }

            factory.MoveDockable(owner, hit.Dock, doc, null);
            if (!ReferenceEquals(doc.Owner, hit.Dock))
            {
                return;
            }

            factory.SetActiveDockable(doc);
            CenterDropToMdi.AfterLeave(factory, owner); // 빈 분할 칸이면 정리한다 — 주 영역이 커지므로 배치는 레이아웃 뒤에 한다
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(doc.Owner, hit.Dock) && hit.Dock.LayoutMode == DocumentLayoutMode.Mdi && (CenterDropToMdi.ViewOf(hit.Dock) ?? hit.View) is { } view)
                {
                    CenterDropToMdi.Place(doc, view); // 정리 뒤 칸 원점 기준 커서 자리(c2dev_d 실측: 정리 전 원점이면 커서보다 위로 감)
                }
            }, DispatcherPriority.Loaded);
        });
        return true;
    }

    /// <summary>분할 칸이 비었고 아직 트리에 있으면 정리한다(몫은 이웃 문서 칸으로 — DocumentSplitCollapse).</summary>
    private static void Tidy(PocDockFactory factory, IDock area)
    {
        if (area.VisibleDockables is { Count: 0 } && factory.Root is { } root && factory.FindDockable(root, d => ReferenceEquals(d, area)) is not null)
        {
            factory.CollapseDock(area);
        }
    }
}

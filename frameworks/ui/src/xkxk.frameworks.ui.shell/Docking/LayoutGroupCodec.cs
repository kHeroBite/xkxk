// MDI 탭 묶음 ↔ 레이아웃 스냅샷 Groups(Version 5) 변환 — 저장 때 묶음 기록, 복원 때 문서 배치 뒤 묶음 재구성
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// LayoutSerializer.Capture·LayoutRestorer 가 부르는 묶음 전용 코덱. 문서 자체(DocumentSnapshot)는 묶음 안 것도 전부 Documents 에 남고,
/// 여기서는 묶음 구성(Id·위치·크기·Z·상태·활성 탭·탭 순서)만 따로 쓴다 — 옛 판 앱은 Groups 를 모르고 문서별 MDI 창으로 연다(유실 없음).
/// </summary>
public static class LayoutGroupCodec
{
    /// <summary>
    /// 현재 MDI 묶음을 snapshot.Groups 에 쓴다(없으면 null). 묶음 안 문서 항목의 위치·크기·상태·Z 는 묶음 창 값으로 맞춘다
    /// — 옛 판 앱이 읽으면 그 문서들이 묶음이 있던 자리에 겹쳐 열린다.
    /// </summary>
    public static void Capture(IDocumentDock documents, LayoutSnapshot snapshot)
    {
        var groups = MdiTabGroup.Groups(documents).Where(g => g.Documents.Count > 0).ToList();
        if (groups.Count == 0)
        {
            snapshot.Groups = null;
            return;
        }

        var items = snapshot.Documents.GroupBy(d => d.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        snapshot.Groups = new List<MdiGroupSnapshot>();
        foreach (var g in groups)
        {
            var b = g.MdiBounds;
            var group = new MdiGroupSnapshot
            {
                Id = g.Id,
                X = b.X,
                Y = b.Y,
                Width = b.Width,
                Height = b.Height,
                State = g.MdiState,
                ZIndex = g.MdiZIndex,
                ActiveId = (g.ActiveDockable as PocDocument)?.Id,
                DocIds = g.Documents.Select(d => d.Id).ToList(),
            };
            foreach (var id in group.DocIds)
            {
                if (items.TryGetValue(id, out var item))
                {
                    (item.X, item.Y, item.Width, item.Height) = (b.X, b.Y, b.Width, b.Height);
                    item.MdiState = g.MdiState;
                    item.ZIndex = g.MdiZIndex;
                }
            }

            snapshot.Groups.Add(group);
        }
    }

    /// <summary>
    /// 문서 배치(추가·패널 보내기) 뒤, Z 순서 확정 전에 부른다. MDI dock 바로 아래 문서만 묶는다(분리 창·측면 패널 문서는 건너뜀).
    /// 남은 문서가 2개 미만인 묶음은 만들지 않는다. Groups 가 null(옛 저장본 Version ≤4)이거나 MDI 모드가 아니면 아무것도 안 한다. 만든 묶음 수를 돌려준다.
    /// Z 는 묶음 창 값으로 넣어 두고, 최종 순서는 호출자의 SetMdiZOrder(묶음 인지 — 구성 문서 중 가장 위 순위)가 확정한다.
    /// </summary>
    public static int Restore(IFactory factory, IDocumentDock mdi, LayoutSnapshot snapshot, IEnumerable<PocDocument> restored)
    {
        if (snapshot.Groups is not { Count: > 0 } || mdi.LayoutMode != DocumentLayoutMode.Mdi)
        {
            return 0;
        }

        var byId = new Dictionary<string, PocDocument>(StringComparer.Ordinal);
        foreach (var doc in restored)
        {
            byId.TryAdd(doc.Id, doc);
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var made = 0;
        foreach (var g in snapshot.Groups)
        {
            var members = g.DocIds
                .Where(id => used.Add(id)) // 한 문서는 한 묶음에만(손상 저장본 방어)
                .Select(id => byId.GetValueOrDefault(id))
                .Where(d => d is not null && ReferenceEquals(d.Owner, mdi))
                .Cast<PocDocument>()
                .ToList();
            if (members.Count < 2)
            {
                continue;
            }

            var group = MdiTabGroup.CreateGroup(factory, mdi, members[0], g.Id);
            foreach (var doc in members.Skip(1))
            {
                MdiTabGroup.Join(factory, mdi, group, doc);
            }

            if (g.Width > 0 && g.Height > 0)
            {
                group.MdiBounds = new DockRect(g.X, g.Y, g.Width, g.Height);
            }

            group.MdiState = g.State;
            group.MdiZIndex = g.ZIndex;
            group.ActiveDockable = members.FirstOrDefault(d => d.Id == g.ActiveId) ?? members[0];
            made++;
        }

        return made;
    }
}

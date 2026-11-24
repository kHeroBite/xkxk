// 원격 명령(D-DAD1 판정) — dock.asDocument: ▾ '문서로 도킹'과 같은 PocDockFactory.DockAsDocument 를 직접 불러 옮겨진 자리를 돌려준다(측면 패널 ▾ 는 결정 (나)로 숨김)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Text.Json.Nodes;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemoteDockAsDocumentCommands
{
    // 공용 멤버 전달자(W2 F) — 본문은 옛 이름 그대로 쓴다
    private static PocDockFactory Factory => RemoteDockAccess.Factory;

    private static IDockable FindDock(string id) => RemoteDockAccess.FindDock(id);

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("dock.asDocument",
            "▾ '문서로 도킹' 재현 id=<도구·문서 Id 또는 측면 탭 영역 Id(LeftPane.Tabs 등)> → {status: Moved|Unchanged|Rejected(바깥 패널·컨테이너, reason), id, before, after, toCenter, centerId}"
            + " — PocDockFactory.DockAsDocument 그대로(알림·불가 항목은 Unchanged)", a =>
            {
                var target = FindDock(a.ReqStr("id"));
                if (target is IDock and not PanelDocumentDock)
                {
                    // 바깥 패널·분할 컨테이너는 받지 않는다(D-DAD2). 팩토리도 막지만 시험이 그 방어에 기대지 않게 여기서 먼저 거부한다
                    return new JsonObject { ["status"] = "Rejected", ["id"] = target.Id, ["reason"] = "container-not-allowed" };
                }

                var item = target is PanelDocumentDock tabs ? tabs.ActiveDockable : target; // 탭 영역이 오면 활성 탭(재정의와 같은 규칙)
                var before = item?.Owner?.Id;
                Factory.DockAsDocument(target);
                var after = item?.Owner?.Id;
                return new JsonObject
                {
                    ["status"] = before != after ? "Moved" : "Unchanged",
                    ["id"] = item?.Id,
                    ["before"] = before,
                    ["after"] = after,
                    ["toCenter"] = after is not null && after == Factory.Documents?.Id,
                    ["centerId"] = Factory.Documents?.Id,
                };
            });
    }
}
#endif

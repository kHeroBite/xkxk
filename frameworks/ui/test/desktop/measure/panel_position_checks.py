#!/usr/bin/env python3
# 패널 '위치' 메뉴 검사 항목(데스크톱·웹 공용) — c(cmd, **kw) 하나로 원격 명령을 불러 값을 모은다. 구동기는 retest_panel_position_menu.py·retest_panel_position_menu_web.py
import json
import time

SETTLE_S = 0.6
NP, LP, BP = "NotificationPane", "LeftPane", "BottomPane"
EDGES = ["Top", "Bottom", "Left", "Right"]
EDGE_NAMES = {"Top": "위", "Bottom": "아래", "Left": "왼쪽", "Right": "오른쪽"}
SAVE_NAME = "panel-menu-retest"


class Checks:
    """c(cmd, **kw) 만 있으면 데스크톱·웹 어디서나 돈다."""

    def __init__(self, c, on_step=None):
        self.c, self.items, self.on_step = c, [], on_step

    def check(self, name, passed, detail=None):
        self.items.append({"name": name, "pass": bool(passed), "detail": detail})
        print(("PASS " if passed else "FAIL ") + name + ("  " + json.dumps(detail, ensure_ascii=False)[:240] if detail is not None else ""), flush=True)

    def menu(self, **kw):
        return self.c("panelmenu.read", **kw) or {}

    def click(self, target, **kw):
        res = self.c("panelmenu.click", target=target, **kw) or {}
        time.sleep(SETTLE_S)
        return res

    def tabs(self, pane):
        return [t["id"] for t in (self.c("panel.tabs", id=pane) or {}).get("tabs", [])]

    def panels(self):
        return {p["id"]: p for p in self.c("panel.list") or []}

    def open_doc(self):
        before = {d["id"] for d in self.c("docs.list") or []}
        self.c("docs.open", kind="grid", allowDuplicate=True)
        time.sleep(SETTLE_S)
        return [d["id"] for d in self.c("docs.list") or [] if d["id"] not in before][0]


def rows(menu):
    return {i["target"]: i for i in menu.get("items", [])}


def tree_ids(node, out):
    if isinstance(node, dict):
        if node.get("id"):
            out.append((str(node["id"]), node.get("visible", True)))
        for v in node.values():
            if isinstance(v, (list, dict)):
                tree_ids(v, out)
    elif isinstance(node, list):
        for v in node:
            tree_ids(v, out)
    return out


def title_and_combo(k):
    k.c("lang.set", code="ko")
    time.sleep(SETTLE_S)
    k.check("title.5", k.menu(pane=NP).get("title") == "알림(5건)", k.menu(pane=NP).get("title"))
    k.check("title.panelList", k.panels()[NP]["title"] == "알림(5건)", k.panels()[NP]["title"])
    for n, want in ((3, "알림(3건)"), (0, "알림"), (1, "알림(1건)"), (5, "알림(5건)")):
        k.c("panelmenu.notifyCount", n=n)
        time.sleep(SETTLE_S)
        got = k.menu(pane=NP).get("title")
        k.check("title.count%d.immediate" % n, got == want, got)
    k.c("lang.set", code="en")
    time.sleep(SETTLE_S)
    en = k.menu(pane=NP)
    k.check("title.en", en.get("title") == "Notifications (5)", en.get("title"))
    k.check("menu.en", en.get("header") == "Panel position" and rows(en)["Edge.Top"]["header"] == "Top", [en.get("header"), rows(en).get("Edge.Top")])
    k.c("lang.set", code="ko")
    time.sleep(SETTLE_S)
    ids = dict(tree_ids(k.c("tree.get", named=True, depth=60), []))
    gone = [i for i in ("notify.menu", "notify.mnuPos", "notify.mnuPosTop") if i in ids]
    k.check("combo.removed", not gone, gone)
    shown = [i for i in ("notify.title", "notify.count") if ids.get(i)]
    k.check("countRow.hiddenInPanel", not shown, {"visible": shown, "root": ids.get("notify.root")})


def panel_menus(k):
    homes = {LP: "Left", BP: "Bottom", NP: "Right"}
    for pane, current in homes.items():
        others = [p for p in homes if p != pane]
        m = k.menu(pane=pane)
        r = rows(m)
        ok = m.get("visible") and m.get("header") == "패널 위치" and set(r) == {pane, *others} | {"Edge." + e for e in EDGES}
        ok = ok and r[pane]["checked"] and not r[pane]["enabled"] and all(r[o]["enabled"] and not r[o]["checked"] for o in others)
        ok = ok and r["Edge." + current]["checked"] and not r["Edge." + current]["enabled"] and all(r["Edge." + e]["enabled"] for e in EDGES if e != current)
        k.check("menu.panel." + pane, ok, m.get("items"))
    k.check("menu.label.left", rows(k.menu(pane=BP))[LP]["header"] == "왼쪽 패널: 탐색기", rows(k.menu(pane=BP))[LP]["header"])
    k.check("menu.label.notify", rows(k.menu(pane=LP))[NP]["header"] == "오른쪽 패널: 알림(5건)", rows(k.menu(pane=LP))[NP]["header"])
    k.check("menu.desktop.noButton", not k.menu(pane=NP).get("menuButton") and not k.menu(pane=LP).get("menuButton"), k.menu(pane=NP).get("menuButton"))


def edge_moves(k):
    """세 패널 모두 네 가장자리로 간다. 돌아올 때 원래 가장자리로 끝낸다."""
    for pane, home, watch in ((NP, "Right", LP), (LP, "Left", NP), (BP, "Bottom", NP)):
        for edge in [e for e in ("Top", "Left", "Bottom", "Right") if e != home] + [home]:
            res = k.click("Edge." + edge, pane=pane)
            side = k.panels()[pane]["side"]
            r = rows(k.menu(pane=pane))
            ok = res.get("clicked") and side == edge and r["Edge." + edge]["checked"] and not r["Edge." + edge]["enabled"]
            ok = ok and all(r["Edge." + e]["enabled"] for e in EDGES if e != edge)
            bounds = k.c("panel.bounds") or {}
            n, d = bounds.get(pane) or {}, bounds.get("Documents") or {}
            geo = {"Top": n.get("y", 0) < d.get("y", 0), "Bottom": n.get("y", 0) > d.get("y", 0), "Left": n.get("x", 0) < d.get("x", 0), "Right": n.get("x", 0) > d.get("x", 0)}[edge]
            k.check("edge.move.%s.%s" % (pane, edge), ok and geo, {"side": side, "panel": n, "docs": d})
            again = k.click("Edge." + edge, pane=pane)
            k.check("edge.currentRejected.%s.%s" % (pane, edge), again.get("reason") == "disabled" and k.panels()[pane]["side"] == edge, again)
            label = rows(k.menu(pane=watch))[pane]["header"]
            k.check("edge.label.follows.%s.%s" % (pane, edge), label.startswith(EDGE_NAMES[edge] + " 패널"), label)


def same_edge(k):
    """같은 가장자리에 패널 둘: 겹치지 않고 옆 줄에 나란히 놓인다. 끌면 탭으로 합칠 수도 있다(메뉴의 패널 항목)."""
    k.click("Edge.Left", pane=NP)
    b = k.c("panel.bounds") or {}
    n, l = b.get(NP) or {}, b.get(LP) or {}
    k.check("sameEdge.sideBySide", n and l and n.get("x", 0) + n.get("w", 0) <= l.get("x", 0) + 1 and k.panels()[NP]["side"] == k.panels()[LP]["side"] == "Left", {"notify": n, "left": l})
    k.click("Edge.Right", pane=NP)


def moved_behaviour(k):
    """옮긴 위치에서 핀·닫기와 복구·집중 숨김·모서리 버튼·근접 표시 구독이 같게 동작한다. 탐색기 오른쪽(알림과 같은 가장자리), 로그 위."""
    k.click("Edge.Right", pane=LP)
    k.click("Edge.Top", pane=BP)
    for pane in (LP, BP):
        before = (k.c("panel.bounds") or {}).get(pane) or {}
        k.c("panel.pin", id=pane, on=True)
        time.sleep(SETTLE_S)
        pinned = k.panels()[pane]["pinned"]
        k.c("panel.pin", id=pane, on=False)
        time.sleep(SETTLE_S)
        after = (k.c("panel.bounds") or {}).get(pane) or {}
        same = all(abs(before.get(a, 0) - after.get(a, 0)) <= 2 for a in ("x", "y", "w", "h"))
        k.check("moved.pin." + pane, pinned and same and not k.panels()[pane]["pinned"], {"before": before, "after": after})
    k.c("panel.close", id=LP)
    time.sleep(SETTLE_S)
    closed = not k.panels()[LP]["visible"]
    k.c("panel.show", tool="Explorer")
    time.sleep(SETTLE_S)
    p = k.panels()
    k.check("moved.closeReveal", closed and p[LP]["visible"] and p[LP]["side"] == "Right", {"closed": closed, "side": p[LP]["side"]})
    k.c("layout.template.apply", id="tpl.focus")
    time.sleep(SETTLE_S)
    p = k.panels()
    k.check("moved.focus.hides", not p[LP]["visible"] and not p[BP]["visible"] and not p[NP]["visible"], {i: p[i]["visible"] for i in (LP, BP, NP)})
    k.c("layout.template.apply", id="tpl.mdi-cascade")
    time.sleep(SETTLE_S)
    p = k.panels()
    k.check("moved.focus.restored", p[LP]["visible"] and p[BP]["visible"] and p[NP]["visible"] and p[LP]["side"] == "Right" and p[BP]["side"] == "Top" and p[NP]["side"] == "Right", {i: (p[i]["visible"], p[i]["side"]) for i in (LP, BP, NP)})
    state = k.c("panel.corner.state") or {}
    buttons = state.get("buttons") or []
    subscribed = [w.get("subscribed") for w in (state.get("proximity") or {}).get("windows", [])]
    k.check("moved.corner.buttons", bool(buttons) and any(subscribed), {"buttons": [b.get("id") for b in buttons], "subscribed": subscribed})
    if buttons:
        b = buttons[0]
        res = k.c("panel.corner.click", panel=b["id"].split(".")[2], corner=b["corner"]) or {}
        time.sleep(SETTLE_S)
        owners = (k.c("panel.corner.state") or {}).get("owners")
        k.check("moved.corner.claim", res.get("status") == "Done" and bool(owners), {"res": res.get("status"), "owners": owners})
        k.c("panel.corner.release", corner=b["corner"])
        time.sleep(SETTLE_S)
    k.click("Edge.Left", pane=LP)
    k.click("Edge.Bottom", pane=BP)
    time.sleep(SETTLE_S)


def split_pane_to_edge(k):
    """분할 패널도 네 가장자리로 간다. 빈 자리(여분 패널)를 쓰고, 원래 영역의 분할은 풀린다."""
    doc = k.open_doc()
    res = k.c("split.do", target=LP, op="Bottom", doc=doc) or {}
    time.sleep(SETTLE_S)
    split = next((t for t in rows(k.menu(pane=BP)) if t.startswith(LP + ".")), None)
    k.check("splitEdge.created", res.get("status") == "Split" and bool(split), res.get("status"))
    if split:
        m = k.click("Edge.Top", pane=split)
        panes = k.panels()
        k.check("splitEdge.toTop", m.get("clicked") and "ExtraPane" in panes and panes["ExtraPane"]["side"] == "Top" and doc in k.tabs("ExtraPane") and split not in panes, panes.get("ExtraPane"))
        k.click(BP, pane="ExtraPane")
        k.check("splitEdge.mergeBack", doc in k.tabs(BP) and "ExtraPane" not in k.panels(), k.tabs(BP))
    k.click("Documents", tab=doc)
    time.sleep(SETTLE_S)


def notify_as_tab(k):
    """알림도 다른 도구처럼 탭으로 옮기고 다시 나온다. 패널이 없어져도 보기 메뉴 경로(RevealTool)로 돌아온다."""
    k.click(LP, pane=NP)
    p = k.panels()
    k.check("notifyTab.merged", "Notifications" in k.tabs(LP) and not p[NP]["visible"], {"left": k.tabs(LP), "visible": p[NP]["visible"]})
    m = k.menu(tab="Notifications")
    k.check("notifyTab.menu", m.get("header") == "이 탭 옮기기" and "Edge.Top" in rows(m), m.get("items"))
    shown = k.c("panel.show", tool="Notifications") or {}
    k.check("notifyTab.reveal", shown.get("shown") and ("Notifications" in k.tabs(NP) or "Notifications" in k.tabs(LP)), {"shown": shown, "left": k.tabs(LP), "home": k.tabs(NP)})
    if "Notifications" not in k.tabs(NP):
        k.click(NP, tab="Notifications")
    k.check("notifyTab.back", k.panels()[NP]["visible"] and "Notifications" in k.tabs(NP), k.tabs(NP))


def tab_moves(k):
    before = (k.c("panel.bounds") or {}).get(LP)
    doc = k.open_doc()
    k.c("panel.join", id=LP, doc=doc)
    time.sleep(SETTLE_S)
    m = k.menu(tab="Explorer")
    k.check("menu.tab.tool", m.get("visible") and m.get("header") == "이 탭 옮기기" and set(rows(m)) == {LP, BP, NP} | {"Edge." + e for e in EDGES} and rows(m)[LP]["checked"] and not rows(m)[LP]["enabled"], m.get("items"))
    m = k.menu(tab=doc)
    k.check("menu.tab.doc", m.get("visible") and "Documents" in rows(m) and rows(m)["Documents"]["enabled"], m.get("items"))
    k.check("menu.tab.keepsOthers", m.get("others", 0) > 0, m.get("others"))

    res = k.click(BP, tab=doc)
    k.check("tab.doc.toBottom", res.get("clicked") and doc in k.tabs(BP) and doc not in k.tabs(LP), {"bottom": k.tabs(BP), "left": k.tabs(LP)})
    k.check("tab.doc.activeAfterMove", (k.c("panel.tabs", id=BP) or {}).get("active") == doc)

    k.click(NP, tab=doc)
    k.check("tab.doc.toNotify", doc in k.tabs(NP), k.tabs(NP))
    m = k.menu(tab="Notifications")
    k.check("menu.tab.notify.sameAsOthers", m.get("header") == "이 탭 옮기기" and set(rows(m)) >= {"Edge." + e for e in EDGES} | {LP, BP}, m.get("items"))
    k.check("menu.tab.docInNotify.current", rows(k.menu(tab=doc))[NP]["checked"], rows(k.menu(tab=doc)).get(NP))
    k.click("Edge.Left", pane=NP)
    k.check("edge.withJoinedTab", k.panels()[NP]["side"] == "Left" and doc in k.tabs(NP), {"side": k.panels()[NP]["side"], "tabs": k.tabs(NP)})
    k.click("Edge.Right", pane=NP)
    k.click("Documents", tab=doc)
    k.check("tab.doc.toDocuments", doc not in k.tabs(NP) and doc not in k.tabs(LP) and doc not in k.tabs(BP), k.tabs(NP))

    k.c("panel.join", id=LP, doc=doc)
    time.sleep(SETTLE_S)
    k.click(BP, tab="Explorer")
    k.check("tab.tool.toBottom", "Explorer" in k.tabs(BP) and "Explorer" not in k.tabs(LP), {"bottom": k.tabs(BP), "left": k.tabs(LP)})
    k.click(LP, tab="Explorer")
    k.check("tab.tool.back", "Explorer" in k.tabs(LP), k.tabs(LP))
    k.click("Documents", tab=doc)

    # 닫힌 패널·핀 패널을 대상으로 고른 경우
    k.c("panel.join", id=LP, doc=doc)
    time.sleep(SETTLE_S)
    k.c("panel.close", id=BP)
    time.sleep(SETTLE_S)
    k.check("target.closed.precondition", not k.panels()[BP]["visible"], k.panels()[BP]["visible"])
    k.click(BP, tab=doc)
    k.check("target.closed.reopened", k.panels()[BP]["visible"] and doc in k.tabs(BP), {"visible": k.panels()[BP]["visible"], "tabs": k.tabs(BP)})
    k.click(LP, tab=doc)
    k.c("panel.pin", id=BP, on=True)
    time.sleep(SETTLE_S)
    k.click(BP, tab=doc)
    p = k.panels()[BP]
    k.check("target.pinned.keepsPin", p["pinned"] and doc in k.tabs(BP), {"pinned": p["pinned"], "tabs": k.tabs(BP)})
    k.c("panel.pin", id=BP, on=False)
    time.sleep(SETTLE_S)
    k.click("Documents", tab=doc)

    # 패널 전체: 빈 패널은 접히고, 접힌 패널로도 돌아올 수 있다
    k.click(BP, pane=LP)
    p = k.panels()
    k.check("panel.merge.toBottom", set(k.tabs(BP)) >= {"Explorer", "Log"} and not p[LP]["visible"], {"bottom": k.tabs(BP), "leftVisible": p[LP]["visible"]})
    r = rows(k.menu(pane=BP))
    k.check("panel.collapsedStillTarget", r.get(LP, {}).get("enabled") is True, r.get(LP))
    k.click(LP, tab="Explorer")
    p = k.panels()
    after = (k.c("panel.bounds") or {}).get(LP)
    k.check("panel.restoredSize", p[LP]["visible"] and before and after and abs(before["w"] - after["w"]) <= 2, {"before": before, "after": after})
    return doc


def split_and_focus(k, doc):
    doc2 = k.open_doc()
    res = k.c("split.do", target=LP, op="Bottom", doc=doc2) or {}
    time.sleep(SETTLE_S)
    r = rows(k.menu(pane=BP))
    split = [t for t in r if t.startswith(LP + ".")]
    k.check("split.listed", res.get("status") == "Split" and bool(split) and r[split[0]]["header"].startswith("왼쪽 패널"), r)
    if split:
        k.click(BP, pane=split[0])
        k.check("split.mergedAway", doc2 in k.tabs(BP) and split[0] not in rows(k.menu(pane=BP)), rows(k.menu(pane=BP)))
    k.click("Documents", tab=doc2)
    time.sleep(SETTLE_S)

    k.c("panel.join", id=NP, doc=doc)
    time.sleep(SETTLE_S)
    k.c("layout.template.apply", id="tpl.focus")
    time.sleep(SETTLE_S)
    hidden = k.menu(tab=doc)
    k.check("focus.panelTabHidden", not hidden.get("visible"), {"found": hidden.get("found"), "visible": hidden.get("visible")})
    k.c("layout.template.apply", id="tpl.mdi-cascade")
    time.sleep(SETTLE_S)
    k.check("focus.panelTabKept", doc in k.tabs(NP), k.tabs(NP))
    k.c("layout.template.apply", id="tpl.tabbed")  # 문서 영역 탭은 탭 배치에서만 찾을 수 있다
    time.sleep(SETTLE_S)
    k.click("Documents", tab=doc)
    time.sleep(SETTLE_S)
    center = k.menu(tab=doc)
    k.check("tab.center.noMenu", center.get("found") is True and center.get("visible") is False, {"found": center.get("found"), "visible": center.get("visible")})
    k.c("layout.template.apply", id="tpl.mdi-cascade")
    time.sleep(SETTLE_S)


def corner(k):
    state = k.c("panel.corner.state") or {}
    buttons = state.get("buttons") or []
    if not buttons:
        k.check("corner.buttonAvailable", False, state)
        return
    b = buttons[0]
    k.c("panel.corner.click", panel=b["id"].split(".")[2], corner=b["corner"])  # id=panel.corner.<패널>.<모서리>
    time.sleep(SETTLE_S)
    owned = (k.c("panel.corner.state") or {}).get("owners")
    k.click("Edge.Left", pane=NP)
    reset = k.c("panel.corner.state") or {}
    k.check("corner.resetOnEdgeMove", k.panels()[NP]["side"] == "Left" and reset.get("owners") != owned, {"owned": owned, "after": reset.get("owners")})
    k.click("Edge.Right", pane=NP)


def edge_with_doc_split(k):
    """모서리 변경 + 문서 분할에서도 가장자리를 옮긴다. 문서 칸이 패널 단과 분리돼 트리가 깨지지 않고 문서 분할은 그대로다."""
    a, b = k.open_doc(), k.open_doc()
    k.c("layout.template.apply", id="tpl.tabbed")  # MDI 메뉴는 열 때 붙어 팝업 없이 못 부른다. 탭 문서 모드 탭 메뉴로 분할한다
    time.sleep(SETTLE_S)
    button = ((k.c("panel.corner.state") or {}).get("buttons") or [{}])[0]
    k.c("panel.corner.click", panel=button.get("id", "...").split(".")[2], corner=button.get("corner"))
    time.sleep(SETTLE_S)
    res = k.c("panelmenu.docSplit", doc=b, on=True) or {}
    time.sleep(SETTLE_S)
    r = rows(k.menu(pane=NP))
    owners = (k.c("panel.corner.state") or {}).get("owners")
    ready = res.get("done") is True and bool(owners)
    k.check("edge.docSplit.precondition", ready, {"split": res, "owners": owners})
    if ready:
        k.check("edge.docSplit.enabled", r["Edge.Top"]["enabled"] and r["Edge.Right"]["checked"], r)
        press = k.click("Edge.Top", pane=NP)
        k.check("edge.docSplit.moved", press.get("clicked") and k.panels()[NP]["side"] == "Top", press)
        k.click("Edge.Right", pane=NP)
        back = k.c("panelmenu.docSplit", doc=b, on=False) or {}
        time.sleep(SETTLE_S)
        k.check("edge.docSplit.unsplit", back.get("done") is True and k.panels()[NP]["side"] == "Right", back)
    k.c("panel.corner.release", corner=button.get("corner"))
    k.c("layout.template.apply", id="tpl.mdi-cascade")
    time.sleep(SETTLE_S)
    for doc in (a, b):
        k.c("docs.close", doc=doc)
    time.sleep(SETTLE_S)


def saved_layout(k):
    """저장·복원(Version 4): 알림 왼쪽, 로그 위, 탐색기를 로그 패널로(왼쪽 접힘)."""
    k.click("Edge.Left", pane=NP)
    k.click("Edge.Top", pane=BP)
    k.click(BP, pane=LP)
    want = {"side": k.panels()[NP]["side"], "log": k.panels()[BP]["side"], "bottom": k.tabs(BP)}
    saved = k.c("layout.save", name=SAVE_NAME) or {}
    k.click("Edge.Right", pane=NP)
    k.click("Edge.Bottom", pane=BP)
    k.click(LP, tab="Explorer")
    k.c("layout.load", name=SAVE_NAME)
    time.sleep(SETTLE_S * 3)
    p = k.panels()
    got = {"side": p[NP]["side"], "log": p[BP]["side"], "bottom": k.tabs(BP)}
    k.check("save.named.restored", saved and got == want and not p[LP]["visible"], {"saved": saved, "want": want, "got": got, "leftVisible": p[LP]["visible"]})


def after_restart(k):
    p = k.panels()
    k.check("restart.edge", p[NP]["side"] == "Left", p[NP]["side"])
    k.check("restart.logEdge", p[BP]["side"] == "Top", p[BP]["side"])
    k.check("restart.tabs", "Explorer" in k.tabs(BP) and "Log" in k.tabs(BP), k.tabs(BP))
    k.check("restart.leftCollapsed", not p[LP]["visible"], p[LP]["visible"])
    k.check("restart.title", p[NP]["title"] == "알림(5건)", p[NP]["title"])
    r = rows(k.menu(pane=NP))
    k.check("restart.menuCurrent", r["Edge.Left"]["checked"] and not r["Edge.Left"]["enabled"], r.get("Edge.Left"))


def run_all(k):
    title_and_combo(k)
    panel_menus(k)
    edge_moves(k)
    same_edge(k)
    moved_behaviour(k)
    doc = tab_moves(k)
    split_and_focus(k, doc)
    split_pane_to_edge(k)
    notify_as_tab(k)
    corner(k)
    edge_with_doc_split(k)
    saved_layout(k)

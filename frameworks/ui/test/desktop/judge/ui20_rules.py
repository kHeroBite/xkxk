# UI-20 패널 영역 분할 판정 규칙(Checker a~f) — ui20_check.py 에서 줄 수 원칙으로 분리한 모듈, 동작은 그대로다
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from ui20_lib import (  # noqa: E402,F401  (같은 폴더의 공용 모듈 — 원격 전송·판정 도우미)
    CROSS_OP, HOME, SPLIT_OP, TOOL_HOME, Remote, Undecidable, WebRemote, area, near, size_of, structural,
)


class Checker:
    def __init__(self, remote, areas):
        self.r = remote
        self.areas = areas
        self.items = {k: {"pass": None, "detail": []} for k in "abcdef"}
        self.opened = []  # 이 판정이 연 문서 id — 끝날 때 닫아 다음 측정(docs_at_open)을 오염시키지 않는다

    def cleanup(self):
        """시험 중 연 문서를 닫는다(실패는 무시 — 이미 닫혔거나 연결이 끊긴 경우)."""
        for doc in self.opened:
            try:
                self.r.call("docs.close", doc=doc)
            except Exception:  # noqa: BLE001
                pass
        self.opened = []

    def mark(self, key, ok, msg):
        it = self.items[key]
        it["detail"].append(("PASS " if ok else "FAIL ") + msg)
        it["pass"] = ok if it["pass"] is None else (it["pass"] and ok)

    def dump(self):
        return self.r.result("split.dump")

    def open_docs(self, n):
        before = {d["id"] for d in self.r.result("docs.list")}
        self.r.result("docs.open", kind="form", count=n, allowDuplicate="true")
        ids = [d["id"] for d in self.r.result("docs.list") if d["id"] not in before]
        if len(ids) < n:
            raise Undecidable("문서 %d개를 열지 못함(%d개)" % (n, len(ids)))
        self.opened.extend(ids)
        return ids

    def split(self, target, op, item):
        # 도구(탐색기·로그)는 tool=, 문서는 doc= 로 보낸다
        key = "tool" if item in TOOL_HOME else "doc"
        return self.r.result("split.do", target=target, op=op, **{key: item})

    def out(self, item):
        """패널 탭에서 빼낸다: 문서는 문서 영역으로, 도구는 원래 패널 탭으로."""
        if item in TOOL_HOME:
            self.r.call("panel.join", id=TOOL_HOME[item], tool=item)
        else:
            self.r.call("panel.returnDoc", doc=item)

    def run(self):
        d0 = self.dump()
        if d0.get("compact"):
            raise Undecidable("폰 모드(바형) — UI-20 대상 아님(기존 동작 유지 항목)")
        if d0.get("sidePanelsHidden"):
            raise Undecidable("집중 모드(측면 패널 숨김) 상태 — 해제 후 다시 실행")
        docs = self.open_docs(4 * len(self.areas) + 1)
        layout_name = "ui20_check_%d" % int(time.time())
        made = {}
        split_op, cross_op = dict(SPLIT_OP), dict(CROSS_OP)
        if d0.get("notificationEdge") in ("Top", "Bottom"):  # 알림이 위·아래 가장자리면 알림 영역은 가로로 쌓인다
            split_op["Right"], cross_op["Right"] = "Right", "Top"
        for name in self.areas:
            d1, d2, d3, d4 = docs[:4]
            docs = docs[4:]
            home = HOME[name]
            op = split_op[name]
            # b 가로지르는 방향 거부
            first = d1
            cross = self.split(home, cross_op[name], first)
            self.mark("b", cross["status"] == "Rejected" and area(cross["dump"], name)["paneCount"] == 1,
                      "%s 가로지르는 방향(%s) 거부 status=%s" % (name, cross_op[name], cross["status"]))
            # b 1/2 분할
            res = self.split(home, op, first)
            a = area(res["dump"], name)
            props = [p["proportion"] for p in a["panes"]]
            sizes = [size_of(p["bounds"], a["vertical"]) for p in a["panes"]]
            half = len(props) == 2 and all(near(p, 0.5, 0.03) for p in props)
            ratio = sizes[0] / (sizes[0] + sizes[1]) if len(sizes) == 2 and None not in sizes and sum(sizes) > 0 else None
            self.mark("b", res["status"] == "Split" and a["wrapped"] and half and near(ratio, 0.5, 0.1),
                      "%s 분할 status=%s panes=%d props=%s sizes=%s" % (name, res["status"], a["paneCount"], props, sizes))
            new_pane = next((p for p in a["panes"] if not p["isHome"]), None)
            if new_pane is None:
                self.mark("c", False, "%s 새 분할 패널 없음 — 이후 항목 건너뜀" % name)
                continue
            self.mark("a", first in new_pane["tabs"], "%s 분할로 옮긴 %s 가 새 패널 탭에 있음 tabs=%s" % (name, first, new_pane["tabs"]))
            # c 다중 분할(새 패널을 다시 쪼갬)
            res = self.split(new_pane["id"], op, d2)
            a = area(res["dump"], name)
            pmap = {p["id"]: p for p in a["panes"]}
            third = next((p for p in a["panes"] if not p["isHome"] and p["id"] != new_pane["id"]), None)
            ok = a["paneCount"] >= 3 and third is not None and near(pmap[new_pane["id"]]["proportion"], third["proportion"], 0.02)
            self.mark("c", ok, "%s 다중 분할 panes=%d props=%s" % (name, a["paneCount"], [p["proportion"] for p in a["panes"]]))
            # a 분할 패널로 탭 합류
            j = self.r.result("panel.join", id=new_pane["id"], doc=d3)
            self.mark("a", j.get("status") == "Joined" and any(t["id"] == d3 for t in j.get("tabs", [])),
                      "%s 분할 패널 %s 탭 합류 status=%s" % (name, new_pane["id"], j.get("status")))
            if name != "Right":
                j = self.r.result("panel.join", id=home, doc=d4)
                self.mark("a", j.get("status") == "Joined", "%s 기본 패널 탭 합류 status=%s" % (name, j.get("status")))
            # d 크기 조절(영역 안·문서 경계)
            for at in ("0", "outer"):
                dr = self.r.result("split.drag", area=name, at=at, delta=40)
                if dr.get("status") != "Dragged":
                    self.mark("d", False, "%s 분할선 at=%s 끌기 불가 %s" % (name, at, dr))
                    continue
                b, af = dr["before"], dr["after"]
                if at == "outer":
                    changed = not near(b["slotProportion"], af["slotProportion"], 0.005)
                else:
                    changed = not near(b["panes"][0]["proportion"], af["panes"][0]["proportion"], 0.005)
                can = all(s.get("canResize") for s in af["splitters"]) and (af["outerSplitter"] or {}).get("canResize", False)
                self.mark("d", changed and can, "%s 분할선 at=%s 비율 변화=%s canResize=%s" % (name, at, changed, can))
            made[name] = {"third": third and third["id"], "new": new_pane["id"], "docs": [d1, d2, d3, d4]}

        # f 저장·복원
        cap = self.r.result("split.capture")
        before = self.dump()
        saved = self.r.result("layout.save", name=layout_name, overwrite="true")
        layout_id = saved.get("id") if isinstance(saved, dict) else None  # layout.delete 는 id= 만 받는다
        for name, m in made.items():  # 분할을 흐트러뜨린다(문서를 문서 영역으로)
            for doc in m["docs"]:
                self.r.call("panel.returnDoc", doc=doc)
        time.sleep(0.5)
        self.r.result("layout.load", name=layout_name)
        time.sleep(1.0)
        after = self.dump()
        for name in made:
            ba, aa = area(before, name), area(after, name)
            same_n = ba["paneCount"] == aa["paneCount"]
            same_tabs = [sorted(p["tabs"]) for p in ba["panes"]] == [sorted(p["tabs"]) for p in aa["panes"]]
            same_p = all(near(x["proportion"], y["proportion"], 0.02) for x, y in zip(ba["panes"], aa["panes"])) and \
                near(ba["slotProportion"], aa["slotProportion"], 0.02)
            self.mark("f", same_n and same_tabs and same_p, "%s 복원 panes %d→%d 탭일치=%s 비율일치=%s" % (
                name, ba["paneCount"], aa["paneCount"], same_tabs, same_p))
        self.mark("f", any(t.get("area") for t in cap.get("tabs", [])), "저장 캡처에 분할 정보(area) 포함")

        # e 빈 패널 정리(복원된 상태에서 마지막 분할 패널을 비운다 → 사라짐, 이어서 모두 비우면 감싸기 풀림)
        for name, m in made.items():
            a = area(self.dump(), name)
            if not a["wrapped"]:
                self.mark("e", False, "%s 복원 뒤 분할 없음 — e 판정 불가" % name)
                continue
            n0 = a["paneCount"]
            last = next(p for p in reversed(a["panes"]) if not p["isHome"])
            for doc in last["tabs"]:
                self.out(doc)
            time.sleep(0.5)
            d = self.dump()
            a1 = area(d, name)
            self.mark("e", a1["paneCount"] == n0 - 1 and last["id"] not in [p["id"] for p in a1["panes"]] and not structural(d),
                      "%s 빈 분할 패널 %s 정리 panes %d→%d 불변식=%s" % (name, last["id"], n0, a1["paneCount"], structural(d) or "OK"))
            for p in a1["panes"]:
                if not p["isHome"]:
                    for doc in p["tabs"]:
                        self.out(doc)
            for doc in m["docs"]:
                self.r.call("panel.returnDoc", doc=doc)
            time.sleep(0.5)
            d = self.dump()
            a2 = area(d, name)
            self.mark("e", not a2["wrapped"] and a2["paneCount"] == 1 and not structural(d),
                      "%s 모두 비우면 감싸기 풀림 wrapped=%s panes=%d" % (name, a2["wrapped"], a2["paneCount"]))
        if "Right" in self.areas:
            self.tool_split_right(split_op["Right"])
        if layout_id:
            self.r.call("layout.delete", id=layout_id, confirm="true")
        return self.items

    def tool_split_right(self, op):
        """알림 영역을 도구(로그)로 분할 → 새 패널에 로그 → 로그를 원래 패널로 되돌리면 정리·감싸기 풀림(D-UI20R 전 경로 유지 확인)."""
        res = self.split(HOME["Right"], op, "Log")
        a = area(res["dump"], "Right")
        new_pane = next((p for p in a["panes"] if not p["isHome"]), None)
        self.mark("b", res["status"] == "Split" and new_pane is not None and "Log" in new_pane["tabs"],
                  "Right 도구(Log) 분할 status=%s panes=%d" % (res["status"], a["paneCount"]))
        self.out("Log")
        time.sleep(0.5)
        d = self.dump()
        a1 = area(d, "Right")
        self.mark("e", not a1["wrapped"] and a1["paneCount"] == 1 and not structural(d),
                  "Right 도구 분할 정리 wrapped=%s panes=%d 불변식=%s" % (a1["wrapped"], a1["paneCount"], structural(d) or "OK"))

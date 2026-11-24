# 웹 D-2 판별 시험 — 레이아웃 템플릿 tpl.focus→tpl.mdi-cascade, tpl.tabbed→tpl.mdi-cascade 적용 뒤 활성 문서 본문이 보이는지(effectivelyVisible) 확인한다
# 사용: PD_URL=http://127.0.0.1:5185/ python3 test/web/measure/retest_browser11.py   (헤드리스 전용, 웹 지속성 격리는 ?layout-key)
"""
판정(데스크톱 retest_window_menu_body.py 와 같은 기준):
  - 단계마다 docs.list 의 활성 문서(active=true)를 찾고, 종류별 본문 대표 요소(grid=grid.lblCount, form=form.txtTitle, gantt=gantt.lblZoom)를
    ui.find id=<요소> doc=<문서> 로 조회해 effectivelyVisible 이 True 인지 본다.
  - 요소가 없거나 effectivelyVisible 이 True 가 아니면 False(FAIL).
  - 활성 문서가 없거나 대표 요소를 모르는 종류면 UNDET(판정 불능) — PASS 로 세지 않는다.
  - 템플릿 적용 응답이 True 가 아니면 그 단계는 False.
항목 판정: False 가 하나라도 있으면 FAIL, 그 밖에 UNDET 가 있으면 UNDET, 모두 True 면 PASS.
"""
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
UNDET = "UNDET"
BODY_IDS = {"grid": "grid.lblCount", "form": "form.txtTitle", "gantt": "gantt.lblZoom"}
ORDERS = [  # 각 줄 = 한 회차. 회차 시작 전 tpl.mdi-cascade 로 초기화한다
    ["tpl.focus", "tpl.mdi-cascade"],   # D-2 재현 순서(집중 → 계단식 뒤 본문 빈 화면)
    ["tpl.tabbed", "tpl.mdi-cascade"],  # 탭 → 계단식
]
KINDS = ["form", "grid", "gantt"]  # 활성 문서 종류를 바꿔 가며 회차를 반복
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:480]), flush=True)


def body_state(r, step):
    """활성 문서 본문 대표 요소 확인 → {step, doc, kind, id, ok(True|False|UNDET), why?}."""
    act = next((d for d in (r.call("docs.list") or []) if d.get("active")), None)
    if not act:
        return {"step": step, "ok": UNDET, "why": "활성 문서 없음"}
    kind, did = act.get("kind"), act.get("id")
    bid = BODY_IDS.get(kind)
    if not bid:
        return {"step": step, "doc": did, "kind": kind, "ok": UNDET, "why": "대표 요소 미정 종류"}
    f = r.call("ui.find", id=bid, doc=did)
    if not f:
        err = (r.calls[-1] if r.calls else {}).get("error")
        return {"step": step, "doc": did, "kind": kind, "id": bid, "ok": False, "why": "본문 요소 없음(트리에 없음 또는 원격 오류)", "error": err}
    return {"step": step, "doc": did, "kind": kind, "id": bid, "ok": f.get("effectivelyVisible") is True,
            "effectivelyVisible": f.get("effectivelyVisible"), "bounds": f.get("bounds")}


def _walk(nodes, path):
    for n in nodes or []:
        p = path + [n]
        yield n, p
        yield from _walk(n.get("children"), p)


def full_tree(r):
    return r.call("tree.get", window=0, named=False, depth=200) or {}


def ancestor_path(r, node_id, tree=None):
    """전체 트리(tree.get named=false)에서 id 노드의 조상 경로(type[#id]·visible·isVisible) — lead 요청: 숨은 DocumentControl 아래 남았는지 확인용."""
    tr = tree or full_tree(r)
    for n, p in _walk(tr.get("tree"), []):
        if n.get("id") == node_id:
            return {"truncated": tr.get("truncated"), "path": [
                "%s%s%s" % (x.get("type"), ("#" + x["id"]) if x.get("id") else "", "" if x.get("visible") else "(숨김 isVisible=%s)" % x.get("isVisible"))
                for x in p]}
    return {"truncated": tr.get("truncated"), "path": None, "why": "트리에서 못 찾음"}


def layer_from_tree(r, tree=None):
    """MdiLayoutPanel 노드 bounds 를 트리에서 직접 실측(⑦ 레이어 W·H 1순위). 보이는 것 중 가장 큰 것을 쓴다."""
    tr = tree or full_tree(r)
    cands = [n.get("bounds") for n, _ in _walk(tr.get("tree"), []) if n.get("type") == "MdiLayoutPanel" and n.get("visible") and n.get("bounds")]
    if not cands:
        return None, {"truncated": tr.get("truncated"), "count": 0}
    b = max(cands, key=lambda x: (x.get("w") or 0) * (x.get("h") or 0))
    return (b["w"], b["h"]), {"truncated": tr.get("truncated"), "count": len(cands), "bounds": b}


def containers(tree):
    """① 예비안 B(odev-2r): 모드에 맞는 문서 컨테이너만 있어야 한다 — DocumentControl·MdiDocumentControl 개수(보이는 것 수)."""
    out = {}
    for n, p in _walk(tree.get("tree"), []):
        ty = n.get("type")
        # 문서 영역(DocumentDockControl#Documents) 아래만 센다 — 탐색기·로그 도구 패널도 DocumentControl 을 쓴다(i0944 실측)
        if ty in ("DocumentControl", "MdiDocumentControl") and any(x.get("type") == "DocumentDockControl" and x.get("id") == "Documents" for x in p[:-1]):
            c = out.setdefault(ty, {"count": 0, "visible": 0})
            c["count"] += 1
            c["visible"] += 1 if n.get("visible") else 0
    return out


def panel_mdi(tree):
    """보조(odev-2r): 문서 영역 밖(측면 패널 DocumentDockControl — 항상 Tabbed)의 MdiDocumentControl 개수. 예비안 B 에서는 0 이어야 함."""
    n_out = 0
    for n, p in _walk(tree.get("tree"), []):
        if n.get("type") == "MdiDocumentControl" and not any(x.get("type") == "DocumentDockControl" and x.get("id") == "Documents" for x in p[:-1]):
            n_out += 1
    return n_out


def mode_check(step, cont, anc):
    """①② 판정: tabbed/focus 는 DocumentControl 만, mdi-* 는 MdiDocumentControl 만 존재하고, 본문 조상 경로에 그 컨테이너가 있으며 숨은 조상이 없어야 함."""
    want, other = ("MdiDocumentControl", "DocumentControl") if step.startswith("tpl.mdi") else ("DocumentControl", "MdiDocumentControl")
    only = cont.get(want, {}).get("count", 0) > 0 and cont.get(other, {}).get("count", 0) == 0
    path = (anc or {}).get("path") or []
    under = any(p.startswith(want) for p in path) and not any("숨김" in p for p in path)
    return {"want": want, "onlyWantExists": only, "bodyUnderVisibleWant": under}


def grid_first_row(r, gid):
    """③ 같은 뷰 인스턴스 확인용: 1순위 grid.visible(odev-3, 읽기 전용 — first·selectedIndex), 없는 판이면 ui.scroll lines=0(이동 없음)."""
    v = r.call("grid.visible", id="grid.dgMain", doc=gid)
    if v is not None:
        return {"after": {"first": v.get("first"), "last": v.get("last"), "rows": v.get("rows")}, "selectedIndex": v.get("selectedIndex"), "offsetY": v.get("offsetY"), "src": "grid.visible"}
    s = r.call("ui.scroll", id="grid.dgMain", doc=gid, lines=0) or {}
    return {"after": s.get("after"), "selectedIndex": None, "src": "ui.scroll"}


def grid_prepare(r, gid, index=300):
    """③ 회차 시작: grid.select 로 300행 선택·스크롤(odev-3), 없는 판이면 ui.scroll index=300. → (첫 보이는 행 정보, 선택 행 또는 None)."""
    s = r.call("grid.select", id="grid.dgMain", index=index, scroll=True, doc=gid)
    if s is not None:
        time.sleep(0.4)
        v = grid_first_row(r, gid)
        return v.get("after") or {"error": "grid.visible 실패"}, v.get("selectedIndex")
    return (r.call("ui.scroll", id="grid.dgMain", doc=gid, index=index) or {}).get("after") or {"error": "시작 스크롤 실패(본문 미표시 등)"}, None


def painted(page, bounds, pad=8):
    """렌더링 확인(i1006 교훈: 트리는 visible 인데 화면이 빈칸) — 본문 요소 영역(±pad)을 잘라 찍어 색 수를 센다. 10색 미만이면 그려지지 않은 것."""
    if not bounds or not bounds.get("w") or not bounds.get("h"):
        return {"painted": None, "why": "bounds 없음"}
    from io import BytesIO
    from PIL import Image
    vp = page.viewport_size or {"width": 1280, "height": 800}
    x0, y0 = max(0, bounds["x"] - pad), max(0, bounds["y"] - pad)
    x1, y1 = min(vp["width"], bounds["x"] + bounds["w"] + pad), min(vp["height"], bounds["y"] + bounds["h"] + pad)
    if x1 - x0 < 2 or y1 - y0 < 2:
        return {"painted": False, "why": "화면 밖"}
    png = page.screenshot(clip={"x": x0, "y": y0, "width": x1 - x0, "height": y1 - y0})
    colors = Image.open(BytesIO(png)).convert("RGB").getcolors(maxcolors=4096)
    n = len(colors) if colors is not None else 4096
    # 문턱 10색: i1006 실측 — 빈칸 영역은 1~3색(머리 띠·경계선 포함), 글자가 그려진 영역은 219~368색
    return {"painted": n >= 10, "colors": n, "clip": (x0, y0, x1 - x0, y1 - y0)}


def verdict(vals):
    if any(v is False for v in vals):
        return "FAIL"
    if any(v == UNDET for v in vals) or not vals:
        return "UNDET(판정 불능 — PASS 아님)"
    return "PASS" if all(v is True for v in vals) else "FAIL"


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-d2")
        con = []  # 진단판 콘솔의 [d2-guard] 표식(HiddenPresenterGuard, odev-2r) — 단계별로 잘라 기록
        page.on("console", lambda m: con.append(m.text) if "d2-guard" in m.text else None)
        r.call("docs.close", all=True)
        ids = {}
        for k in KINDS:  # MDI 문서(sdi 아님) 종류별 1개
            od = r.call("docs.open", kind=k, title="D2-%s" % k, allowDuplicate=True)
            ids[k] = od[0]["id"] if od else None
            time.sleep(1.0)
        tl = r.call("layout.template.list") or []
        rec("D-2 사전: 문서 3종 열기·템플릿 목록", "form·grid·gantt 문서 id, tpl.focus·tpl.tabbed·tpl.mdi-cascade 존재",
            {"docs": ids, "templates": [x.get("id") for x in tl] if isinstance(tl, list) else tl},
            "PASS" if all(ids.values()) and isinstance(tl, list) and {"tpl.focus", "tpl.tabbed", "tpl.mdi-cascade"} <= {x.get("id") for x in tl} else "FAIL")
        lw, linfo = layer_from_tree(r)
        rec("참고: MDI 레이어(MdiLayoutPanel) bounds 트리 실측", "보이는 MdiLayoutPanel 노드 bounds", {"wh": lw, "info": linfo}, "기록")
        for k in KINDS:
            if not ids.get(k):
                rec("D-2 %s 활성" % k, "문서 열림", None, "UNDET(판정 불능 — PASS 아님)")
                continue
            for seq in ORDERS:
                r.call("layout.template.apply", id="tpl.mdi-cascade")
                time.sleep(0.6)
                r.call("docs.activate", doc=ids[k])
                time.sleep(0.6)
                grid0, sel0 = None, None
                if k == "grid":  # ③ 회차 시작 때 그리드를 300행으로 스크롤해 두고 단계마다 유지되는지 본다
                    grid0, sel0 = grid_prepare(r, ids[k])
                    time.sleep(0.4)
                steps = []
                for tid in seq:
                    c0 = len(con)
                    applied = r.call("layout.template.apply", id=tid) is True
                    time.sleep(0.8)
                    st = body_state(r, tid)
                    st["d2guard"] = con[c0:]
                    st["pixel"] = painted(page, st.get("bounds"))
                    if st.get("ok") is True and st["pixel"].get("painted") is False:
                        st["ok"] = False
                        st["why"] = "트리는 보임(effectivelyVisible=True)인데 화면 영역이 단색 — 렌더링 안 됨"
                    st["applied"] = applied
                    tree = full_tree(r)
                    st["containers"] = containers(tree)
                    st["panelMdiDocumentControl"] = panel_mdi(tree)
                    if st.get("id"):
                        st["ancestors"] = ancestor_path(r, st["id"], tree)
                    st["modeCheck"] = mode_check(tid, st["containers"], st.get("ancestors"))
                    if grid0 is not None:
                        g = grid_first_row(r, ids[k])
                        # 모드마다 뷰포트 높이가 달라 보이는 행 수·첫 행이 1~2행 흔들린다(i1006 실측 289→290~292). 뷰가 새로 만들어지면 0행으로 돌아가므로 첫 행 ±3 이내면 유지로 본다.
                        f0, f1 = (grid0 or {}).get("first"), (g.get("after") or {}).get("first")
                        sel_ok = sel0 is None or g.get("selectedIndex") == sel0  # 선택 행 유지(grid.select·grid.visible 있는 판만)
                        st["gridScroll"] = {"start": grid0, "startSelected": sel0, "now": g, "kept": f0 is not None and f1 is not None and abs(f1 - f0) <= 3 and sel_ok}
                    if st.get("doc"):
                        st["docEntry"] = next(({kk: d.get(kk) for kk in ("state", "zIndex", "zSource", "managed", "host")} for d in (r.call("docs.list") or []) if d.get("id") == st["doc"]), None)
                    if not applied:
                        st["ok"] = False
                        st["why"] = "템플릿 적용 응답이 True 아님"
                    shot = os.path.join(OUT, "d2_%s_%s_%d_%s.png" % (k, "-".join(x.split(".")[1] for x in seq), len(steps) + 1, tid.split(".")[1]))
                    page.screenshot(path=shot)
                    st["shot"] = os.path.basename(shot)
                    steps.append(st)
                order = "→".join(seq)
                rec("D-2 [%s 활성] %s 뒤 활성 문서 본문 보임" % (k, order), "단계마다 본문 대표 요소 effectivelyVisible=True 이고 그 화면 영역에 글자·선이 그려짐(10색 이상, UNDET 는 PASS 아님)",
                    steps, verdict([s["ok"] for s in steps]))
        r.call("layout.template.apply", id="tpl.mdi-cascade")  # 정리: 집중 해제
        # 예비안 B ①~③ 요약(기록 — 판정은 위 본문 가시성 행이 기준)
        allsteps = [s for x in res if x["item"].startswith("D-2 [") for s in x["actual"]]
        rec("예비안 B ① 모드에 맞는 컨테이너만 존재 / ② 본문이 보이는 그 컨테이너 아래",
            "tabbed·focus=DocumentControl 만, mdi=MdiDocumentControl 만; 본문 조상에 그 컨테이너·숨김 없음",
            [{"step": s["step"], "doc": s.get("doc"), "containers": s.get("containers"), **(s.get("modeCheck") or {})} for s in allsteps],
            "PASS" if allsteps and all((s.get("modeCheck") or {}).get("onlyWantExists") and (s.get("modeCheck") or {}).get("bodyUnderVisibleWant") for s in allsteps) else "FAIL")
        pm = [(s["step"], s.get("doc"), s.get("panelMdiDocumentControl")) for s in allsteps]
        rec("예비안 B 보조: 측면 패널 MdiDocumentControl=0", "문서 영역 밖 MdiDocumentControl 개수가 모든 단계에서 0", pm,
            "PASS" if pm and all(x[2] == 0 for x in pm) else "FAIL")
        gs = [s["gridScroll"] for s in allsteps if s.get("gridScroll")]
        rec("예비안 B ③ 모드 왕복 뒤 같은 뷰 인스턴스(그리드 스크롤·선택 유지)", "회차 시작 때 300행 선택·스크롤한 첫 보이는 행이 단계마다 ±3 이내(뷰 재생성이면 0행으로 돌아감), grid.visible 있는 판은 selectedIndex 도 같음", gs,
            "PASS" if gs and all(g["kept"] for g in gs) else ("FAIL" if gs else "UNDET(판정 불능 — PASS 아님)"))
        # ④ 활성 전환 3바퀴 뒤 zReason(계단식 MDI)
        order = [ids[k] for k in KINDS if ids.get(k)]
        laps = []
        for lap in range(3):
            for did in order:
                r.call("docs.activate", doc=did)
                time.sleep(0.3)
            laps.append([(d.get("id"), d.get("active"), d.get("zIndex"), d.get("zReason"), d.get("zSource")) for d in (r.call("docs.list") or []) if d.get("id") in order])
        last = laps[-1] if laps else []
        top = max(last, key=lambda x: x[2] if x[2] is not None else -1) if last else None
        rec("예비안 B ④ 활성 전환 3바퀴 뒤 zReason·zIndex(계단식)", "zReason=ok·zIndex 값 있음, 마지막 활성(%s)이 최상단" % (order[-1] if order else None), laps,
            "PASS" if last and all(x[3] == "ok" and x[2] is not None for x in last) and top and top[0] == order[-1] else "FAIL")
        rec("진단 콘솔 [d2-guard] 표식 전체", "가드판이면 표식이 찍힘(없으면 가드 미포함 또는 미발동)", {"count": len(con), "lines": con}, "기록")
        with open(os.path.join(OUT, "retest_web11.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        ctx.close()
        b.close()


if __name__ == "__main__":
    main()

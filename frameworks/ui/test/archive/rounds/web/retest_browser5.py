# 브라우저(WASM) 통합 재시험 — 관리형 SDI 창 제목줄 버튼 마우스 실클릭 6종, MRU 기반 zIndex 저장·복원, perf-ui short 를 한 번에 실행하는 스크립트
# 사용: python3 tools/retest_browser5.py [--only sdi,mru,lockdrag,perf]   (기본 전부)  결과: logs/otest_w2/retest_web5.json, perf_ui_web_short.json
import json
import re
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402
from playwright.sync_api import Page  # noqa: E402

_orig_shot = Page.screenshot


def _safe_shot(self, *a, **k):
    """path 가 있으면 메모리로 받아 NTFS 일시 I/O 오류(OSError 5)를 재시도하며 저장한다."""
    path = k.pop("path", None)
    data = _orig_shot(self, *a, **k)
    if path:
        for i in range(6):
            try:
                with open(path, "wb") as f:
                    f.write(data)
                break
            except OSError:
                time.sleep(0.5 * (i + 1))
    return data


Page.screenshot = _safe_shot

OUT = t.OUT
res = []


def shot(page, path):
    """NTFS 일시적 I/O 오류(OSError 5)에 대비해 메모리로 받아 재시도로 저장한다."""
    data = page.screenshot()
    for i in range(5):
        try:
            with open(path, "wb") as f:
                f.write(data)
            return
        except OSError:
            time.sleep(0.5 * (i + 1))


JLOG = []


def parse_j(probe_resp):
    """joinProbe 응답의 candidates=Id:x,y,w,h:visible[:j=1|0];… 에서 {패널Id: j}를 뽑는다(j 가 없으면 None)."""
    txt = str((probe_resp or {}).get("probe", ""))
    m = re.search(r"candidates=([^|]*)", txt)
    out = {}
    for item in (m.group(1).split(";") if m else []):
        parts = item.split(":")
        if not parts[0]:
            continue
        j = next((x[2:] for x in parts[1:] if x.startswith("j=")), None)
        out[parts[0]] = (int(j) if j in ("0", "1") else None)
    return out


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:400]), flush=True)


def click_id(page, r, ident, doc):
    """ui.find 의 bounds 중심을 실제 마우스로 클릭한다(Automation Invoke 가 아님)."""
    c = r.call("ui.find", id=ident, doc=doc)
    if not c or not c.get("visible"):
        return None
    b = c["bounds"]
    page.mouse.click(b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)
    time.sleep(0.6)
    return c


def part_sdi(b):
    ctx, page, r = t.boot(b)
    ids = [x["id"] for x in r.call("docs.open", kind="grid", count=2)]
    time.sleep(2)
    d = ids[0]
    r.call("docs.sdi", doc=d, on=True)
    time.sleep(2)
    st = t.doc(r, d)
    rec("SDI 준비: 관리형 창", "floating·managedWindow", {"floating": st.get("floating"), "managed": st.get("managedWindow")}, "PASS" if st.get("floating") and st.get("managedWindow") else "FAIL")
    shot(page, os.path.join(OUT, "sdi_click_0_before.png"))
    cs = lambda: r.call("chrome.state", doc=d) or {}
    for ident, field in (("chrome.btnPin", "pinned"), ("chrome.btnLock", "locked"), ("chrome.btnSync", "sync"), ("chrome.btnDup", "allowDuplicate")):
        before = cs().get(field)
        c = click_id(page, r, ident, d)
        mid = cs().get(field)
        click_id(page, r, ident, d)
        after = cs().get(field)
        ok = c is not None and before is not None and mid == (not before) and after == before
        rec("SDI 실클릭 %s(%s)" % (ident, field), "클릭 1회 토글, 2회 원복", "%s→%s→%s clicked=%s" % (before, mid, after, c is not None), "PASS" if ok else "FAIL")
        page.screenshot(path=os.path.join(OUT, "sdi_click_%s.png" % ident.split(".")[1]))
    # 잠금 시 손잡이
    g0 = r.call("ui.find", id="chrome.gripResize", doc=d)
    if not cs().get("locked"):
        click_id(page, r, "chrome.btnLock", d)
    g1 = r.call("ui.find", id="chrome.gripResize", doc=d)
    rec("SDI 잠금 시 gripResize", "잠금 후 visible=false", {"locked": cs().get("locked"), "before": g0 and g0.get("visible"), "after": g1 and g1.get("visible")},
        "PASS" if g1 and not g1.get("visible") else "FAIL")
    click_id(page, r, "chrome.btnLock", d)
    # 도움말
    h = click_id(page, r, "chrome.btnHelp", d)
    ht = r.call("ui.find", id="chrome.helpText", doc=d)
    if not ht:  # 도움말 팝업에 AutomationId 가 없을 수 있어 화면 텍스트('화면입니다'·'닫기')로 확인
        texts = [v for _, v in collect_texts(r)]
        ht = {"text": next((v for v in texts if "화면입니다" in v or "화면 입니다" in v), None)}
        ht = ht if ht["text"] else None
    rec("SDI 실클릭 chrome.btnHelp", "도움말 표시", {"clicked": h is not None, "helpText": ht and (ht.get("text") or ht.get("visible"))}, "PASS" if h and ht else "FAIL")
    shot(page, os.path.join(OUT, "sdi_click_help.png"))
    page.keyboard.press("Escape")
    # 새로고침(캐시): 클릭 → 인라인 확인 → 취소 → 확인
    click_id(page, r, "chrome.btnCache", d)
    ok1 = r.call("ui.find", id="chrome.refreshOk", doc=d)
    rec("SDI 실클릭 chrome.btnCache", "인라인 확인(확인/취소) 표시", bool(ok1), "PASS" if ok1 else "FAIL")
    if ok1:
        click_id(page, r, "chrome.refreshCancel", d)
        same = d in [x["id"] for x in r.call("docs.list")]
        rec("SDI 실클릭 refreshCancel", "문서 유지", same, "PASS" if same else "FAIL")
        click_id(page, r, "chrome.btnCache", d)
        click_id(page, r, "chrome.refreshOk", d)
        now = [x["id"] for x in r.call("docs.list")]
        rec("SDI 실클릭 refreshOk", "문서 교체", {"before": ids, "after": now}, "PASS" if d not in now else "FAIL")
    # 재결합 버튼(제목줄)
    nd = next((x["id"] for x in r.call("docs.list") if x.get("floating")), None)
    if nd:
        click_id(page, r, "chrome.btnSdi", nd)
        back = not t.doc(r, nd).get("floating")
        rec("SDI 실클릭 chrome.btnSdi(재결합)", "floating=false", back, "PASS" if back else "FAIL")
    ctx.close()


def part_mru(b):
    ctx, page, r = t.boot(b)
    ids = [x["id"] for x in r.call("docs.open", kind="grid", count=5)]
    time.sleep(2)
    r.call("chrome.pin", doc=ids[1])
    seq = [3, 0, 4, 2, 0]
    mru = []
    for i in seq:
        r.call("docs.activate", doc=ids[i])
        time.sleep(0.4)
        mru = [ids[i]] + [x for x in mru if x != ids[i]]
        lst = r.call("docs.list")
        z = {x["id"]: x.get("zIndex") for x in lst}
        top = max((k for k in z if k != ids[1]), key=lambda k: z[k])
        rec("MRU 활성 %s 직후 비핀 최상단" % ids[i], ids[i], {"top": top, "z": z}, "PASS" if top == ids[i] else "FAIL")
    # 기대: 비핀 문서의 zIndex 순서 = MRU 역순(최근이 위), 핀 문서는 맨 위
    lst = r.call("docs.list")
    z = {x["id"]: x.get("zIndex") for x in lst}
    unpinned = [k for k in z if k != ids[1]]
    seen = [k for k in reversed(mru) if k in unpinned]
    untouched = [k for k in unpinned if k not in mru]
    expect = untouched + seen  # 건드리지 않은 문서는 아래(열린 순서), 이어서 오래된 MRU → 최근
    got = sorted(unpinned, key=lambda k: z[k])
    rec("MRU zIndex 순서(아래→위)", expect, got, "PASS" if got == expect and z[ids[1]] == max(z.values()) else "FAIL")
    before = [(x["id"], x.get("zIndex"), x.get("active")) for x in lst]
    r.call("layout.save")
    page.close()
    ctx2, p2, r2 = t.boot(b, "?load-layout", ctx=ctx)
    time.sleep(3)
    after = [(x["id"], x.get("zIndex"), x.get("active")) for x in r2.call("docs.list")]
    rec("MRU 저장→복원 zIndex·활성", "동일", {"before": before, "after": after}, "PASS" if before == after else "FAIL")
    ctx.close()


def part_lockdrag(b):
    """잠긴 창(MDI·SDI)을 제목줄이 아닌 내용 영역에서 눌러 끌면 움직이는지 확인한다. 움직이면 FAIL. 대조로 잠금 전 제목줄 끌기도 잰다."""
    for mode in ("MDI", "SDI"):
        ctx, page, r = t.boot(b)
        ids = [x["id"] for x in r.call("docs.open", kind="grid", count=2)]
        time.sleep(2)
        d = ids[0]
        if mode == "SDI":
            r.call("docs.sdi", doc=d, on=True)
            time.sleep(2)

        def pos():
            return (r.call("ui.find", id="chrome.btnLock", doc=d) or {}).get("bounds")

        def drag(x, y, dx=150, dy=90):
            page.mouse.move(x, y)
            page.mouse.down()
            for i in range(1, 11):
                page.mouse.move(x + dx * i / 10, y + dy * i / 10)
                time.sleep(0.03)
            page.mouse.up()
            time.sleep(0.8)

        lb = pos()
        b0 = lb
        drag(lb["x"] - 120, lb["y"] + lb["h"] / 2)  # 제목줄(버튼 왼쪽 제목 글자 부근)
        b1 = pos()
        rec("%s 잠금 전 제목줄 끌기(대조)" % mode, "움직임", {"before": b0, "after": b1}, "PASS" if b0 != b1 else "미확정(끌기 방법이 창에 안 먹힘)")
        r.call("chrome.lock", doc=d)
        time.sleep(0.5)
        lb = pos()
        shot(page, os.path.join(OUT, "lockdrag_%s_before.png" % mode))
        b0 = lb
        drag(lb["x"] - 250, lb["y"] + 200)  # 내용 영역(제목줄 아래)
        b1 = pos()
        shot(page, os.path.join(OUT, "lockdrag_%s_after.png" % mode))
        rec("%s 잠금 후 내용 영역 끌기" % mode, "움직이지 않음", {"before": b0, "after": b1}, "PASS" if b0 == b1 else "FAIL(잠금인데 이동)")
        lb = pos()
        b0 = lb
        drag(lb["x"] - 120, lb["y"] + lb["h"] / 2)
        b1 = pos()
        rec("%s 잠금 후 제목줄 끌기" % mode, "움직이지 않음", {"before": b0, "after": b1}, "PASS" if b0 == b1 else "FAIL(잠금인데 이동)")
        ctx.close()


def part_panel(b):
    """Q15(패널 탭 합류)·Q16(알림 패널) — odev-6r 계약: panel.list(side)·panel.dock(status Docked|Rejected)·panel.tabs·panel.activate·panel.join."""
    ctx, page, r = t.boot(b)
    h = r.call("help")
    if "panel." not in json.dumps(h, ensure_ascii=False):
        rec("Q15·Q16 panel.* 명령", "help 에 panel.* 존재", "없음", "미검증(번들에 panel 명령 없음)")
        ctx.close()
        return
    lst = lambda: r.call("panel.list") or []
    find = lambda pid: next((p for p in lst() if p.get("id") == pid), {})
    panels = lst()
    notif = next((p for p in panels if "알림" in str(p.get("title"))), None)
    rec("Q16(a) 알림 패널 존재", "오른쪽 '알림' 패널", [(p.get("id"), p.get("title"), p.get("side")) for p in panels],
        "PASS" if notif and str(notif.get("side")).lower() == "right" else "FAIL")
    if notif:
        nid = notif["id"]
        rec("Q16(b·c) 항상 위·위치 고정 플래그", "alwaysOnTop=true, locked=true", {"alwaysOnTop": notif.get("alwaysOnTop"), "locked": notif.get("locked")},
            "PASS" if notif.get("alwaysOnTop") and notif.get("locked") else "FAIL")
        c = r.call("panel.dock", id=nid, side="center")
        rec("Q16(d) 가운데 도킹 거부", "status=Rejected 이고 side 불변 right", {"result": c, "side": find(nid).get("side")},
            "PASS" if (c or {}).get("status") == "Rejected" and str(find(nid).get("side")).lower() == "right" else "FAIL")
        shot(page, os.path.join(OUT, "q16_right.png"))
        # (e) 위치 메뉴 실클릭(제안 Id notify.mnuPos*): 열기 방법은 셸 확정 후 보강, 원격 panel.dock 으로 4변 확인
        for side in ("top", "bottom", "left", "right"):
            res = r.call("panel.dock", id=nid, side=side)
            time.sleep(0.5)
            cur = str(find(nid).get("side")).lower()
            rec("Q16(d·e) 가장자리 %s 허용" % side, "status=Docked, side=%s" % side, {"result": res, "side": cur}, "PASS" if (res or {}).get("status") == "Docked" and cur == side else "FAIL")
            shot(page, os.path.join(OUT, "q16_%s.png" % side))
        r.call("ui.click", id="notify.mnuPos")  # 위치 메뉴 열기
        time.sleep(0.8)
        for side, mid in (("top", "notify.mnuPosTop"), ("bottom", "notify.mnuPosBottom"), ("left", "notify.mnuPosLeft"), ("right", "notify.mnuPosRight")):
            c2 = r.call("ui.find", id=mid)
            if c2 is None:
                rec("Q16(e) 위치 메뉴 항목 %s" % mid, "메뉴 항목 존재(실클릭)", "ui.find 실패", "미검증(메뉴 열기 절차·Id 확정 필요)")
                break
        # (c) 실마우스 끌어 가운데로: 위치 불변
        side0 = str(find(nid).get("side")).lower()
        bb = find(nid).get("bounds") or {}
        if bb:
            cx, cy = page.viewport_size["width"] / 2, page.viewport_size["height"] / 2
            sx, sy = bb["x"] + min(40, bb["w"] / 2), bb["y"] + 10
            page.mouse.move(sx, sy)
            page.mouse.down()
            for i in range(1, 16):
                page.mouse.move(sx + (cx - sx) * i / 15, sy + (cy - sy) * i / 15)
                time.sleep(0.04)
            shot(page, os.path.join(OUT, "q16_drag_guides.png"))  # 끌기 중 도킹 안내에 가운데가 없어야 함(판독)
            page.mouse.up()
            time.sleep(0.8)
            f = find(nid)
            rec("Q16(c·d) 실마우스로 가운데에 끌어 놓기", "위치·bounds 불변", {"side": [side0, f.get("side")], "bounds": [bb, f.get("bounds")]},
                "PASS" if str(f.get("side")).lower() == side0 and f.get("bounds") == bb else "FAIL")
    # Q16(d) 도킹 안내: 다른 도구 창(로그)을 끌어 알림 패널 위에 올려 놓은 채(놓지 않음) 캡처 — 알림 패널 위에 안내가 없어야 한다
    logp = next((p for p in lst() if "로그" in str(p.get("title"))), None)
    if notif and logp and (logp.get("bounds") and find(notif["id"]).get("bounds")):
        lb, nb = logp["bounds"], find(notif["id"])["bounds"]
        sx, sy = lb["x"] + min(30, lb["w"] / 2), lb["y"] + 10
        tx, ty = nb["x"] + nb["w"] / 2, nb["y"] + nb["h"] / 2
        can = r.call("panel.canDrop", id=notif["id"], source=logp["id"])
        page.mouse.move(sx, sy)
        page.mouse.down()
        for i in range(1, 16):
            page.mouse.move(sx + (tx - sx) * i / 15, sy + (ty - sy) * i / 15)
            time.sleep(0.04)
        time.sleep(0.5)
        shot(page, os.path.join(OUT, "q16_guides_over_notify.png"))  # 놓기 전 상태(안내 판독)
        page.mouse.up()
        time.sleep(0.8)
        rec("Q16(d) 알림 패널 위 도킹 안내", "안내 없음·놓아도 합류 불가(스크린샷 판독 + canDrop)", {"canDrop": can, "notif_side": find(notif["id"]).get("side")},
            "증거" if can is None else ("PASS" if can in (False, "None", "Rejected") or (isinstance(can, dict) and not can.get("zones")) else "FAIL"))
    # Q15 탭 합류(odev-1 확정: 실마우스로 LeftPane 안쪽(탭 내용 영역)에 끌어 놓기)
    def drag(sx, sy, tx, ty, shot=None, steps=15, probe_class=None, dwell=0.45, after_up=None):
        page.mouse.move(sx, sy)
        page.mouse.down()
        for i in range(1, steps + 1):
            page.mouse.move(sx + (tx - sx) * i / steps, sy + (ty - sy) * i / steps)
            time.sleep(0.04)
        time.sleep(dwell)  # 패널 위 체류(합류 조건 250ms 이상 — 기본 450ms)
        probe_mid = r.call("panel.joinProbe", x=tx, y=ty)
        print("   [joinProbe 끌기 중 (%d,%d)] %s" % (tx, ty, probe_mid), flush=True)
        JLOG.append(("during", parse_j(probe_mid)))
        cls_mid = (r.call("ui.find", dock="LeftPane") or {}).get("classes")
        JLOG.append(("classes_during", cls_mid))
        print("   [ui.find dock=LeftPane classes 끌기 중] %s" % (cls_mid,), flush=True)
        if probe_class is not None:
            # 최종 판정은 tree.get 의 classes 에 pdJoinTarget 이 정확히 1개 + 끄는 중 캡처(육안)다. joinProbe 의 hover 는 '코드가 대상을 골랐다'는 참고 기록일 뿐 판정에 쓰지 않는다.
            # (classes 필드는 odev-6r 다음 판부터 — 그 전 판에서는 0건으로 나오며 '미검증'으로 해석한다)
            probe_class.append(json.dumps(r.call("tree.get", window=0, named=True, depth=50) or {}, ensure_ascii=False).count("pdJoinTarget"))
        if shot:
            page.screenshot(path=os.path.join(OUT, shot))
        page.mouse.up()
        probe_after = r.call("panel.joinProbe", x=tx, y=ty)
        print("   [joinProbe 놓은 직후] %s" % (probe_after,), flush=True)
        JLOG.append(("after", parse_j(probe_after)))
        cls_after = (r.call("ui.find", dock="LeftPane") or {}).get("classes")
        JLOG.append(("classes_after", cls_after))
        print("   [ui.find dock=LeftPane classes 놓은 직후] %s" % (cls_after,), flush=True)
        if after_up is not None:
            after_up()
        time.sleep(1.0)

    ids = [x["id"] for x in r.call("docs.open", kind="grid", count=2)]
    time.sleep(1)
    left = next((p for p in lst() if p.get("id") == "LeftPane" or "탐색기" in str(p.get("title"))), None)
    if not left:
        rec("Q15 탐색기 패널(LeftPane)", "panel.list 에 LeftPane", None, "FAIL")
        ctx.close()
        return
    eid = left["id"]
    lb = left.get("bounds") or {}
    tx, ty = lb.get("x", 0) + lb.get("w", 200) / 2, lb.get("y", 0) + lb.get("h", 300) / 2 + 30  # 탭 내용 영역(머리줄 아래)
    before = r.call("panel.tabs", id=eid) or {}
    # (a) 그리드 문서 → LeftPane 안쪽: 시작점 두 가지(MDI 문서 창 제목줄 / 셸 탭 스트립 shell.tab)를 각각 시도해 둘 다 기록
    after_a, n1 = before, len(before.get("tabs") or [])
    for label, did in (("MDI 제목줄", ids[0]),):  # shell.tab 시작은 미구현(odev-1) — 아래에서 별도 기록
        cur = r.call("panel.tabs", id=eid) or {}
        c0 = len(cur.get("tabs") or [])
        if label == "MDI 제목줄":
            lk = r.call("ui.find", id="chrome.btnLock", doc=did) or {}
            bb = lk.get("bounds")
            orig_lock = bb
            ttl = next((x.get("title") for x in (r.call("docs.list") or []) if x.get("id") == did), None)
            cands = [c for c in find_text_nodes(r, ttl or "") if bb and abs((c["bounds"]["y"] + c["bounds"]["h"] / 2) - (bb["y"] + bb["h"] / 2)) <= 14]
            if cands:  # 제목 글자 영역(PART_Header 안, 로고·캡션 버튼 제외)
                c0b = cands[0]["bounds"]
                pt = (c0b["x"] + c0b["w"] / 2, c0b["y"] + c0b["h"] / 2)
            else:
                pt = (bb["x"] - 120, bb["y"] + bb["h"] / 2) if bb else None
        else:
            st = r.call("ui.find", id="shell.tab." + did) or {}
            bb = st.get("bounds")
            pt = (bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2) if bb else None
        cd0 = r.call("panel.canDrop", source=did, target=eid, inner=True)
        jc = []
        samples = []

        def sample_after_up(did=did, samples=samples):
            """놓은 직후 0·50·100·200ms 부근에 문서 창 chrome 이 MDI 영역에 잔상으로 나타나는지 연속 확인."""
            t0 = time.time()
            for _ in range(8):
                lkn = r.call("ui.find", id="chrome.btnLock", doc=did)
                samples.append({"ms": round((time.time() - t0) * 1000), "visible": bool(lkn and lkn.get("visible")), "bounds": lkn and lkn.get("bounds")})
                time.sleep(0.03)
        if pt is None:
            rec("Q15(a) 끌기 시작점 %s" % label, "컨트롤 bounds", None, "미검증(시작점을 못 찾음)")
            continue
        lbx = (next((p for p in lst() if p.get("id") == eid), {}).get("bounds") or lb)
        drag(pt[0], pt[1], lbx.get("x", 0) + lbx.get("w", 200) / 2, lbx.get("y", 0) + lbx.get("h", 300) / 2 + 30, shot="q15_a_%s_dragging.png" % ("title" if label.startswith("MDI") else "shelltab"), steps=14, probe_class=jc, after_up=sample_after_up)
        cd1 = r.call("panel.canDrop", source=did, target=eid, inner=True)
        after_a = r.call("panel.tabs", id=eid) or {}
        c1 = len(after_a.get("tabs") or [])
        n1 = c1
        rec("Q15(a) 그리드 문서 %s → LeftPane 안쪽 끌어 놓기" % label, "탭 수 +1", {"canDrop": [cd0, cd1], "tabs": [c0, c1], "active": after_a.get("active"), "tab_ids": [x.get("id") for x in (after_a.get("tabs") or [])]},
            "PASS" if c1 == c0 + 1 else "FAIL(%s 시작점은 합류 안 됨)" % label)
        dj = next((v for k, v in reversed(JLOG) if k == "during"), {})
        aj = next((v for k, v in reversed(JLOG) if k == "after"), {})
        if dj and all(v is not None for v in dj.values()):
            j_ok = dj.get("LeftPane") == 1 and all(v == 0 for k, v in dj.items() if k != "LeftPane") and all(v == 0 for v in aj.values())
            cm = next((v for k, v in reversed(JLOG) if k == "classes_during"), None)
            ca = next((v for k, v in reversed(JLOG) if k == "classes_after"), None)
            cls_ok = cm is not None and ("pdJoinTarget" in cm) and not (ca and "pdJoinTarget" in ca)
            rec("Q15 강조 부착(joinProbe j 필드 + ui.find dock=LeftPane classes 교차)", "끌기 중 LeftPane j=1·classes 에 pdJoinTarget, 나머지 j=0, 놓은 뒤 전부 해제 + 끄는 중 캡처 육안(q15_a_title_dragging.png)",
                {"during": dj, "after": aj, "classes_during": cm, "classes_after": ca}, "PASS(캡처 판독 필요)" if (j_ok and cls_ok) else ("FAIL" if j_ok != cls_ok or not j_ok else "FAIL"))
        else:
            rec("Q15 강조 부착(joinProbe j 필드)", "candidates 항목에 :j=1|0", {"during": dj, "after": aj}, "미검증(j 필드 없는 판본)")
        mdi_vis = next((x.get("visible") for x in (r.call("docs.list") or []) if x.get("id") == did), None)
        zreason = next((x.get("zReason") for x in (r.call("docs.list") or []) if x.get("id") == did), None)
        zr_ok = zreason == "notMdi"  # otest-1 권고 양성 지표: MDI 쌓임에서 빠짐
        act_ok = (r.call("panel.tabs", id=eid) or {}).get("active") == did  # 합류한 문서가 패널의 활성 탭
        print("   [(a②) 양성 지표] zReason=%s panel.tabs.active==doc:%s" % (zreason, act_ok), flush=True)
        near = lambda bx: bool(bx and orig_lock and abs(bx["x"] - orig_lock["x"]) <= 2 and abs(bx["y"] - orig_lock["y"]) <= 2)
        bounce = [x for x in samples if x["visible"] and near(x["bounds"])]
        late = [x for i, x in enumerate(samples) if i >= 1 and x["visible"]]
        rec("Q15 ⑧ 놓은 직후 창이 끌기 전 자리로 튀지 않음(위치 기준)", "시작 bounds(±2px)에 보이면 FAIL, 놓은 위치에 보이는 것은 첫 샘플까지만 허용(합류 시)", {"orig": orig_lock, "samples": samples, "bounce": len(bounce), "late_visible": len(late)},
            "PASS" if (c1 == c0 + 1 and not bounce and not late) else ("FAIL(튐)" if bounce else "FAIL"))
        rec("Q15(a②) 합류한 문서가 MDI 영역에서 사라짐·pdJoinTarget 클래스", "문서 창 chrome 이 MDI 에서 사라짐(ui.find 부재), 끌기 중 pdJoinTarget 정확히 1개", {"doc_visible_flag(참고)": mdi_vis, "zReason": zreason, "panel_active_is_doc": act_ok, "pdJoinTarget_count_during_drag": jc}, "PASS" if (c1 == c0 + 1 and not any(x["visible"] for x in samples) and jc and jc[0] == 1 and zr_ok and act_ok) else "FAIL")
        page.screenshot(path=os.path.join(OUT, "q15_a_%s_after.png" % ("title" if label.startswith("MDI") else "shelltab")))
    # ⑥ 패널 위를 빠르게 지나쳐 문서 영역에서 놓기 / ⑦ 패널에 도착하자마자 놓기: 합류하지 않아야 한다
    for tag, doc_i in (("⑥", 0), ("⑦", 1)):
        nd = [x["id"] for x in r.call("docs.open", kind="grid", count=1)][0]
        time.sleep(0.8)
        lk = r.call("ui.find", id="chrome.btnLock", doc=nd) or {}
        if not lk.get("bounds"):
            rec("Q15 %s 시험 문서 준비" % tag, "제목줄 bounds", None, "미검증")
            continue
        sx, sy = lk["bounds"]["x"] - 120, lk["bounds"]["y"] + lk["bounds"]["h"] / 2
        lbx = next((p for p in lst() if p.get("id") == eid), {}).get("bounds") or lb
        px, py = lbx.get("x", 0) + lbx.get("w", 200) / 2, lbx.get("y", 0) + lbx.get("h", 300) / 2 + 30
        t_b = len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])
        pass_probe = ""
        page.mouse.move(sx, sy)
        page.mouse.down()
        if tag == "⑥":  # 패널 중앙을 거쳐 곧바로 문서 영역(패널 오른쪽)으로 이동 후 놓기
            for i in range(1, 7):
                page.mouse.move(sx + (px - sx) * i / 6, sy + (py - sy) * i / 6)
            pass_probe = str((r.call("panel.joinProbe", x=px, y=py) or {}).get("probe", ""))  # 패널 중앙을 지나는 순간의 probe
            for i in range(1, 7):
                page.mouse.move(px + 500 * i / 6, py)
            page.mouse.up()
        else:  # 패널 바로 앞(오른쪽 문서 영역)까지 이동한 뒤 한 번에 패널로 점프하고 즉시 놓기(체류 거의 0)
            for i in range(1, 11):
                page.mouse.move(sx + (lbx.get("x", 0) + lbx.get("w", 200) + 60 - sx) * i / 10, sy + (py - sy) * i / 10)
            page.mouse.move(px, py)
            page.mouse.up()
        time.sleep(1.0)
        t_a = len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])
        lp = str((r.call("panel.joinProbe", x=px, y=py) or {}).get("probe", ""))
        mlast = re.search(r"last=([^|]*(?:\|[^|]*){0,5})", lp)
        dw = re.search(r"dwellMs=(\d+)", lp)
        acc = re.search(r"accepted=(True|False)", lp)
        if tag == "⑥":
            # odev-1: probe 의 hoverLog 에서 마지막 'start:' 뒤 LeftPane 구간(진입-이탈(머문ms))을 읽는다. 구간≥1건(패널을 지나감)·모든 머문ms<250·accepted=False·탭 불변이면 PASS
            hl = re.search(r"hoverLog=([^|]*)", lp)
            segs = []
            if hl:
                items = hl.group(1).split(";")
                last_start = max([i for i, x in enumerate(items) if x.startswith("start:")] or [-1])
                for x in items[last_start + 1:]:
                    m = re.match(r"(\w+)@[^(]*\((\d+)\)", x.strip())
                    if m:
                        segs.append((m.group(1), int(m.group(2))))
            left = [ms for pid, ms in segs if pid == "LeftPane"]
            rec("Q15 ⑥ 패널을 빠르게 지나쳐 문서 영역에서 놓기(hoverLog 판정)", "마지막 start 뒤 LeftPane 구간 ≥1건·모든 머문ms<250 + accepted=False + 탭 불변",
                {"tabs": [t_b, t_a], "hoverLog": (hl.group(1)[:200] if hl else None), "leftpane_segments_ms": left, "accepted": (acc.group(1) if acc else None)},
                "FAIL(합류함)" if t_a != t_b else ("판정 불능(hoverLog 필드 없음 — 구판)" if not hl else ("PASS" if (left and all(ms < 250 for ms in left) and acc and acc.group(1) == "False") else "FAIL(조건 불충족 또는 통과 구간 없음)")))
            continue
        rec("Q15 %s %s" % (tag, "패널을 빠르게 지나쳐 문서 영역에서 놓기" if tag == "⑥" else "패널에 도착하자마자(<250ms) 놓기"), "탭 수 불변 + 놓은 직후 joinProbe last 의 accepted=False 와 dwellMs<250(값이 없거나 dwellMs>=250 이면 판정 불능)",
            {"tabs": [t_b, t_a], "probe_last": (mlast.group(0)[:160] if mlast else None), "dwellMs": (int(dw.group(1)) if dw else None), "accepted": (acc.group(1) if acc else None)},
            ("FAIL(합류함)" if t_a != t_b else ("판정 불능(dwellMs/accepted 값 없음 또는 dwellMs>=250)" if not (dw and acc and int(dw.group(1)) < 250 and acc.group(1) == "False") else "PASS")))
    # shell.tab(작업표시줄 탭) 시작 끌기: 8 DIP 이상+450ms 체류=합류 / 4 DIP 만 움직이면 클릭(활성화) / Esc 취소 / 패널 밖 놓기 / pdJoinTarget 1개
    def tab_pt(did):
        st = r.call("ui.find", id="shell.tab." + did) or {}
        bb = st.get("bounds")
        return (bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2) if bb else None

    lbx = next((p for p in lst() if p.get("id") == eid), {}).get("bounds") or lb
    px, py = lbx.get("x", 0) + lbx.get("w", 200) / 2, lbx.get("y", 0) + lbx.get("h", 300) / 2 + 30
    tabn = lambda: len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])
    nd = [x["id"] for x in r.call("docs.open", kind="grid", count=4)]
    time.sleep(1)
    a_pt = tab_pt(nd[0])
    if a_pt:
        t_b = tabn()
        cnt = []
        drag(a_pt[0], a_pt[1], px, py, shot="q15_tab_join.png", steps=14, probe_class=cnt)
        rec("Q15 shell.tab 시작: 8DIP 이상·450ms 체류 후 놓기 → 합류", "탭 +1, 끌기 중 pdJoinTarget 1개", {"tabs": [t_b, tabn()], "pdJoinTarget": cnt}, "PASS" if tabn() == t_b + 1 and cnt and cnt[0] == 1 else "FAIL")
    else:
        rec("Q15 shell.tab 시작", "shell.tab.<id> bounds", None, "FAIL(탭 못 찾음)")
    b_pt = tab_pt(nd[1])
    if b_pt:
        t_b = tabn()
        page.mouse.move(b_pt[0], b_pt[1])
        page.mouse.down()
        page.mouse.move(b_pt[0] + 4, b_pt[1])
        page.mouse.up()
        time.sleep(0.8)
        act = ((r.call("status") or {}).get("active") or {}).get("id")
        rec("Q15 shell.tab 시작: 4DIP 이동 후 놓기 = 일반 클릭", "문서 활성화, 합류 안 함", {"active": act, "tabs": [t_b, tabn()]}, "PASS" if act == nd[1] and tabn() == t_b else "FAIL")
    c_pt = tab_pt(nd[2])
    if c_pt:
        t_b = tabn()
        page.mouse.move(c_pt[0], c_pt[1])
        page.mouse.down()
        for i in range(1, 15):
            page.mouse.move(c_pt[0] + (px - c_pt[0]) * i / 14, c_pt[1] + (py - c_pt[1]) * i / 14)
            time.sleep(0.03)
        time.sleep(0.45)
        page.keyboard.press("Escape")
        time.sleep(0.3)
        page.mouse.up()
        time.sleep(0.8)
        rec("Q15 shell.tab 시작: Esc 로 취소", "탭 수 불변", [t_b, tabn()], "PASS" if tabn() == t_b else "FAIL")
    d_pt = tab_pt(nd[3])
    if d_pt:
        t_b = tabn()
        drag(d_pt[0], d_pt[1], px + 450, py - 80, steps=14)
        rec("Q15 shell.tab 시작: 패널 밖(문서 영역)에 놓기", "탭 수 불변", [t_b, tabn()], "PASS" if tabn() == t_b else "FAIL")
    # 핀(자동 숨김) 상태: 가) pdJoinTarget 수·종류 나) 강조 덮개/가림 캡처 다) 놓았을 때 합류 여부
    nd2 = [x["id"] for x in r.call("docs.open", kind="grid", count=1)] if os.environ.get("PIN_TEST") else []
    time.sleep(1)
    lb_now = next((p for p in lst() if p.get("id") == eid), {}).get("bounds") or lbx
    if nd2:
        page.mouse.click(lb_now["x"] + lb_now["w"] - 17, lb_now["y"] + 12)  # LeftPane 핀 → 자동 숨김(PIN_TEST=1 일 때만)
    time.sleep(1.2)
    e_pt = tab_pt(nd2[0]) if nd2 else None
    if e_pt:
        t_b = tabn()
        cnt2 = []
        drag(e_pt[0], e_pt[1], 14, 400, shot="q15_pinned_hover.png", steps=14, probe_class=cnt2)
        rec("Q15 탐색기 핀(자동 숨김) 상태: 핀 막대 위에 450ms 머문 뒤 놓기", "기록: (가) pdJoinTarget 수 (나) 캡처 (다) 합류 여부", {"pdJoinTarget": cnt2, "tabs": [t_b, tabn()], "capture": "q15_pinned_hover.png"}, "증거")
    # ④ 알림 패널 위에 놓기: 합류하지 않고 MDI 이동만
    notif2 = next((p for p in lst() if p.get("id") == "NotificationPane"), None)
    d4 = ids[1]
    lk4 = r.call("ui.find", id="chrome.btnLock", doc=d4) or {}
    if notif2 and notif2.get("bounds") and lk4.get("bounds"):
        nb2 = notif2["bounds"]
        base_tabs = len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])
        drag(lk4["bounds"]["x"] - 120, lk4["bounds"]["y"] + lk4["bounds"]["h"] / 2, nb2["x"] + nb2["w"] / 2, nb2["y"] + nb2["h"] / 2, shot="q15_d4_notify.png", steps=14)
        now_tabs = len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])
        nt = len((r.call("panel.tabs", id=notif2["id"]) or {}).get("tabs") or [])
        rec("Q15 ④ 알림 패널 위에 놓기는 합류 안 함", "LeftPane 탭 수 불변, 알림 패널 탭 합류 없음", {"left_tabs": [base_tabs, now_tabs], "notify_tabs": nt}, "PASS" if now_tabs == base_tabs and nt <= 1 else "FAIL")
    # ⑤ 그리드 셀 드래그 선택 중 패널 위에서 놓기: 합류 안 함
    ids5 = [x["id"] for x in r.call("docs.open", kind="grid", count=1)]
    time.sleep(1)
    g5 = r.call("ui.find", id="grid.dgMain", doc=ids5[0]) or {}
    lb5 = next((p for p in lst() if p.get("id") == eid), {}).get("bounds") or lb
    if g5.get("bounds"):
        gb = g5["bounds"]
        t_before = len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])
        drag(gb["x"] + 80, gb["y"] + 60, lb5.get("x", 0) + lb5.get("w", 200) / 2, lb5.get("y", 0) + lb5.get("h", 300) / 2 + 30, shot="q15_d5_cell.png", steps=14)
        t_after = len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])
        rec("Q15 ⑤ 그리드 셀 끌기 선택 후 패널 위에서 놓기는 합류 안 함(오탐 방지)", "탭 수 불변", [t_before, t_after], "PASS" if t_before == t_after else "FAIL")
    # (b) 로그 탭 → LeftPane 안쪽
    logp = next((p for p in lst() if p.get("id") == "BottomPane" or "로그" in str(p.get("title"))), None)
    if logp and logp.get("bounds"):
        lgb = logp["bounds"]
        cd2 = r.call("panel.canDrop", source="Log", target=eid, inner=True)
        left_b = (next((p for p in lst() if p.get("id") == eid), {}).get("bounds") or lb)
        tx2, ty2 = left_b.get("x", 0) + left_b.get("w", 200) / 2, left_b.get("y", 0) + left_b.get("h", 300) / 2 + 30
        n2 = len((r.call("panel.tabs", id=eid) or {}).get("tabs") or [])  # 끌기 직전 실제 탭 수
        cands = find_text_nodes(r, "로그")  # BottomPane 안의 '로그' 글자 위치(위→아래)
        cands = [c for c in cands if lgb["x"] - 5 <= c["bounds"]["x"] <= lgb["x"] + lgb["w"] and lgb["y"] - 5 <= c["bounds"]["y"] <= lgb["y"] + lgb["h"]]
        rec("Q15(b) BottomPane 안 '로그' 글자 노드", "① 바깥 머리줄, ② 안쪽 탭 머리(더 아래)", [(c.get("type"), c["bounds"]) for c in cands], "증거" if cands else "미검증(노드 못 찾음)")
        order = ([("②안쪽 탭 머리", cands[-1])] if len(cands) >= 2 else []) + ([("①바깥 머리줄", cands[0])] if cands else [])
        if not order:
            order = [("추정 +34px", {"bounds": {"x": lgb["x"], "y": lgb["y"] + 34, "w": 60, "h": 18}})]
        for lab, cnd in order:
            bx = cnd["bounds"]
            drag(bx["x"] + min(20, bx["w"] / 2), bx["y"] + bx["h"] / 2, tx2, ty2, shot="q15_b_dragging_%s.png" % ("inner" if lab.startswith("②") else "outer" if lab.startswith("①") else "guess"))
            after_b = r.call("panel.tabs", id=eid) or {}
            nn = len(after_b.get("tabs") or [])
            cd3 = r.call("panel.canDrop", source="Log", target=eid, inner=True)
            rec("Q15(b) 시작점 %s" % lab, "②는 탭 +1, ①은 '탭 영역 전체 끌기'(기록용)", {"tabs": [n2, nn], "node": cnd.get("type"), "canDrop": cd3}, "PASS" if ((lab.startswith("②") or lab.startswith("추정")) and nn == n2 + 1) else ("증거" if lab.startswith("①") else "FAIL"))
            n2 = nn
            page.screenshot(path=os.path.join(OUT, "q15_b_after_%s.png" % ("inner" if lab.startswith("②") else "outer" if lab.startswith("①") else "guess")))
            if lab.startswith("②") and nn == n1 + 1:
                break
        pass
        shot(page, os.path.join(OUT, "q15_b_after.png"))
    else:
        after_b = after_a
        rec("Q15(b) 로그 패널", "BottomPane/로그 존재", None, "미검증")
    # 탭 전환: 실클릭(탭 헤더는 AutomationId 없음 → 텍스트 위치 대신 원격 activate 와 비교)
    tabs = [x.get("id") for x in (after_b.get("tabs") or [])]
    if len(tabs) >= 2:
        r.call("panel.activate", id=eid, tab=tabs[0])
        a0 = (r.call("panel.tabs", id=eid) or {}).get("active")
        r.call("panel.activate", id=eid, tab=tabs[-1])
        a1 = (r.call("panel.tabs", id=eid) or {}).get("active")
        rec("Q15(b) 탭 전환(원격)", "지정한 탭이 활성", {"first": [tabs[0], a0], "last": [tabs[-1], a1]}, "PASS" if a0 == tabs[0] and a1 == tabs[-1] else "FAIL")
        shot(page, os.path.join(OUT, "q15_tab_switched.png"))
    # UI-09R 핀 회귀: LeftPane 머리줄 핀(오른쪽 끝) 실클릭 → 자동 숨김 전환
    lbn = next((p for p in lst() if p.get("id") == eid), {}).get("bounds") or lb
    shot(page, os.path.join(OUT, "ui09r_pin_before.png"))
    page.mouse.click(lbn["x"] + lbn["w"] - 17, lbn["y"] + 12)
    time.sleep(1.2)
    shot(page, os.path.join(OUT, "ui09r_pin_after.png"))
    rec("UI-09R 핀 회귀(LeftPane 머리줄 핀 실클릭)", "고정↔자동숨김 전환(스크린샷 판독)", "ui09r_pin_before.png / ui09r_pin_after.png", "증거")
    # 저장·복원
    r.call("layout.save")
    page.close()
    ctx2, p2, r2 = t.boot(b, "?load-layout", ctx=ctx)
    time.sleep(3)
    back = r2.call("panel.tabs", id=eid) or {}
    rec("Q15(e) 레이아웃 저장·복원에 탭 구성 포함", tabs, [x.get("id") for x in (back.get("tabs") or [])], "PASS" if [x.get("id") for x in (back.get("tabs") or [])] == tabs else "FAIL")
    ctx.close()


def find_text_nodes(r, text):
    """tree.get 에서 text(또는 content) 가 정확히 text 인 노드의 type·bounds 를 위→아래 순으로 돌려준다."""
    tr = r.call("tree.get", window=0, named=True, depth=50) or {}
    out = []

    def walk(n):
        if isinstance(n, dict):
            v = n.get("text") or n.get("content") or n.get("header")
            if isinstance(v, str) and v.strip() == text and n.get("bounds") and n.get("visible", True):
                out.append({"type": n.get("type"), "bounds": n["bounds"]})
            for c in n.get("children") or []:
                walk(c)
        elif isinstance(n, list):
            for c in n:
                walk(c)

    walk(tr.get("tree", tr) if isinstance(tr, dict) else tr)
    return sorted(out, key=lambda c: c["bounds"]["y"])


def collect_texts(r, win=0):
    """tree.get 의 모든 노드에서 text/name/title 류 문자열을 모은다(화면 전체 텍스트)."""
    tr = r.call("tree.get", window=win, named=True, depth=50) or {}
    out = []

    def walk(n):
        if isinstance(n, dict):
            for k in ("text", "title", "header", "tooltip", "placeholder", "content"):
                v = n.get(k)
                if isinstance(v, str) and v.strip():
                    out.append((n.get("id") or n.get("name") or n.get("type"), v))
            for c in n.get("children") or []:
                walk(c)
        elif isinstance(n, list):
            for c in n:
                walk(c)

    walk(tr.get("tree", tr) if isinstance(tr, dict) else tr)
    return out


def part_lang(b):
    """Q17 다국어 — ko↔en 실시간 전환, 한글 잔존·키 노출·영어 넘침을 검사한다. 명령은 lang.set 또는 settings.set language= 중 있는 쪽을 쓴다."""
    import re
    ctx, page, r = t.boot(b)
    r.call("docs.open", kind="grid", count=1)
    r.call("docs.open", kind="gantt", count=1)
    r.call("docs.open", kind="form", count=1)
    time.sleep(2)
    hangul = re.compile(r"[가-힣ㄱ-ㆎ]")
    keylike = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$|^\{[A-Za-z0-9_.]+\}$|^\[[A-Za-z0-9_.]+\]$")

    def set_lang(code):
        res = r.call("lang.set", code=code)
        if res is None:
            res = r.call("settings.set", language=code)
        time.sleep(1.5)
        return res

    ko = collect_texts(r)
    if len(ko) <= 10:
        rec("Q17 ko 기준 화면 텍스트 수집", "한글 텍스트 10개 초과", len(ko), "FAIL(수집 실패 — 이후 검사 무효)")
        ctx.close()
        return
    rec("Q17 ko 기준 화면 텍스트 수집", "한글 텍스트 다수", len(ko), "PASS" if len(ko) > 10 and any(hangul.search(v) for _, v in ko) else "FAIL")
    shot(page, os.path.join(OUT, "q17_ko.png"))
    res = set_lang("en")
    if res is None:
        rec("Q17 언어 설정 명령", "lang.set 또는 settings.set language", "둘 다 실패", "미검증(명령 미구현)")
        ctx.close()
        return
    time.sleep(3)
    lg = r.call("lang.get") or {}
    rec("Q17 lang.set 응답·3초 뒤 lang.get", "language=en, culture=en-US", {"set_resp": {"language": (res or {}).get("language"), "culture": (res or {}).get("culture")}, "get": {"language": lg.get("language"), "culture": lg.get("culture")}},
        "PASS" if (res or {}).get("language") == "en" and lg.get("language") == "en" and lg.get("culture") == "en-US" else "FAIL")
    t0 = time.time()
    en = collect_texts(r)
    shot(page, os.path.join(OUT, "q17_en.png"))
    left = [(i, v) for i, v in en if hangul.search(v)]
    keys = [(i, v) for i, v in en if keylike.match(v.strip())]
    rec("Q17 en 전환 후 한글 잔존", "0건(사용자 데이터 제외)", {"count": len(left), "sample": left[:15]}, "PASS" if not left else "FAIL")
    rec("Q17 en 전환 후 키 문자열 노출", "0건", {"count": len(keys), "sample": keys[:15]}, "PASS" if not keys else "FAIL")
    changed = sum(1 for (a, x), (c, y) in zip(ko, en) if x != y)
    rec("Q17 실시간 전환(재기동·새로고침 없이 텍스트 변경)", "변경된 문자열 다수", {"changed": changed, "total": len(ko), "same_doc_count": len(r.call("docs.list") or [])}, "PASS" if changed > 10 else "FAIL")
    miss = r.call("i18n.missing", lang="en")
    rec("Q17 i18n.missing(en)", "빈 목록", miss, "PASS" if miss == [] or (isinstance(miss, dict) and not miss.get("missing")) else ("미검증(명령 없음)" if miss is None else "FAIL"))
    scan = r.call("i18n.scan", window=0)
    cnt = scan.get("count") if isinstance(scan, dict) else (len(scan) if isinstance(scan, list) else None)
    rec("Q17 i18n.scan 고정 문자열 후보(en 전환 후)", "0건", {"language": (scan or {}).get("language") if isinstance(scan, dict) else None, "count": cnt, "sample": (scan or {}).get("hits", [])[:8] if isinstance(scan, dict) else scan},
        "PASS" if cnt == 0 else ("미검증(명령 없음)" if scan is None else "FAIL"))
    # 영어 넘침: 모든 문서·메뉴 영역의 가로 스크롤/잘림은 스크린샷으로 판독 + 컨트롤 bounds 가 부모 폭을 넘는지
    page.screenshot(path=os.path.join(OUT, "q17_en_390.png"), clip=None)
    ctx390 = b.new_context(viewport={"width": 390, "height": 844})
    p390 = ctx390.new_page()
    p390.goto(t.URL + "?open=grid:1")
    p390.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
    r390 = rb_remote(p390)
    set_lang_390 = r390.call("lang.set", code="en") or r390.call("settings.set", language="en")
    time.sleep(1.5)
    shot(p390, os.path.join(OUT, "q17_en_390.png"))
    rec("Q17 영어 390px 넘침 캡처", "잘림·겹침 없음(스크린샷 판독)", "q17_en.png / q17_en_390.png", "증거")
    ctx390.close()
    # ko 복귀
    set_lang("ko")
    back = collect_texts(r)
    same = [v for _, v in back] == [v for _, v in ko]
    rec("Q17 ko 복귀 시 원문 동일", "원래 텍스트 복원", {"same": same, "diff": [(a, b2) for (_, a), (_, b2) in zip(ko, back) if a != b2][:10]}, "PASS" if same else "FAIL")
    # 저장 유지: 새 탭 재로드 후 언어 유지
    set_lang("en")
    page.close()
    ctx2, p2, r2 = t.boot(b, "", ctx=ctx)
    cur = (r2.call("settings.get") or {}).get("language")
    rec("Q17 언어 설정 저장 유지(새 탭)", "en", cur, "PASS" if cur == "en" else "FAIL")
    ctx.close()


def rb_remote(page):
    r = t.rb.WebRemote(page)
    for _ in range(60):
        if r.call("status") is not None:
            break
        time.sleep(1)
    return r


PERF_ARGS = [] if os.environ.get("PERF_DEFAULT") else ["--use-gl=angle", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"]


def part_perf(b):
    """웹 PERF-UI short — odev-3 권고: --use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader, 대기는 wait_for_timeout, 결과는 __pdPoc.logs 의 PERF_JSON. 렌더 백엔드를 반드시 기록한다."""
    args = PERF_ARGS
    if True:
        br = b
        ctx = br.new_context(viewport={"width": 1280, "height": 800})
        pg = ctx.new_page()
        perf_lines = []
        prog = []
        pg.on("console", lambda m: (perf_lines.append(m.text) if m.text.startswith("PERF_JSON:") else (prog.append(m.text[:120]) if "[perf-ui]" in m.text else None)))
        backend = pg.evaluate("""() => { try { const c = document.createElement('canvas'); const g = c.getContext('webgl2', {failIfMajorPerformanceCaveat: true}); if (!g) return 'webgl2 실패(canvas2d 대체)'; const e = g.getExtension('WEBGL_debug_renderer_info'); return e ? g.getParameter(e.UNMASKED_RENDERER_WEBGL) : 'webgl2 OK(렌더러 정보 없음)'; } catch (x) { return '오류 ' + x; } }""")
        print("render_backend:", backend, flush=True)
        pg.goto(t.URL + "?cmd=perf-ui&profile=short&perf-out=perf_short.json")
        t0 = time.time()
        js = None
        wait = int(os.environ.get("PERF_WAIT", "900"))
        n_logs = 0
        while time.time() - t0 < wait:
            pg.wait_for_timeout(5000)
            n_logs = len(prog)
            js = perf_lines[0] if perf_lines else None
            if js:
                break
        end = time.time()
        if not js:
            rec("PERF-UI short(웹)", "완주", {"waited_sec": round(end - t0), "perf_ui_console_lines": n_logs, "last": (prog[-1] if prog else None), "render_backend": backend}, "미완료")
        else:
            d = json.loads(js[len("PERF_JSON:"):])
            with open(os.path.join(OUT, "perf_ui_web_short.json"), "w", encoding="utf-8") as f:
                json.dump({"render_backend": backend, "args": args, "perf": d}, f, ensure_ascii=False, indent=1)
            ops = [o for o in (d.get("operations") or []) if str(o.get("name", "")).startswith("perfui.")]
            rec("PERF-UI short(웹)", "완주·p50/p95 표", {"profile": d.get("perfui_profile"), "ops": len(ops), "sec": round(end - t0), "render_backend": backend}, "참고치(소프트웨어 렌더)")
            for o in ops:
                print("   %-36s n=%s frame p50=%s p95=%s" % (o["name"], o.get("count"), o.get("frame_p50_ms"), o.get("frame_p95_ms")))
        res.append({"perf_window": [t0, end]})
        ctx.close()


def main():
    only = set()
    if "--only" in sys.argv:
        only = set(sys.argv[sys.argv.index("--only") + 1].split(","))
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        for name, fn in (("sdi", part_sdi), ("mru", part_mru), ("lockdrag", part_lockdrag), ("panel", part_panel), ("lang", part_lang), ("perf", part_perf)):
            if only and name not in only:
                continue
            print("== %s 시작 %s" % (name, time.strftime("%H:%M:%S")), flush=True)
            try:
                fn(p.chromium.launch(headless=True, args=PERF_ARGS) if name == "perf" else b)
            except Exception as ex:
                rec("부분 %s 예외" % name, "예외 없음", repr(ex), "FAIL")
            print("== %s 종료 %s" % (name, time.strftime("%H:%M:%S")), flush=True)
        with open(os.path.join(OUT, "retest_web5.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()

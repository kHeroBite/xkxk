#!/usr/bin/env python3
# 웹 창 탭 합류(W-23, oplan §7 항목3 ①~⑧) 측정 — headless Chrome 실마우스로 MDI 창·관리형 SDI 창을 끌어 아이콘 칸·탭 줄 머무름 합류, 겹침·짧은 머무름 비합류, 탭 끌어내기·묶음 해제, 저장 복원(Version 5·옛 4), 끄는 동안 강조를 잰다
# 제목 탭 닫기(×) 표시 방식(01 §2-1, TitleTabCloseMode — hover·키보드 포커스·가운데 클릭·터치 Auto) 케이스도 같은 제목 탭 시험으로 여기서 잰다(--only tabclose)
"""사용: python3 -B w23_tab_join_web.py --bundle <진단판 wwwroot> --out <결과 폴더> [--port 5826] [--only join|tabclose] | --self-test
입력은 Playwright 실마우스(headless Chrome 안 — 사용자 PC 입력·화면과 무관). 실마우스로 합류가 안 되면 진단 명령 window.tabJoin(L0)으로 판정하고
결과 JSON 의 paths 에 'L0' 로 남긴다(실포인터 실패 사실 기록). 기준: 머무름 250ms·끌기 8 DIP(TabJoinRules, 정본 01 §4.4·04 W-23). 종료 코드 0=통과, 1=실패."""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

CHECKS, PATHS = [], {}
HOLD_OK, HOLD_SHORT = 0.45, 0.0  # 250ms 넘김 / 도착 즉시 놓기
HEAD = 16  # 제목줄 가운데(제목줄 32)


def check(name, ok, detail=None):
    CHECKS.append({"name": name, "pass": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(detail, ensure_ascii=False)[:260], flush=True)


def walk(node, out):
    if isinstance(node, dict):
        out.append(node)
    for c in node if isinstance(node, list) else (node.get("children") or []) if isinstance(node, dict) else []:
        walk(c, out)
    return out


def inside(b, box, tol=1):
    return box["x"] - tol <= b["x"] and box["y"] - tol <= b["y"] and b["x"] + b["w"] <= box["x"] + box["w"] + tol and b["y"] + b["h"] <= box["y"] + box["h"] + tol


def center(r, dx=0, dy=0):
    return (r["x"] + r["w"] / 2 + dx, r["y"] + r["h"] / 2 + dy)


def shift(r, ox, oy):
    return {"x": r["x"] + ox, "y": r["y"] + oy, "w": r["w"], "h": r["h"]}


def legacy_v4(text):
    """레이아웃 JSON 을 옛 Version 4 저장본으로 바꾼다(Version=4, Groups 제거). 키 대소문자는 직렬화 정책과 무관하게 맞춘다."""
    d = json.loads(text)
    for k in list(d):
        if k.lower() == "version":
            d[k] = 4
        elif k.lower() == "groups":
            del d[k]
    return json.dumps(d, ensure_ascii=False)


def drag(w, start, end, hold, probe=None):
    """실마우스 끌기 — 누름→16단계 이동→hold 초 머무름(1px 떨림으로 이동 이벤트 유지)→probe(놓기 전 상태 읽기)→놓기."""
    m = w.page.mouse
    m.move(*start)
    m.down()
    for i in range(1, 17):
        m.move(start[0] + (end[0] - start[0]) * i / 16, start[1] + (end[1] - start[1]) * i / 16)
        time.sleep(0.02)
    t0 = time.time()
    while time.time() - t0 < hold:
        m.move(end[0] + 1, end[1])
        time.sleep(0.1)
        m.move(*end)
    seen = probe() if probe else None
    m.up()
    time.sleep(1.0)
    return seen


def nodes(w):
    return walk((w.res("tree.get", named=False, depth=120) or {}).get("tree"), [])


def armed(w):  # 끄는 동안 강조 — 트리에서 pdTabJoinTarget·pdTabJoinArmed 클래스가 붙은 노드 수
    cls = [c for n in nodes(w) for c in (n.get("classes") or [])]
    return {"target": cls.count("pdTabJoinTarget"), "armed": cls.count("pdTabJoinArmed")}


def pstate(w):
    return w.res("placement.state") or {}


def mdi_page(w, doc_or_group):  # MDI 창(문서 또는 묶음)의 페이지 사각형 — area 원점 + MDI 좌표
    s = pstate(w)
    a = s.get("area") or {}
    return next((shift(i["bounds"], a.get("x", 0), a.get("y", 0)) for i in (s.get("docs") or []) + (s.get("groups") or [])
                 if i.get("id") == doc_or_group and i.get("bounds")), None)


def groups(w):
    return pstate(w).get("groups") or []


def l0(w, item, source, target, kind):
    PATHS[item] = "L0"
    print("  ⚠️ 실마우스 합류 실패 → L0 window.tabJoin 판정 (%s)" % item, flush=True)
    return w.res("window.tabJoin", source=source, target=target, kind=kind)


def open_mdi(w, n):
    w.res("docs.close", all=True)
    for _ in range(n):
        w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.2)
    ids = [d["id"] for d in w.res("docs.list")]
    for i, d in enumerate(ids):  # 겹치지 않게 펼친다(아이콘 칸이 다른 창에 가리지 않게)
        w.res("placement.markPlaced", doc=d, x=40 + i * 330, y=40 + i * 150, w=300, h=220)
    time.sleep(0.8)
    return ids


def mdi_cases(w):
    a, b, c = open_mdi(w, 3)
    ra, rb = mdi_page(w, a), mdi_page(w, b)
    drag(w, center(ra, 0, -ra["h"] / 2 + HEAD), center(rb, 0, 20), HOLD_OK)  # ① 겹치기만
    check("① MDI 겹쳐 놓기 → 합류 0", not groups(w), groups(w))
    a, b, c = open_mdi(w, 3)
    icon = next((t for t in (w.res("window.tabJoinTargets", source=a) or {}).get("mdi") or [] if t["id"] == b + "#icon"), None)
    ar, rb = pstate(w).get("area") or {}, mdi_page(w, b)
    check("표적 B 아이콘 칸이 B 창 안", bool(icon) and inside(shift(icon["rect"], ar["x"], ar["y"]), rb), {"icon": icon, "b": rb})
    if not icon:
        return
    ip, ra = center(shift(icon["rect"], ar["x"], ar["y"])), mdi_page(w, a)
    drag(w, (ra["x"] + 60, ra["y"] + HEAD), ip, HOLD_SHORT)  # ④ 250ms 미만
    check("④ 머무름 250ms 미만 → 합류 0", not groups(w), groups(w))
    w.res("placement.markPlaced", doc=a, x=40, y=40, w=300, h=220)
    time.sleep(0.8)
    ra = mdi_page(w, a)
    seen = drag(w, (ra["x"] + 60, ra["y"] + HEAD), ip, HOLD_OK, probe=lambda: (armed(w), w.shot("mdi_join_hold")))
    check("⑧ MDI 끄는 동안 표적 강조(pdTabJoinTarget)", seen and seen[0]["target"] >= 1, seen and seen[0])
    g = groups(w)
    PATHS["②"] = "real"
    if not g:
        l0(w, "②", a, b, "icon")
        g = groups(w)
    check("② 아이콘 칸 250ms 머문 뒤 놓기 → 탭 2개 묶음·끌던 문서 활성", len(g) == 1 and sorted(g[0]["docs"]) == sorted([a, b]) and g[0]["active"] == a, g)
    if not g:
        return
    gid = g[0]["id"]
    strip = next((t for t in (w.res("window.tabJoinTargets", source=c) or {}).get("mdi") or [] if t["kind"] == "strip"), None)
    rc = mdi_page(w, c)
    PATHS["③"] = "real"
    if strip and rc:
        drag(w, (rc["x"] + 60, rc["y"] + HEAD), center(shift(strip["rect"], ar["x"], ar["y"])), HOLD_OK)
    g = groups(w)
    if not g or len(g[0]["docs"]) != 3:
        l0(w, "③", c, gid, "strip")
        g = groups(w)
    check("③ 묶음 탭 줄 위 놓기 → 탭 3개", len(g) == 1 and len(g[0]["docs"]) == 3 and g[0]["active"] == c, {"strip": strip, "groups": g})
    detach(w, g[0] if g else None)


def detach(w, g):
    """⑤ 3개 묶음에서 탭을 끌어내 2개 → 한 번 더 끌어내 1개 남으면 묶음 해제. 끌어낸 문서는 MDI 창(SDI 창 수 불변)."""
    sdi0 = len(sdi_windows(w))
    for left in (2, 0):
        box = mdi_page(w, g["id"]) if g else None
        items = sorted((n["bounds"] for n in nodes(w) if n.get("type") == "DocumentTabStripItem" and n.get("effectivelyVisible")
                        and (n.get("bounds") or {}).get("h", 0) > 0 and inside(n["bounds"], box)), key=lambda r: r["x"]) if box else []
        if not items:
            check("⑤ 묶음 창 안 탭 찾기", False, box)
            return
        ar = pstate(w)["area"]  # 끝점은 문서 칸 안이어야 MDI 창이 된다(칸 밖이면 SDI 로 뜸)
        drag(w, center(items[-1]), (min(box["x"] + box["w"] + 150, ar["x"] + ar["w"] - 60), min(box["y"] + box["h"] + 60, ar["y"] + ar["h"] - 60)), 0.2)
        now, docs, sdi = groups(w), w.res("docs.list") or [], len(sdi_windows(w))
        ok = (len(now) == 1 and len(now[0]["docs"]) == 2) if left == 2 else not now
        check("⑤ 탭 끌어내기 → MDI 창%s" % (" (묶음 2개 남음)" if left == 2 else ", 1개 남으면 묶음 해제"),
              ok and len(docs) == 3 and sdi == sdi0, {"groups": now, "docs": len(docs), "sdiWindows": [sdi0, sdi]})
        g = now[0] if now else None


def reload(w):  # 문서를 모두 닫고 자동 저장 슬롯에서 복원한다
    w.res("docs.close", all=True)
    time.sleep(0.8)
    w.res("layout.load")
    time.sleep(1.5)


def save_restore(w):
    a, b, c = open_mdi(w, 3)
    w.res("window.tabJoin", source=a, target=b, kind="icon")  # ⑦ 은 저장 형식 판정이라 묶음은 L0 로 만든다
    before = groups(w)
    w.res("layout.save")
    lay = w.res("layout.get") or {}
    ver = next((v for k, v in lay.items() if k.lower() == "version"), None)
    gs = next((v for k, v in lay.items() if k.lower() == "groups"), None)
    check("⑦ 저장본 Version 6 이상·Groups 1건", isinstance(ver, int) and ver >= 6 and isinstance(gs, list) and len(gs) == 1, {"version": ver, "groups": gs})
    reload(w)
    after = groups(w)
    check("⑦ 복원 왕복 → 묶음·탭 순서·활성 같음", len(after) == 1 and before and after[0]["docs"] == before[0]["docs"] and after[0]["active"] == before[0]["active"],
          {"before": before, "after": after})
    # ?fresh 기동의 자동 저장 슬롯은 실행 중 메모리다(FreshGuardLayoutStore) → localStorage 에 레이아웃 키가 없다.
    # 그래서 현재 레이아웃(layout.get)을 옛 Version 4 로 바꿔 격리 키(?layout-key)의 localStorage 슬롯에 넣고, ?fresh 없이 그 키로 다시 기동해 기동 복원을 본다.
    key = "pd-poc-layout-w23v4"
    old = legacy_v4(json.dumps(w.res("layout.get") or {}, ensure_ascii=False))
    w.page.evaluate("([k, v]) => localStorage.setItem(k, v)", [key, old])
    w.page.goto("http://127.0.0.1:%d/?layout-key=%s" % (w.port, key), wait_until="load", timeout=90000)
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    docs = []
    for _ in range(40):  # 기동 복원이 끝나길 기다린다
        docs = w.res("docs.list") or []
        if len(docs) == 3:
            break
        time.sleep(0.5)
    check("⑦ 옛 Version 4 저장본 → 문서 3개 유실 없음·묶음 없음", len(docs) == 3 and not groups(w), {"key": key, "docs": len(docs), "groups": groups(w)})
    w.page.evaluate("k => localStorage.removeItem(k)", key)


def sdi_windows(w):
    return [x for x in w.res("windows.list") or [] if not x.get("main")]


def sdi_bounds(w, *docs):  # 문서마다 그 문서를 담은 관리형 SDI 창의 페이지 사각형(없으면 None)
    wins = sdi_windows(w)
    return [next((x["bounds"] for x in wins if d in (x.get("docs") or [])), None) for d in docs]


def sdi_cases(w):
    w.res("docs.close", all=True)
    for k in ("grid", "gantt"):
        w.res("docs.open", kind=k)
    time.sleep(1.2)
    a, b = [d["id"] for d in w.res("docs.list")][:2]
    for d in (a, b):
        w.res("docs.sdi", doc=d, on=True)
        time.sleep(1.2)
    ab, bb = sdi_bounds(w, a, b)
    if not (ab and bb):
        check("⑥ 관리형 SDI 창 2개 준비", False, sdi_windows(w))
        return
    title = lambda r: (r["x"] + 200, r["y"] + HEAD)  # noqa: E731 — 제목줄 왼쪽에서 200(창이 뷰포트 오른쪽으로 나가도 잡히게)
    drag(w, title(bb), (title(bb)[0] + 180, title(bb)[1] + 140), 0.2)  # 떨어뜨려 놓기(아이콘 칸이 가리지 않게)
    ab, bb = sdi_bounds(w, a, b)
    drag(w, title(bb), center(ab, 0, 40), HOLD_OK)  # 겹치기만
    check("⑥ SDI 겹쳐 놓기 → 창 2개 유지", len(sdi_windows(w)) == 2, [x.get("docs") for x in sdi_windows(w)])
    ab, bb = sdi_bounds(w, a, b)
    surf = next((s for s in (w.res("window.tabJoinTargets") or {}).get("sdi") or [] if a in (s.get("docs") or [])), None)
    icon = next((t["rect"] for t in (surf or {}).get("targets") or [] if t["kind"] == "icon" and t.get("rect")), None)
    PATHS["⑥"] = "real"
    seen = drag(w, title(bb), center(shift(icon, ab["x"], ab["y"])), HOLD_OK, probe=lambda: (armed(w), w.shot("sdi_join_hold"))) if icon else None
    check("⑧ SDI 끄는 동안 표적 강조(pdTabJoinTarget)", seen and seen[0]["target"] >= 1, {"icon": icon, "seen": seen and seen[0]})
    if len(sdi_windows(w)) != 1:
        l0(w, "⑥", b, a, "icon")
    wins = sdi_windows(w)
    active = next((d["id"] for d in w.res("docs.list") or [] if d.get("active")), None)
    check("⑥ SDI 로고 머문 뒤 놓기 → 대상 창 탭 합류·원천 창 닫힘·끌던 문서 활성",
          len(wins) == 1 and sorted(wins[0].get("docs") or []) == sorted([a, b]) and active == b, {"windows": [x.get("docs") for x in wins], "active": active})
    w.res("docs.redock", all=True)
    time.sleep(1)


def tab_state(w, **a):  # 제목 탭 × 표시 상태(원격 ui.tabClose — 01 §2-1, AC-108~110)
    return w.res("ui.tabClose", **a) or {}


def tab_of(st, doc):
    return next((t for t in st.get("tabs") or [] if t.get("id") == doc), None)


def mouse_away(w):  # 포인터를 탭 줄 밖(문서 칸 가운데 아래)으로 옮겨 hover 를 푼다
    w.page.mouse.move(700, 850)
    time.sleep(0.4)


def same_x(a, b, tol=0.5):
    return a is not None and b is not None and abs(a - b) <= tol


def close_at_right(tab, close, tol=1):
    """× 가 탭 오른쪽 끝 35% 안에 있는가 — 탭 항목(shell.tabItem.*)과 ×(shell.tabClose.*)의 실제 ui.find 사각형(같은 좌표계)으로 잰다.
    탭 왼쪽 끝을 제목 글자 x 로 추정하지 않는다(제목 칸 앞에 아이콘 칸이 생기면 추정이 틀어진다)."""
    if not tab or not close:
        return False
    return (close["x"] - tab["x"] >= tab["w"] * 0.65 - tol
            and close["x"] + close["w"] <= tab["x"] + tab["w"] + tol)


CLOSE_MODE_CLASSES = ("closeHover", "closeActive")  # ×표시 방식 클래스(MainView.TabClose.cs) — 없음=항상. tabsTitle·busyInline 등 다른 목록 클래스는 무관


def tab_close_cases(w):
    """제목 탭 닫기(×) 표시(TitleTabCloseMode). hover 는 headless Chrome 실마우스(page.mouse.move = page.hover 와 같은 포인터 이벤트),
    키보드는 실키(Tab·방향키), 닫기는 실마우스 클릭·가운데 클릭. 판정 값은 원격 ui.tabClose(Opacity·OpacityMask·제목 글자 x)."""
    PATHS["tabClose"] = "real-mouse+ui.tabClose"
    w.res("docs.close", all=True)
    for _ in range(3):
        w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.2)
    ids = [d["id"] for d in w.res("docs.list") or []]
    st = tab_state(w, mode="Auto")
    check("C 데스크톱 웹 Auto → Hover(마우스 기본)", st.get("effective") == "Hover" and st.get("touch") is False and "closeHover" in (st.get("stripClasses") or []),
          {k: st.get(k) for k in ("mode", "effective", "touch", "stripClasses")})
    if len(ids) < 3 or len(st.get("tabs") or []) < 3:
        check("C 제목 탭 3개 준비", False, {"docs": ids, "tabs": st.get("tabs")})
        return
    a, b, c = ids[:3]
    st = tab_state(w, mode="Hover")
    mouse_away(w)
    hidden = tab_state(w)
    sel = next((t["id"] for t in hidden["tabs"] if t.get("selected")), None)
    check("C Hover: 포인터 밖이면 × 숨김(Opacity 0)·흐림 없음(선택 탭 포함)",
          all(t.get("closeOpacity") == 0 and not t.get("fade") for t in hidden["tabs"]), [(t["id"], t.get("closeOpacity"), t.get("fade"), t.get("selected")) for t in hidden["tabs"]])
    tgt = next(x for x in (a, b, c) if x != sel)  # 비선택 탭
    th = tab_of(hidden, tgt)
    pc = w.center("shell.tabItem." + tgt)
    if not pc:
        check("C 탭 항목 id(shell.tabItem.*) 조회", False, tgt)
        return
    w.page.mouse.move(*pc)
    time.sleep(0.4)
    shown = tab_state(w)
    ts = tab_of(shown, tgt)
    others = [t for t in shown["tabs"] if t["id"] != tgt]
    check("C Hover: 올린 탭만 × 보임·제목 끝 흐림", ts and ts.get("closeOpacity") == 1 and ts.get("fade") and all(t.get("closeOpacity") == 0 for t in others),
          {"hovered": ts, "others": [(t["id"], t.get("closeOpacity")) for t in others]})
    check("C × 표시 전후 제목 글자 x·폭 같음(밀림 없음, 숨김 때 × 자리까지 제목 칸)",
          th and ts and same_x(th.get("titleX"), ts.get("titleX")) and same_x(th.get("titleWidth"), ts.get("titleWidth")),
          {"before": th and [th.get("titleX"), th.get("titleWidth")], "after": ts and [ts.get("titleX"), ts.get("titleWidth")]})
    tab_b = (w.res("ui.find", id="shell.tabItem." + tgt) or {}).get("bounds")
    close_b = (w.res("ui.find", id="shell.tabClose." + tgt) or {}).get("bounds")
    check("C × 는 탭 오른쪽 끝(탭 폭 35% 이내)에 겹침",
          ts and ts.get("closeX") is not None and ts.get("titleX") is not None
          and ts["closeX"] >= ts["titleX"] and close_at_right(tab_b, close_b),
          ts and {k: ts.get(k) for k in ("titleX", "titleWidth", "closeX", "tabWidth")} | {"tabRect": tab_b, "closeRect": close_b})
    w.shot("tabclose_hover")
    # 실마우스로 × 클릭 → 그 탭만 닫힘
    cc = w.center("shell.tabClose." + tgt)
    if cc:
        w.page.mouse.move(*cc)
        time.sleep(0.3)
        w.page.mouse.click(*cc)
        time.sleep(0.8)
    left = [d["id"] for d in w.res("docs.list") or []]
    check("C Hover: 올려서 보인 × 클릭 → 그 탭 닫힘", cc and tgt not in left and len(left) == 2, {"closed": tgt, "left": left})
    # 가운데 클릭 닫기
    w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.0)
    ids = [d["id"] for d in w.res("docs.list") or []]
    mid = ids[-1]
    pm = w.center("shell.tabItem." + mid)
    if pm:
        w.page.mouse.click(*pm, button="middle")
        time.sleep(0.8)
    left = [d["id"] for d in w.res("docs.list") or []]
    check("C 가운데 클릭 → 그 탭 닫힘", pm and mid not in left and len(left) == len(ids) - 1, {"closed": mid, "left": left})
    # 키보드: 탭을 실클릭(포인터 포커스 — × 남지 않아야 함) → 방향키(키보드 포커스) → × 보임 → Tab 으로 × 도달 → Enter 닫기
    ids = left
    first = w.center("shell.tabItem." + ids[0])
    w.page.mouse.click(*first)
    time.sleep(0.5)
    mouse_away(w)
    pst = tab_of(tab_state(w), ids[0])
    check("C 마우스 클릭 포커스 뒤 포인터가 떠나면 × 숨김(키보드 포커스만 보임)", pst and pst.get("closeOpacity") == 0 and not pst.get("kbfocus"), pst)
    # 실키 Tab 순환으로 제목 탭 목록에 들어온다(목록은 Tab 한 번에 들어오고 나가는 구조 — 들어오면 선택 탭 항목이 키보드 포커스를 받는다).
    # Enter 는 다른 컨트롤(UI 형 스위치 등)을 누르지 않게 × 에 포커스가 있을 때만 누른다.
    kt, kst, trail = None, {}, []
    for _ in range(14):
        w.page.keyboard.press("Tab")
        time.sleep(0.25)
        kst = tab_state(w)
        trail.append(kst.get("focused"))
        kt = next((t for t in kst.get("tabs") or [] if t.get("kbfocus")), None)
        if kt:
            break
    PATHS["tabCloseKbFocus"] = "real-key Tab" if kt else "L0 ui.tabClose focus=shell.tabItem"
    if not kt:
        kst = tab_state(w, focus="shell.tabItem." + ids[0])
        kt = next((t for t in kst.get("tabs") or [] if t.get("kbfocus")), None)
    check("C 키보드 포커스가 탭에 오면 × 보임·흐림(01 §2-1)", kt and kt.get("closeOpacity") == 1 and kt.get("fade"),
          {"focused": kst.get("focused"), "kb": kt, "path": PATHS["tabCloseKbFocus"], "trail": trail})
    # × 단추: 목록이 Tab 한 번에 빠져나가 실키 Tab 으로는 × 에 머물지 않는다(기존 구조) → Tab 방식 포커스(L0)로 × 를 잡고 실키 Enter 로 닫는다
    PATHS["tabCloseKeyboard"] = "L0 focus=shell.tabClose + real-key Enter"
    reached = "shell.tabClose." + (kt or {"id": ids[0]})["id"]
    fst = tab_state(w, focus=reached)
    ft = tab_of(fst, reached.split(".")[-1])
    check("C × 키보드 포커스 → × 보임", fst.get("focused") == reached and ft and ft.get("closeOpacity") == 1, {"focused": fst.get("focused"), "tab": ft, "path": PATHS["tabCloseKeyboard"]})
    n0 = len(w.res("docs.list") or [])
    if fst.get("focused") == reached:
        w.page.keyboard.press("Enter")
        time.sleep(0.8)
    n1 = len(w.res("docs.list") or [])
    check("C × 포커스 Enter → 닫힘", fst.get("focused") == reached and n1 == n0 - 1, {"before": n0, "after": n1, "path": PATHS["tabCloseKeyboard"]})
    sty = w.res("ui.style") or {}
    if str(sty.get("effective")).lower() != "pc":  # 키보드 순환 중 UI 형이 바뀌었으면 원복(뒤 케이스는 PC형 제목 탭 기준)
        check("C 키보드 순환 뒤 UI 형 PC 유지", False, sty)
        w.res("ui.style", set="pc")
        time.sleep(2)
    # ActiveAndHover·Always
    for _ in range(2):
        w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.0)
    tab_state(w, mode="ActiveAndHover")
    mouse_away(w)
    st = tab_state(w)
    check("C ActiveAndHover: 선택 탭만 × 항상, 나머지 숨김",
          all((t.get("closeOpacity") == 1) == bool(t.get("selected")) for t in st["tabs"]) and any(t.get("selected") for t in st["tabs"]),
          [(t["id"], t.get("selected"), t.get("closeOpacity")) for t in st["tabs"]])
    tab_state(w, mode="Always")
    st = tab_state(w)
    check("C Always: 모든 탭 × 보임(실행 중 변경 즉시 반영)", all(t.get("closeOpacity") == 1 and t.get("fade") for t in st["tabs"])
          and not any(c in CLOSE_MODE_CLASSES for c in (st.get("stripClasses") or [])),
          {"tabs": [(t["id"], t.get("closeOpacity")) for t in st["tabs"]], "stripClasses": st.get("stripClasses")})
    tab_state(w, mode="Auto")


def chrome_tab_cases(w):
    """문서 창 크롬 단추(chrome.btn*) Tab 도달(이월 3, AC-202 웹 축). 실키 Tab 만 쓴다(L0 포커스 대체 없음 — 도달 못 하면 FAIL).
    도달한 토글은 Space 로 눌러 IsChecked 가 바뀌는지 본다. 2px 포커스 링은 포커스-비저블 상태를 원격으로 읽을 수 없어 데스크톱 캡처 축이 맡는다."""
    w.res("docs.close", all=True)
    w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.2)
    w.page.mouse.click(700, 850)  # 문서 본문이 아닌 곳을 눌러 시작 포커스를 흩뜨린다
    trail, hit = [], None
    for _ in range(40):
        w.page.keyboard.press("Tab")
        time.sleep(0.2)
        f = tab_state(w).get("focused")
        trail.append(f)
        if f and str(f).startswith("chrome.btn"):
            hit = f
            break
    check("C 문서 창 크롬 단추에 실키 Tab 도달", hit is not None, {"hit": hit, "trail": trail})
    toggles = ("chrome.btnSdi", "chrome.btnPin", "chrome.btnLock", "chrome.btnSync", "chrome.btnDup")
    if hit in toggles:
        before = (w.res("ui.find", id=hit, props="IsChecked") or {}).get("props", {}).get("IsChecked")
        w.page.keyboard.press("Space")
        time.sleep(0.6)
        after = (w.res("ui.find", id=hit, props="IsChecked") or {}).get("props", {}).get("IsChecked")
        check("C 포커스된 크롬 토글 Space → 상태 전환", before is not None and before != after, {"id": hit, "before": before, "after": after})
        w.page.keyboard.press("Space")  # 원복
        time.sleep(0.4)


def tab_close_touch(w):
    """--form-factor touch(InputFormFactor.HostTouch 재정의)로 다시 열어 Auto → Always 확인. Fold5 실기는 AC-207 몫."""
    w.page.goto("http://127.0.0.1:%d/?fresh&form-factor=touch" % w.port, wait_until="load", timeout=90000)
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    for _ in range(60):
        if w.call("status").get("ok"):
            break
        time.sleep(1)
    sty = w.res("ui.style") or {}  # 터치 넓은 화면은 태블릿형(DOCK)이라 제목 탭이 없다 — PC형으로 바꿔 제목 탭을 띄운다
    if str(sty.get("effective")).lower() != "pc":
        w.res("ui.style", set="pc")
        time.sleep(2)
    w.res("docs.close", all=True)
    for _ in range(2):
        w.res("docs.open", kind="grid", allowDuplicate=True)
    time.sleep(1.2)
    st = tab_state(w, mode="Auto")
    tabs = st.get("tabs") or []
    check("C 터치(--form-factor touch) Auto → Always, 모든 탭 × 보임",
          st.get("touch") is True and st.get("effective") == "Always" and tabs and all(t.get("closeOpacity") == 1 for t in tabs),
          {k: st.get(k) for k in ("touch", "effective", "stripClasses")} | {"tabs": [(t["id"], t.get("closeOpacity")) for t in tabs]})
    w.shot("tabclose_touch")


def self_test():
    box = {"x": 10, "y": 10, "w": 10, "h": 10}
    cases = [("legacy_v4 대문자", json.loads(legacy_v4('{"Version":5,"Groups":[{"Id":"g"}],"Documents":[1]}')) == {"Version": 4, "Documents": [1]}),
             ("legacy_v4 소문자", json.loads(legacy_v4('{"version":5,"groups":null,"documents":[]}')) == {"version": 4, "documents": []}),
             ("inside 경계 허용 1", inside({"x": 9, "y": 10, "w": 10, "h": 10}, box)), ("inside 밖", not inside({"x": 0, "y": 0, "w": 50, "h": 5}, box)),
             ("shift·center", center(shift({"x": 0, "y": 0, "w": 20, "h": 20}, 5, 7)) == (15, 17)),
             # B09 실측값(탭 폭 81, × x 63·폭 16, 탭 왼쪽 0) — 제목 앞 아이콘 칸으로 titleX=25 여도 통과
             ("close_at_right 오른쪽 끝", close_at_right({"x": 0, "y": 0, "w": 81, "h": 24}, {"x": 63, "y": 4, "w": 16, "h": 16})),
             ("close_at_right 탭 원점 이동", close_at_right({"x": 100, "y": 0, "w": 81, "h": 24}, {"x": 163, "y": 4, "w": 16, "h": 16})),
             ("close_at_right 가운데면 실패", not close_at_right({"x": 0, "y": 0, "w": 81, "h": 24}, {"x": 30, "y": 4, "w": 16, "h": 16})),
             ("close_at_right 탭 밖이면 실패", not close_at_right({"x": 0, "y": 0, "w": 81, "h": 24}, {"x": 80, "y": 4, "w": 16, "h": 16})),
             ("close_at_right 사각형 없음 실패", not close_at_right(None, {"x": 63, "y": 4, "w": 16, "h": 16})),
             ("Always 클래스 busyInline 허용", not any(c in CLOSE_MODE_CLASSES for c in ["tabsTitle", "busyInline"])),
             ("Always 클래스 closeHover 남으면 실패", any(c in CLOSE_MODE_CLASSES for c in ["closeHover", "busyInline"]))]
    for n, ok in cases:
        print(("PASS " if ok else "FAIL ") + n)
    return 0 if all(ok for _, ok in cases) else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle")
    ap.add_argument("--out")
    ap.add_argument("--port", type=int, default=5826)
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--only", choices=["join", "tabclose", "chrometab"], help="join=탭 합류 ①~⑧만, tabclose=제목 탭 × 표시(C)만, chrometab=크롬 단추 Tab 도달(AC-202)만. 생략=전부")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not (a.bundle and a.out):
        ap.error("--bundle 과 --out 이 필요하다")
    from playwright.sync_api import sync_playwright
    from ui28_web import Web
    os.makedirs(a.out, exist_ok=True)
    errors = ["브라우저 열기 실패"]
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.out)
        try:
            w.open(p)
            w.page.set_viewport_size({"width": 1400, "height": 900})
            time.sleep(2)
            steps = {"join": (mdi_cases, save_restore, sdi_cases), "tabclose": (tab_close_cases, chrome_tab_cases, tab_close_touch), "chrometab": (chrome_tab_cases,)}
            for step in steps[a.only] if a.only else steps["join"] + steps["tabclose"]:  # 터치 재기동은 맨 끝(다른 묶음은 데스크톱 폼팩터)
                try:
                    step(w)
                except Exception as e:  # noqa: BLE001 — 한 묶음이 죽어도 나머지 항목은 잰다
                    check("예외 " + step.__name__, False, str(e)[:200])
            errors = w.errors[:20]
        finally:
            w.close()
    check("X 콘솔 오류 0건", not errors, errors)
    with open(os.path.join(a.out, "w23_tab_join.json"), "w", encoding="utf-8") as f:
        json.dump({"checks": CHECKS, "paths": PATHS}, f, ensure_ascii=False, indent=1)
    failed = [c for c in CHECKS if not c["pass"]]
    print("결과 %d/%d  경로 %s" % (len(CHECKS) - len(failed), len(CHECKS), PATHS))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

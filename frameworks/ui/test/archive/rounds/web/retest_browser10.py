# 웹 관리형 SDI 창 위치 초기화(Q6 #7) 보강 시험 — 창을 기본 위치가 아닌 곳으로 옮기고 bounds 로 이동을 확인한 뒤 sdi.resetPositions 로 초기화해 전·후를 대조한다
# 사용: PD_URL=http://127.0.0.1:5185/ python3 tools/retest_browser10.py   (웹 지속성 격리는 ?layout-key, ?fresh 는 쓰지 않음)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:480]), flush=True)


IDS = {}


def managed(r):
    """창 위치 대리 지표: 각 SDI 문서 창 제목줄 버튼(chrome.btnLock)의 bounds 를 {제목: bounds} 로 돌려준다.
    (windows.list 의 ManagedWindow bounds 는 두 창 모두 관리형 창 컨테이너 전체(0,114,1280,649)로 나와 개별 창 위치를 구별하지 못한다 — 2026-10-29 03:53 실측)"""
    out = {}
    for title, did in IDS.items():
        n = r.call("ui.find", id="chrome.btnLock", doc=did) or {}
        out[title] = n.get("bounds")
    return out


def snap(r):
    s = r.call("window.snapshot") or {}
    return [(x.get("title") or x.get("id"), x.get("x"), x.get("y"), x.get("w"), x.get("h")) for x in s.get("sdi", [])]


from retest_browser5 import find_text_nodes  # noqa: E402


# ⑦ 웹 기대값(lead 확정, 2026-10-29 09:4x — 표시 위치는 Dock ClassicMdiLayoutManager.ClampBounds 를 거친 값):
# 축마다 w≤W−xf → x=xf(크기 유지) / W−xf<w≤W → x=W−w(크기 유지) / w>W → 크기를 W−xf 로 줄이고 x=xf. y·h 도 같다.
# 좌상단은 서로 달라야 함(같으면 FAIL). MdiBounds(모델, chrome.state mdiBounds)와 표시 위치(inContainer)가 같아야 함.
# xf·yf = SdiResetLayout 공식 위치(레이어 (40,40) → +30 계단), W·H = 관리형 창 레이어 크기(inContainer 좌표계).
def axis_exp(f, s, L):
    if s <= L - f:
        return f, s
    if s <= L:
        return L - s, s
    return f, L - f


def clamp_exp(xf, yf, w, h, W, H):
    """→ ((x, y), (w, h)) 기대 좌상단과 기대 크기."""
    (x, ew), (y, eh) = axis_exp(xf, w, W), axis_exp(yf, h, H)
    return (x, y), (ew, eh)


def models_by_title(r):
    """관리형 SDI 문서의 Dock 창 모델 {제목: docs.list dockWindow{x,y,w,h,state,host}}(odev-6r 추가 키, i0944 이후 판).
    키가 없는 판이면 값이 None — 대조는 '판정불가(읽기 명령 없음)'. chrome.state mdiBounds 는 SDI 에서 재결합용 MDI 자리라 쓰지 않는다(odev-6r)."""
    out = {}
    for d in (r.call("docs.list") or []):
        if d.get("managedBounds"):
            out[d.get("title")] = d.get("dockWindow")
    return out


def near(a, b, tol=1.0):
    return a is not None and b is not None and abs(a[0] - b[0]) <= tol and abs(a[1] - b[1]) <= tol


def layer_wh(r):
    """관리형 창 레이어 크기(W,H). 1순위 = 트리의 MdiLayoutPanel bounds 직접 실측, 2순위 = SDI 창 최대화 inContainer w·h(둘 다 기록).
    PD_LAYER_W/H 환경변수가 있으면 그 값."""
    if os.environ.get("PD_LAYER_W") and os.environ.get("PD_LAYER_H"):
        return (float(os.environ["PD_LAYER_W"]), float(os.environ["PD_LAYER_H"])), "env"
    from retest_browser11 import layer_from_tree
    r.call("docs.close", all=True)
    did0 = r.call("docs.open", kind="form", title="LAYER-TREE", sdi=True, allowDuplicate=True)[0]["id"]  # 관리형 창 레이어가 생기도록 SDI 1개
    time.sleep(1.0)
    tw, tinfo = layer_from_tree(r)
    r.call("docs.close", doc=did0)
    mx, msrc = _layer_wh_max(r)
    rec("⑦ 레이어 W·H 두 방식 대조", "트리 MdiLayoutPanel bounds(1순위) vs 최대화 inContainer(2순위)", {"tree": tw, "treeInfo": tinfo, "maximized": mx, "maximizedSource": msrc, "diff": (round(tw[0] - mx[0], 1), round(tw[1] - mx[1], 1)) if tw else None}, "기록")
    if tw:
        return (float(tw[0]), float(tw[1])), "tree-MdiLayoutPanel"
    return mx, msrc


def _layer_wh_max(r):
    r.call("docs.close", all=True)
    did = r.call("docs.open", kind="form", title="LAYER-PROBE", sdi=True, allowDuplicate=True)[0]["id"]
    time.sleep(1.0)
    r.call("docs.state", doc=did, state="maximized")
    time.sleep(0.8)
    mb = next((d.get("managedBounds") for d in (r.call("docs.list") or []) if d.get("id") == did), None)
    ic = (mb or {}).get("inContainer") or {}
    r.call("docs.close", all=True)
    time.sleep(0.6)
    if ic.get("w") and ic.get("h"):
        return (ic["w"], ic["h"]), "maximized-probe"
    return (1280.0, 649.0), "fallback(03:53 실측 컨테이너 — 추정값)"


def judge_clamp(post_by_title, sizes_by_title, W, H, origin=40, gap=30, models=None):
    """제목 순서로 xf=origin+gap·i 공식 위치에 ClampBounds 규칙을 적용한 기대(좌상단·크기)와 실측(inContainer)을 비교.
    models({제목: mdiBounds})가 있으면 모델과 표시 사각형이 ±1 로 같아야 한다. 좌상단이 겹치면 FAIL."""
    rows = []
    ok = True
    for i, k in enumerate(sorted(post_by_title)):
        ic = post_by_title[k]
        act = (ic.get("x"), ic.get("y"))
        act_wh = (ic.get("w"), ic.get("h"))
        w, h = sizes_by_title.get(k, act_wh)
        exp, exp_wh = clamp_exp(origin + gap * i, origin + gap * i, w, h, W, H)
        hit = near(act, exp) and near(act_wh, exp_wh)
        row = {"title": k, "inputWH": (w, h), "expect": exp, "expectWH": exp_wh, "actual": act, "actualWH": act_wh}
        if models is not None:
            m = models.get(k)
            if m is None:
                # dockWindow 키가 없는 판(i0944 이하): 기록만, FAIL 로 세지 않음(lead 09:5x 결정)
                row.update(dockWindow=None, modelCheck="판정불가(읽기 명령 없음)")
            else:
                mrect = (m.get("x"), m.get("y"), m.get("w"), m.get("h"))
                msame = None not in mrect and near(mrect[:2], act) and near(mrect[2:], act_wh)
                row.update(dockWindow=m, modelEqualsDisplay=msame, modelCheck="PASS" if msame else "FAIL")
                hit = hit and msame
        row["match"] = hit
        ok = ok and hit
        rows.append(row)
    tops = [r_["actual"] for r_ in rows]
    distinct = len(set(tops)) == len(tops)
    return ok and distinct, distinct, rows


def part_z_and_bounds(page, r):
    """odev-6r 수정 판(SdiZ.cs 341d6870 이후) 확인: ① windows.list ManagedWindow bounds 가 창마다 다르고 창 이동에 따라 변하는지 ② docs.list SDI zIndex 가 활성화 순서대로 바뀌는지(zSource=visual) ③ 초기화 전후 창 크기(개별 bounds w·h) 불변."""
    (LW, LH), lsrc = layer_wh(r)
    rec("⑦ 레이어 크기 실측(W,H)", "SDI 최대화 시 inContainer w·h", {"W": LW, "H": LH, "source": lsrc}, "PASS" if lsrc != "fallback(03:53 실측 컨테이너 — 추정값)" else "판정불가(추정값 사용)")
    r.call("docs.close", all=True)
    ids = []
    for i in (1, 2):
        ids.append(r.call("docs.open", kind="form", title="Z-SDI%d" % i, sdi=True, allowDuplicate=True)[0]["id"])
        time.sleep(1.0)
    time.sleep(1.5)
    wl = [w for w in (r.call("windows.list") or []) if w.get("managed")]
    bs = [w.get("bounds") for w in wl]
    full = all(b == {"x": 0, "y": 114, "w": 1280, "h": 649} for b in bs) and bool(bs)
    srcs = [(b or {}).get("boundsSource") for b in bs]
    rec("Q12·Q6 #7 개별 창 bounds 노출(windows.list ManagedWindow)", "창마다 다른 사각형, boundsSource=managed-window(컨테이너 전체가 아님)", {"bounds": bs, "boundsSource": srcs}, "FAIL(여전히 컨테이너 전체 — 구판)" if full else ("PASS" if len(bs) == 2 and bs[0] != bs[1] and all(x == "managed-window" for x in srcs) else "판정불가(boundsSource 없음 또는 동일)"))
    # 창을 서로 다른 크기로: 두 번째 창을 리사이즈 손잡이로 키운 뒤 한 창을 옮기고, 초기화 전후 개별 w·h 와 x·y 를 비교(Q6 #7 확정 판정)
    def wl_bounds():
        return {w.get("title"): w.get("bounds") for w in (r.call("windows.list") or []) if w.get("managed")}
    g = r.call("ui.find", id="chrome.gripResize", doc=ids[1]) or {}
    gb = g.get("bounds")
    if gb:
        sx, sy = gb["x"] + gb["w"] - 3, gb["y"] + gb["h"] - 3
        page.mouse.move(sx, sy)
        page.mouse.down()
        for i in range(1, 11):
            page.mouse.move(sx + 120 * i / 10, sy + 80 * i / 10)
            time.sleep(0.03)
        page.mouse.up()
        time.sleep(1.0)
    pre = wl_bounds()
    a = r.call("sdi.resetPositions") or {}
    time.sleep(1.0)
    post = wl_bounds()
    sizes_equal = bool(pre) and all(pre.get(k) and post.get(k) and pre[k]["w"] == post[k]["w"] and pre[k]["h"] == post[k]["h"] for k in pre)
    sz_differ = len({(b["w"], b["h"]) for b in pre.values() if b}) == 2
    icp = {k: (v or {}).get("inContainer") or {} for k, v in post.items()}
    szp = {k: (((pre.get(k) or {}).get("inContainer") or {}).get("w"), ((pre.get(k) or {}).get("inContainer") or {}).get("h")) for k in post}
    okc, dist, rows = judge_clamp(icp, szp, LW, LH, models=models_by_title(r)) if all(icp.values()) and len(icp) == 2 else (False, False, [])
    rec("Q6 #7 ⑦ 위치 공식(웹 ClampBounds 규칙, xf=(40,40)/(70,70))", "축마다 w≤W−xf→xf / W−xf<w≤W→W−w / w>W→크기 W−xf·위치 xf, ±1, 모델(mdiBounds)=표시, 좌상단 서로 다름(같으면 FAIL)", {"layer": (LW, LH), "rows": rows, "resp": {k: a.get(k) for k in ("status", "positioning", "positionUnchanged")}},
        "PASS" if okc else ("FAIL(좌상단 겹침)" if rows and not dist else ("FAIL" if rows else "판정불가(inContainer 없음)")))
    rec("Q6 #7 ⑦ 초기화 전후 창 크기 불변(문서 '크기는 그대로')", "각 창 w·h 불변(서로 다른 크기로 시작)", {"pre": pre, "post": post, "sizes_started_different": sz_differ}, "PASS" if sizes_equal and sz_differ else ("FAIL(크기가 바뀜 — 문서와 구현 불일치: 640×420 으로 설정)" if sz_differ and not sizes_equal else "판정불가(크기 키우기 실패)"))

    def zinfo():
        out = {}
        for d in (r.call("docs.list") or []):
            if d.get("id") in ids:
                out[d["id"]] = (d.get("zIndex"), d.get("zSource"), d.get("zReason"), d.get("managedBounds"))
        return out
    z0 = zinfo()
    r.call("docs.activate", doc=ids[0])
    time.sleep(0.6)
    za = zinfo()
    r.call("docs.activate", doc=ids[1])
    time.sleep(0.6)
    zb = zinfo()
    top_a = max(za, key=lambda k: (za[k][0] if za[k][0] is not None else -1))
    top_b = max(zb, key=lambda k: (zb[k][0] if zb[k][0] is not None else -1))
    rec("Q12 웹 SDI zIndex: 활성화 순서대로 최상단이 바뀜", "활성 문서가 최상단(zSource=visual)", {"initial": z0, "after_act1": za, "after_act2": zb, "top": [top_a, top_b]}, "PASS" if top_a == ids[0] and top_b == ids[1] and all(v[0] is not None for v in za.values()) else "FAIL")
    # 크기 불변: 한 창을 리사이즈 손잡이로 키운 뒤 초기화, managedBounds 의 w·h 비교
    mb0 = {k: v[3] for k, v in zb.items()}
    return mb0


def part_odev2_procedure(b):
    """odev-2 ⑦ 절차(SdiResetLayout 공식, 레이어 좌표): 기본 (40,40) → 겹치면 +30,+30 → 100×100 이상 안 보이면 레이어 가운데 → 크기 불변 → 위치 고정 창 제외. 다음 웹 판부터 유효."""
    def wl(r):
        return {w.get("title"): w.get("bounds") for w in (r.call("windows.list") or []) if w.get("managed")}

    def inc(bd):
        return (bd or {}).get("inContainer") or {}

    def open_sdi(r, n, tag):
        r.call("docs.close", all=True)
        ids = []
        for i in range(1, n + 1):
            ids.append(r.call("docs.open", kind="form", title="%s%d" % (tag, i), sdi=True, allowDuplicate=True)[0]["id"])
            time.sleep(0.6)
        time.sleep(1.0)
        return ids

    ctx, page, r = t.boot(b, "?layout-key=otestw2-odev2")
    (LW, LH), lsrc = layer_wh(r)
    rec("⑦-0 레이어 크기 실측(W,H)", "SDI 최대화 시 inContainer w·h", {"W": LW, "H": LH, "source": lsrc}, "PASS" if lsrc != "fallback(03:53 실측 컨테이너 — 추정값)" else "판정불가(추정값 사용)")
    # 1)+2) 크기 불변 + 공식 위치
    ids = open_sdi(r, 2, "F-")
    g = (r.call("ui.find", id="chrome.gripResize", doc=ids[0]) or {}).get("bounds")
    if g:
        sx, sy = g["x"] + g["w"] - 3, g["y"] + g["h"] - 3
        page.mouse.move(sx, sy)
        page.mouse.down()
        for i in range(1, 11):
            page.mouse.move(sx + 260 * i / 10, sy + 180 * i / 10)
            time.sleep(0.03)
        page.mouse.up()
        time.sleep(1.0)
    pre = wl(r)
    r.call("sdi.resetPositions")
    time.sleep(1.0)
    post = wl(r)
    same = bool(pre) and all(pre.get(k) and post.get(k) and pre[k]["w"] == post[k]["w"] and pre[k]["h"] == post[k]["h"] for k in pre)
    started_diff = len({(v["w"], v["h"]) for v in pre.values() if v}) == 2
    rec("⑦-1 크기 불변(1번 창을 키운 뒤 초기화)", "두 창 w·h 가 초기화 전과 같음(예전 결함: 640×420 으로 줄어듦)", {"pre": {k: (v["w"], v["h"]) for k, v in pre.items() if v}, "post": {k: (v["w"], v["h"]) for k, v in post.items() if v}}, "PASS" if same and started_diff else ("FAIL" if started_diff else "판정불가(크기 키우기 실패)"))
    ok2, dist2, rows2 = judge_clamp({k: inc(v) for k, v in post.items()}, {k: (inc(v).get("w"), inc(v).get("h")) for k, v in pre.items()}, LW, LH, models=models_by_title(r))
    rec("⑦-2 위치 공식(웹 ClampBounds 규칙): 1번 xf=(40,40), 2번 xf=(70,70)", "축별 규칙 기대(좌상단·크기) ±1, 모델(mdiBounds)=표시, 좌상단 서로 다름(같으면 FAIL) — i0451 실측 (40,0)·(70,53) 사례", {"layer": (LW, LH), "rows": rows2}, "PASS" if ok2 else ("FAIL(좌상단 겹침)" if not dist2 else "FAIL"))
    # 3) 위치 고정 제외: 1번을 (40,40)에 둔 채 고정 → 2번 (70,70); 1번을 다른 곳에 고정 → 2번 (40,40)
    page.mouse.move(0, 0)
    n1 = r.call("chrome.lock", doc=ids[0])
    r.call("sdi.resetPositions")
    time.sleep(1.0)
    p3 = wl(r)
    keys = sorted(p3)
    a1 = (inc(post[keys[0]]).get("x"), inc(post[keys[0]]).get("y"))  # 고정 직전 1번 위치(⑦-2 결과)
    # 2번 공식 시작점: (40,40) 에서 시작해 고정된 1번 좌상단과 같을 때만 +30(SdiResetLayout.AvoidSame) — 1번이 (40,0) 으로 당겨져 있으면 2번은 (40,40)
    f2 = (40, 40)
    while near(f2, a1, 0.5):
        f2 = (f2[0] + 30, f2[1] + 30)
    e2, e2wh = clamp_exp(f2[0], f2[1], inc(post[keys[1]]).get("w"), inc(post[keys[1]]).get("h"), LW, LH)  # 입력 크기 = 고정 직전 2번 크기
    g1 = (inc(p3[keys[0]]).get("x"), inc(p3[keys[0]]).get("y"))
    g2 = (inc(p3[keys[1]]).get("x"), inc(p3[keys[1]]).get("y"))
    g2wh = (inc(p3[keys[1]]).get("w"), inc(p3[keys[1]]).get("h"))
    m3 = models_by_title(r)
    m3has = all(m3.get(k) for k in keys)
    m3ok = m3has and all(near((m3[k].get("x"), m3[k].get("y")), (inc(p3[k]).get("x"), inc(p3[k]).get("y"))) and near((m3[k].get("w"), m3[k].get("h")), (inc(p3[k]).get("w"), inc(p3[k]).get("h"))) for k in keys)
    rec("⑦-3a 고정한 1번은 그대로, 2번은 공식 시작점(고정 창과 같을 때만 +30) 규칙", "1번 불변(±1), 2번 = %s 크기 %s(±1), 모델=표시, 좌상단 서로 다름" % (e2, e2wh), {"w1_before": a1, "w1_after": g1, "w2_after": g2, "w2_wh": g2wh, "w2_expect": e2, "w2_expect_wh": e2wh, "dockWindow": m3, "modelCheck": ("PASS" if m3ok else "FAIL") if m3has else "판정불가(읽기 명령 없음)"},
        "PASS" if near(g1, a1) and near(g2, e2) and near(g2wh, e2wh) and g1 != g2 and (m3ok or not m3has) else "FAIL")
    r.call("chrome.lock", doc=ids[0])  # 고정 해제
    # ⑦-3b: 1번을 레이어 좌표 (300,300) 근처로 옮겨 고정하면 2번은 (40,40) 이어야 한다(겹치는 위치만 비켜 놓는 Mars 규칙의 직접 확인)
    # 앞 단계에서 1번 창을 키워 레이어 높이(649)와 같아졌으므로, 기본 크기 창 2개로 새로 시작한다.
    ids = open_sdi(r, 2, "B-")
    r.call("sdi.resetPositions")
    time.sleep(1.0)
    cur = wl(r)
    k1 = sorted(cur)[0]
    ic = inc(cur[k1])
    lk = (r.call("ui.find", id="chrome.btnLock", doc=ids[0]) or {}).get("bounds")
    if lk and ic:
        dx, dy = 300 - ic.get("x", 0), 300 - ic.get("y", 0)
        sx, sy = lk["x"] - 150, lk["y"] + lk["h"] / 2
        page.mouse.move(sx, sy)
        page.mouse.down()
        for i in range(1, 15):
            page.mouse.move(sx + dx * i / 14, sy + dy * i / 14)
            time.sleep(0.03)
        page.mouse.up()
        time.sleep(1.0)
    moved_pos = wl(r)
    p1 = inc(moved_pos[k1])
    r.call("chrome.lock", doc=ids[0])  # 고정
    r.call("sdi.resetPositions")
    time.sleep(1.0)
    p3b = wl(r)
    k2 = sorted(p3b)[1]
    rec("⑦-3b 1번을 (300,300) 근처에 고정 → 1번 불변, 2번 (40,40) (겹칠 때만 비켜 놓기)", "1번 고정 위치 유지(±3px), 2번 (40,40)",
        {"moved_before_lock": (p1.get("x"), p1.get("y")), "w1_after": (inc(p3b[k1]).get("x"), inc(p3b[k1]).get("y")), "w2_after": (inc(p3b[k2]).get("x"), inc(p3b[k2]).get("y"))},
        "PASS" if abs(p1.get("x", -99) - 300) <= 40 and abs(inc(p3b[k1]).get("x", -99) - p1.get("x", -99)) <= 3 and abs(inc(p3b[k1]).get("y", -99) - p1.get("y", -99)) <= 3 and (inc(p3b[k2]).get("x"), inc(p3b[k2]).get("y")) == (40, 40) else ("판정불가(1번을 (300,300) 근처로 옮기지 못함)" if abs(p1.get("x", -99) - 300) > 40 else "FAIL(무조건 계단식 구현 의심)"))
    r.call("chrome.lock", doc=ids[0])  # 고정 해제
    # 4) 화면 밖 보정: 작은 뷰포트에서 SDI 8개
    ctx2 = t.allow_dup(b.new_context(viewport={"width": 800, "height": 600}))  # 웹 단일 탭 잠금 해제(odev-3)
    pg2 = ctx2.new_page()
    pg2.goto(t.URL + "?layout-key=otestw2-odev2b")
    pg2.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
    r2 = t.rb.WebRemote(pg2)
    for _ in range(90):
        if r2.call("status") is not None and r2.call("docs.list") is not None:
            break
        time.sleep(1)
    open_sdi(r2, 8, "S-")
    r2.call("sdi.resetPositions")
    time.sleep(1.5)
    p4 = wl(r2)
    lay = r2.call("ui.find", id="shell.dock") or {}
    tops = [(inc(v).get("x"), inc(v).get("y")) for v in p4.values()]
    vis = []
    for v in p4.values():
        ic = inc(v)
        vw = min(ic.get("x", 0) + ic.get("w", 0), 800) - max(ic.get("x", 0), 0)
        vh = min(ic.get("y", 0) + ic.get("h", 0), 600 - 114) - max(ic.get("y", 0), 0)
        vis.append((vw, vh))
    rec("⑦-4 화면 밖 보정(800×600, SDI 8개)", "모든 창이 레이어 안에 100×100 이상 보이고 좌상단이 서로 다름", {"tops": tops, "visible_wh": vis}, "PASS" if len(set(tops)) == len(tops) == 8 and all(w >= 100 and h >= 100 for w, h in vis) else "FAIL")
    ctx2.close()
    # 5) 최대화된 SDI 창은 보통 크기로 돌아와 공식대로
    ids5 = open_sdi(r, 2, "M-")
    r.call("docs.state", doc=ids5[0], state="maximized")
    time.sleep(0.8)
    r.call("sdi.resetPositions")
    time.sleep(1.0)
    st = next((d.get("state") for d in (r.call("docs.list") or []) if d.get("id") == ids5[0]), None)
    p5 = wl(r)
    ok5, dist5, rows5 = judge_clamp({k: inc(v) for k, v in p5.items()}, {k: (inc(v).get("w"), inc(v).get("h")) for k, v in p5.items()}, LW, LH, models=models_by_title(r))
    rec("⑦-5 최대화된 SDI 창은 보통 크기로 돌아와 공식 위치(웹 ClampBounds 규칙)", "state=Normal, xf=(40,40)·(70,70) 규칙 기대 ±1, 모델=표시, 좌상단 서로 다름", {"state": st, "layer": (LW, LH), "rows": rows5}, "PASS" if st == "Normal" and ok5 else "FAIL")
    # 6) 레이어보다 큰 창 2개(lead 확정 기대): w>W·h>H 이므로 크기는 (W−xf, H−yf) 로 줄고 좌상단은 (40,40)·(70,70). 둘 다 (0,0) 이면 FAIL. 모델=표시.
    ids6 = open_sdi(r, 2, "L-")
    req6 = (LW + 200, LH + 150)
    set6 = []
    for d6 in ids6:
        sb = r.call("sdi.setBounds", doc=d6, w=req6[0], h=req6[1]) or {}
        set6.append({"status": sb.get("status"), "model": sb.get("model")})
        time.sleep(0.6)
    pre6 = wl(r)
    pre6_models = models_by_title(r)
    r.call("sdi.resetPositions")
    time.sleep(1.0)
    post6 = wl(r)
    # 레이어보다 크게 만들었는지는 sdi.setBounds 응답의 Dock 창 모델(IDockWindow w·h)로 본다(chrome.state mdiBounds 는 SDI 에서 (0,0,640,420) 고정 — i0944 실측)
    big = len(set6) == 2 and all((s.get("model") or {}).get("w", 0) > LW and (s.get("model") or {}).get("h", 0) > LH for s in set6)
    ok6, dist6, rows6 = judge_clamp({k: inc(v) for k, v in post6.items()}, {k: req6 for k in post6}, LW, LH, models=models_by_title(r))
    tops6 = [row["actual"] for row in rows6]
    both00 = len(tops6) == 2 and all(near(p, (0, 0)) for p in tops6)
    rec("⑦-6 레이어보다 큰 창 2개: 크기=(W−xf,H−yf), 좌상단 (40,40)·(70,70)", "둘 다 (0,0) 이면 FAIL, 기대 좌상단·크기 ±1, 모델(mdiBounds)=표시, 좌상단 서로 다름",
        {"layer": (LW, LH), "requestWH": req6, "setBounds": set6, "pre_display": {k: inc(v) for k, v in pre6.items()}, "pre_models": pre6_models, "rows": rows6},
        "판정불가(레이어보다 크게 만들지 못함 — 모델 크기 기준)" if not big else ("FAIL(둘 다 (0,0))" if both00 else ("PASS" if ok6 else ("FAIL(좌상단 겹침)" if not dist6 else "FAIL"))))
    ctx.close()


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-sdi")
        r.call("docs.close", all=True)
        for i in (1, 2):
            od = r.call("docs.open", kind="form", title="WM-SDI%d" % i, sdi=True, allowDuplicate=True)
            IDS["WM-SDI%d" % i] = od[0]["id"]
            time.sleep(1.0)
        time.sleep(1.5)
        m0 = managed(r)
        s0 = snap(r)
        rec("Q6 #7 사전: 관리형 SDI 2개 기본 위치(제목줄 버튼 bounds)", "2창 bounds", {"btnLock": m0, "windows.list(참고, 컨테이너 전체)": [(w.get("title"), w.get("bounds")) for w in (r.call("windows.list") or []) if w.get("managed")], "snapshot": s0}, "PASS" if len(m0) == 2 and all(m0.values()) else "FAIL(bounds 미노출)")
        # 첫 창을 제목줄로 끌어 기본 위치 밖으로 이동(실마우스)
        tl = [n for n in (r.call("tree.get", window=0, named=False, depth=50) or {}).get("tree", [])]  # 구조 확인용(미사용)
        node = None
        txt = r.call("ui.find", id="WM-SDI1")  # 제목 텍스트는 Id 가 없을 수 있어 아래 text 노드 탐색으로 대체
        lk0 = (r.call("ui.find", id="chrome.btnLock", doc=IDS["WM-SDI1"]) or {}).get("bounds")
        cands = [c for c in find_text_nodes(r, "WM-SDI1") if lk0 and abs((c["bounds"]["y"] + c["bounds"]["h"] / 2) - (lk0["y"] + lk0["h"] / 2)) <= 14]
        if cands:
            bb = cands[0]["bounds"]
            sx, sy = bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2
            page.mouse.move(sx, sy)
            page.mouse.down()
            for i in range(1, 15):
                page.mouse.move(sx + 260 * i / 14, sy + 130 * i / 14)
                time.sleep(0.03)
            page.mouse.up()
            time.sleep(1.0)
        m1 = managed(r)
        s1 = snap(r)
        moved = any(m0.get(k) and m1.get(k) and (abs(m1[k]["x"] - m0[k]["x"]) >= 100 or abs(m1[k]["y"] - m0[k]["y"]) >= 60) for k in m0)
        rec("Q6 #7 사전: 창을 기본 위치 밖으로 이동(bounds 변화 확인)", "한 창의 x 또는 y 가 100/60px 이상 변함", {"before": m0, "after": m1, "snapshot": s1, "title_node": bool(cands)}, "PASS" if moved else "FAIL(이동 못 함 — 이동 수단 확인 필요)")
        page.screenshot(path=os.path.join(OUT, "q6_7_moved.png"))
        a = r.call("sdi.resetPositions") or {}
        time.sleep(1.0)
        m2 = managed(r)
        s2 = snap(r)
        pos2 = [(v["x"], v["y"]) for v in m2.values() if v]  # 제목줄 버튼 좌표(창 위치 대리)
        distinct = len(set(pos2)) == len(pos2) == 2
        left_displaced = m1.get("WM-SDI1") and m2.get("WM-SDI1") and (abs(m2["WM-SDI1"]["x"] - m1["WM-SDI1"]["x"]) >= 100 or abs(m2["WM-SDI1"]["y"] - m1["WM-SDI1"]["y"]) >= 60)
        rec("Q6 #7 sdi.resetPositions: 옮겼던 창이 이동 위치에서 벗어남", "이동했던 위치(803,237 등)에 남아 있지 않음", {"moved": m1.get("WM-SDI1"), "reset": m2.get("WM-SDI1"), "status": a.get("status")}, "PASS" if moved and left_displaced else ("FAIL" if moved else "판정불가(이동 실패)"))
        rec("Q6 #7 초기화 후 창끼리 겹치지 않음(위치 서로 다름)", "위치 2종", {"positions": pos2}, "PASS" if distinct else "FAIL")
        # 결정성: 다른 창(SDI2)을 옮긴 뒤 다시 초기화하면 처음 초기화와 같은 위치가 나와야 한다(기본 위치는 고정값)
        m3 = m2
        moved_any = None
        for title in ("WM-SDI2", "WM-SDI1"):
            lkx = (r.call("ui.find", id="chrome.btnLock", doc=IDS[title]) or {}).get("bounds")
            if not lkx:
                continue
            sx, sy = lkx["x"] - 150, lkx["y"] + lkx["h"] / 2  # 제목줄 버튼 왼쪽 제목 영역
            page.mouse.move(sx, sy)
            page.mouse.down()
            for i in range(1, 15):
                page.mouse.move(sx - 100 * i / 14, sy + 160 * i / 14)
                time.sleep(0.03)
            page.mouse.up()
            time.sleep(1.0)
            m3 = managed(r)
            if m3.get(title) and m2.get(title) and abs(m3[title]["y"] - m2[title]["y"]) >= 60:
                moved_any = title
                break
        m3 = managed(r)
        moved2 = moved_any is not None
        r.call("sdi.resetPositions")
        time.sleep(1.0)
        m4 = managed(r)
        same = all(m4.get(k) and m2.get(k) and abs(m4[k]["x"] - m2[k]["x"]) <= 2 and abs(m4[k]["y"] - m2[k]["y"]) <= 2 for k in m2)
        rec("Q6 #7 기본 위치는 고정값(다른 창을 옮긴 뒤 다시 초기화해도 같은 위치)", "두 번째 초기화 위치 == 첫 초기화 위치(±2px)", {"first": m2, "after_move": m3, "second": m4, "second_window_moved": moved_any}, "PASS" if moved2 and same else ("FAIL" if moved2 else "판정불가(두 번째 이동 실패)"))
        page.screenshot(path=os.path.join(OUT, "q6_7_reset.png"))
        part_z_and_bounds(page, r)
        if os.environ.get("ODEV2_PROC"):
            part_odev2_procedure(b)
        with open(os.path.join(OUT, "retest_web10.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()

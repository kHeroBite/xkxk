# 디자인 테마 축(REQ-104) 시험 케이스 중 전체 보기·페이지 전환·웹 움직임 케이스 — design_theme_cases.py 에서 300줄 규칙으로 분리
import time

from design_theme_cases import Blocked, res, state, select, reset_all, need_no_fallback, set_setting  # noqa: F401  (순환 임포트 — 이 이름들은 이미 정의된 뒤 불린다)

__all__ = ["overview_flat", "overview3d", "hit_test_3d", "prefers_reduced_motion", "page_flip", "overview_nav_flat"]


def overview_flat(b, ctx):
    reset_all(b)
    select(b, "ds-classic")
    b.call("overview.open")
    o = b.call("overview.state") or {}
    n = len(state(b).get("windows") or [])
    b.call("overview.select", index=0)
    ok = o.get("open") and o.get("mode") == "flat" and len(o.get("cards") or []) == n > 0
    return res(bool(ok), {"mode": o.get("mode"), "cards": len(o.get("cards") or []), "windows": n}, "평면 모드, 카드 수 = 창 수")


def open_overview3d(b):
    reset_all(b)
    need_no_fallback(select(b, "ds-depth"))
    for kind in ("grid", "gantt"):
        b.call("docs.open", kind=kind)
        time.sleep(1)
    b.call("overview.open")
    return b.call("overview.state") or {}, state(b)


def overview3d(b, ctx):
    o, s = open_overview3d(b)
    wins = [w.get("id") for w in s.get("windows") or []]
    cards = o.get("cards") or []
    pick = 1 if len(wins) > 1 else 0
    b.call("overview.select", index=pick)
    after = state(b)
    ok = o.get("mode") == "carousel3d" and len(cards) == len(wins) >= 2 and after.get("active") == wins[pick]
    reset_all(b)
    return res(bool(ok), {"mode": o.get("mode"), "cards": len(cards), "windows": len(wins), "선택 창": after.get("active")}, {"mode": "carousel3d", "cards": "= 창 수", "선택 창": wins[pick] if wins else None})


def hit_test_3d(b, ctx):
    o, s = open_overview3d(b)
    cards = o.get("cards") or []
    if len(cards) < 2:
        raise Blocked("카드 2개 미만 — 창을 더 열어야 함")
    # 카드 배치는 활성 창을 가운데에 두므로 고를 때마다 바뀐다. 매번 다시 연 덮개의 state 에서 그 창의 좌표를 읽는다
    # (2026-11-16 K9 수정 — 처음 state 좌표를 재사용해 두 번째 클릭부터 다른 카드를 눌렀다: Grid-4→Grid-3 등)
    targets = [c.get("windowId") for c in cards]
    bad = []
    for n, wid in enumerate(targets):
        cur = next((c for c in ((b.call("overview.state") or {}).get("cards") or []) if c.get("windowId") == wid), None)
        if cur is None:
            bad.append([wid, "state 에 카드 없음"])
            continue
        r = b.call("overview.select", x=cur.get("x"), y=cur.get("y")) or {}
        if r.get("hit") != wid:
            bad.append([wid, r.get("hit")])
        if n < len(targets) - 1:  # 마지막 뒤에는 다시 열지 않는다 — 열린 덮개가 남으면 다음 케이스(overview_flat)가 그 방식(carousel3d)을 읽는다(2026-11-16 K8 연쇄 FAIL)
            b.call("overview.open")
    if (b.call("overview.state") or {}).get("open"):
        b.call("overview.select", index=0)  # 놓친 클릭으로 덮개가 열려 있으면 닫는다
    reset_all(b)
    return res(not bad, {"불일치": bad}, "기울어진 카드 중심 좌표 클릭 → 해당 창")


def prefers_reduced_motion(b, ctx):
    emu = ctx.get("emulate_reduced")
    if not emu:
        raise Blocked("웹 실행기만 prefers-reduced-motion 모의(Emulation.setEmulatedMedia) 제공")
    reset_all(b)
    select(b, "ds-depth")
    set_setting(b, "theme.motion.level", "system")
    emu(True)
    ctx["backend"].restart()
    b = ctx["backend"].b
    select(b, "ds-depth")
    e = state(b)["effective"]
    reduced = e.get("depth.open") == "none" and e.get("depth.overview") == "flat"
    emu(False)
    ctx["backend"].restart()
    b = ctx["backend"].b
    select(b, "ds-depth")
    back = state(b)["effective"].get("depth.open") == "tilt"
    reset_all(b)
    return res(reduced and back, {"reduce 중 depth.open": e.get("depth.open"), "해제 뒤 복원": back}, "prefers-reduced-motion=reduce 면 3D 끔, 해제하면 복원")


def page_flip(b, ctx):
    reset_all(b)
    need_no_fallback(select(b, "ds-depth"))
    p = b.call("depth.probe", kind="page")
    if not isinstance(p, dict) or "samples" not in p:
        raise Blocked("depth.probe kind=page 응답 없음: %r" % (p,))
    tilted = any(abs(float(x.get("angleY", 0))) > 0 for x in p["samples"])
    reset_all(b)
    return res(tilted and p.get("restored") is True, {"기울기 관측": tilted, "복원": p.get("restored")}, "페이지 전환 중 AngleY≠0, 끝 뒤 복원")


def overview_nav_flat(b, ctx):
    reset_all(b)
    select(b, "ds-depth")
    if not (state(b).get("windows") or []):  # 전제 조건: 창이 0개면 전체 보기를 열지 않는다(18 §7) — 문서를 먼저 연다(2026-11-16 K10 cards=0 원인)
        for kind in ("grid", "gantt"):
            b.call("docs.open", kind=kind)
            time.sleep(1)
    b.call("overview.open")
    o = b.call("overview.state") or {}
    b.call("overview.select", index=0)
    reset_all(b)
    return res(o.get("mode") == "flat" and bool(o.get("cards")), {"mode": o.get("mode"), "cards": len(o.get("cards") or [])}, "NAV(폰) 전체 보기는 평면 목록")

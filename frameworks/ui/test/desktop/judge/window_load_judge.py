# AC-018a·b·c·019 판정기 — 창 아이콘 불러오는 중(첫 열림·검색·새로고침·겹침·취소·지연·닫힘·UI 막힘 중 회전)을 판정한다
"""
사용
  window_load_judge.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json>
  window_load_judge.py --bundle <wwwroot> --port <포트> --out <결과.json>      (웹 결과)
  window_load_judge.py --self-test

지연 판정은 ui.shell.busy_icon.slow_seconds=2 로 낮춰 본다. 웹은 새 프로필에 BusyIconSlowSeconds=2 를 심고,
데스크톱은 기동한 쪽이 격리 폴더의 UI 설정에 같은 값을 넣어 두고 --fresh 없이 띄운다(--fresh 는 설정을 읽지 않는다. 2 가 아니면 그 사례만 DEFERRED).
실패 토큰은 실화면 주입점이 없어 앱 안 자기시험(complete_fail_cancel)으로만 보고, 근거를 결과에 남긴다.
UI 막힘 중 회전은 막힘 구간 안에서 시작하고 끝난 캡처 3장이 있을 때만 잰다. 없으면 DEFERRED,
3장에 모양은 있고 차이가 없으면 RECORDED(23 설계 한계 기록 + 최종 보고가 있어야 AC-018c PASS).
"""
import sys
import time

import judge_session as session
from judge_session import box, delta, shape
from window_load_rules import SHAPE_PX, inside, judge_cycle, judge_overlap, judge_rotation, judge_switch_active, self_test

DELAY_MS = 1500
BLOCK_MS = 1400  # AC 는 600 이상 — 0.2초 간격 3장이 막힘 안에 들도록 넉넉히
SLOW_SECONDS = 2


class Scene:
    def __init__(self, s):
        self.s = s
        self.doc = None

    def load(self, delay=0, block=0):
        self.s.call("sample.load", delayMs=delay, blockMs=block)

    def state(self):
        r = self.s.call("window.load.state")
        d = next((x for x in r["docs"] if x["id"] == self.doc), {})
        return dict(d, states=r["states"], pending=r["pendingTimers"], slow_seconds=r["slowSeconds"])

    def icon(self):
        return self.s.call("ui.find", id="PdDocIcon", props="IsBusyShown")

    def shown(self):
        return str(self.icon()["props"]["IsBusyShown"]).lower() == "true"

    def crop(self, name):
        bounds = self.icon()["bounds"]
        shot, scale = self.s.capture_icon(name)  # 스피너는 컴포지션 비주얼이라 데스크톱은 OS 합성 캡처로 찍는다(judge_session)
        return shot.crop(box(bounds, scale))

    def clear(self):
        self.s.call("docs.close", all=True)
        time.sleep(0.8)
        self.doc = None

    def open(self):
        self.s.call("docs.open", kind="grid", count=1)
        self.doc = self.s.call("docs.list")[0]["id"]

    def idle(self, seconds=15):
        until = time.time() + seconds
        while self.state().get("isLoading") or self.shown():
            if time.time() > until:
                raise session.Abort("INVALID", "idle 로 돌아오지 않는다")
            time.sleep(0.15)
        time.sleep(0.4)


def case_cycle(scene, name, start, original):
    scene.load(DELAY_MS)
    start()
    samples, busy, realized = [], None, False
    until = time.time() + DELAY_MS / 1000.0 + 5
    while time.time() < until:
        item = {"loading": bool(scene.state().get("isLoading")), "shown": scene.shown()}
        samples.append(item)
        if item["loading"] and item["shown"] and busy is None:
            busy = scene.crop(name + "_busy")
            realized = bool(scene.s.call("ui.find", id="grid.dgMain", doc=scene.doc).get("visible"))
        if len(samples) > 1 and not item["loading"] and not item["shown"]:
            break
        time.sleep(0.1)
    time.sleep(0.4)
    after = scene.crop(name + "_after")
    out = judge_cycle(samples, delta(after, original), delta(busy, original) if busy else 0, shape(busy) if busy else 0)
    out["view_realized_while_loading"] = realized
    if name == "open" and not realized and out["verdict"] == "PASS":
        out.update(verdict="FAIL", why="뷰가 붙은 동안의 loading 을 보지 못함(뷰 부착만으로 복귀하지 않음 미확인)")
    return out


def case_active_switch(scene):
    """두 창이 모두 불러오는 중일 때 활성을 넘긴다 — 창 제목 줄·제목 탭의 활성·비활성 자리 4곳 모두."""
    scene.load(0)
    scene.clear()
    scene.s.call("docs.open", kind="grid", count=2, allowDuplicate=True)
    time.sleep(1.0)
    scene.s.call("docs.arrange", mode="tile-v")  # 겹치면 전환 뒤 비활성 창 제목 줄이 가려져 본문을 재게 된다
    time.sleep(1.0)
    docs = scene.s.call("docs.list")
    scene.load(12000)
    for d in docs:
        scene.s.call("grid.refresh", doc=d["id"])
    time.sleep(0.6)
    rows, first = [], next(d["id"] for d in docs if not d.get("active"))
    for step in ("전환 전", "전환 뒤"):
        if rows:
            scene.s.call("docs.activate", doc=first)
            time.sleep(0.6)
        found = [(i, scene.s.call("ui.find", id=i, props="IsBusyShown")) for i in ("PdDocIcon", "PdDocIcon#2", "TabIcon", "TabIcon#2")]
        shot, scale = scene.s.capture_icon("switch_active_%d" % len(rows))
        for place, info in found:
            crop = shot.crop(box(info["bounds"], scale))
            key = "%s@%s,%s" % (place.split("#")[0], info["bounds"]["x"], info["bounds"]["y"])
            rows.append((step, key, shape(crop), max(crop.getcolors(crop.width * crop.height))[1]))
    loading = [bool(x["isLoading"]) for x in scene.s.call("window.load.state")["docs"]]
    out = judge_switch_active(rows)
    if not all(loading) or len(loading) != 2:
        out.update(verdict="INVALID", why="캡처가 끝나기 전에 불러오기가 끝났다")
    scene.load(0)
    scene.clear()  # 남은 12초 작업은 닫힘으로 해제된다
    return out


def case_overlap(scene):
    scene.clear()
    scene.load(5000)
    scene.open()
    time.sleep(0.6)
    rows = []

    def note(name, want):
        rows.append((name, want, scene.state().get("count"), scene.shown()))

    note("열기", 1)
    scene.load(6000)
    scene.s.call("grid.search", doc=scene.doc, text="")
    time.sleep(0.4)
    note("열기+검색", 2)
    scene.load(300)
    scene.s.call("grid.refresh", doc=scene.doc)  # 앞 검색은 취소된다
    time.sleep(1.3)
    note("검색 취소·새로고침 끝", 1)
    scene.idle()
    note("모두 끝", 0)
    return judge_overlap(rows)


def case_slow(scene):
    scene.load(0)
    now = scene.state()["slow_seconds"]
    if now != SLOW_SECONDS:
        return {"verdict": "DEFERRED", "why": "slow_seconds=%s — 2 로 낮춰 기동해야 한다" % now}
    scene.load(4500)
    scene.s.call("grid.refresh", doc=scene.doc)
    time.sleep(1.0)
    early = bool(scene.state().get("isSlow"))
    time.sleep(2.0)
    late = scene.state()
    shown = scene.shown()
    scene.crop("slow_badge")
    scene.idle()
    ok = not early and late.get("isSlow") and late.get("isLoading") and shown and not scene.state().get("isSlow")
    return {"verdict": "PASS" if ok else "FAIL", "why": "초과 뒤 지연 표지·스피너 유지·끝나면 해제",
            "early_slow": early, "late": late, "shown": shown}


def case_close(scene):
    scene.load(5000)
    scene.s.call("grid.refresh", doc=scene.doc)
    time.sleep(0.5)
    before = scene.state().get("count")
    scene.s.call("docs.close", all=True)
    time.sleep(1.2)
    left = scene.state()
    busy = [x for x in scene.s.call("busy.state")["scopes"] if x["scope"] == scene.doc]
    ok = before == 1 and not left["states"] and not left["pending"] and not busy
    return {"verdict": "PASS" if ok else "FAIL", "why": "닫힘 뒤 남은 상태·예약·스코프 0",
            "before": before, "states": left["states"], "pending": left["pending"], "busy": busy}


def case_blocked(scene):
    scene.load(0)
    scene.clear()
    scene.open()
    scene.idle()
    # 색 때문에 안 보이는 것(018a 가 잡음)과 회전을 섞지 않도록 비활성 창의 아이콘(첫 PdDocIcon)으로 잰다
    scene.s.call("docs.open", kind="grid", count=1, allowDuplicate=True)
    time.sleep(1.2)
    bounds = scene.icon()["bounds"]
    plain, scale = scene.s.capture("blocked_idle")
    block = getattr(scene.s, "block_ms", BLOCK_MS)
    scene.load(1200, block)
    area = box(bounds, scale)
    shots, gaps = scene.s.blocked_burst(lambda: scene.s.call("grid.refresh", doc=scene.doc), 2.6 + block / 1000.0, area=area)
    scene.load(0)
    scene.idle()
    # 웹은 막히기 전에 낸 프레임이 늦게 올라온다 — settle_ms 뒤만 막힘 중 화면으로 센다
    late = [(g0 + getattr(scene.s, "settle_ms", 0), g1 - 50) for g0, g1 in gaps]
    late = [g for g in late if g[1] - g[0] >= 400]
    picked = [shot[0].crop(area) for shot in inside(shots, late)]
    out = judge_rotation([shape(c) for c in picked], [delta(a, b) for a, b in zip(picked, picked[1:])])
    for i, crop in enumerate(picked):
        crop.save("%s/blocked_%d.png" % (scene.s.shots, i))
    free = [x[0].crop(area) for x in shots if gaps and x[1] < gaps[0][0]]
    out["moving_before_block_px"] = max([delta(a, b) for a, b in zip(free, free[1:])] or [0])  # 양성 대조
    if late and not picked and scene.s.scope == "web":  # screencast 는 화면이 바뀔 때만 온다 = 없으면 앞 화면 그대로
        held = [x for x in shots if x[1] <= late[0][1]][-1:]
        updates = sum(late[0][0] <= x[1] <= late[0][1] for x in shots)
        seen = shape(held[0][0].crop(area)) if held else 0
        out.update(updates_in_block=updates, held_shape_px=seen)
        if not updates and seen >= SHAPE_PX:
            out.update(verdict="RECORDED", rotation="멈춤", why="웹은 막힘 중 화면 갱신 0 — 스피너 모양은 남고 회전은 멈춘다")
    return dict(out, block_ms=block, gaps_ms=gaps, shots_ms=[(t0, t1) for _, t0, t1 in shots])


def judge(s):
    scene = Scene(s)
    cases = {}
    try:
        scene.load(0)
        scene.clear()
        scene.open()
        scene.idle()
        original = scene.crop("original")
        scene.clear()
        cases["AC-018a.open"] = case_cycle(scene, "open", scene.open, original)
        for name, cmd in (("search", "grid.search"), ("refresh", "grid.refresh")):
            cases["AC-018b." + name] = case_cycle(scene, name, lambda: s.call(cmd, doc=scene.doc), original)
        cases["AC-018a.active_switch"] = case_active_switch(scene)
        cases["AC-019.overlap_cancel"] = case_overlap(scene)
        fail = [c for c in s.call("window.load.selftest")["cases"] if c["name"] == "complete_fail_cancel"]
        cases["AC-019.fail"] = {"verdict": "PASS" if fail and fail[0]["pass"] else "FAIL", "case": fail,
                                "why": "근거는 앱 안 자기시험(실화면 실패 주입점 없음)"}
        cases["AC-019.slow"] = case_slow(scene)
        cases["AC-019.close"] = case_close(scene)
        cases["AC-018c.blocked"] = case_blocked(scene)
    finally:
        scene.load(0)
        s.call("docs.close", all=True)
    return cases


def main():
    args = session.parser(__doc__).parse_args()
    if args.self_test:
        return self_test() or session.burst_self_test() or session.pick_self_test()  # 판정 규칙 + 화면 burst 파싱 + 창 찾기 후보 규칙(C# 컴파일 포함)
    return session.run(args, "window_load_judge", judge, {"BusyIconSlowSeconds": SLOW_SECONDS})


if __name__ == "__main__":
    sys.exit(main())

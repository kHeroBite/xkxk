#!/usr/bin/env python3
# AC-011 활성화 출처 실조작 판정 — Chrome headless 에서 창 목록·창 더 보기·탭·본문·단축키·모바일형 콤보를 실클릭·실키로 눌러 전환 효과 재생을 잰다
"""
사용
  motion_web_origin_click.py --bundle <wwwroot> --port <포트> --out-dir <고정 결과 폴더> [--repeat 5] [--only list,more,negative,origin,nav] | --self-test
전환은 page.mouse·page.keyboard 로만 만든다. 원격 명령은 준비·읽기에만 쓴다(docs.activateFrom 은 출처 표식 경로를 건너뛰므로 금지).
예외는 잔류·오표식 사례(INJECT 3건)다. 취소 직후 600ms 안에, 또는 사용자 선택 없이 전환을 만들어야 해서 docs.activate 를 쓴다.

판정(정본 21 §3.2, AC-011)
  활성 문서가 기대와 다르면 INVALID 다. 재생 0 이어도 음성 사례의 PASS 로 세지 않는다.
  시행마다 조작 직전 ui.style 실효값·활성 문서 zReason·state 를(첫 시행은 캡처도) 남기고, 기대 배치와 다르면 INVALID 다.
  묶음마다 양성 대조군(열기 pushLeft)에 재생이 없으면 그 묶음 전체가 INVALID 다.
  행의 evidence 는 real(실클릭·실키) 또는 inject(원격 — 대조군·잔류 사례)다. inject 는 실경로 PASS 로 세지 않는다.
  양성: 매 시행 perf 구간 motion.play 가 있고, 최대화·DOCK 은 나가는 창 짝이 있다.
  짝 판정 근거=lastSwitch(앱 자기 기록): 같은 조작을 한 번 더 해서 seq 가 늘고 ghost 가 started·done 이면 짝 있음, none·null 이면 FAIL.
  폴링 동시 재생 수(pair)는 보조 기록이다. pair=1 은 짝 누락 FAIL, seq 가 늘지 않아 판단할 수 없으면 INVALID(관측 없음).
  화면 근거가 필요한 판정(첫 프레임·혼합)은 close·frames 판정기(desktop/judge/motion_close_*)가 맡는다.
  음성: 매 시행 motion.play 가 없다.
  재생 근거(played_basis): perf 구간 motion.play, 없으면 짝 사례는 첫 조작 앞뒤 lastSwitch(seq 증가·ghost started·done).
  perf 구간은 재생 중 프레임 표본이 0장이면 버려지므로(부하) 둘 다 없으면 INVALID(관측 없음). 음성 사례는 perf 없음이 그대로 PASS 다.
  한계: 겹침(짝 없음)은 보조 근거가 없어 빈 구간 탈락이 INVALID 로 남는다(앱 진단 필드 SwitchNote Enter 는 다음 작업 후보).
한계: switch_sources 목록 변경·탭 키 조작·모바일형 서랍 문서 목록·독립 창은 재지 않는다(이 파일 밖).
rc 0=전부 PASS 1=FAIL 있음 2=실행 오류 4=INVALID 만 남음
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from motion_web_origin_rules import exit_code, judge, judge_pair, layout_problem, resolve_played, self_test  # noqa: E402
import motion_web_origin_ops as ops  # noqa: E402
PLACES = ("overlap", "max", "dock")
INJECT = ("more.cancel", "origin.overlap.tab_cancel", "nav.program")
STYLES = {"dock": "tablet", "nav": "mobile"}
POLL_JS = """([total]) => {
  window.__pdPlay = {max: 0, done: false}; const t0 = performance.now();
  const body = JSON.stringify({cmd: 'motion.probe', args: {hold: 'release'}});
  const step = async () => {
    const r = JSON.parse(await window.pdAutomation.invoke(body));
    if (r.ok) window.__pdPlay.max = Math.max(window.__pdPlay.max, r.result.released);
    if (performance.now() - t0 < total) setTimeout(step, 30); else window.__pdPlay.done = true;
  };
  setTimeout(step, 30);
}"""


class Run:
    def __init__(self, web, check):
        self.w, self.check, self.need = web, check, check.need

    def docs(self):
        return self.need(self.w, "docs.list")

    def pair(self):
        act = self.check.active(self.w)
        return act, next(d["id"] for d in self.docs() if d["id"] != act)

    def box(self, ctl, **kw):
        b = self.need(self.w, "ui.find", id=ctl, **kw)["bounds"]
        if not b["w"] or not b["h"]:
            raise RuntimeError("보이지 않는다: %s" % ctl)
        return b

    def click(self, ctl, **kw):
        b = self.box(ctl, **kw)
        self.w.page.mouse.click(b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)

    def setup(self, place, count=2):
        w, need = self.w, self.need
        until = time.time() + 90  # 기동 레이아웃 복원이 끝나기 전에는 docs.close 를 거절한다
        while not w.call("docs.close", all=True).get("ok") and time.time() < until:
            time.sleep(2)
        need(w, "docs.close", all=True)
        time.sleep(1)
        need(w, "ui.style", set=STYLES.get(place, "pc"))
        time.sleep(2)
        need(w, "docs.open", kind="form" if count > 2 else "grid", count=count, allowDuplicate=True)  # 10창은 가벼운 화면으로
        time.sleep(3)
        if place == "nav":
            return
        need(w, "docs.arrange", mode="tabbed" if place == "dock" else "cascade")
        time.sleep(1.5)
        if place == "max":
            for doc in self.docs():
                need(w, "docs.state", doc=doc["id"], state="maximized")
                time.sleep(0.8)

    def open_menu(self):  # 아래 조작 함수는 (기대 활성 문서, 실행 함수) 를 돌려준다. 실행 함수만 전환을 만든다
        self.click("shell.mnuWindow")
        time.sleep(1.2)

    def list_item(self, how, same=False):
        act, other = self.pair()
        target = act if same else other
        ids, title = [d["id"] for d in self.docs()], next(d["title"] for d in self.docs() if d["id"] == target)
        self.open_menu()
        for number in range(1, len(ids) + 1):
            ctl = "shell.mnuWinList%d" % number
            header = self.need(self.w, "ui.find", id=ctl, props="Header")["props"].get("Header")
            if title in str(header):
                break
        else:
            raise RuntimeError("창 목록에 없다: %s" % title)
        if how == "click":
            return target, lambda: self.click(ctl)
        if how == "access_key":
            return target, lambda: self.w.page.keyboard.press(str(number))
        for _ in range(80):  # 항목 수는 빌드·설정마다 다르다 — 선택이 닿을 때까지 아래로
            if self.need(self.w, "ui.find", id=ctl, props="IsSelected")["props"].get("IsSelected") == "True":
                return target, lambda: self.w.page.keyboard.press("Enter")
            self.w.page.keyboard.press("ArrowDown")
            time.sleep(0.1)
        raise RuntimeError("화살표로 %s 에 닿지 못했다" % ctl)

    def more(self, how):
        act = self.check.active(self.w)
        ids = [d["id"] for d in self.docs()]
        row = 1 if ids[0] == act else 0
        self.open_menu()
        self.click("shell.mnuWinListMore")
        time.sleep(1.2)
        b, keys = self.box("shell.moreWindowsList"), self.w.page.keyboard
        self.w.page.mouse.click(b["x"] + 40, b["y"] + 14)
        keys.press("Home")
        for _ in range(row):
            keys.press("ArrowDown")
        time.sleep(0.8)  # 앞 클릭과 더블클릭으로 묶이지 않게
        if how == "ok":
            return ids[row], lambda: self.click("shell.moreWindowsOk")
        if how == "enter":
            return ids[row], lambda: keys.press("Enter")
        if how == "dblclick":
            height = b["h"] / len(ids) if b["h"] < 359 else 32
            x, y = b["x"] + 40, b["y"] + height * row + 14
            return ids[row], lambda: self.w.page.mouse.dblclick(x, y)

        def cancel_then_program():
            self.click("shell.moreWindowsCancel")
            self.w.call("docs.activate", doc=ids[row])  # 잔류 사례만의 예외(파일 머리말)
        return ids[row], cancel_then_program

    def tab(self, hold_ms=0):
        _, other = self.pair()
        x, y = ops.tab_point(self, other)
        return other, lambda: self.w.page.mouse.click(x, y, delay=hold_ms)

    def key(self):
        b = self.box("grid.dgMain", doc=self.check.active(self.w))
        self.w.page.mouse.click(b["x"] + min(b["w"] / 2, 260), b["y"] + min(b["h"] / 2, 120))
        time.sleep(1.5)  # 단축키는 앱 안에 초점이 있어야 먹는다
        return self.pair()[1], lambda: self.w.page.keyboard.press("Control+F6")

    def body(self):
        act, other = self.pair()
        top, back = self.box("PART_OuterBorder", doc=act), self.box("PART_OuterBorder", doc=other)
        x = back["x"] + 8 if top["x"] > back["x"] + 12 else back["x"] + back["w"] - 8
        return other, lambda: self.w.page.mouse.click(x, back["y"] + back["h"] / 2)

    def motion_played(self):
        return "motion.play" in [p.get("name") for p in self.need(self.w, "perf.get").get("phases") or []]

    def layout(self, place, shot):
        doc = next((d for d in self.docs() if d.get("active")), None)
        seen = {"effective": self.need(self.w, "ui.style").get("effective"), "zReason": (doc or {}).get("zReason"), "state": (doc or {}).get("state")}
        if shot:  # 캡처는 사례마다 첫 시행, 배치 값은 매 시행
            self.w.shot(shot)
        return {"layout": seen, "capture": shot and shot + ".png", "problem": layout_problem(place, seen, doc is not None)}

    def control(self):
        """양성 대조군: 열기 효과 재생. 연 문서는 다시 닫는다."""
        before = {d["id"] for d in self.docs()}
        self.need(self.w, "perf.reset")
        self.need(self.w, "docs.open", kind="grid", allowDuplicate=True)
        played = any(time.sleep(1) or self.motion_played() for _ in range(15))  # 창이 많으면 여는 데 오래 걸린다
        for doc in [d["id"] for d in self.docs() if d["id"] not in before]:
            self.need(self.w, "docs.close", doc=doc)
        time.sleep(1.5)
        return played

    def trial(self, make, want, paired, place, shot, control):
        w, need = self.w, self.need
        time.sleep(1)
        at = self.layout(place, shot)
        target, act = make()
        need(w, "perf.reset")
        first = {"before": self.last_switch()}
        act()
        time.sleep(2.5)
        perf = self.motion_played()
        first["after"] = self.last_switch()
        played, basis = resolve_played(want, perf, first)  # perf 빈 구간 탈락 보조(짝 사례만) — 근거 없으면 None → INVALID
        reached, pair, switch = self.check.active(w) == target, None, None
        if want and paired and reached and played:  # 짝은 같은 조작을 한 번 더 해서 본다
            time.sleep(1)
            target, act = make()
            before = self.last_switch()
            w.page.evaluate(POLL_JS, [2200])  # 폴링 pair 는 보조 기록(부하에서 재생을 못 볼 수 있다 — rules.judge_pair)
            act()
            w.page.wait_for_function("() => window.__pdPlay.done", timeout=20000)
            reached, pair = self.check.active(w) == target, w.page.evaluate("() => window.__pdPlay.max")
            switch = {"before": before, "after": self.last_switch()}
        verdict = judge(want, reached, played, pair, at["problem"], control, switch)
        row = dict(at, verdict=verdict, reached=reached, played=played, pair=pair, played_basis=basis, perf_played=perf, firstSwitch=first)
        if switch is not None:  # 짝 판정은 앱 자기 기록(lastSwitch)에 기댄다. 화면 근거(첫 프레임·혼합)는 close·frames 판정기가 맡는다
            row.update(basis="lastSwitch", lastSwitch=switch, pair_why=judge_pair(switch, pair)[1] if reached else "활성 미전환")
        return row

    def restore_normal(self):
        """더블클릭 둘째 클릭 등으로 Maximized 가 남으면 다음 시행 전에 Normal 로 되돌린다. 되돌렸거나 처음부터 없으면 True, 못 돌리면 False."""
        for doc in [d for d in self.docs() if d.get("state") == "Maximized"]:
            self.w.call("docs.state", doc=doc["id"], state="normal")
            time.sleep(0.8)
        return not any(d.get("state") == "Maximized" for d in self.docs())

    def last_switch(self):
        """motion.probe 의 lastSwitch(마지막 전환 1건: seq·source·ghost). 표에 없는 effect 를 주면 표본 재생 없이 기록만 읽는다."""
        return self.need(self.w, "motion.probe", effect="__last__").get("lastSwitch")


def cases(run, only):
    """(묶음 준비, [(이름, 조작, 재생 기대, 짝 필요, 전환 설정)])"""
    on, off = "same_as_open", "none"
    if "list" in only:
        for place in PLACES:
            yield (place, 2), [("list.%s.%s" % (place, how), lambda h=how: run.list_item(h), True, place != "overlap", on)
                               for how in ("click", "access_key", "arrows_enter")]
    if "more" in only:
        rows = [("more." + how, lambda h=how: run.more(h), True, False, on) for how in ("ok", "dblclick")]
        rows.append(("more.cancel", lambda: run.more("cancel"), False, False, on))
        yield ("overlap", 10), rows + [("more.enter", lambda: run.more("enter"), True, False, on)]
    if "negative" in only:
        yield ("overlap", 2), [("negative.switch_none", lambda: run.list_item("click"), False, False, off),
                               ("negative.same_window", lambda: run.list_item("click", True), False, False, on)]
    if "origin" in only:
        for place in PLACES:
            paired = place != "overlap"
            rows = [("origin.%s.keyboard" % place, run.key, True, paired, on), ("origin.%s.tab" % place, run.tab, True, paired, on),
                    ("origin.%s.tab_hold700" % place, lambda: run.tab(700), True, paired, on),
                    ("origin.%s.tab_hold2000" % place, lambda: run.tab(2000), True, paired, on)]
            if place == "overlap":
                rows.append(("origin.overlap.body_default", run.body, False, False, on))
                rows.append(("origin.overlap.tab_cancel", lambda: ops.tab_cancel(run), False, False, on))
            yield (place, 2), rows
    if "nav" in only:
        rows = [("nav.combo." + how, lambda h=how: ops.combo(run, h), True, False, on) for how in ("keys", "closed_key", "click")]
        yield ("nav", 2), rows + [("nav.program", lambda: ops.program(run), False, False, on)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle")
    ap.add_argument("--port", type=int, default=0)
    ap.add_argument("--out-dir")
    ap.add_argument("--repeat", type=int, default=5)
    ap.add_argument("--only", default="list,more,negative,origin,nav")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not (a.bundle and a.out_dir and a.port) or a.port in (5180, 5220):
        ap.error("--bundle·--out-dir·--port(5180·5220 제외) 가 필요하다")
    import motion_web_check as check
    from playwright.sync_api import sync_playwright
    from ui28_web import ARGS, Web

    w, out = Web(a.bundle, a.port, a.out_dir, viewport=(1500, 1700)), {"cases": {}, "errors": []}
    now, t0 = {"context": None, "case": None}, time.time()

    def where():
        """기록 전용 — 지금 묶음(배치.창 수)·사례 이름·실행 시작 뒤 초."""
        return {"context": now["context"], "case": now["case"], "t_s": round(time.time() - t0, 1)}
    try:
        with sync_playwright() as p:
            w.browser = p.chromium.launch(headless=True, args=ARGS)
            run = Run(w, check)
            for (place, count), rows in cases(run, a.only.split(",")):
                size = (402, 876) if place == "nav" else (1500, 1700)
                context = w.browser.new_context(viewport=dict(zip(("width", "height"), size)))
                w.page = context.new_page()
                w.page.set_default_timeout(90000)  # swiftshader 캡처는 부하가 높으면 30초를 넘는다
                w.page.on("pageerror", lambda e: out["errors"].append(str(e)[:300]))  # 판정·집계는 종전대로(오류 1건 = FAIL 1)
                # 기록 전용(판정 불변): 오류·실패 요청이 어느 사례·시각에 났는지. r11 'Failed to fetch' 12건은 요청이 남지 않아 원인을 못 가렸다
                w.page.on("pageerror", lambda e: out.setdefault("error_context", []).append(dict(where(), error=str(e)[:300])))
                w.page.on("requestfailed", lambda r: out.setdefault("request_failures", []).append(
                    dict(where(), url=r.url[:300], method=r.method, resource=r.resource_type, failure=str(r.failure)[:200])))
                w.page.on("response", lambda r: r.status >= 400 and out.setdefault("request_failures", []).append(
                    dict(where(), url=r.url[:300], method=r.request.method, resource=r.request.resource_type, failure="HTTP %d" % r.status)))
                now["context"], now["case"] = "%s.%d" % (place, count), "setup"
                try:
                    w.page.goto("http://127.0.0.1:%d/" % a.port, wait_until="load", timeout=90000)
                    check.ready(w)
                    run.setup(place, count)
                    for key, value in (("motion", "cinematic"), ("motion.open", "pushLeft"), ("motion.close", "none")):
                        check.effect(w, key, value)
                    control = run.control()
                    out.setdefault("controls", []).append({"place": place, "count": count, "played": control, "evidence": "inject"})
                    for name, make, want, paired, mode in rows:
                        now["case"] = name
                        check.effect(w, "motion.switch", mode)
                        trials = out["cases"][name] = []
                        evidence = "inject" if name in INJECT else "real"
                        for i in range(a.repeat):
                            try:
                                trials.append(dict(run.trial(make, want, paired, place, None if i else name, control), evidence=evidence))
                            except Exception as e:  # noqa: BLE001
                                trials.append({"verdict": "ERROR", "why": repr(e)[:300]})
                                w.page.keyboard.press("Escape")
                            try:
                                if name.startswith("more.") and ops.dismiss(run):
                                    trials[-1]["dialog_left"] = True  # 판정은 그대로 두고 다음 시행만 살린다
                                if name.startswith("more.") and place != "max" and not run.restore_normal():
                                    trials[-1]["verdict"], trials[-1]["why"] = "INVALID", "상태 잔존"  # Maximized 를 못 돌렸다
                            except Exception as e:  # noqa: BLE001
                                trials[-1]["dismiss_error"] = repr(e)[:300]
                        print(name, [t["verdict"] for t in trials], flush=True)
                except Exception as e:  # noqa: BLE001
                    out["cases"]["setup.%s.%d" % (place, count)] = [{"verdict": "ERROR", "why": repr(e)[:300]}]
                    print("setup.%s.%d ERROR %s" % (place, count, repr(e)[:300]), flush=True)
                context.close()
            w.browser.close()
    finally:
        w.httpd.shutdown()
        verdicts = [t["verdict"] for v in out["cases"].values() for t in v] + ["FAIL" for _ in out["errors"]]
        out["counts"] = {v: verdicts.count(v) for v in sorted(set(verdicts))}
        out["rc"] = exit_code(verdicts)
        with open(os.path.join(a.out_dir, "motion_web_origin_click.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
        print("RESULT", out["counts"], "rc=%d" % out["rc"], flush=True)
    return out["rc"]


if __name__ == "__main__":
    sys.exit(main())

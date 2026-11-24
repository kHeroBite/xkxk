# AC-010·022 Fold5 닫기 효과 사례 — 전면(Nav)·펼침(DockTab)에서 터치·뒤로 가기·원격으로 각각 닫고 배치·캡처·lastClose 를 행마다 적는다
"""
motion_nav_fold5.py --only close 가 부른다. 안전 규칙·원복은 그 파일 것을 그대로 쓴다(깨우기·잠금 해제 없음).
판정은 재생 기록(motion_close_rules.py)과 화면(motion_nav_fold5_frames.py — 중간 프레임·첫 프레임 1:1·바탕 유지)을 함께 본다.
화면 표본이 없으면 PASS 로 적지 않는다. 실경로는 INVALID(전체 UNDET), 원격은 UNCHECKED(기록만, 셈에서 뺌)다.
덮개를 가리려고 표본은 서로 다른 화면(그리드·간트·입력폼, 현재=입력폼)으로 채우고 사례마다 density 를 남긴다.
  evidence=touch  화면 좌표 실터치
  evidence=back   OS 뒤로 가기 키
  evidence=inject 원격 docs.close. 실경로 PASS 로 세지 않는다
사례마다 닫기 직전 ui.style 실효값·활성 문서 zReason·state 와 캡처를 남기고, 기대 배치와 다르면 닫지 않고 INVALID 다.
뒤로 가기는 화면마다 1회 실제로 누른다. 문서 수가 그대로면(앱 이탈 또는 시트만 닫힘) 닫기 경로가 아니므로 N/A 로 두고 판정에서 뺀다(PASS 아님).
문서 수가 줄면 닫기 경로이므로 다른 행과 같게 판정한다(재생 0=FAIL). 앱을 벗어났으면 홈 화면일 때만 앱을 다시 앞으로 부른다.
표본 문서를 닫으므로 시작 때 문서가 표본 화면(Grid-n·Gantt-n·Form-n)뿐일 때만 돌고, 끝에 같은 종류로 다시 연다.
다시 열면 번호가 바뀌어 옛 id → 새 id 표(saved.remap)를 남긴다. 탭 순서는 이름순으로 돌아온다.
행은 생길 때마다 <out-dir>/close_rows.json 에 덮어쓴다(중간에 끊겨도 남게).
"""
import json
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "desktop", "judge"))
from motion_close_rules import combine, judge_close, judge_residue, layout_problem, result  # noqa: E402
import motion_nav_fold5_frames as frames  # noqa: E402
from motion_nav_fold5_rules import close_point, may_start  # noqa: E402

NOT_CLOSE_PATH = "N/A"
BACK_CODE = ("뒤로 가기는 열린 메뉴·서랍·시트만 닫고 없으면 기본 동작(앱 이탈)이다 — "
             "src/xkxk.frameworks.ui.android/MainActivity.cs:71(DispatchKeyEvent)·107~115(OnHostBackRequested)·118(DismissTop)")
SAMPLE = re.compile(r"^(Grid|Gantt|Form)-\d+$")
SETTINGS = (("motion", "cinematic"), ("motion.switch", "none"), ("motion.open", "none"), ("motion.close", "fade"))
SCREENS = {"cover": ("nav", "Nav"), "inner": ("dock", "DockTab")}
REAL = ("touch", "back")
KINDS = ("grid", "gantt", "form")
END_WAIT_S = 2.5


def can_run(docs):
    return all(SAMPLE.match(d) for d in docs)


def verdict_of(rows):
    """실경로(터치·뒤로 가기)만으로 PASS 를 정한다. 원격 행은 실패만 전체에 반영한다."""
    controls = [r for r in rows if r["case"] == "control_open"]
    if len(controls) < 2 or any(r["verdict"] != "PASS" for r in controls):
        return "UNDET", "열기 대조군 없음·재생 0 또는 기대 배치 아님(INVALID)"
    cases = [r for r in rows if r["case"] != "control_open" and r["verdict"] != NOT_CLOSE_PATH]
    real = [r["verdict"] for r in cases if r["evidence"] in REAL]
    inject = [r["verdict"] for r in cases if r["evidence"] not in REAL]
    why = "실경로(화면 포함) %d/%d·inject 기록만 %d/%d" % (real.count("PASS"), len(real), inject.count("UNCHECKED"), len(inject))
    if "FAIL" in real + inject:
        return "FAIL", why
    if not real or "INVALID" in real + inject:
        return "UNDET", why
    return "PASS", why


class Close:
    def __init__(self, ctx, call, apk_md5, device):
        """call(cmd, **kw) 는 화면 상태·전면 앱을 매번 확인하고 부르는 함수다. device 는 sh·app_on_top·top_activity."""
        self.c, self.call, self.md5 = ctx, call, apk_md5
        self.sh, self.on_top, self.top = device.sh, device.app_on_top, device.top_activity
        self.device, self.rows, self.screen, self.folder = device, [], "cover", None

    def add(self, row):
        self.rows.append(row)
        with open(os.path.join(self.c.out, "close_rows.json"), "w", encoding="utf-8") as f:
            json.dump({"apk_md5": self.md5, "rows": self.rows}, f, ensure_ascii=False, indent=1, default=str)

    def ids(self):
        return [d["id"] for d in self.call("style.state")["windows"]]

    def active(self):
        return self.call("style.state")["active"]

    def last(self):
        return self.call("motion.probe", effect="__last__").get("lastClose")

    def effect(self, key, value):
        self.call("style.effect.set", key=key, value=value)

    def played(self):
        return "motion.play" in [p.get("name") for p in self.call("perf.get").get("phases") or []]

    def fill(self, count):
        self.effect("motion.close", "none")
        self.call("docs.close", all=True)
        time.sleep(1)
        for kind in KINDS[-count:]:  # 같은 그림이면 덮개를 가릴 수 없다
            self.call("docs.open", kind=kind, allowDuplicate=True)
            time.sleep(1.5)
        if self.screen == "inner":
            self.call("docs.arrange", mode="tabbed")
            time.sleep(1.5)
            self.call("docs.activate", doc=next(d for d in self.ids() if d.startswith("Form")))
            time.sleep(1.5)

    def layout(self, name):
        """(같은 회차의 배치 상태·캡처, 기대와 다른 이유)."""
        style = self.call("ui.style").get("effective")
        doc = next((d for d in self.call("docs.list") if d.get("active")), None)
        seen = {"effective": style, "zReason": (doc or {}).get("zReason"), "state": (doc or {}).get("state")}
        shot = self.c.shot("close_%02d_%s_%s" % (len(self.rows), self.screen, name))
        problem = layout_problem(SCREENS[self.screen][0], seen, doc is not None)
        return {"ui_style": style, "layout": seen, "capture": shot}, problem

    def control_open(self):
        self.fill(1)
        self.effect("motion.open", "fade")
        self.call("perf.reset")
        self.call("docs.open", kind="gantt", allowDuplicate=True)
        time.sleep(END_WAIT_S)
        played = self.played()
        self.effect("motion.open", "none")
        seen, problem = self.layout("control")
        self.add(dict(seen, case="control_open", evidence="inject", screen=self.screen, played=played, problem=problem,
                              verdict="PASS" if played and not problem else "INVALID"))
        return played and not problem

    def press(self, how, doc):
        """닫는 조작. 못 했으면 이유."""
        if how == "inject":
            self.call("docs.close", doc=doc)
            return None
        self.call("status")  # 실입력 직전 안전 확인
        if how == "back":
            self.sh("input keyevent KEYCODE_BACK")
            return None
        xml = self.c.dump()
        if self.screen == "cover":
            point = self.c.bounds(xml, "shell.compactClose")
        else:
            point = close_point(xml, next(d["title"] for d in self.call("docs.list") if d["id"] == doc))
        if not point:
            return "닫기 단추를 화면에서 못 찾았다"
        self.call("status")  # dump 가 오래 걸리므로 누르기 직전 다시 확인
        return frames.shoot(self.device, point[0], point[1] + self.c.bounds_top(xml, "android:id/content"), self.folder)

    def come_back(self):
        if not may_start(self.top()):
            return  # 다른 앱이 앞이면 사람 흔적이다 — 부르지 않고 다음 안전 확인이 멈춘다
        name = self.sh("cmd package resolve-activity --brief com.pd.poc").splitlines()
        if name and "/" in name[-1]:
            self.sh("am start -n '%s'" % name[-1])
            time.sleep(4)

    def case(self, name, count, how, effect="fade", play=True, stage=None, other=False):
        self.fill(count)
        self.effect("motion.close", effect)
        ids, current = self.ids(), self.active()
        doc = next(d for d in ids if d != current) if other else current
        expect_at = SCREENS[self.screen][1]
        resolved = self.call("motion.probe", effect="fade", placement=expect_at)["resolved"]
        busy = name.endswith("busy")
        if busy:
            self.call("busy.begin", scope="main")
            time.sleep(1.5)
        row = {"case": name, "evidence": how, "path": how, "apk_md5": self.md5, "screen": self.screen, "before_count": count,
               "resolved_close(probe)": resolved.get("close"), "density": " / ".join(self.sh("wm density").splitlines())}
        self.folder = os.path.join(self.c.out, "frames", "%02d_%s_%s_%d" % (len(self.rows), self.screen, name, count))
        try:
            seen, problem = self.layout(name + "_" + how)
            row.update(seen)
            if not problem:
                before = self.last()
                self.call("perf.reset")
                problem = self.press(how, doc)
            if problem:
                self.add(dict(row, verdict="INVALID", why=problem))
                return
            time.sleep(END_WAIT_S)
            if how == "back":
                row["left_app"] = not self.on_top()
                if row["left_app"]:
                    self.come_back()
                row["left"] = len(self.ids())
                if row["left"] == len(ids):
                    self.add(dict(row, closed=False, verdict=NOT_CLOSE_PATH, why=BACK_CODE))
                    return
            after, played = self.last(), self.played()
        finally:
            if busy and self.on_top():
                self.call("busy.end", scope="main")
        left = self.ids()
        closed = doc not in left
        expect = {"placement": expect_at if play else None, "effect": effect if play else None, "play": play, "closed": closed}
        expect.update(stage or {})
        if closed or how == "back":  # 뒤로 가기로 문서 수가 줄었으면 닫기 경로다
            parts = {"close": judge_close(before, after, expect)}
        else:
            parts = {"close": result("INVALID", "조작 뒤에도 닫히지 않았다")}
        if other:
            same = self.active() == current
            parts["active"] = result("PASS" if same else "FAIL", "현재 문서 유지=%s" % same)
        parts["residue"] = judge_residue(self.call("motion.probe", hold="release").get("released"))
        if closed and how in REAL:
            shown = frames.judge_dir(self.folder, self.screen, play) if how == "touch" else result("INVALID", "화면 표본이 없다")
            parts["pixels"] = {k: v for k, v in shown.items() if k != "seq"}
            row["frames"] = {"folder": self.folder if how == "touch" else None, "seq": shown.get("seq")}
        done = combine(**parts)
        if how not in REAL and done["verdict"] == "PASS":
            row["visibility"] = "unchecked"
            done = result("UNCHECKED", "재생 기록만 맞음 — 화면 표본 없음(PASS 아님)")
        row.update(placement={"expect": expect_at, "actual": (after or {}).get("placement")}, left=len(left),
                   lastClose={"before": before, "after": after}, played=played, closed=closed)
        self.add(dict(row, verdict=done["verdict"], why=done["why"]))

    def cover(self):
        self.screen = "cover"
        if not self.control_open():
            return
        for how in ("touch", "inject"):
            for count in (3, 2):
                self.case("nav_left", count, how)
            self.case("nav_last", 1, how)
            self.case("nav_none", 2, how, effect="none", play=False, stage={"stage": "none"})
        self.case("nav_other", 2, "inject", play=False, stage={"stage": "skipped", "reason": "gap"}, other=True)
        self.case("nav_busy", 2, "inject", play=False, stage={"stage": "busy"})
        self.case("nav_back", 2, "back")

    def inner(self):
        self.screen = "inner"
        if not self.control_open():
            return
        for how in ("touch", "inject"):
            for count in (3, 2):
                self.case("dock_left", count, how)
            self.case("dock_last", 1, how)
        self.case("dock_none", 2, "touch", effect="none", play=False, stage={"stage": "none"})
        self.case("dock_back", 2, "back")

    def rebuild(self, docs, active, remap):
        """시작 때의 표본 문서를 같은 종류로 다시 열고 옛 id → 새 id 를 remap 에 바로바로 적는다."""
        self.effect("motion.close", "none")
        self.call("docs.close", all=True)
        for doc in docs:
            before = set(self.ids())
            self.call("docs.open", kind=doc.split("-")[0].lower(), allowDuplicate=True)
            for _ in range(10):
                new = set(self.ids()) - before
                if new:
                    break
                time.sleep(0.5)
            if len(new) != 1:
                raise RuntimeError("다시 연 문서를 특정하지 못했다: %s → %s" % (doc, sorted(new)))
            remap[doc] = new.pop()
        time.sleep(1.5)
        if active in remap:
            self.call("docs.activate", doc=remap[active])


def run(ctx, call, saved, apk_md5, device, expect):
    """device 는 sh·app_on_top 을 가진 기기 부품, expect["state"] 는 안전 확인이 기대하는 화면 상태다(깨우지 않음)."""
    if not can_run(saved["docs"]):
        return {"verdict": "UNDET", "why": "표본이 아닌 문서가 열려 있어 닫지 않았다", "docs": saved["docs"]}
    close = Close(ctx, call, apk_md5, device)

    def show(state, arg):
        device.sh("cmd device_state state " + arg)
        expect["state"] = state
        time.sleep(4)
        call("status")  # 바꾼 화면에서 안전 확인

    def back():
        show("CLOSED", "0")
        close.screen = "cover"
        remap.clear()
        close.rebuild(saved["docs"], saved["active"], remap)

    remap = saved["remap"] = {}  # 여기부터 표본 문서를 닫는다. 다시 다 열기 전에는 옛 id 로 원복하지 않는다
    error = None
    try:
        for key, value in SETTINGS:
            close.effect(key, value)
        close.cover()
        show("OPENED", "reset")
        close.inner()
    except (RuntimeError, KeyError, SystemExit, subprocess.SubprocessError) as e:  # 도구 오류는 되돌리고 남긴다. 사람 흔적(Human)이면 기기를 더 만지지 않는다
        error = repr(e)[:300]
    out = {"rows": close.rows}
    try:
        back()
    except (RuntimeError, KeyError, SystemExit) as e:
        out["restore_error"] = repr(e)[:300]
    if error or "restore_error" in out:
        return dict(out, verdict="ERROR", why=error or out["restore_error"])
    verdict, why = verdict_of(close.rows)
    return dict(out, verdict=verdict, why=why)

#!/usr/bin/env python3
# AC-021 웹 움직임 효과·배경 — Chrome headless 에서 Ctrl+F6 전환 효과, 배경 이미지 선택·새로고침 유지, prefers-reduced-motion 을 잰다
"""
사용
  motion_web_check.py --bundle <wwwroot> --port <포트> --out-dir <고정 결과 폴더> --build-started '<YYYY-mm-dd HH:MM:SS>'
  motion_web_check.py --self-test
ui28_web.Web(127.0.0.1 정적 서버 + headless Chromium)을 재사용한다. 새로고침 유지를 보려고 ?fresh 없이 열고, 격리는 빈 브라우저 컨텍스트가 맡는다.
포트 5180·5220 은 거부한다. 사용자 PC 입력·Windows 프로세스와 무관하다.

판정 기준(정본 21 §4·§5, AC-011·013·014·021)
  ctrl_f6        motion.switch=same_as_open 이면 활성이 바뀌고 들어오는 창이 도중에 움직였다가 제자리로 돌아온다
  ctrl_f6_none   motion.switch=none 이면 활성만 바뀌고 움직이지 않는다
  reduced        reduce 이면 wipeRight → fade(열기·전환)이고 Ctrl+F6 에 확대·축소가 없다. 풀면 원래 효과로 돌아온다
  bg_pick        화면의 파일 고르기로 고른 이미지가 decor=image·ready 가 된다
  bg_persist     새로고침 뒤에도 같은 파일이 ready 다(IndexedDB)
  bg_area_only   이미지를 4번 켜고 끌 때 작업 영역만 바뀌고 문서 창 안은 화소가 그대로다
  favorite·title_tab  motion_web_regress.py(즐겨찾기 P0 회귀)
이 판에 없는 명령·컨트롤은 FAIL 이 아니라 NOT_IN_BUILD 로 적고, 빌드 시작 뒤 바뀐 소스 목록을 함께 남긴다.

한계: 창 움직임은 ui.find 좌표 표본이라 불투명도만 바뀌는 효과의 재생은 못 본다(reduced 는 해석값 + 확대·축소 없음으로 본다).
      파일 고르기는 showOpenFilePicker 를 숨겨 input[type=file] 경로로 잰다(네이티브 고르기 창은 headless 에서 다룰 수 없다).
rc 0=전부 PASS 1=FAIL 있음 2=실행 오류 4=NOT_IN_BUILD·UNDET 만 남음
"""
import argparse
import json
import os
import socketserver
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from PIL import Image  # noqa: E402
from playwright.sync_api import Error as PlaywrightError, sync_playwright  # noqa: E402
from motion_web_regress import case_favorite, case_title_tab  # noqa: E402
from motion_web_rules import exit_code, judge_area, judge_switch, not_in_build, self_test  # noqa: E402
from ui28_web import ARGS, Web  # noqa: E402

UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SAMPLE_EVERY_MS, SAMPLE_FOR_MS = 25, 1500
NO_FS_ACCESS = "delete window.showOpenFilePicker; delete Window.prototype.showOpenFilePicker;"
SAMPLER_JS = """([body, every, total]) => {
  window.__pdSamples = [];
  const t0 = performance.now();
  const timer = setInterval(() => {
    const t = performance.now() - t0;
    if (t > total) { clearInterval(timer); return; }
    Promise.resolve(window.pdAutomation.invoke(body)).then(r => window.__pdSamples.push({ t, r }));
  }, every);
}"""


class Missing(Exception):
    """이 판에 없는 명령·컨트롤."""


def need(w, cmd, **kw):
    r = w.call(cmd, **kw)
    if r.get("ok"):
        return r.get("result")
    error = str(r.get("error"))
    if not_in_build(error):
        raise Missing("%s %s: %s" % (cmd, kw, error[:160]))
    raise RuntimeError("%s %s: %s" % (cmd, kw, error[:200]))


def effect(w, key, value):
    r = need(w, "style.effect.set", key=key, value=value)
    if not r.get("applied"):
        raise Missing("효과 키 적용 안 됨 %s=%s: %s" % (key, value, r))


def active(w):
    return need(w, "style.state").get("active")


def ready(w):
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    for _ in range(60):
        if w.call("status").get("ok"):
            time.sleep(1.5)
            return
        time.sleep(1)
    raise RuntimeError("앱이 준비되지 않았다")


def two_docs(w):
    docs = need(w, "style.state")["windows"]
    if len(docs) < 2:
        need(w, "docs.open", kind="grid", count=2 - len(docs))
        time.sleep(2.5)
    return [d["id"] for d in need(w, "style.state")["windows"]]


def press_f6(w):
    """Ctrl+F6 을 실제 키로 누르고 들어오는 창의 자리 표본을 모은다."""
    docs, before = two_docs(w), active(w)
    b = need(w, "ui.find", id="PART_OuterBorder", doc=before)["bounds"]
    w.page.mouse.click(b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)  # 단축키는 MainView KeyBindings 라 앱 안에 초점이 있어야 한다
    time.sleep(1)
    target = next(d for d in docs if d != before)
    rest = need(w, "ui.find", id="PART_Grid", doc=target)["bounds"]
    body = json.dumps({"cmd": "ui.find", "args": {"id": "PART_Grid", "doc": target}})
    w.page.evaluate(SAMPLER_JS, [body, SAMPLE_EVERY_MS, SAMPLE_FOR_MS])
    w.page.keyboard.press("Control+F6")
    time.sleep(SAMPLE_FOR_MS / 1000.0 + 1.5)
    rows = [json.loads(s["r"]) for s in w.page.evaluate("() => window.__pdSamples")]
    samples = [r["result"]["bounds"] for r in rows if r.get("ok")]
    last = need(w, "ui.find", id="PART_Grid", doc=target)["bounds"]
    return active(w) == target, rest, samples, last


def case_switch(w, switch, want_motion):
    for key, value in (("motion", "cinematic"), ("motion.open", "zoomOut"), ("motion.switch", switch)):
        effect(w, key, value)
    probe = need(w, "motion.probe", effect="zoomOut")
    resolved, expect = dict(probe["resolved"], platform=probe.get("platform")), "zoomOut" if want_motion else "none"
    changed, rest, samples, last = press_f6(w)
    verdict, why = judge_switch(want_motion, changed, rest, samples, last)
    if (resolved.get("switch"), resolved["platform"]) != (expect, "Web"):
        verdict, why = "FAIL", "해석값 %s, 기대 switch=%s·platform=Web" % (resolved, expect)
    return {"verdict": verdict, "why": why, "resolved": resolved, "rest": rest, "samples": samples}


def case_reduced(w):
    for key, value in (("motion", "cinematic"), ("motion.open", "wipeRight"), ("motion.switch", "same_as_open")):
        effect(w, key, value)
    full = need(w, "motion.probe", effect="fade", placement="MdiMaximized")["resolved"]
    w.page.emulate_media(reduced_motion="reduce")
    try:
        time.sleep(1.5)
        low = need(w, "motion.probe", effect="fade", placement="MdiMaximized")["resolved"]
        state = {k: need(w, "style.state").get(k) for k in ("motionLevel", "fallbackReason")}
        verdict, why = judge_switch(False, *press_f6(w))  # wipeRight 그대로: 줄이면 fade 라 자리·크기가 그대로다(21 §7, zoomOut 은 낮춤 대상 아님)
    finally:
        w.page.emulate_media(reduced_motion="no-preference")
    time.sleep(1.5)
    back = need(w, "motion.probe", effect="fade", placement="MdiMaximized")["resolved"]
    if (full["open"], low["open"], low["switch"], back["open"]) != ("wipeRight", "fade", "fade", "wipeRight"):
        verdict, why = "FAIL", "해석값이 wipeRight → fade → wipeRight 가 아니다"
    return {"verdict": verdict, "why": why, "full": full, "reduced": low, "back": back, "state": state}


def case_bg_pick(w, image):
    effect(w, "decor", "image")  # 고르기 버튼은 장식이 image 일 때만 보인다
    need(w, "theme.openEditor")
    time.sleep(2)
    need(w, "ui.select", id="theme.tabArea", index=2)  # 디자인 테마 탭
    time.sleep(2)
    found = need(w, "ui.find", id="designTheme.btnBgPick")  # 첫 자리(스크롤 전)
    area = need(w, "ui.find", id="theme.tabArea")["bounds"]
    top, bottom = area["y"] + 60, area["y"] + area["h"] - 20  # 탭 머리 아래 스크롤 영역
    for _ in range(40):
        b = found["bounds"]
        x, y = b["x"] + b["w"] / 2, b["y"] + b["h"] / 2
        if b["w"] and top < y < bottom:
            break
        w.page.mouse.move(area["x"] + 30, (top + bottom) / 2)  # 값이 휠로 바뀌는 콤보를 피해 이름 열에서 굴린다
        w.page.mouse.wheel(0, 120 if y >= bottom else -120)
        time.sleep(1)
        found = need(w, "ui.find", id="designTheme.btnBgPick")
    w.shot("bg_editor")
    try:
        with w.page.expect_file_chooser(timeout=15000) as chooser:
            w.page.mouse.click(x, y)
        chooser.value.set_files(image)
    except PlaywrightError as e:
        return {"verdict": "UNDET", "why": "파일 고르기가 열리지 않았다: %s" % str(e)[:160], "point": [x, y]}
    state = {}
    for _ in range(20):
        time.sleep(1)
        state = need(w, "background.state")
        if state.get("ready") and state.get("file"):
            break
    ok = state.get("decorEffective") == "image" and state.get("ready") and state.get("file")
    return {"verdict": "PASS" if ok else "FAIL", "why": "선택 뒤 배경 상태", "state": state}


def case_bg_persist(w, picked):
    if picked.get("verdict") != "PASS":
        return {"verdict": "UNDET", "why": "배경 선택이 안 돼 잴 수 없다"}
    w.page.reload(wait_until="load", timeout=90000)
    ready(w)
    state = {}
    for _ in range(15):
        state = need(w, "background.state")
        if state.get("ready"):
            break
        time.sleep(1)
    ok = state.get("ready") and state.get("decorEffective") == "image" and state.get("file") == picked["state"]["file"]
    return {"verdict": "PASS" if ok else "FAIL", "why": "새로고침 뒤 배경 상태", "state": state}


def case_bg_area_only(w, persisted):
    if persisted.get("verdict") != "PASS":
        return {"verdict": "UNDET", "why": "배경 이미지가 없어 잴 수 없다"}
    two_docs(w)
    w.page.mouse.move(w.viewport[0] - 2, w.viewport[1] - 2)  # 도구 설명이 뜨지 않는 자리
    time.sleep(6)  # 새로고침 뒤 글자·창 배치가 멎을 때까지
    doc = active(w)  # 맨 위 창 — 다른 창·배경에 가리지 않는다
    b = need(w, "ui.find", id="PART_OuterBorder", doc=doc)["bounds"]
    inset = 6
    box = (int(b["x"]) + inset, int(b["y"]) + inset, min(int(b["x"] + b["w"]) - inset, w.viewport[0]), min(int(b["y"] + b["h"]) - inset, w.viewport[1]))
    shots = {"image": [], "none": []}
    for i in range(4):
        for decor in ("image", "none"):
            effect(w, "decor", decor)
            time.sleep(1.5)
            shots[decor].append(w.shot("bg_%s_%d" % (decor, i)))
    verdict, why = judge_area(shots["image"], shots["none"], box)
    return {"verdict": verdict, "why": why, "box": box}


def newer_sources(started):
    out = []
    for root, dirs, files in os.walk(os.path.join(UI, "src")):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
        out += [os.path.relpath(os.path.join(root, f), UI) for f in files if os.path.getmtime(os.path.join(root, f)) > started]
    return sorted(out)


def run(args, res):
    out = os.path.abspath(args.out_dir)
    os.makedirs(out, exist_ok=True)
    res["changed_after_build"] = newer_sources(time.mktime(time.strptime(args.build_started, "%Y-%m-%d %H:%M:%S")))
    image = os.path.join(out, "bg_fixture.png")
    img = Image.new("RGB", (640, 400), (230, 30, 160))
    img.paste((20, 190, 90), (320, 0, 640, 400))
    img.save(image)
    socketserver.TCPServer.allow_reuse_address = True
    w = Web(os.path.abspath(args.bundle), args.port, out, viewport=(1200, 800))
    cases = res["cases"]

    def case(name, fn, *a):
        try:
            cases[name] = fn(w, *a)
        except Missing as e:
            cases[name] = {"verdict": "NOT_IN_BUILD", "why": str(e)}
        except (RuntimeError, KeyError, StopIteration, PlaywrightError) as e:
            cases[name] = {"verdict": "ERROR", "why": repr(e)[:300]}
        print(name, cases[name]["verdict"], cases[name]["why"], flush=True)
        return cases[name]

    try:
        with sync_playwright() as p:
            w.browser = p.chromium.launch(headless=True, args=ARGS)
            w.page = w.browser.new_page(viewport=dict(zip(("width", "height"), w.viewport)))
            w.page.add_init_script(NO_FS_ACCESS)
            w.page.on("pageerror", lambda e: w.errors.append("pageerror: " + str(e)[:300]))
            w.page.on("console", lambda m: w.errors.append(m.text[:300]) if m.type == "error" else None)
            w.page.goto("http://127.0.0.1:%d/" % args.port, wait_until="load", timeout=90000)
            ready(w)
            two_docs(w)
            for i in range(5):  # 결함 판정은 반복 표본으로 한다
                case("ctrl_f6_%d" % (i + 1), case_switch, "same_as_open", True)
            case("ctrl_f6_none", case_switch, "none", False)
            case("reduced", case_reduced)
            picked = case("bg_pick", case_bg_pick, image)
            kept = case("bg_persist", case_bg_persist, picked)
            case("bg_area_only", case_bg_area_only, kept)
            case("title_tab", case_title_tab, need)
            case("favorite", case_favorite, need, ready)
            res["page_errors"] = w.errors[:20]
            w.browser.close()
    finally:
        w.httpd.shutdown()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--self-test", action="store_true")
    for name in ("--bundle", "--out-dir", "--build-started"):
        ap.add_argument(name)
    ap.add_argument("--port", type=int)
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    if not (args.bundle and args.out_dir and args.build_started and args.port):
        ap.error("--bundle --port --out-dir --build-started 가 모두 필요하다")
    if args.port in (5180, 5220):
        ap.error("5180·5220 은 쓰지 않는다")
    res = {"tool": os.path.basename(__file__), "bundle": os.path.abspath(args.bundle), "cases": {}}
    try:
        run(args, res)
    except (OSError, RuntimeError, PlaywrightError) as e:
        res["error"] = repr(e)[:400]
    rc = 2 if "error" in res else exit_code([c["verdict"] for c in res["cases"].values()])
    res["rc"], res["finished"] = rc, time.strftime("%Y-%m-%d %H:%M:%S")
    os.makedirs(args.out_dir, exist_ok=True)
    out = os.path.join(args.out_dir, "motion_web_check.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print("RESULT rc=%d %s %s" % (rc, res.get("error", ""), out), flush=True)
    return rc


if __name__ == "__main__":
    sys.exit(main())

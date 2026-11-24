#!/usr/bin/env python3
# 도움말 카탈로그 플랫폼 측정 — 내장 카탈로그 진행, 파일 카탈로그로 단계 추가·순서 변경·문구 변경, 대상 없음·숨김·조건 건너뛰기, 빈 카탈로그, 생성된 키 바인딩을 값으로 남긴다
"""
사용: PD_REMOTE_INFO=<임시 data>/remote-control.json python3 -B retest_tour_catalog.py --catalog src/Pd.Poc.Shell/Tour/tour_catalog.json --work <임시 폴더> --out logs/<이름>/<회차>/tour_catalog.json
앱은 먼저 임시 폴더·화면 밖으로 띄운다:
  run_desktop_bg.sh --conf run/<복사본> --no-kill -- --remote-control --multi-instance on --data-dir <임시> --profile-dir <임시>
      --bg-pos 21500,100 --primary-override 18000,0,6000,1440 --offscreen-guard on
규칙(결과 보기 전에 고정): 아래 CHECKS 이름이 모두 참이어야 한다. 키 입력은 진단 명령의 라우팅 이벤트와 명령 실행이며 실제 키가 아니다.
최소화·최대화 항목은 넣지 않는다. 격리되지 않은 앱은 rc=3 으로 거부하고, 겹침·전경은 GuardBreach 로 즉시 멈춘다.
종료 코드: 0=전부 참, 1=거짓 있음, 2=연결 실패, 3=실행 거부, 4=가드 중단.
"""
import argparse
import copy
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)
from offscreen_guard import OffscreenGuard, GuardBreach  # noqa: E402
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402

SETTLE_S = 0.35
MAX_STEPS = 60
DEMO_TITLE = "데모 단계 제목"
DEMO_BODY = "사전에 더한 데모 본문"


class Run:
    def __init__(self, remote, guard):
        self.remote = remote
        self.guard = guard
        self.results = {}

    def call(self, cmd, **kw):
        self.guard.check()
        try:
            return self.remote.call(cmd, **kw)
        except Exception as ex:  # noqa: BLE001 — 실패도 측정값이다
            return {"error": str(ex)}

    def check(self, name, ok, value):
        self.results[name] = {"ok": bool(ok), "value": value}
        print("%s %s %s" % ("PASS" if ok else "FAIL", name, json.dumps(value, ensure_ascii=False)[:300]))

    def state(self):
        state = self.call("tour.state")
        return state[0] if isinstance(state, list) and state else None

    def visible(self, control_id):
        node = self.call("ui.find", id=control_id)
        return bool(isinstance(node, dict) and node.get("effectivelyVisible"))

    def text(self, control_id):
        node = self.call("ui.find", id=control_id)
        return node.get("text") if isinstance(node, dict) else None

    def walk(self):
        """메인 창 투어를 시작해 마지막 레이어까지 간다. (단계, 번호, 전체) 목록과 마지막 상태를 돌려준다."""
        self.call("tour.start")
        time.sleep(SETTLE_S)
        trail = []
        for _ in range(MAX_STEPS):
            state = self.state()
            if not state or state.get("onShortcutLayer"):
                return trail, state
            trail.append({"id": state.get("stepId"), "index": state.get("index"), "count": state.get("count"), "title": self.text("tour.title")})
            self.call("tour.next")
            time.sleep(SETTLE_S)
        return trail, self.state()

    def end(self):
        self.call("tour.end")
        time.sleep(SETTLE_S)

    def load(self, data, work, name):
        path = os.path.join(work, name)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False)
        win = subprocess.check_output(["wslpath", "-w", path], text=True).strip()
        return self.call("tour.catalog.load", path=win)


def main_step_ids(catalog):
    return [s["id"] for s in catalog["steps"] if s["scope"] == "MainWindow"]


def numbered(trail):
    return [t["index"] for t in trail] == list(range(1, len(trail) + 1)) and len({t["count"] for t in trail}) <= 1


def measure_embedded(run, catalog):
    info = run.call("tour.catalog.info")
    run.check("내장 카탈로그 읽음(오류 0, 파일과 수 일치)",
              isinstance(info, dict) and info.get("errors") == [] and len(info.get("stepIds") or []) == len(catalog["steps"])
              and info.get("shortcuts") == len(catalog["shortcuts"]) and info.get("features") == len(catalog["features"]), info)
    trail, last = run.walk()
    ids = [t["id"] for t in trail]
    run.check("진행 순서가 카탈로그 순서(대상 없는 단계만 빠짐)", ids == [i for i in main_step_ids(catalog) if i in ids] and len(ids) >= 8, ids)
    run.check("번호가 실제 수를 따름", numbered(trail) and (last or {}).get("index") == (last or {}).get("count") == len(trail) + 1, {"trail": [(t["index"], t["count"]) for t in trail], "last": last})
    run.check("마지막 단축키 레이어 표시", run.visible("tour.shortcuts") and run.visible("tour.key.next") and run.visible("tour.feature.autosave"), None)
    rows = {name: run.text("tour.feature.menu." + name) for name in ("File", "Window", "View", "Sys")}
    run.check("메뉴 줄이 메뉴 카탈로그에서 생성됨", all(run.visible("tour.feature.menu." + name) for name in rows), rows)
    run.check("없는 기능 줄 없음(벤치·열린 창 목록)", not run.visible("tour.feature.bench"), None)
    run.end()


def measure_input(run):
    run.call("docs.open", kind="grid", title="cat-a", allowDuplicate=True)
    time.sleep(SETTLE_S)
    run.call("docs.open", kind="gantt", title="cat-b", allowDuplicate=True)
    time.sleep(SETTLE_S)
    run.call("tour.start")
    time.sleep(SETTLE_S)
    probe = run.call("tour.probe", id="shell.btnTheme")
    run.check("투어 중 본창 입력 거부", isinstance(probe, dict) and probe.get("blocked") is True, probe)
    keys = run.call("tour.key", key="F4", ctrl=True)
    run.check("투어 중 Ctrl+F4 로 문서 안 닫힘", isinstance(keys, dict) and keys.get("docsBefore") == keys.get("docsAfter") and keys.get("sessionAlive") is True, keys)
    run.call("tour.skip")
    time.sleep(SETTLE_S)
    run.check("건너뛰기 → 마지막 레이어", (run.state() or {}).get("onShortcutLayer") is True, run.state())
    esc = run.call("tour.esc")
    run.check("Esc 로 종료, 문서 수 불변(라우팅 이벤트)", isinstance(esc, dict) and esc.get("ended") is True and esc.get("docsBefore") == esc.get("docsAfter"), esc)
    run.check("종료 뒤 세션 0", run.call("tour.state") == [], None)


def measure_bindings(run, catalog):
    expected = sorted(s["keys"] for s in catalog["shortcuts"] if s.get("binding"))
    found = run.call("tour.catalog.bindings")
    rows = (found or {}).get("bindings") if isinstance(found, dict) else None
    run.check("KeyBinding 이 카탈로그에서 생성됨", isinstance(rows, list) and sorted(r["gesture"] for r in rows) == expected and all(r["hasCommand"] for r in rows), {"found": rows, "expected": expected})
    before = (run.call("docs.list") or {})
    active_before = run.call("tour.snapshot").get("activeDocument")
    ran = run.call("tour.catalog.bindings", run="Ctrl+F6")
    time.sleep(SETTLE_S)
    active_after = run.call("tour.snapshot").get("activeDocument")
    run.check("생성된 Ctrl+F6 명령이 활성 문서를 바꿈(명령 실행)", isinstance(ran, dict) and ran.get("ran") is True and active_before != active_after, {"before": active_before, "after": active_after, "docs": len(before.get("documents", [])) if isinstance(before, dict) else None})


def measure_edit(run, catalog, work):
    data = copy.deepcopy(catalog)
    steps = data["steps"]
    first, second = steps[0], steps[1]
    steps[0], steps[1] = second, first
    steps.insert(2, {"id": "demo.added", "target": "shell.btnTheme", "text": "demo.added", "scope": "MainWindow", "modes": "Wide"})
    data["texts"] = {lang: {"sysview.tour.demo.added.title": DEMO_TITLE, "sysview.tour.demo.added.body": DEMO_BODY} for lang in ("ko", "en")}
    run.load(data, work, "edit.json")
    trail, _ = run.walk()
    ids = [t["id"] for t in trail]
    run.check("순서 변경 반영", ids[:2] == [second["id"], first["id"]], ids[:4])
    run.check("단계 추가 반영(번호 포함)", ids[2:3] == ["demo.added"] and numbered(trail), [(t["id"], t["index"], t["count"]) for t in trail[:4]])
    run.check("추가 단계 문구 반영", any(t["id"] == "demo.added" and t["title"] == DEMO_TITLE for t in trail), [t["title"] for t in trail[:4]])
    run.end()


def measure_skip(run, catalog, work):
    base_trail, _ = run.walk()
    run.end()
    data = copy.deepcopy(catalog)
    extra = [
        {"id": "skip.missing", "target": "shell.removedDemo", "text": "main.menu", "scope": "MainWindow", "modes": "Wide"},
        {"id": "skip.hidden", "target": "shell.navTabBar", "text": "main.menu", "scope": "MainWindow", "modes": "Wide"},
        {"id": "skip.condition", "target": "shell.btnTheme", "text": "main.menu", "scope": "MainWindow", "modes": "Wide", "requires": "noSuchCondition"},
        {"id": "skip.permission", "target": "shell.btnTheme", "text": "main.menu", "scope": "MainWindow", "modes": "Nav"},
        {"id": "skip.platform", "target": "shell.btnTheme", "text": "main.menu", "scope": "MainWindow", "modes": "Wide", "platforms": "Web"},
    ]
    data["steps"][1:1] = extra
    run.load(data, work, "skip.json")
    trail, last = run.walk()
    ids = [t["id"] for t in trail]
    reasons = {k["id"]: k["reason"] for k in (last or {}).get("skipped") or []}
    run.check("없는·숨은·조건 불일치 단계가 오류 없이 빠짐", ids == [t["id"] for t in base_trail] and not any(i.startswith("skip.") for i in ids), ids)
    run.check("번호는 건너뛴 뒤의 수", numbered(trail) and (last or {}).get("count") == len(base_trail) + 1, {"count": (last or {}).get("count"), "base": len(base_trail)})
    expected = {"skip.missing": "target-missing", "skip.condition": "requires:noSuchCondition", "skip.permission": "mode", "skip.platform": "platform"}
    run.check("건너뜀 사유 기록", all(reasons.get(k) == v for k, v in expected.items()) and reasons.get("skip.hidden") in ("target-hidden", "target-missing"), reasons)
    run.end()


def measure_empty(run, work):
    path = os.path.join(work, "broken.json")
    with open(path, "w", encoding="utf-8") as f:
        f.write('{"version": 1, "steps": [')
    win = subprocess.check_output(["wslpath", "-w", path], text=True).strip()
    info = run.call("tour.catalog.load", path=win)
    run.check("깨진 카탈로그는 오류로 기록", isinstance(info, dict) and len(info.get("errors") or []) >= 1 and info.get("stepIds") == [], info)
    started = run.call("tour.start")
    time.sleep(SETTLE_S)
    state = run.state()
    run.check("빈 카탈로그에서도 투어가 시작돼 마지막 레이어를 보임", isinstance(started, dict) and started.get("started") is True and (state or {}).get("onShortcutLayer") is True and run.visible("tour.shortcuts"), state)
    run.check("빈 카탈로그에서도 문의·메뉴 줄 표시", run.visible("tour.feature.menu.File"), None)
    esc = run.call("tour.esc")
    run.check("빈 카탈로그에서도 Esc 종료", isinstance(esc, dict) and esc.get("ended") is True, esc)


def drawer_labels(run):
    """서랍 항목은 버튼이라 ui.find 의 text 가 비어 있다. 트리에서 항목 안쪽 글자를 읽는다."""
    labels = {}

    def walk(node, inside=None):
        node_id = node.get("id") or ""
        current = node_id if node_id.startswith("shell.navDrawer.") else inside
        if current and node.get("text"):
            labels.setdefault(current, node["text"])
        for child in node.get("children") or []:
            walk(child, current)

    tree = run.call("tree.get", depth=40)
    for root in (tree or {}).get("tree", []) if isinstance(tree, dict) else []:
        walk(root)
    return labels


def measure_nav(run, catalog):
    run.call("ui.style", set="mobile")
    time.sleep(1.2)
    trail, last = run.walk()
    ids = [t["id"] for t in trail]
    nav = [s["id"] for s in catalog["steps"] if s["scope"] == "Nav"]
    run.check("모바일형 단계는 NAV 집합", bool(ids) and all(i in nav for i in ids) and numbered(trail), ids)
    run.check("모바일형 기능 줄에 PC 전용 없음", not run.visible("tour.feature.window") and not run.visible("tour.feature.magnet"), None)
    run.end()
    run.call("ui.click", id="shell.btnDrawer")
    time.sleep(0.8)
    labels = drawer_labels(run)
    wanted = ("shell.navDrawer." + name for name in ("File", "Window", "View", "Sys"))
    run.check("모바일형 서랍에 액세스 키 표기 없음", all(labels.get(w) for w in wanted) and not any("_" in v for v in labels.values()), labels)
    run.call("ui.click", id="shell.btnDrawer")
    time.sleep(0.5)
    run.call("ui.style", set="auto")
    time.sleep(1.2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--catalog", required=True)
    ap.add_argument("--work", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    if "/logs/ui28/" in os.path.abspath(args.out).replace("\\", "/"):
        print("logs/ui28 쓰기 금지")
        return 3
    remote = Remote()
    status = remote.call("status")
    if not status:
        return 2
    pid = status.get("pid")
    iso = retest_user_files.isolation(pid)
    if not (iso and iso[0]):
        print("격리되지 않은 앱이라 거부한다: %s" % (iso,))
        return 3
    with open(args.catalog, encoding="utf-8") as f:
        catalog = json.load(f)
    os.makedirs(args.work, exist_ok=True)
    out = {"pid": pid}
    rc = 0
    try:
        with OffscreenGuard(pid) as guard:
            run = Run(remote, guard)
            measure_embedded(run, catalog)
            measure_input(run)
            measure_bindings(run, catalog)
            measure_edit(run, catalog, args.work)
            run.call("tour.catalog.reset")
            measure_skip(run, catalog, args.work)
            measure_empty(run, args.work)
            reset = run.call("tour.catalog.reset")
            run.check("내장 카탈로그로 복귀", isinstance(reset, dict) and reset.get("errors") == [], {"steps": len((reset or {}).get("stepIds") or [])})
            measure_nav(run, catalog)
            guard.check()
            out["results"] = run.results
        out["guard"] = guard.summary()
        rc = 0 if all(v["ok"] for v in out["results"].values()) else 1
    except GuardBreach as ex:
        out["breach"] = str(ex)
        out["results"] = run.results
        rc = 4
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    failed = [k for k, v in out["results"].items() if not v["ok"]]
    print("항목 %d · 실패 %d %s · guard=%s" % (len(out["results"]), len(failed), failed, out.get("guard") or out.get("breach")))
    return rc


if __name__ == "__main__":
    sys.exit(main())

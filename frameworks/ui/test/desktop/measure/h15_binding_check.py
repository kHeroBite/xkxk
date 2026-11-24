#!/usr/bin/env python3
# h15 빈 패널 머리줄 Binding 오류 점검 실행기 — 격리 기동한 앱에서 문서↔패널 합류·복귀·전체 닫기를 돌리고 앱 로그의 Binding 오류 건수로 판정한다
"""
사용:
  python3 -B h15_binding_check.py --run-dir <bin/run/<이름>/net11.0> --work <작업폴더> --out <결과.json>
  python3 -B h15_binding_check.py --self-test
판정:
  원 H-15 두 줄 = 로그의 [Binding] 오류 중 'ActiveDockable.Title' 또는 'CloseDockable' 을 담은 줄. 0건이면 PASS.
  DockAdornerHost 줄은 따로 센다(별건 — 판정에는 쓰지 않고 보고만 한다).
rc: 0=PASS, 1=FAIL, 2=실행 오류, 3=판정불가(기동은 됐으나 시나리오·로그를 확인하지 못함)
안전: 앱은 mdi_drag_app.launch 로 임시 폴더·화면 밖에 띄우고, 종료도 명령줄에 자기 임시 폴더가 있는 PID 만 한다. 실입력은 쓰지 않는다.
"""
import argparse
import json
import os
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

SETTLE_S = 0.8
PANES = (("LeftPane", "Explorer"), ("NotificationPane", "Notifications"))
H15_KEYS = ("ActiveDockable.Title", "CloseDockable")
ADORNER_KEY = "DockAdornerHost"


def is_binding(line):
    return "[Binding]" in line and "error" in line.lower()


def classify(lines):
    """로그 줄 목록 → (원 H-15 줄, DockAdornerHost 줄, 그 밖의 Binding 오류 줄)."""
    h15, adorner, other = [], [], []
    for ln in lines:
        if not is_binding(ln):
            continue
        if any(k in ln for k in H15_KEYS):
            h15.append(ln.strip())
        elif ADORNER_KEY in ln:
            adorner.append(ln.strip())
        else:
            other.append(ln.strip())
    return h15, adorner, other


def read_new(log_path, pos):
    """로그 파일의 pos 이후 줄과 새 위치."""
    if not os.path.exists(log_path):
        return [], pos
    with open(log_path, "rb") as f:
        f.seek(pos)
        data = f.read()
    return data.decode("utf-8", errors="replace").splitlines(), pos + len(data)


def judge(result):
    """결과 dict → (verdict, rc). measured=False 이거나 시나리오 단계가 모두 실패면 판정불가."""
    if not result.get("measured"):
        return "NOT_MEASURED", 3
    if result.get("h15_count", 0) > 0:
        return "FAIL", 1
    return "PASS", 0


def run_scenario(call, log_path):
    phases, ok_steps, pos = {}, [], 0
    _, pos = read_new(log_path, 0)

    def phase(name, fn):
        nonlocal pos
        res = fn()
        time.sleep(SETTLE_S)
        lines, pos = read_new(log_path, pos)
        h15, adorner, other = classify(lines)
        phases[name] = {"result": json.dumps(res, ensure_ascii=False)[:300], "h15_lines": h15, "adorner_lines": adorner, "other_binding_lines": other}
        return res

    phase("close_all_start", lambda: call("docs.close", all="true"))
    for pane, _tool in PANES:
        before = {d["id"] for d in call("docs.list") or []}
        phase(pane + ":open_doc", lambda: call("docs.open", kind="grid", allowDuplicate=True))
        time.sleep(SETTLE_S)
        new = [d["id"] for d in call("docs.list") or [] if d["id"] not in before]
        if not new:
            phases[pane + ":open_doc"]["note"] = "새 문서 id 를 찾지 못함"
            continue
        doc = new[0]
        j = phase(pane + ":doc_into_panel", lambda: call("panel.join", id=pane, doc=doc))
        ok_steps.append(bool(j and j.get("status") == "Joined"))
        phase(pane + ":doc_out_returnDoc", lambda: call("panel.returnDoc", doc=doc))
        j = phase(pane + ":doc_in_again", lambda: call("panel.join", id=pane, doc=doc))
        ok_steps.append(bool(j and j.get("status") == "Joined"))
        phase(pane + ":doc_out_again", lambda: call("panel.returnDoc", doc=doc))
        phase(pane + ":no_active_doc_close_all", lambda: call("docs.close", all="true"))
    phase("idle_after", lambda: call("status"))
    return phases, ok_steps


def run(args):
    import mdi_drag_app as app
    os.makedirs(args.work, exist_ok=True)
    try:
        pid, info, log_path = app.launch(args.run_dir, args.work)
    except SystemExit as e:  # launch 는 문자열로 SystemExit 한다 — rc 1(FAIL)과 섞이지 않게 실행 오류로 돌린다
        return {"verdict": "ERROR", "error": str(e)}, 2
    marker = app.win(os.path.dirname(info))  # 앱 명령줄에는 Windows 경로가 들어 있다
    result = {"run_dir": args.run_dir, "pid": pid, "log": log_path, "measured": False}
    try:
        from retest_remote import Remote
        r = Remote()
        phases, ok_steps = run_scenario(r.call, log_path)
        result["phases"] = phases
        h15 = [ln for p in phases.values() for ln in p["h15_lines"]]
        adorner = [ln for p in phases.values() for ln in p["adorner_lines"]]
        other = [ln for p in phases.values() for ln in p["other_binding_lines"]]
        result.update({"h15_count": len(h15), "adorner_count": len(adorner), "other_binding_count": len(other),
                       "h15_lines": h15, "joined_ok": ok_steps})
        result["measured"] = bool(ok_steps) and all(ok_steps) and os.path.exists(log_path)
        if not result["measured"]:
            result["reason"] = "panel.join 이 Joined 가 아니거나 로그가 없어 판정불가"
    except Exception as e:  # noqa: BLE001
        result["error"] = repr(e)
        result["verdict"] = "ERROR"
        return _finish(result, pid, marker, app, 2)
    verdict, rc = judge(result)
    result["verdict"] = verdict
    return _finish(result, pid, marker, app, rc)


def _finish(result, pid, marker, app, rc):
    result["stop"] = app.stop(pid, marker)
    return result, rc


def self_test():
    lines = [
        "[Binding]An error occurred binding 'ActiveDockable.Title' to X: null",
        "[Binding]An error occurred binding 'CloseDockable' to Y: null",
        "[Binding]An error occurred binding 'DockAdornerHost' to '$visualParent[MdiDocumentControl]': 'Ancestor not found.'",
        "[Binding]An error occurred binding 'Other' to Z",
        "일반 로그 ActiveDockable.Title 은 Binding 줄이 아니다",
    ]
    h15, ad, other = classify(lines)
    checks = [("h15=2", len(h15) == 2), ("adorner=1", len(ad) == 1), ("other=1", len(other) == 1)]
    checks.append(("PASS rc0", judge({"measured": True, "h15_count": 0}) == ("PASS", 0)))
    checks.append(("adorner 만이면 PASS", judge({"measured": True, "h15_count": 0, "adorner_count": 3}) == ("PASS", 0)))
    checks.append(("FAIL rc1", judge({"measured": True, "h15_count": 2}) == ("FAIL", 1)))
    checks.append(("판정불가 rc3", judge({"measured": False, "h15_count": 0}) == ("NOT_MEASURED", 3)))
    with tempfile.NamedTemporaryFile("w", suffix=".log", delete=False, encoding="utf-8") as f:
        f.write("a\n")
        path = f.name
    _, pos = read_new(path, 0)
    with open(path, "a", encoding="utf-8") as f:
        f.write(lines[0] + "\n")
    new, _ = read_new(path, pos)
    os.unlink(path)
    checks.append(("로그 증분 읽기", len(new) == 1 and "ActiveDockable.Title" in new[0]))
    bad = [n for n, ok in checks if not ok]
    for n, ok in checks:
        print(("PASS " if ok else "FAIL ") + n)
    return 0 if not bad else 1


def main():
    ap = argparse.ArgumentParser(description="h15 빈 패널 머리줄 Binding 오류 점검(rc 0=PASS 1=FAIL 2=실행오류 3=판정불가)")
    ap.add_argument("--run-dir", help="bin/run/<이름>/net* 폴더(xkxk.frameworks.ui.desktop.exe 가 있는 곳)")
    ap.add_argument("--work", help="임시 데이터·로그 작업 폴더")
    ap.add_argument("--out", help="결과 JSON 경로")
    ap.add_argument("--self-test", action="store_true", help="앱 없이 판정·집계 로직만 자가시험")
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    if not (args.run_dir and args.work):
        ap.error("--run-dir 와 --work 가 필요하다")
    try:
        result, rc = run(args)
    except Exception as e:  # noqa: BLE001
        result, rc = {"verdict": "ERROR", "error": repr(e)}, 2
    text = json.dumps(result, ensure_ascii=False, indent=1)
    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        with open(args.out, "w", encoding="utf-8") as f:
            f.write(text)
    print(text)
    return rc


if __name__ == "__main__":
    sys.exit(main())

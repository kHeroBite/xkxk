# 사이클27 판정기(B1~B4) 공용 — RESULTS.json 항목 기록·evidence/ 경로 거부·증거 목록·앱 렌더 스테이지·시스템 글자 크기 읽기
"""
menu_icons_judge.py · settings_modal_judge.py · main_font_wheel_judge.py · ../../web/measure/menu_modal_font_web.py 가 import 한다.
직접 실행은 --self-test 만(앱 없이 기록·경로 규칙을 본다).
RESULTS.json 형식: {"items": {"B1": {"verdict", "evidence": [절대경로…], "method": "10자 이상", "recorded"}}} — 항목마다 잠금 아래 원자적으로 덮어쓴다.
"""
import fcntl
import glob
import json
import os
import shutil
import subprocess
import sys
import time

import judge_session as session

HERE = os.path.dirname(os.path.abspath(__file__))
STAGE = os.path.abspath(os.path.join(HERE, "..", "..", "..", "logs", "c27_stage"))  # 앱(Windows)이 쓸 수 있는 NTFS 자리(ext4 \\wsl 경로는 못 씀)
TOP_MENU_ITEM = "shell.mnuFile"  # 메인 크롬 글자 크기를 읽고 휠을 굴리는 대표 자리(메뉴 줄 '파일')


def out_problem(path, what="--out"):
    """결과 경로를 쓸 수 없는 이유. 쓸 수 있으면 None."""
    if not path:
        return "필수 인자 없음: " + what
    if "evidence" in os.path.abspath(path).split(os.sep):
        return "%s 가 정리 대상 evidence/ 아래다: %s" % (what, path)
    return None


def evidence_of(out, tool, started):
    """결과 JSON + 이번 실행이 남긴 캡처(<out 폴더>/<tool>_shots, started 이후)."""
    files = [os.path.abspath(out)]
    shots = os.path.join(os.path.dirname(os.path.abspath(out)), tool + "_shots")
    if os.path.isdir(shots):
        files += sorted(os.path.join(shots, f) for f in os.listdir(shots) if os.path.getmtime(os.path.join(shots, f)) >= started - 1)
    return files


def record(results, item, verdict, evidence, method):
    """RESULTS.json items[item] 를 잠금 아래 원자적으로 갱신한다(다른 판정기 항목은 그대로)."""
    os.makedirs(os.path.dirname(os.path.abspath(results)), exist_ok=True)
    with open(results + ".lock", "a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        data = {}
        if os.path.isfile(results):
            with open(results, encoding="utf-8") as f:
                data = json.load(f)
        data.setdefault("items", {})[item] = {"verdict": verdict, "evidence": evidence, "method": method,
                                              "recorded": time.strftime("%Y-%m-%dT%H:%M:%S%z")}
        tmp = results + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
        os.replace(tmp, results)


def record_from_out(args, tool, items, started):
    """--out 결과의 사례를 접두어로 나눠 RESULTS 항목별 판정으로 넣는다. items = {항목: (사례 접두어, method)}."""
    with open(args.out, encoding="utf-8") as f:
        res = json.load(f)
    cases, ev = res.get("cases") or {}, evidence_of(args.out, tool, started)
    for item, (prefix, method) in items.items():
        sub = {k: v for k, v in cases.items() if k.startswith(prefix)}
        record(args.results, item, session.overall(sub) if sub else res.get("verdict", "INVALID"), ev, method)


def run_c27(args, tool, judge, items, settings=None):
    """경로 확인 → judge_session.run(데스크톱 또는 웹) → --results 가 있으면 항목별 기록. 종료 코드는 judge_session 과 같다."""
    bad = out_problem(args.out) or (out_problem(args.results, "--results") if args.results else None)
    if bad:
        print(json.dumps({"tool": tool, "verdict": "DEFERRED", "reason": bad}, ensure_ascii=False))
        return session.common.RC["DEFERRED"]
    started = time.time()
    rc = session.run(args, tool, judge, settings)
    if args.results:
        record_from_out(args, tool, items, started)
    return rc


def add_c27_args(ap):
    ap.add_argument("--results", help="RESULTS.json(주면 items 에 판정을 넣는다, evidence/ 아래 거부)")
    return ap


def verdict(problems, why, **data):
    return dict(data, verdict="FAIL" if problems else "PASS", why=why, problems=problems)


def win_path(path):
    return subprocess.check_output(["wslpath", "-w", path], text=True).strip()


def stage_render(s, name, **kw):
    """menu.render 를 NTFS 스테이지에 쓰게 하고 캡처 폴더로 옮긴다 → (경로, 응답)."""
    os.makedirs(STAGE, exist_ok=True)
    stage = os.path.join(STAGE, "%d_%s.png" % (os.getpid(), name))
    r = s.call("menu.render", out=win_path(stage), **kw)
    dst = os.path.join(s.shots, name + ".png")
    shutil.move(stage, dst)
    return dst, r


def find_key(node, key):
    """JSON 트리에서 key(대소문자 무시)의 첫 숫자 값. 없으면 None."""
    if isinstance(node, dict):
        for k, v in node.items():
            if k.lower() == key.lower() and isinstance(v, (int, float)):
                return float(v)
        for v in node.values():
            hit = find_key(v, key)
            if hit is not None:
                return hit
    elif isinstance(node, list):
        for v in node:
            hit = find_key(v, key)
            if hit is not None:
                return hit
    return None


def saved_shell_size(info):
    """격리 data-dir 아래 가장 최근 settings.json 의 ShellFontSize(저장값). 파일·값이 없으면 None."""
    files = glob.glob(os.path.join(os.path.dirname(os.path.abspath(info)), "**", "settings.json"), recursive=True)
    for path in sorted(files, key=os.path.getmtime, reverse=True):
        try:
            with open(path, encoding="utf-8-sig") as f:
                v = find_key(json.load(f), "ShellFontSize")
        except (OSError, ValueError):
            continue
        if v is not None:
            return v
    return None


def shown_font(s, cid=TOP_MENU_ITEM, **scope):
    """컨트롤의 실제 FontSize(ui.find props 문자열 → 숫자). 못 읽으면 None."""
    raw = (s.call("ui.find", id=cid, props="FontSize", **scope).get("props") or {}).get("FontSize")
    try:
        return float(str(raw).replace(",", "."))
    except (TypeError, ValueError):
        return None


def set_verdict(results, item, verdict_, reader):
    """판독자 판정을 RESULTS 항목에 넣는다(증거·method 유지, method 끝에 판독자 기록)."""
    with open(results, encoding="utf-8") as f:
        cur = json.load(f)["items"][item]
    record(results, item, verdict_, cur["evidence"], cur["method"] + " | 판독: %s → %s" % (reader, verdict_))


def self_test():
    import tempfile

    tmp = tempfile.mkdtemp()
    res = os.path.join(tmp, "RESULTS.json")
    for item, v, ev in (("B1", "PASS", "/x"), ("B2", "FAIL", "/y")):
        record(res, item, v, [ev], "방법 서술 열 자 이상")
    items = json.load(open(res, encoding="utf-8"))["items"]
    os.makedirs(os.path.join(tmp, "p", "settings"))
    with open(os.path.join(tmp, "p", "settings", "settings.json"), "w", encoding="utf-8") as f:
        json.dump({"Theme": "Dark", "Shell": {"ShellFontSize": 16}}, f)
    saved = saved_shell_size(os.path.join(tmp, "remote-control.json"))
    shots = os.path.join(tmp, "t_shots")
    os.makedirs(shots)
    open(os.path.join(shots, "a.png"), "w").close()
    ev = evidence_of(os.path.join(tmp, "o.json"), "t", time.time() - 5)
    shutil.rmtree(tmp)
    return session.run_cases([
        ("evidence/ 경로 거부", out_problem("/a/evidence/b.json") is not None),
        ("test_artifacts 경로 허용", out_problem("/a/test_artifacts/c27/b.json") is None),
        ("--out 없음 거부", out_problem(None) is not None),
        ("RESULTS 두 항목 보존", items["B1"]["verdict"] == "PASS" and items["B2"]["evidence"] == ["/y"]),
        ("method·recorded 기록", len(items["B1"]["method"]) >= 10 and items["B1"]["recorded"]),
        ("settings.json 중첩 키 읽기", saved == 16.0),
        ("키 없음 None", find_key({"a": [{"b": 1}]}, "ShellFontSize") is None),
        ("증거 = 결과 + 이번 캡처", len(ev) == 2 and ev[1].endswith("a.png")),
        ("판정 값", verdict({}, "w")["verdict"] == "PASS" and verdict({"x": 1}, "w")["verdict"] == "FAIL"),
    ])


if __name__ == "__main__":
    if "--set-verdict" in sys.argv:  # --set-verdict <RESULTS.json> <항목> <PASS|FAIL> <판독자·시각>
        i = sys.argv.index("--set-verdict")
        bad = out_problem(sys.argv[i + 1], "RESULTS")
        if bad or sys.argv[i + 3] not in ("PASS", "FAIL") or len(sys.argv) < i + 5:
            print(bad or "사용: --set-verdict <RESULTS.json> <항목> <PASS|FAIL> <판독자·시각>")
            sys.exit(2)
        set_verdict(sys.argv[i + 1], sys.argv[i + 2], sys.argv[i + 3], sys.argv[i + 4])
        sys.exit(0)
    sys.exit(self_test() if "--self-test" in sys.argv else 2)

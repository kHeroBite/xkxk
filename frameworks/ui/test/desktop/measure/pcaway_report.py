#!/usr/bin/env python3
# PC 비움 결과 어댑터 — 기존 측정 도구의 원자료를 pcaway_gate 형식 결과(logs/pcaway/<ID>.json)로 옮긴다. 값을 만들거나 기준을 낮추지 않으며 없는 값은 미측정 실패로 남긴다
"""
사용:
  pcaway_report.py safety-begin --state <상태.json>             측정 직전 사용자 Pd 창·파일 값을 저장(읽기 전용)
  pcaway_report.py safety-end --state <상태.json>               측정 직후 값을 읽어 안전 값을 저장
  pcaway_report.py emit --item A01 --raw main=<원자료.json> --state <상태.json> --build-file <산출물> --writer otest-d --phase discovery
        [--script <도구>] [--rc plan=0] [--capture <png>] [--measure-file <otest 측정.json>]
  pcaway_report.py s2 --state <상태.json> --writer otest-d --phase final     S2 측정(읽기 전용, 사용자 PC 조작 없음)
  pcaway_report.py matrix [--out <표.md>]                        SPEC 전 항목의 결과 작성 경로·없는 측정 이름 표
  pcaway_report.py --self-test
--measure-file 은 도구 원자료에 없는 측정 이름을 otest 가 직접 잰 값으로 넣는 통로다. 형식 {"measurements":[{"name","value","pass","source"}]}. source 필수, SPEC 밖 이름과 대응표 값 덮어쓰기는 거부한다.
측정 구간(복귀 표지 뒤 주입 검사에 쓴다)은 측정 파일의 measured_started_at·measured_finished_at, safety-begin~safety-end, 원자료 전부의 started_at·finished_at 을 합쳐 결과에 남긴다. 셋 다 없으면 복귀 표지가 있는 결과는 FAIL 이다.
전용 도구가 있는 항목(retest_real_input·retest_product_launch·retest_title_strip)은 emit 을 거부한다.
캡처 판독 기록은 retest_real_input.py --annotate 로 넣는다. 종료 코드: 0=측정 통과, 1=실패 있음, 3=거부, 2=판정기 없음(matrix).
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
import retest_product_launch as product  # noqa: E402
import retest_title_strip as strip  # noqa: E402
from pcaway_report_map import MAP  # noqa: E402

MIN_DISK_GB = 15
DEDICATED = {**{i: "retest_real_input.py" for i in ri.REQ}, **{i: "retest_product_launch.py" for i in product.REQ}, **{i: "retest_title_strip.py" for i in strip.REQ},
             **{i: "retest_pcaway_extra.py" for i in ("F03", "F06", "F10", "F15", "B3")}}
STATIC_ONLY = "결과 JSON 불필요(판정기가 소스·git 만 본다)"
ROLES = ("desktop_work", "desktop_product", "android_debug", "android_release", "android_prod", "web_diag", "web_prod")


def write_json(path, doc):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)


def safety_begin(args):
    if not ri.resolve_user_pid(args):
        return 3
    write_json(args.state, {"user_pid": args.user_pid, "measured_started_at": ri.now_iso(), "before": ri.snapshot_user(args.user_pid), "fg_before": fg_pid()})
    print("저장", args.state)
    return 0


def fg_pid():
    text = ri.run_ps_file(ri.FG_INFO).stdout
    m = ri.re.search(r"pid=(\d+)", text)
    return int(m.group(1)) if m else None


def safety_end(args):
    with open(args.state, encoding="utf-8") as f:
        doc = json.load(f)
    before, after, fg = doc["before"], ri.snapshot_user(doc["user_pid"]), fg_pid()
    before["rects"] = [list(r) for r in before["rects"]]
    after["rects"] = [list(r) for r in after["rects"]]
    # input_leak: 측정 도구가 입력을 보내지 않거나 보낸 입력이 사용자 Pd 로 갔는지는 이 끝값으로만 본다(도중 잠깐의 전경 변화는 못 본다)
    leak = fg == doc["user_pid"] and doc.get("fg_before") != doc["user_pid"]
    # user_pd_untouched 는 present_untouched·absent_both 두 경우에만 참이다(retest_real_input.user_pd_safety, team-lead 계약 해석 11-07)
    doc.update(measured_finished_at=ri.now_iso(), after=after, fg_after=fg, safety=dict(ri.user_pd_safety(before, after),
                                                user_files_same=before["files"] == after["files"] and all(before["files"].values()), input_leak=leak, user_returned=ri.away.user_returned()))
    write_json(args.state, doc)
    print(json.dumps(doc["safety"], ensure_ascii=False))
    return 0


def parse_time(value):
    try:
        t = datetime.fromisoformat(value)
    except (TypeError, ValueError):
        return None
    return t if t.tzinfo else None


def measured_window(sources, raws):
    """측정 구간(시작, 끝 ISO). sources(측정 파일·safety 상태)의 measured_*, 원자료 전부의 started_at·finished_at 중 온전한 구간이 있어야 낸다. 없으면 None 이고 값을 채우지 않는다.
    일부 원자료에만 있는 시각은 구간을 넓히는 데만 쓴다. 측정 시작을 뒤로 당겨 주입을 가리지 못하게 하기 위해서다."""
    spans, loose = [], []
    for src in sources:
        a, b = parse_time((src or {}).get("measured_started_at")), parse_time((src or {}).get("measured_finished_at"))
        if a and b:
            spans += [a, b]
    docs = [d for d in raws.values() if isinstance(d, dict)]
    pairs = [(parse_time(d.get("started_at")), parse_time(d.get("finished_at"))) for d in docs]
    if pairs and len(docs) == len(raws) and all(a and b for a, b in pairs):
        spans += [t for pair in pairs for t in pair]
    if not spans:
        return None
    loose = [t for pair in pairs for t in pair if t]
    every = spans + loose
    return min(every).isoformat(), max(every).isoformat()


def finish(res, gate, spec, measured=None):
    """Result.write 는 복귀 표지만 보고 FAIL 을 쓴다. 판정기 returned_block 으로 축별 판단을 받아 같은 결론을 결과에 남긴다. safety.user_returned 값은 그대로 둔다. measured 는 측정 구간이다."""
    passed = res.write()
    path = os.path.join(res.dir, res.ac + ".json")
    if res.safety["user_returned"] is not True and not measured:
        return passed and not res.safety["user_returned"]
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    if measured:
        doc["measured_started_at"], doc["measured_finished_at"] = measured
    if res.safety["user_returned"] is not True:
        write_json(path + ".tmp", doc)
        os.replace(path + ".tmp", path)
        return passed and not res.safety["user_returned"]
    why = gate.returned_block(doc, spec, res.root)
    doc["notes"].append({"user_returned_judgement": why or "면제 축, 실행 구간에 PC 쪽 주입·포그라운드 표시 없음(계획 §2-3)"})
    if passed and why is None:
        doc["verdict"] = "PASS"
    write_json(path + ".tmp", doc)
    os.replace(path + ".tmp", path)
    print("복귀 표지 판단 verdict=%s %s" % (doc["verdict"], why or "면제"), flush=True)
    return passed and why is None


def load_raws(items):
    raws = {}
    for item in items or []:
        key, _, path = item.partition("=")
        with open(path, encoding="utf-8-sig") as f:
            raws[key] = json.load(f)
    return raws


def parse_rc(items):
    return {k: int(v) for k, _, v in (i.partition("=") for i in items or [])}


def manifest_state(session, root):
    try:
        with open(os.path.join(session or "", "plans", "pcaway_final_build.json"), encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError):
        return None
    rows = {}
    for role in ROLES:
        e = data.get(role) or {}
        path = os.path.join(root, e.get("path", "")) if e.get("path") else None
        rows[role] = {"listed": bool(e), "same": bool(path and os.path.isfile(path) and ri.sha256(path) == e.get("sha256"))}
    return {"roles": rows, "complete": all(r["same"] for r in rows.values())}


def merge_measure_file(path, res, req, mapped):
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    rows = doc.get("measurements") or []
    for c in doc.get("captures") or []:  # retest_pcaway_extra2 가 찍은 캡처. sha 는 다시 계산하고 판독 기록은 비운 채 옮긴다
        res.capture(os.path.join(res.root, c["path"]))
    for row in rows:
        name = row.get("name")
        if name not in req:
            raise SystemExit("--measure-file: SPEC 밖 이름 거부: %s" % name)
        if name in mapped:
            raise SystemExit("--measure-file: 원자료 대응 값을 덮어쓸 수 없다: %s" % name)
        if not str(row.get("source", "")).strip() or "value" not in row or not isinstance(row.get("pass"), bool):
            raise SystemExit("--measure-file: name·value·pass·source 필수: %s" % name)
        res.add(name, {"value": row["value"], "source": row["source"]}, row["pass"])


def emit(args, root=ri.ROOT):
    if not args.writer.startswith("otest"):
        print("거부: 결과 writer 는 otest 로 시작해야 한다")
        return 3
    if args.item in DEDICATED:
        print("거부: %s 는 전용 도구 %s 를 쓴다" % (args.item, DEDICATED[args.item]))
        return 3
    gate = ri.gate_module(args.session)
    if gate is None or args.item not in gate.SPEC:
        print("거부: 판정기 또는 항목 없음: %s" % args.item)
        return 3
    spec, entry = gate.SPEC[args.item], MAP.get(args.item, {})
    req = spec["req"]
    if not req:
        print("거부: %s" % STATIC_ONLY)
        return 3
    script = args.script or entry.get("script")
    script_path = script if script and os.path.isabs(script) else os.path.join(root, script or "")
    if not script or not os.path.isfile(script_path):
        print("거부: 측정 도구(--script)가 필요하다: %s" % script)
        return 3
    build = None
    if not spec["static"]:
        if not args.build_file or not os.path.isfile(args.build_file):
            print("거부: --build-file 이 필요하다")
            return 3
        build = {"path": ri.rel(args.build_file, root), "sha256": ri.sha256(args.build_file)}
    res = ri.Result(args.item, req, args, ri.script_info(script_path, root), build, root=root)
    if spec["human"]:
        res.human = ["사람 확인 잔여(계획서 §5)"]
    ctx = {"rc": parse_rc(args.rc), "manifest": manifest_state(args.session, root)}
    mapped = entry["fn"](load_raws(args.raw), ctx) if entry else {}
    for name, (value, ok) in mapped.items():
        if name not in req:
            raise SystemExit("대응표 오류: SPEC 밖 이름 %s" % name)
        res.add(name, value, ok)
    if args.measure_file:
        merge_measure_file(args.measure_file, res, req, mapped)
    for png in args.capture or []:
        res.capture(png)
    state, measure_doc = None, None
    if args.state:
        with open(args.state, encoding="utf-8") as f:
            state = json.load(f)
        if "safety" not in state:
            print("거부: safety-end 전 상태 파일이다")
            return 3
        res.safety = state["safety"]
        res.notes.append({"user_pd_before": state["before"]["rects"], "user_pd_after": state["after"]["rects"], "user_files": state["after"]["files"]})
    if args.measure_file:
        with open(args.measure_file, encoding="utf-8") as f:
            measure_doc = json.load(f)
    return 0 if finish(res, gate, spec, measured_window([measure_doc, state], load_raws(args.raw))) else 1


def s2(args, root=ri.ROOT):
    """사이클 전체 안전 값. 사용자 PC 를 조작하지 않고 읽기만 한다. no_signal·test_window_monitor_rule 은 원자료가 없어 내지 않는다(--measure-file)."""
    if not args.writer.startswith("otest"):
        print("거부: 결과 writer 는 otest 로 시작해야 한다")
        return 3
    gate = ri.gate_module(args.session)
    req = gate.SPEC["S2"]["req"]
    with open(args.state, encoding="utf-8") as f:
        state = json.load(f)
    if "safety" not in state:
        print("거부: safety-end 전 상태 파일이다")
        return 3
    res = ri.Result("S2", req, args, ri.script_info(os.path.abspath(__file__), root), None, root=root)
    before, after = state["before"], state["after"]
    # team-lead 계약 해석(11-07): present_untouched(같은 PID·시작 시각·창 사각형 유지) 또는 absent_both(시작·끝 0개·실행 기록 없음)일 때만 통과한다
    pd_sf = ri.user_pd_safety(before, after)
    res.add("user_pd_alive_end", {"pid": state["user_pid"], "alive": after["alive"], "case": pd_sf["user_pd_case"], "before": pd_sf["user_pd_before"], "after": pd_sf["user_pd_after"],
                                  "launches": pd_sf["user_pd_launches"]}, pd_sf["user_pd_case"] is not None)
    for name, key in (("user_layout_md5_same", "layout.json"), ("user_settings_md5_same", "settings.json")):
        res.add(name, {"before": before["files"].get(key), "after": after["files"].get(key)}, bool(before["files"].get(key)) and before["files"].get(key) == after["files"].get(key))
    out = subprocess.run(["/mnt/c/Windows/System32/tasklist.exe", "/FI", "IMAGENAME eq Pd.Poc.Desktop.exe", "/NH", "/FO", "CSV"], capture_output=True).stdout.decode("utf-8", "replace")
    pids = [int(p) for p in ri.re.findall(r'^"[^"]+","(\d+)"', out, ri.re.M)]
    res.add("own_pids_cleaned", {"running": pids, "user_pid": state["user_pid"]}, [p for p in pids if p != state["user_pid"]] == [])
    free = shutil.disk_usage("/mnt/c").free / (1 << 30)
    res.add("disk_free_ge_15gb", {"free_gb": round(free, 1)}, free >= MIN_DISK_GB)
    if args.measure_file:
        merge_measure_file(args.measure_file, res, req, {m["name"] for m in res.measurements})
    res.safety = state["safety"]
    # s2 는 이 함수 안에서 값을 읽으므로 실행 구간 자체가 측정 구간이다. safety 상태의 구간과 합친다
    own = {"measured_started_at": res.started_at, "measured_finished_at": ri.now_iso()}
    return 0 if finish(res, gate, gate.SPEC["S2"], measured_window([own, state], {})) else 1


def classify(item, spec):
    """(결과 작성 경로, 원자료 도구, 도구 원자료에 없는 측정 이름). 코드에서 뽑으므로 표와 구현이 어긋나지 않는다."""
    if not spec["req"]:
        return STATIC_ONLY, "-", []
    if item in DEDICATED:
        return "전용 도구 " + DEDICATED[item], DEDICATED[item], []
    if item == "S2":
        return "pcaway_report.py s2 + retest_pcaway_extra2.py", "tasklist·md5·디스크·측정 로그", []
    if item in MAP:
        extra = " + retest_pcaway_extra2.py" if MAP[item].get("extra2") else ""
        return "pcaway_report.py emit" + extra, str(MAP[item]["script"] or "--script 지정"), MAP[item]["unsourced"]
    return "pcaway_report.py emit --measure-file 만", "저장소 측정 도구 없음", spec["req"]


def matrix(args):
    gate = ri.gate_module(args.session)
    if gate is None:
        print("판정기 없음")
        return 2
    lines = ["| 항목 | 필수 측정 수 | 결과 작성 경로 | 원자료 도구 | 도구 원자료에 없는 측정 이름 |", "|---|---|---|---|---|"]
    for item, spec in gate.SPEC.items():
        path, tool, missing = classify(item, spec)
        lines.append("| %s | %d | %s | %s | %s |" % (item, len(spec["req"]), path, tool, ", ".join(missing) or "없음"))
    text = "\n".join(lines) + "\n"
    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        with open(args.out, "w", encoding="utf-8") as f:
            f.write(text)
    print(text)
    return 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("cmd", nargs="?", choices=("safety-begin", "safety-end", "emit", "s2", "matrix"))
    p.add_argument("--item")
    p.add_argument("--raw", action="append")
    p.add_argument("--rc", action="append")
    p.add_argument("--capture", action="append")
    p.add_argument("--state")
    p.add_argument("--script")
    p.add_argument("--build-file")
    p.add_argument("--measure-file")
    p.add_argument("--writer", default="")
    p.add_argument("--phase", choices=("discovery", "final"), default="discovery")
    p.add_argument("--user-pid", type=int, default=None, help="생략하면 실행 경로로 찾는다. 주면 그 경로의 프로세스인지 확인한다")
    p.add_argument("--session", default=os.environ.get("PCAWAY_SESSION") or (os.path.join(os.path.expanduser("~"), ".claude", "session-env", os.environ["PIPELINE_UUID"]) if os.environ.get("PIPELINE_UUID") else None))
    p.add_argument("--out")
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args()
    if args.self_test or args.cmd in ("emit", "s2", "matrix"):
        # 세션 미지정 시 session-env 상위로 떨어져 판정기 없음으로 조용히 실패하던 문제를 막는다(판정 로직은 그대로)
        if not args.session:
            print("거부: PIPELINE_UUID 또는 --session 필요(PCAWAY_SESSION 도 가능)", file=sys.stderr)
            return 2
        gate_path = os.path.join(args.session, "pcaway_gate.py")
        if not os.path.exists(gate_path):
            print("거부: 판정기 없음 — " + gate_path, file=sys.stderr)
            return 2
    if args.self_test:
        import pcaway_report_selftest
        return pcaway_report_selftest.run(args, sys.modules[__name__])
    table = {"safety-begin": safety_begin, "safety-end": safety_end, "emit": emit, "s2": s2, "matrix": matrix}
    if args.cmd not in table:
        p.print_help()
        return 3
    return table[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())

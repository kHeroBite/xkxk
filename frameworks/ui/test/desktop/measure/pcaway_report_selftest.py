#!/usr/bin/env python3
# pcaway_report 자가시험 — 임시 트리에서 실제 판정기로 왕복해 없는 값·실패 값이 FAIL 로 남고 합성·덮어쓰기가 거부되는지 잰다. 앱·입력은 쓰지 않는다
import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timedelta, timezone

import retest_real_input as ri
from pcaway_report_map import MAP

SOURCE_FREE = ("F08", "R1", "D2")  # 원자료 인자 없이 저장소 문서·logs/ui28 을 읽는 항목


def run(args, report):
    fails = []
    gate_path = os.path.join(args.session or "", "pcaway_gate.py")
    gate = ri.gate_module(args.session)
    if gate is None:
        print("판정기 없음: %s" % gate_path)
        return 1
    for item, entry in MAP.items():
        req = gate.SPEC[item]["req"]
        fails += ["%s 없는 이름이 SPEC 밖: %s" % (item, n) for n in entry["unsourced"] if n not in req]
        if entry["fn"]({}, {"rc": {}, "manifest": None}) and item not in SOURCE_FREE:
            fails.append("%s 빈 원자료에서 측정값이 나옴" % item)
    fails += ["%s 분류 없음" % i for i, s in gate.SPEC.items() if not report.classify(i, s)[0]]
    tmp = tempfile.mkdtemp(prefix="pd_rep_")
    try:
        root, sess = os.path.join(tmp, "repo"), os.path.join(tmp, "sess")
        os.makedirs(os.path.join(root, "poc", "ui", "bin"))
        os.makedirs(os.path.join(sess, "plans"))
        tool, bin_file, png = os.path.join(root, "poc", "ui", "t.py"), os.path.join(root, "poc", "ui", "bin", "a.dll"), os.path.join(root, "poc", "ui", "c.png")
        for p in (tool, bin_file):
            with open(p, "w", encoding="utf-8") as f:
                f.write("x\n")
        with open(png, "wb") as f:
            f.write(b"\x89PNG\r\n\x1a\nabc")
        same = {"path": ri.rel(bin_file, root), "sha256": ri.sha256(bin_file)}
        report.write_json(os.path.join(sess, "plans", "pcaway_final_build.json"), {"desktop_work": same, "android_debug": same, "android_release": same, "android_prod": same, "web_prod": same})
        env = dict(os.environ, PCAWAY_ROOT=root, PCAWAY_SESSION=sess)
        raw, state, bad = os.path.join(tmp, "raw.json"), os.path.join(tmp, "s.json"), os.path.join(tmp, "m.json")
        report.write_json(raw, {"rows": [{"case": "MAXIMIZED_MDI_TO_SDI", "pass": True}, {"case": "NEW_DOC_SDI", "pass": True}]})
        report.write_json(state, {"user_pid": 1, "before": {"rects": []}, "after": {"rects": [], "files": {}}, "safety": {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False}})

        def ns(**kw):
            base = dict(item="A02", writer="otest-x", phase="final", session=args.session, script=tool, build_file=bin_file, raw=["main=" + raw], rc=[], capture=[png], state=state, measure_file=None)
            base.update(kw)
            return argparse.Namespace(**base)

        def gate_rc(item="A02"):
            return subprocess.run([sys.executable, "-I", gate_path, "item", item], env=env, capture_output=True, text=True).returncode
        result = os.path.join(root, "poc", "ui", "logs", "pcaway", "A02.json")
        if report.emit(ns(), root) != 0:
            fails.append("정상 원자료가 통과하지 않음")
        if gate_rc() != 1:
            fails.append("판독 전 판정기 FAIL 아님")
        ri.annotate(argparse.Namespace(annotate=result, capture="all", read_by="otest-x", finding="캡처를 읽은 결과 기록"))
        if gate_rc() != 0:
            fails.append("판독 후 판정기 PASS 아님")
        report.write_json(raw, {"rows": [{"case": "NEW_DOC_SDI", "pass": True}]})
        if report.emit(ns(), root) != 1 or json.load(open(result, encoding="utf-8"))["verdict"] != "FAIL":
            fails.append("없는 원자료가 FAIL 아님(합성 의심)")
        if gate_rc() != 1:
            fails.append("없는 원자료인데 판정기 FAIL 아님")
        report.write_json(raw, {"rows": [{"case": "MAXIMIZED_MDI_TO_SDI", "pass": False}, {"case": "NEW_DOC_SDI", "pass": True}]})
        if report.emit(ns(), root) != 1:
            fails.append("원자료 실패가 그대로 옮겨지지 않음")
        if report.emit(ns(writer="odev"), root) != 3 or report.emit(ns(item="A03"), root) != 3 or report.emit(ns(state=None, build_file=None), root) != 3:
            fails.append("거부 경로 오류")
        for row, why in (({"name": "nope", "value": 1, "pass": True, "source": "s"}, "SPEC 밖"), ({"name": "mdi_max_doc_to_sdi_monitor", "value": 1, "pass": True, "source": "s"}, "덮어쓰기"),
                         ({"name": "new_doc_sdi_monitor", "value": 1, "pass": True}, "source 없음")):
            report.write_json(bad, {"measurements": [row]})
            report.write_json(raw, {"rows": [{"case": "MAXIMIZED_MDI_TO_SDI", "pass": True}]})
            try:
                report.emit(ns(measure_file=bad), root)
                fails.append("--measure-file %s 를 거부하지 않음" % why)
            except SystemExit:
                pass
        # 복귀 표지: emit 과 판정기의 판단이 축별로 같아야 한다(계획 §2-3)
        back = os.path.join(tmp, "s_back.json")
        report.write_json(back, {"user_pid": 1, "before": {"rects": []}, "after": {"rects": [], "files": {}}, "safety": {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": True}})
        log = os.path.join(root, gate.INJECTION_LOG)
        now_ms = int(time.time() * 1000)

        def set_log(*rows):
            os.makedirs(os.path.dirname(log), exist_ok=True)
            with open(log, "w", encoding="utf-8") as f:
                f.writelines(json.dumps(r) + "\n" for r in rows)

        def iso_ms(ms):
            return datetime.fromtimestamp(ms / 1000, ri_tz).isoformat(timespec="seconds")
        ri_tz = timezone(timedelta(hours=9))
        window = {"measured_started_at": iso_ms(now_ms - 120000), "measured_finished_at": iso_ms(now_ms)}

        def exempt_case(name, item, want_rc):
            """필수 측정 전부 통과로 emit 한 뒤 rc·verdict·판정기·user_returned 기록을 대조한다."""
            spec = gate.SPEC[item]
            rows = [{"name": n, "value": spec["expect"].get(n, ("", 1))[1], "pass": True, "source": "자가시험"} for n in spec["req"]]
            report.write_json(bad, {"measurements": rows, **window})
            rc = report.emit(ns(item=item, state=back, raw=[], measure_file=bad, capture=[png] * spec["caps"]), root)
            path = os.path.join(root, "poc", "ui", "logs", "pcaway", item + ".json")
            if spec["caps"]:
                ri.annotate(argparse.Namespace(annotate=path, capture="all", read_by="otest-x", finding="캡처를 읽은 결과 기록"))
            with open(path, encoding="utf-8") as f:
                doc = json.load(f)
            want_verdict = "PASS" if want_rc == 0 else "FAIL"
            if rc != want_rc or doc["verdict"] != want_verdict or gate_rc(item) != want_rc or doc["safety"]["user_returned"] is not True:
                fails.append("%s: emit rc=%s verdict=%s 판정기 rc=%s 기대 rc=%s" % (name, rc, doc["verdict"], gate_rc(item), want_rc))

        set_log({"t_ms": now_ms - 3600000, "kind": "click"}, {"t_ms": now_ms, "kind": "returned"}, {"t_ms": now_ms, "kind": "arm"})
        exempt_case("표지+데스크톱 FAIL", "A02", 1)
        exempt_case("표지+Fold5 PASS", "C5", 0)
        exempt_case("표지+웹 PASS", "D4", 0)
        set_log({"t_ms": now_ms, "kind": "click"})
        exempt_case("표지+Fold5 PC 주입 FAIL", "C5", 1)
        exempt_case("표지+웹 PC 주입 FAIL", "D4", 1)
        os.remove(log)
        exempt_case("표지+웹 주입 기록 없음 FAIL", "D4", 1)
        # 측정 구간: 측정 중 주입은 emit 시각과 달라도 잡고, 시각이 없으면 통과시키지 않는다. 값은 입력에서만 온다
        set_log({"t_ms": now_ms - 3600000, "kind": "click"}, {"t_ms": now_ms, "kind": "returned"})
        saved_window = dict(window)
        window.clear()
        exempt_case("표지+측정 시각 없음 FAIL", "D4", 1)
        window.update(measured_started_at=saved_window["measured_started_at"])
        exempt_case("표지+측정 끝 없음 FAIL", "D4", 1)
        window.clear()
        window.update(measured_started_at=iso_ms(now_ms - 3700000), measured_finished_at=iso_ms(now_ms - 3500000))
        exempt_case("표지+측정 중 주입(emit 밖) FAIL", "D4", 1)
        window.clear()
        window.update(saved_window)
        exempt_case("표지+측정 구간 밖 주입 PASS", "D4", 0)
        d4_doc = json.load(open(os.path.join(root, "poc", "ui", "logs", "pcaway", "D4.json"), encoding="utf-8"))
        if d4_doc.get("measured_started_at") != saved_window["measured_started_at"]:
            fails.append("측정 구간이 결과에 남지 않음 %s" % d4_doc.get("measured_started_at"))
        t = lambda sec: iso_ms(now_ms + sec * 1000)
        for name, sources, raws, want in (
            ("상태만", [{"measured_started_at": t(-60), "measured_finished_at": t(-10)}], {}, (t(-60), t(-10))),
            ("끝 없는 상태", [{"measured_started_at": t(-60)}], {}, None),
            ("시간대 없는 값", [{"measured_started_at": "2026-11-05T09:00:00", "measured_finished_at": "2026-11-05T09:01:00"}], {}, None),
            ("원자료 전부", [None], {"a": {"started_at": t(-50), "finished_at": t(-40)}, "b": {"started_at": t(-30), "finished_at": t(-20)}}, (t(-50), t(-20))),
            ("일부 원자료만은 인정 안 함", [None], {"a": {"started_at": t(-50), "finished_at": t(-40)}, "b": {}}, None),
            ("끝만 있는 원자료는 넓히기만", [{"measured_started_at": t(-60), "measured_finished_at": t(-30)}], {"a": {"finished_at": t(-20)}}, (t(-60), t(-20))),
            ("+0900 표기", [None], {"a": {"started_at": "2026-11-05T13:47:45+0900", "finished_at": "2026-11-05T13:48:27+0900"}}, ("2026-11-05T13:47:45+09:00", "2026-11-05T13:48:27+09:00")),
        ):
            got = report.measured_window(sources, raws)
            if got != want:
                fails.append("측정 구간 %s: %s 기대 %s" % (name, got, want))
        # K6: --measure-file 은 값을 {value,source} 로 감싼다. 좋은 값은 판정기까지 통과하고 나쁜 값은 통과 표시를 달아도 FAIL 이다
        for tool_rel in (gate.TABLET_TOOL, gate.REPLAY_JUDGE, gate.REPLAY_MEASURE):
            tool_abs = os.path.join(root, tool_rel)
            os.makedirs(os.path.dirname(tool_abs), exist_ok=True)
            with open(tool_abs, "w", encoding="utf-8") as f:
                f.write("import sys\nsys.exit(0)\n")

        def annotate_item(item):
            ri.annotate(argparse.Namespace(annotate=os.path.join(root, "poc", "ui", "logs", "pcaway", item + ".json"), capture="all", read_by="otest-x", finding="캡처를 읽은 결과 기록"))

        def k6_case(name, mut, want_emit, want_gate):
            spec = gate.SPEC["K6"]
            rows = [{"name": n, "value": spec["expect"][n][1], "pass": True, "source": "자가시험"} for n in spec["req"]]
            mut(rows)
            report.write_json(bad, {"measurements": rows})
            rc = report.emit(ns(item="K6", raw=[], measure_file=bad, capture=[png] * spec["caps"]), root)
            annotate_item("K6")
            if rc != want_emit or gate_rc("K6") != want_gate:
                fails.append("K6 왕복 %s: emit rc=%s 판정기 rc=%s 기대 %s·%s" % (name, rc, gate_rc("K6"), want_emit, want_gate))

        def set_row(name, **kw):
            return lambda rows: [r.update(kw) for r in rows if r["name"] == name]
        k6_case("합격", lambda rows: None, 0, 0)
        k6_case("판정기 rc 1 통과 표시", set_row("tablet_first_doc0", value=1), 0, 1)
        k6_case("안내 없음 통과 표시", set_row("tablet_first_empty_hint", value=False), 0, 1)
        k6_case("치명 1 통과 표시", set_row("tablet_no_fatal", value=1), 0, 1)
        k6_case("판정기 rc 1 실패 표시", set_row("tablet_first_doc0", value=1, **{"pass": False}), 1, 1)
        # K7: 원자료 4개 → 대응표 → 판정기. 값은 원자료에서만 나온다
        apk_sha, apk_rel = ri.sha256(bin_file), ri.rel(bin_file, root)
        good_row = {"sent": True, "nodes": 24, "mnu_extra": 0, "drawer": 0, "on_top": True}

        def replay_doc(mode, **kw):
            doc = {"mode": mode, "apk": apk_rel, "apk_sha256": apk_sha, "docs_opened": 5, "fatal": [0, []], "rows": [dict(good_row) for _ in range(20)]}
            doc.update(kw)
            return doc

        def k7_case(name, change, want_emit, want_gate):
            docs = {"%s_%s" % (role, mode): replay_doc(mode) for role in ("android_prod", "android_release") for mode in ("open", "closed")}
            change(docs)
            raws = []
            for key, doc in docs.items():
                path = os.path.join(tmp, "replay_%s.json" % key)
                report.write_json(path, doc)
                raws.append("%s=%s" % (key, path))
            rc = report.emit(ns(item="K7", raw=raws, capture=[png, png]), root)
            annotate_item("K7")
            if rc != want_emit or gate_rc("K7") != want_gate:
                fails.append("K7 왕복 %s: emit rc=%s 판정기 rc=%s 기대 %s·%s" % (name, rc, gate_rc("K7"), want_emit, want_gate))

        def menu_opened(key):
            return lambda docs: docs[key]["rows"].__setitem__(0, dict(good_row, nodes=0, mnu_extra=12))
        k7_case("합격", lambda docs: None, 0, 0)
        k7_case("prod 펼침 메뉴 열림 1건", menu_opened("android_prod_open"), 1, 1)
        k7_case("release 접힘 메뉴 열림 1건", menu_opened("android_release_closed"), 1, 1)
        k7_case("prod 접힘 sent 19", lambda docs: docs["android_prod_closed"]["rows"].pop(), 1, 1)
        k7_case("release 원자료 없음", lambda docs: docs.pop("android_release_open"), 1, 1)
        k7_case("치명 1", lambda docs: docs["android_release_open"].update(fatal=[1, []]), 1, 1)
        k7_case("펼침·접힘 APK 다름", lambda docs: docs["android_prod_closed"].update(apk_sha256="0" * 64), 1, 1)
        k7_case("모드 뒤바뀜", lambda docs: docs["android_prod_open"].update(mode="closed"), 1, 1)
        k7_case("문서 3개", lambda docs: docs["android_prod_open"].update(docs_opened=3), 1, 1)
        old_sha = next(iter(gate.REPLAY_PREFIX_SHA))
        k7_case("수정 전 sha", lambda docs: [docs[k].update(apk_sha256=old_sha) for k in ("android_prod_open", "android_prod_closed")], 0, 1)
        k7_real = MAP["K7"]["fn"]
        if k7_real({}, {"rc": {}, "manifest": None}):
            fails.append("K7 빈 원자료에서 측정값이 나옴")
        refused = os.path.join(tmp, "refused_state.json")
        real_resolve = ri.resolve_user_pid
        ri.resolve_user_pid = lambda _a: False
        try:
            if report.safety_begin(argparse.Namespace(state=refused, user_pid=None)) != 3 or os.path.exists(refused):
                fails.append("사용자 Pd 를 못 찾았는데 safety-begin 이 거부하지 않음")
        finally:
            ri.resolve_user_pid = real_resolve
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0

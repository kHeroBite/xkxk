# 기기 판별표 14행(정본 08 §1-3-1)을 진단 빌드의 ui.classify·style.selftest 로 대조하는 판정기
"""
사용:
  python3 -I device_class_table.py --self-test
  python3 -I device_class_table.py --info <시험용 인스턴스 remote-control.json> [--out 결과.json]

--info 는 필수다(5180·5220 포트와 사용자 Pd 에는 붙지 않는다). 파일의 port·token 으로 api/cmd 를 호출한다.
rc: 0=PASS, 1=FAIL(기대값 불일치), 2=도구 오류(연결 실패·형식 오류·진단 빌드 아님).
결과 JSON 은 stdout 으로 출력한다.
"""
import argparse
import json
import os
import sys

FORBIDDEN_PORTS = (5180, 5220)

# 판정표 14행 기대값(정본 08 §1-3-1). (행, 이름, ui.classify 인자, 기대 device, 기대 effective)
# 인자 값은 문자열로 보낸다. width 는 창 폭 dp, setting 기본 auto, previous 기본 auto.
ROWS = [
    (1, "row01_fold_folded", dict(platform="Android", hasTouch="true", foldable="true", smallest=401, windowWidth=401), "Phone", "Mobile"),
    (2, "row02_fold_unfolded", dict(platform="Android", hasTouch="true", foldable="true", smallest=805, windowWidth=805), "Tablet", "Tablet"),
    (2, "row02b_fold_unfolded_narrow_compact_off", dict(platform="Android", hasTouch="true", multiWindow="true", smallest=805, windowWidth=500, compact="false"), "Tablet", "Mobile"),
    (2, "row02c_fold_unfolded_narrow_compact_on", dict(platform="Android", hasTouch="true", multiWindow="true", smallest=805, windowWidth=500, compact="true"), "Tablet", "Tablet"),
    (3, "row03_fold_half_open_inner", dict(platform="Android", hasTouch="true", foldable="true", smallest=805, windowWidth=805), "Tablet", "Tablet"),
    (4, "row04_split_window_off", dict(platform="Android", hasTouch="true", multiWindow="true", smallest=800, windowWidth=400), "Tablet", "Mobile"),
    (4, "row04b_split_window_on", dict(platform="Android", hasTouch="true", multiWindow="true", smallest=800, windowWidth=400, compact="true"), "Tablet", "Tablet"),
    (5, "row05_tablet", dict(platform="Android", hasTouch="true", smallest=800, windowWidth=1280), "Tablet", "Tablet"),
    (6, "row06_phone_bar", dict(platform="Android", hasTouch="true", smallest=360, windowWidth=360), "Phone", "Mobile"),
    (7, "row07_desktop_touch_laptop", dict(platform="Windows", hasTouch="true", smallest=700, width=1200), "Pc", "Pc"),
    (8, "row08_web_touch_laptop_fine_hover", dict(platform="Browser", hasTouch="true", precise="true", hover="true", smallest=800, width=1200), "Pc", "Pc"),
    (8, "row08b_web_mobile_fine_but_no_hover", dict(platform="Browser", hasTouch="true", precise="true", hover="false", smallest=400), "Phone", "Mobile"),
    (9, "row09_dex_size_fallback", dict(platform="Android", hasTouch="true", smallest=1000), "Tablet", "Tablet"),
    (10, "row10_chromeos_pc_class", dict(platform="Android", hasTouch="true", pcClass="true", smallest=700, width=1200), "Pc", "Pc"),
    (10, "row10b_chromeos_touch_only", dict(platform="Android", hasTouch="true", precise="false", pcClass="true", smallest=700), "Tablet", "Tablet"),
    (11, "row11_desktop_browser", dict(platform="Browser", hasTouch="false", precise="true", hover="true", smallest=1000, width=1200), "Pc", "Pc"),
    (12, "row12_mobile_browser_phone", dict(platform="Browser", hasTouch="true", precise="false", hover="false", smallest=390), "Phone", "Mobile"),
    (12, "row12b_mobile_browser_tablet", dict(platform="Browser", hasTouch="true", precise="false", hover="false", smallest=820), "Tablet", "Tablet"),
    (13, "row13_ios_phone_idiom", dict(platform="IOS", idiom="Phone", smallest=800), "Phone", "Mobile"),
    (13, "row13b_ios_pad_idiom", dict(platform="IOS", idiom="Pad", smallest=400, width=800), "Tablet", "Tablet"),
    (14, "row14_leanback_size_fallback", dict(platform="Android", hasTouch="false", smallest=960, width=1920), "Pc", "Pc"),
]

MIN_SELFTEST_CASES = 27


class ToolError(Exception):
    """판정 불가(연결 실패·형식 오류) — rc=2."""


def check_classify(call):
    """각 행을 ui.classify 로 호출해 device·effective 를 기대값과 대조한다. call(cmd, args)->dict."""
    out = []
    for row, name, args, want_dev, want_eff in ROWS:
        res = call("ui.classify", args)
        if not isinstance(res, dict) or "device" not in res or "effective" not in res:
            raise ToolError("ui.classify 응답 형식 오류(%s): %r" % (name, res))
        ok = res["device"] == want_dev and res["effective"] == want_eff
        out.append({"row": row, "name": name, "pass": ok,
                    "want": [want_dev, want_eff], "got": [res["device"], res["effective"]]})
    return out


def check_selftest(call):
    """style.selftest: passed==total, 27건 이상, 14행 이름(row01~row14 접두)이 모두 있을 것."""
    res = call("style.selftest", {})
    if not isinstance(res, dict) or not isinstance(res.get("cases"), list) or "passed" not in res or "total" not in res:
        raise ToolError("style.selftest 응답 형식 오류: %r" % (res,))
    names = [str(c.get("name", "")) for c in res["cases"] if isinstance(c, dict)]
    missing = [("row%02d" % n) for n in range(1, 15) if not any(x.startswith("row%02d" % n) for x in names)]
    failed = [c.get("name") for c in res["cases"] if isinstance(c, dict) and not c.get("pass")]
    checks = [
        {"name": "passed_eq_total", "pass": res["passed"] == res["total"], "detail": "%s/%s" % (res["passed"], res["total"])},
        {"name": "total_ge_%d" % MIN_SELFTEST_CASES, "pass": res["total"] >= MIN_SELFTEST_CASES and len(names) >= MIN_SELFTEST_CASES, "detail": "total=%s cases=%d" % (res["total"], len(names))},
        {"name": "rows_01_14_present", "pass": not missing, "detail": "누락 %s" % missing if missing else "14행 모두 있음"},
        {"name": "no_failed_case", "pass": not failed, "detail": "실패 %s" % failed if failed else "실패 없음"},
    ]
    return checks


def judge(call):
    rows = check_classify(call)
    st = check_selftest(call)
    ok = all(r["pass"] for r in rows) and all(c["pass"] for c in st)
    return {"verdict": "PASS" if ok else "FAIL", "classify": rows, "selftest": st}, (0 if ok else 1)


def make_live_call(info_path):
    """시험용 인스턴스의 remote-control.json 으로 api/cmd 를 호출하는 함수. 5180·5220 거부."""
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
    import _paths
    _paths.add()
    import pdctl
    try:
        with open(info_path, encoding="utf-8") as f:
            info = json.load(f)
        port, _ = int(info["port"]), info["token"]
    except (OSError, ValueError, KeyError) as e:
        raise ToolError("연결 정보 파일 오류(%s): %s" % (info_path, e))
    if port in FORBIDDEN_PORTS:
        raise ToolError("포트 %d 는 사용자 Pd 용이라 거부한다. 시험용 인스턴스 주소를 쓴다." % port)

    def call(cmd, args):
        body = json.dumps({"cmd": cmd, "args": {k: str(v) for k, v in args.items()}}, ensure_ascii=False)
        try:
            text = pdctl.curl(info, "api/cmd", body, timeout=60)
        except SystemExit:
            raise ToolError("원격 연결 실패(%s)" % cmd)
        try:
            resp = json.loads(text)
        except ValueError:
            raise ToolError("응답 JSON 아님(%s): %s" % (cmd, text[:200]))
        if not resp.get("ok"):
            raise ToolError("%s 실패(진단 빌드 아님?): %s" % (cmd, resp.get("error")))
        return resp.get("result")
    return call


def self_test():
    """앱 없이 판정 로직 검사: 가짜 응답으로 PASS 1·FAIL 1·형식 오류 1."""
    expect = {name: (d, e) for _, name, _, d, e in ROWS}

    def good(cmd, args):
        if cmd == "ui.classify":
            # 인자로 행을 찾는 대신 호출 순서 큐에서 꺼낸다
            name = queue.pop(0)
            return {"device": expect[name][0], "effective": expect[name][1], "traits": {}}
        cases = [{"name": n, "pass": True, "detail": ""} for n in expect]
        cases += [{"name": "null_%d" % i, "pass": True, "detail": ""} for i in range(MIN_SELFTEST_CASES - len(cases))]
        return {"cases": cases, "passed": len(cases), "total": len(cases)}

    results = []
    # 1) PASS
    queue = [r[1] for r in ROWS]
    rep, rc = judge(good)
    results.append(("PASS 사례", rc == 0 and rep["verdict"] == "PASS"))
    # 2) FAIL — row06 의 effective 를 틀리게
    queue = [r[1] for r in ROWS]

    def bad(cmd, args):
        res = good(cmd, args)
        if cmd == "ui.classify" and res["device"] == "Phone" and res["effective"] == "Mobile" and not bad.done:
            bad.done = True
            res["effective"] = "Tablet"
        return res
    bad.done = False
    rep, rc = judge(bad)
    results.append(("FAIL 사례", rc == 1 and rep["verdict"] == "FAIL"))
    # 3) 형식 오류 — classify 응답에 device 없음
    try:
        judge(lambda c, a: {"oops": 1})
        results.append(("형식 오류 사례", False))
    except ToolError:
        results.append(("형식 오류 사례", True))
    # 4) 14행 이름 누락 + 건수 부족은 FAIL
    queue = [r[1] for r in ROWS]

    def few(cmd, args):
        if cmd == "style.selftest":
            return {"cases": [{"name": "row01_x", "pass": True}], "passed": 1, "total": 1}
        return good(cmd, args)
    rep, rc = judge(few)
    results.append(("셀프테스트 부족 사례", rc == 1))
    # 5) 5180 포트 거부
    import tempfile
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "rc.json")
        with open(p, "w", encoding="utf-8") as f:
            json.dump({"port": 5180, "token": "x"}, f)
        try:
            make_live_call(p)
            results.append(("금지 포트 거부", False))
        except ToolError:
            results.append(("금지 포트 거부", True))
    ok = all(r[1] for r in results)
    print(json.dumps({"self_test": "PASS" if ok else "FAIL",
                      "cases": [{"name": n, "pass": p} for n, p in results]}, ensure_ascii=False))
    return 0 if ok else 1


def main(argv):
    ap = argparse.ArgumentParser(description="기기 판별표 14행 판정")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--info", help="시험용 인스턴스의 remote-control.json 경로(필수, live)")
    ap.add_argument("--out", help="결과 JSON 저장 경로")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if not a.info:
        print(json.dumps({"verdict": "ERROR", "error": "--info 필수(시험용 인스턴스 연결 정보). 5180·5220 기본값 없음"}, ensure_ascii=False))
        return 2
    try:
        rep, rc = judge(make_live_call(a.info))
    except ToolError as e:
        print(json.dumps({"verdict": "ERROR", "error": str(e)}, ensure_ascii=False))
        return 2
    text = json.dumps(rep, ensure_ascii=False)
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            f.write(text)
    print(text)
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

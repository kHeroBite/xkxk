#!/usr/bin/env python3
# B08 메뉴 열기 측정기 — 진단판 원격 menu.open 으로 메뉴 열기(명령 진입 → 팝업 첫 프레임) p50·p95·max 와 메뉴·모듈 구성 시간을 기록하고 판정한다
"""
사용:
  python3 -B perf_menu_open.py --host web [--bundle <진단판 wwwroot>] [--port 5833]            (PC 비움 때 성능 기록)
  python3 -B perf_menu_open.py --host web --functional                                        (PC 비움 무관 기능 확인, 메뉴당 3회)
  python3 -B perf_menu_open.py --host desktop [--conf Debug]                                  (run_desktop_bg 화면 밖·임시 데이터 폴더)
  python3 -B perf_menu_open.py --self-test                                                    (앱 없음 — 판정 경로 전부)
흐름: PC 비움 확인(--functional 은 생략, 비움 아니면 앱을 띄우지 않고 WAIT) → 기동 → perf.ops op=ui.menu.build(기동 구성값, perf.reset 이
지우므로 먼저 읽음) → perf.reset → 메뉴마다 warmup 회(버림) + n 회 menu.open close=true(메뉴 사이·10회마다 비움 재확인, 복귀 시 즉시 멈춤 WAIT)
→ perf.ops op=ui.menu. → perf.modules → 판정. p95 는 nearest-rank. 성능은 실측 ms 로만 판정하고 load1 은 참고 기록만 남긴다.
상태: RECORDED(within_target 참·거짓 모두)·WAIT·INVALID(n<20·timed_out>0·값 없음·메뉴 오류)·FUNCTIONAL_PASS·FAIL
rc: 0=RECORDED/FUNCTIONAL_PASS 1=FAIL 2=INVALID·기동 실패 3=WAIT(비움 아님·복귀·사용자 파일 가드 rc=9 는 carryover UTODO-0031) 4=포트 거부
결과는 logs/b08_menu_open_<host>.json(기능 실행은 _functional.json) 고정 경로에 덮어쓰고, 원자료는 같은 이름 _raw/ 에 둔다.
"""
import argparse
import datetime
import json
import math
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)
SCHEMA = "perf-menu-open/1"
REJECT_PORTS = (5180, 5220, 5242, 5791)  # 사용자 인스턴스·다른 세션 포트
MIN_N = 20  # RECORDED 에 필요한 메뉴당 최소 시행 수
FUNCTIONAL_N = 3
DEFAULT_MENUS = "shell.mnuFile,shell.mnuWindow,shell.mnuView,shell.mnuSys"
AWAY_EVERY = 10
RC = {"RECORDED": 0, "FUNCTIONAL_PASS": 0, "FAIL": 1, "INVALID": 2, "WAIT": 3, "REJECT": 4}


def nearest_rank(vals, p):
    """nearest-rank 백분위: 정렬 뒤 ceil(p/100·n) 번째 값(1부터). 빈 목록은 None."""
    if not vals:
        return None
    s = sorted(vals)
    return s[max(1, math.ceil(p / 100.0 * len(s))) - 1]


def stats(vals):
    return {"n": len(vals), "p50": nearest_rank(vals, 50), "p95": nearest_rank(vals, 95), "max": max(vals) if vals else None}


def result_of(resp):
    return resp.get("result") if isinstance(resp, dict) and resp.get("ok") else None


def away_check(state, max_age=60):
    """pc_away_monitor.py check 의 rc(0=away 1=present 2=불명)."""
    mon = os.path.join(UI, "test", "desktop", "measure", "pc_away_monitor.py")
    return subprocess.run([sys.executable, "-B", mon, "check", "--state", state, "--max-age", str(max_age)],
                          capture_output=True, text=True).returncode


def run_flow(call, a, away, log):
    """원격 호출 순서. 반환 raw = {build_ops, reset, menus:{id:[응답]}, ops, modules, stop}."""
    raw = {"build_ops": call("perf.ops", op="ui.menu.build"), "menus": {}, "stop": None}
    raw["reset"] = call("perf.reset")
    for m in a.menu_list:
        if not a.functional and away() != 0:
            raw["stop"] = "메뉴 %s 시작 전 PC 비움 아님(사용자 복귀)" % m
            log(raw["stop"])
            break
        seq = []
        for i in range(a.warmup + a.n):
            if not a.functional and i > 0 and i % AWAY_EVERY == 0 and away() != 0:
                raw["stop"] = "메뉴 %s %d회째 PC 비움 아님(사용자 복귀)" % (m, i)
                break
            r = call("menu.open", id=m, close=True, timeoutMs=a.timeout_ms)
            if i >= a.warmup or not r.get("ok"):
                seq.append(r)
            if not r.get("ok"):
                break  # 메뉴 오류(못 찾음·이미 열림·팝업 없음)는 그 메뉴만 멈춘다
        raw["menus"][m] = seq
        log("menu %s %d건" % (m, len(seq)))
        if raw["stop"]:
            log(raw["stop"])
            break
    raw["ops"] = call("perf.ops", op="ui.menu.")
    raw["modules"] = call("perf.modules")
    return raw


def judge(raw, a):
    """raw 와 옵션으로 결과 JSON(스키마 perf-menu-open/1)을 만든다 — 앱 없이 시험 가능한 순수 함수."""
    res = {"schema": SCHEMA, "menus": {}, "pooled": None, "target_ms": a.target_ms, "within_target": None,
           "ops": {}, "modules": None, "modules_within_target": None, "carryover": None, "reason": ""}
    bad, pooled = [], []
    for m in a.menu_list:
        seq = raw["menus"].get(m)
        if seq is None:
            bad.append("%s 미측정" % m)
            continue
        errs = [r.get("error") for r in seq if not r.get("ok")]
        rs = [result_of(r) for r in seq if r.get("ok")]
        vals = [r["first_frame_ms"] for r in rs if isinstance(r.get("first_frame_ms"), (int, float)) and not r.get("timed_out")]
        to = sum(1 for r in rs if r.get("timed_out"))
        st = stats(vals)
        st.update(timed_out=to, samples=vals, errors=errs[:3], opened=sum(1 for r in rs if r.get("opened")),
                  sync_p95=nearest_rank([r["sync_ms"] for r in rs if isinstance(r.get("sync_ms"), (int, float))], 95))
        res["menus"][m] = st
        pooled += vals
        need = FUNCTIONAL_N if a.functional else MIN_N
        if errs:
            bad.append("%s 오류 %s" % (m, str(errs[0])[:120]))
        elif to:
            bad.append("%s timed_out %d" % (m, to))
        elif len(vals) < max(need, a.n if a.functional else need):
            bad.append("%s n=%d<%d" % (m, len(vals), need))
        elif a.functional and st["opened"] != len(rs):
            bad.append("%s opened=false" % m)
    res["pooled"] = stats(pooled)
    if res["pooled"]["p95"] is not None:
        res["within_target"] = res["pooled"]["p95"] <= a.target_ms
    for o in (result_of(raw.get("ops")) or []):
        if isinstance(o, dict) and o.get("name"):
            res["ops"][o["name"]] = o
    pre = [o for o in (result_of(raw.get("build_ops")) or []) if isinstance(o, dict) and o.get("name") == "ui.menu.build"]
    if "ui.menu.build" in res["ops"]:
        res["ops"]["ui.menu.build.after_reset"] = res["ops"].pop("ui.menu.build")
    if pre:
        res["ops"]["ui.menu.build"] = dict(pre[0], note="perf.reset 전에 읽은 기동 구성값")
    md = result_of(raw.get("modules"))
    if isinstance(md, dict):
        res["modules"] = md
        tc = md.get("total_configure_ms")
        res["modules_within_target"] = bool(md.get("recorded")) and isinstance(tc, (int, float)) and tc <= a.modules_target_ms
    if raw.get("stop"):
        res.update(status="WAIT", reason=raw["stop"], carryover="UTODO-0030")
        return res
    if a.functional:
        miss = [k for k in ("ui.menu.build", "ui.menu.refresh") if k not in res["ops"]]
        if not (md and md.get("recorded") and isinstance(md.get("build_ms"), (int, float))):
            bad.append("perf.modules recorded=false 또는 build_ms 없음")
        elif not md.get("count"):  # count 는 기록만 하고 판정을 막지 않는다(team-lead 결정, AC-008 modules≥1 은 otest 가 판정)
            res["modules_note"] = "호스트가 모듈을 등록하지 않음(BrowserUiHost·DesktopUiHost) — count 경로는 실측 불가"
        if miss:
            bad.append("조작 기록 없음 %s" % ",".join(miss))
        if raw.get("pageerror"):
            bad.append("pageerror %d" % raw["pageerror"])
        res.update(status="FAIL" if bad else "FUNCTIONAL_PASS", reason="; ".join(bad) or "기능 확인 통과")
    else:
        res.update(status="INVALID" if bad else "RECORDED", reason="; ".join(bad) or "기록 완료(목표 후보 대비 within_target 표기)")
    return res


def write_json(p, v):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(v, f, ensure_ascii=False, indent=1)


def execute(a, launch, away, log=print):
    """포트 → 비움 → 기동 → 흐름 → 판정. (결과, rc) 를 돌려주고 결과 파일·원자료를 쓴다. launch·away 는 주입(자기시험용)."""
    base = {"schema": SCHEMA, "host": a.host, "build": a.build, "functional": a.functional, "n": a.n, "warmup": a.warmup,
            "started": datetime.datetime.now().astimezone().isoformat(timespec="seconds"), "load1": None}
    try:
        base["load1"] = round(os.getloadavg()[0], 2)  # 참고 기록만(부하로 무효 처리하지 않는다)
    except OSError:
        pass
    if a.host == "web" and a.port in REJECT_PORTS:
        return dict(base, status="REJECT", reason="포트 %d 거부(사용자 인스턴스·다른 세션 포트)" % a.port), RC["REJECT"]
    out = a.out
    if not a.functional and away() != 0:
        res = dict(base, status="WAIT", reason="PC 비움 아님(%s) — 앱을 띄우지 않음" % a.away_state, carryover="UTODO-0030")
    else:
        raw = None
        with launch(a, log) as (call, meta):
            if call is not None:
                raw = run_flow(call, a, away, log)
        if raw is None:
            res = dict(base, **meta)
            res.setdefault("status", "INVALID")
        else:
            raw["pageerror"] = meta.get("pageerror", 0)  # 브라우저를 닫은 뒤 채워진다
            stem = os.path.splitext(out)[0] + "_raw"
            for k in ("build_ops", "reset", "menus", "ops", "modules"):
                write_json(os.path.join(stem, k + ".json"), raw.get(k))
            res = dict(base, **judge(raw, a))
            res.update(meta=meta, raw=stem)
    if res.get("status") == "REJECT":
        return res, RC["REJECT"]
    write_json(out, res)
    log("결과 %s — %s → %s" % (res["status"], res.get("reason", ""), out))
    return res, RC.get(res["status"], 2)


def parse(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", choices=["web", "desktop"], default="web")
    ap.add_argument("--bundle", default=os.path.join(UI, "src", "xkxk.frameworks.ui.browser", "bin", "Release", "net11.0-browser", "publish", "wwwroot"))
    ap.add_argument("--port", type=int, default=5833)
    ap.add_argument("--conf", default="Debug", help="데스크톱 구성(run_desktop_bg.sh --conf)")
    ap.add_argument("--n", type=int, default=None, help="메뉴당 시행 수(기본 20, --functional 은 3)")
    ap.add_argument("--warmup", type=int, default=2)
    ap.add_argument("--menus", default=DEFAULT_MENUS)
    ap.add_argument("--target-ms", type=float, default=50.0, help="메뉴 열기 p95 목표 후보")
    ap.add_argument("--modules-target-ms", type=float, default=20.0, help="모듈 구성 합계 목표 후보")
    ap.add_argument("--timeout-ms", type=int, default=1000)
    ap.add_argument("--away-state", default=os.path.join(UI, "logs", "pcaway", "pc_away.json"))
    ap.add_argument("--functional", action="store_true", help="PC 비움 확인 생략, 메뉴당 3회 기능 확인")
    ap.add_argument("--out")
    ap.add_argument("--build", default="", help="빌드 식별(커밋 등)")
    ap.add_argument("--by", default=os.environ.get("PD_SLOT_OWNER", "perf_menu_open"))
    ap.add_argument("--shots", default=os.path.join(UI, "logs", "b08_menu_open_shots"))
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    a.n = a.n if a.n is not None else (FUNCTIONAL_N if a.functional else MIN_N)
    a.menu_list = [m.strip() for m in a.menus.split(",") if m.strip()]
    a.out = os.path.abspath(a.out or os.path.join(UI, "logs", "b08_menu_open_%s%s.json" % (a.host, "_functional" if a.functional else "")))
    return a


def main(argv=None):
    a = parse(argv)
    if a.self_test:
        from perf_menu_open_selftest import self_test
        return self_test()
    import perf_menu_open_hosts as hosts
    res, rc = execute(a, hosts.launch, lambda: away_check(a.away_state))
    if rc == RC["REJECT"]:
        print(res["reason"])
    return rc


if __name__ == "__main__":
    sys.exit(main())

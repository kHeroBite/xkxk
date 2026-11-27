# B10 시험대 스모크(T-SMOKE) — 진단판 원격 명령으로 시험대 창 3종·강타입 표본·모듈 계측(·벤치 열기)을 확인하고 원자료를 남긴다
"""
사용:
  python3 -B testbed_smoke.py --host web [--bundle <진단판 wwwroot>] [--port 5835] [--out logs/b10_testbed_smoke_web.json]
  python3 -B testbed_smoke.py --host desktop [--conf Release] [--out logs/b10_testbed_smoke_desktop.json]
웹은 127.0.0.1 정적 서버 + headless Chromium(임시 프로필), 데스크톱은 tools/run_desktop_bg.sh(화면 밖·임시 --data-dir, 자기 PID 만 종료)로
띄운다(기동부는 perf_menu_open_hosts 재사용, 사용자 포트 5180·5220·5242·5791 거부). 사용자 파일 가드 rc=9 이면 가드를 풀지 않고 WAIT 다.
단계: 1) registry.dump windows 의 xkxk.demo.* 3종 owner·강타입 표본 owner  2) docs.open 을 창 ID 경로(window=xkxk.demo.gantt)와 kind 경로(kind=grid)로 따로 호출해 열림·내용이 자리표시 아님
3) docs.open window=xkxk.testbed.typed param=hello → 트리 testbed.typed.message 텍스트·제목 hello  4) perf.modules count≥1·modules[0].id=시험대(UTODO-0037)
5) 벤치(폼 문서)를 command.run xkxk.bench.open 으로 열고 문서 목록에서 확인(실행 불가 빌드면 skip 기록 → status=PARTIAL, PASS 아님)  6) pageerror 0(웹).
--batch 2(AC-104)는 batch 2 단계만 돈다 — prewarm_ran: perf.get 최상위 prewarm_<kind>_ms·_attached=1(perf.ops 아님, 지표 없으면 skip)  grid_pool_hit: settings.set gridViewPool=true 뒤 그리드 2회 열기 전후 perf.get 의 pool_hit_grid 가 1 이상 증가(키 없으면 skip), perf.ops op=doc.open.grid count≥2 도 확인. 결과는 logs/b10_testbed_smoke_b2_<host>.json.
결과: {status, host, steps:[{name, ok, skipped, problems, raw}], …} 를 logs/b10_testbed_smoke_<host>.json 에 남긴다.
rc: 0=PASS 1=FAIL 2=기동 실패·INVALID 3=WAIT(가드 rc9) 4=포트 거부 5=NOT_IMPLEMENTED 6=PARTIAL(건너뛴 단계 있음, PASS 아님)
"""
import argparse
import contextlib
import datetime
import json
import os
import shutil
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)
from perf_menu_open_hosts import REJECT_PORTS, launch  # noqa: E402 — 앱 없이도 불러올 수 있는 기동부
from window_registry_smoke import BUNDLE, stale_desktop_build, wait_ready  # noqa: E402 — 같은 진단판 번들·낡은 빌드 검사 재사용

MODULE, TYPED = "xkxk.frameworks.ui.testbed", "xkxk.testbed.typed"
DEMOS = ("xkxk.demo.grid", "xkxk.demo.gantt", "xkxk.demo.form")
MESSAGE_ID, PARAM = "testbed.typed.message", "hello"
PLACEHOLDER = "Placeholder"  # 자리표시 보기 형식 이름 일부(XKUI0509 자리표시)
BENCH_CMD, BENCH_TITLE = "xkxk.bench.open", "Bench · "  # 벤치 열기 명령 ID·벤치 문서 제목 접두(TestbedCommands·TestbedBench)


class Step:
    """한 단계의 원자료·문제 목록. 원격 호출은 모두 raw 에 원문으로 남는다."""

    def __init__(self, call):
        self._call, self.raw, self.problems, self.skipped = call, {}, [], None

    def need(self, ok, detail):
        if not ok:
            self.problems.append(str(detail)[:300])
        return bool(ok)

    def remote(self, key, cmd, **args):
        resp = self._call(cmd, **args)
        self.raw[key] = resp
        if not (isinstance(resp, dict) and resp.get("ok")):
            raise RuntimeError("%s 실패: %s" % (cmd, str(resp)[:250]))
        return resp.get("result")


def walk(nodes):
    """트리 덤프의 노드를 평탄하게 돌려준다."""
    for n in nodes or []:
        yield n
        yield from walk(n.get("children"))


def tree_nodes(s, key, **args):
    return list(walk((s.remote(key, "tree.get", **args) or {}).get("tree")))


def windows_of(s):
    dump = s.remote("registry_dump", "registry.dump")
    return {w.get("id"): w for w in dump.get("windows") or []}


def registry_owners(s, ctx):
    wins = windows_of(s)
    ctx["windows"] = wins
    for wid in DEMOS:
        s.need(wid in wins and wins[wid].get("owner") == MODULE, "%s owner=%s(기대 %s)" % (wid, (wins.get(wid) or {}).get("owner"), MODULE))
    s.need(TYPED in wins and wins[TYPED].get("owner") == MODULE, "%s 미등록 또는 owner=%s" % (TYPED, (wins.get(TYPED) or {}).get("owner")))


def check_demo_doc(s, ctx, key, opened, want_window, want_type):
    """문서 1건이 want_window 로 열렸고 내용 트리가 자리표시가 아닌지 판정한다."""
    if not s.need(isinstance(opened, list) and len(opened) == 1, "%s: docs.open 응답이 문서 1건이 아님" % key):
        return
    doc = opened[0]
    s.need(doc.get("windowId") == want_window, "%s: windowId=%s(기대 %s)" % (key, doc.get("windowId"), want_window))
    time.sleep(ctx["settle"])  # 보기 생성
    s.need(any(d.get("id") == doc.get("id") for d in s.remote(key + "_list", "docs.list")), "%s: 열린 문서가 목록에 없음" % key)
    types = [n.get("type", "") for n in tree_nodes(s, key + "_tree", doc=doc.get("id"), named=False)]
    s.raw[key + "_types"] = types[:60]
    s.need(len(types) > 3, "%s: 내용 트리가 비었음(노드 %d)" % (key, len(types)))
    s.need(not any(PLACEHOLDER in t for t in types), "%s: 자리표시 보기가 열림: %s" % (key, [t for t in types if PLACEHOLDER in t][:3]))
    s.need(any(want_type in t for t in types), "%s: %s 내용 형식이 트리에 없음" % (key, want_type))


def demo_open(s, ctx):
    # 창 ID 경로와 kind 경로를 따로 호출한다(둘을 함께 넘기면 window 가 우선해 kind 경로가 가려진다)
    check_demo_doc(s, ctx, "by_window", s.remote("docs_open_window", "docs.open", window="xkxk.demo.gantt"), "xkxk.demo.gantt", "Gantt")
    check_demo_doc(s, ctx, "by_kind", s.remote("docs_open_kind", "docs.open", kind="grid"), "xkxk.demo.grid", "Grid")


def typed_open(s, ctx):
    # 양성만 본다: 표본 창 TArgs 는 string 이고 param=hello 가 표본 뷰(testbed.typed.message)에 보여야 한다.
    # 음성(인수 형식 불일치 XKUI0509)은 계약 시험(odev-44 몫)이 맡는다.
    opened = s.remote("docs_open", "docs.open", window=TYPED, param=PARAM)
    if not s.need(isinstance(opened, list) and len(opened) == 1, "docs.open 응답이 문서 1건이 아님"):
        return
    doc = opened[0]
    s.need(doc.get("windowId") == TYPED, "windowId=%s(기대 %s)" % (doc.get("windowId"), TYPED))
    time.sleep(ctx["settle"])
    listed = next((d for d in s.remote("docs_list", "docs.list") if d.get("id") == doc.get("id")), {})
    s.need(PARAM in str(listed.get("title") or doc.get("title")), "제목에 %s 없음: %s" % (PARAM, listed.get("title") or doc.get("title")))
    texts = [n.get("text") for n in tree_nodes(s, "tree_typed", doc=doc.get("id"), named=True) if n.get("id") == MESSAGE_ID]
    s.raw["message_texts"] = texts
    s.need(texts and all(t == PARAM for t in texts), "%s 텍스트 %s(기대 %s)" % (MESSAGE_ID, texts, PARAM))


def perf_modules(s, ctx):
    m = s.remote("perf_modules", "perf.modules")
    s.need(m.get("recorded") is True, "recorded=%s" % m.get("recorded"))
    mods = m.get("modules") or []
    s.need((m.get("count") or 0) >= 1 and len(mods) >= 1, "모듈 계측 count=%s" % m.get("count"))
    s.need(bool(mods) and mods[0].get("id") == MODULE, "modules[0].id=%s(기대 %s, UTODO-0037)" % ((mods or [{}])[0].get("id"), MODULE))


def bench_open(s, ctx):
    # 벤치는 창 종류가 아니라 폼 문서(제목 접두 BENCH_TITLE)다 — 원격 명령 실행 경로로 열고 문서 목록에서 찾는다
    ran = s.remote("command_run", "command.run", id=BENCH_CMD)
    if not (isinstance(ran, dict) and ran.get("canExecute") and ran.get("executed")):
        s.skipped = "%s 실행 불가(진단+벤치 구성 아닌 빌드) — SKIP 은 PASS 로 세지 않는다: %s" % (BENCH_CMD, str(ran)[:120])
        return
    time.sleep(ctx["settle"])
    titles = [str(d.get("title")) for d in s.remote("docs_list", "docs.list")]
    s.raw["bench_titles"] = titles
    s.need(any(t.startswith(BENCH_TITLE) for t in titles), "벤치 문서(제목 접두 '%s')가 문서 목록에 없음: %s" % (BENCH_TITLE, titles[:8]))


def prewarm_ran(s, ctx):
    # 예열은 진단판만 지표를 남긴다 — prewarm_* 키가 하나도 없으면 진단판이 아니거나 예열이 꺼진 빌드라 skip(PASS 아님)
    got = s.remote("perf_get", "perf.get") or {}
    keys = {k: v for k, v in got.items() if k.startswith("prewarm_")}
    s.raw["prewarm_metrics"] = keys
    if not keys:
        s.skipped = "perf.get 에 prewarm_* 지표 없음(진단판 아님 또는 예열 꺼짐) — SKIP 은 PASS 로 세지 않는다"
        return
    ms = [k for k in keys if k.endswith("_ms")]
    s.need(bool(ms), "prewarm_*_ms 지표 없음: %s" % sorted(keys))
    s.need(any(keys.get(k[:-3] + "_attached") == 1 for k in ms), "붙임(_attached=1)이 확인된 예열 종류 없음: %s" % keys)


def grid_pool_hit(s, ctx):
    # 임시 프로필(임시 --data-dir)이므로 설정 변경이 사용자 파일에 닿지 않는다. 그래도 끝에 원복한다
    before = s.remote("settings_before", "settings.get").get("gridViewPool")
    s.remote("settings_pool_on", "settings.set", gridViewPool=True)
    try:
        s.need(s.remote("settings_after", "settings.get").get("gridViewPool") is True, "gridViewPool 켬이 settings.get 에 반영되지 않음")
        time.sleep(ctx["settle"] * 3)  # 유휴 채우기
        s.remote("ops_reset", "perf.reset")
        hit0 = (s.remote("perf_get_before", "perf.get") or {}).get("pool_hit_grid")  # 키 없음 = 진단 지표 없는 빌드
        for i in (1, 2):
            opened = s.remote("docs_open_%d" % i, "docs.open", kind="grid")
            s.need(isinstance(opened, list) and len(opened) == 1, "그리드 열기 %d 응답이 문서 1건이 아님" % i)
            time.sleep(ctx["settle"] * 3)  # 열린 뒤 다시 채우기
        ops = s.remote("ops", "perf.ops", op="doc.open.grid")
        s.raw["grid_ops"] = ops
        row = next((o for o in ops or [] if o.get("name") == "doc.open.grid"), {})
        s.need((row.get("count") or 0) >= 2, "doc.open.grid 열기 기록 count=%s(기대 2 이상)" % row.get("count"))
        after = s.remote("perf_get_after", "perf.get") or {}
        hit1 = after.get("pool_hit_grid")
        s.raw["pool_metrics"] = {k: v for k, v in after.items() if k.startswith("pool_")}
        if hit0 is None and hit1 is None:
            s.skipped = "perf.get 에 pool_hit_grid 지표 없음(진단판 아님) — SKIP 은 PASS 로 세지 않는다"
        else:
            s.need((hit1 or 0) - (hit0 or 0) >= 1, "pool_hit_grid %s → %s(두 번 열기 뒤 1 이상 증가 기대, pool_miss_grid=%s)" % (hit0, hit1, after.get("pool_miss_grid")))
    finally:
        if before is not None:
            s.remote("settings_restore", "settings.set", gridViewPool=bool(before))


STEPS = [registry_owners, demo_open, typed_open, perf_modules, bench_open]
STEPS2 = [prewarm_ran, grid_pool_hit]


def run_steps(call, log, settle=1.0, steps=None):
    """단계를 차례로 실행한다. 한 단계가 실패해도 다음 단계는 실행해 원자료를 남긴다."""
    ctx, out = {"settle": settle}, []
    for fn in steps or STEPS:
        s = Step(call)
        try:
            fn(s, ctx)
        except Exception as e:  # noqa: BLE001
            s.need(False, "%s: %s" % (type(e).__name__, e))
        out.append({"name": fn.__name__, "ok": not s.problems, "skipped": s.skipped, "problems": s.problems, "raw": s.raw})
        log("%s %s %s" % ("✓" if not s.problems else "✗", fn.__name__, "; ".join(s.problems)[:400] or (s.skipped or "")))
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", choices=["web", "desktop"], required=True)
    ap.add_argument("--bundle", default=BUNDLE, help="웹 진단판 wwwroot")
    ap.add_argument("--port", type=int, default=5835)
    ap.add_argument("--out")
    ap.add_argument("--conf", default="Release", help="데스크톱 구성(run_desktop_bg.sh --conf, 기본 Release=진단판)")
    ap.add_argument("--by", default=os.environ.get("PD_SLOT_OWNER", "testbed_smoke"))
    ap.add_argument("--ready-timeout", type=int, default=90)
    ap.add_argument("--batch", type=int, default=1)
    ap.add_argument("--build", default="", help="빌드 식별(로그·커밋)")
    a = ap.parse_args(argv)
    if a.host == "web" and a.port in REJECT_PORTS:
        print("포트 %d 거부(사용자 인스턴스 포트)" % a.port)
        return 4
    if a.batch not in (1, 2):
        print("--batch %d 단계는 없음 — NOT_IMPLEMENTED(PASS 아님)" % a.batch)
        return 5
    tag = "" if a.batch == 1 else "_b2"
    out = os.path.abspath(a.out or os.path.join(UI, "logs", "b10_testbed_smoke%s_%s.json" % (tag, a.host)))
    lines = []

    def log(msg):
        lines.append("%s %s" % (datetime.datetime.now().strftime("%H:%M:%S"), msg))
        print(lines[-1])

    result = {"status": "INVALID", "host": a.host, "steps": [], "app_pid": None, "build": a.build, "by": a.by,
              "started_at": datetime.datetime.now().astimezone().isoformat(timespec="seconds"), "conf": a.conf if a.host == "desktop" else None}
    stale, result["build_check"] = stale_desktop_build(a.conf) if a.host == "desktop" else (None, None)
    meta = {}
    shots = tempfile.mkdtemp(prefix="b10_testbed_shots_")  # 웹 기동부가 요구하는 폴더 — 이 스모크는 캡처를 남기지 않는다
    launch_args = argparse.Namespace(**{**vars(a), "shots": shots})
    try:
        with contextlib.nullcontext((None, {"status": "INVALID", "reason": stale})) if stale else launch(launch_args, log) as (call, meta):
            if call is not None:
                ready, last = wait_ready(call, a.ready_timeout)
                if ready:
                    result["steps"] = run_steps(call, log, steps=STEPS if a.batch == 1 else STEPS2)
                    skipped = [s["name"] for s in result["steps"] if s["skipped"]]
                    result["skipped_steps"] = skipped
                    if not all(s["ok"] for s in result["steps"]):
                        result["status"] = "FAIL"
                    else:
                        result["status"] = "PARTIAL" if skipped else "PASS"  # SKIP 이 있으면 PASS 로 세지 않는다
                else:
                    result["reason"] = "기동 복원 대기 시간 초과: %s" % str(last)[:300]
    except Exception as e:  # noqa: BLE001 — 기동부·종료부 예외도 결과 파일로 남긴다
        result["reason"] = "%s: %s" % (type(e).__name__, str(e)[:300])
        if result["steps"]:
            result["status"] = "FAIL"
    finally:
        shutil.rmtree(shots, ignore_errors=True)
    meta = dict(meta or {})
    if meta.get("status") in ("WAIT", "INVALID"):  # 기동부가 앱을 못 띄운 사유
        result.update(status=meta.pop("status"), reason=meta.pop("reason", None))
    result.update(meta)
    result["app_pid"] = meta.get("pid")
    if result["status"] == "PASS" and result.get("pageerror", 0):
        result.update(status="FAIL", reason="웹 pageerror %d건" % result["pageerror"])
    if result["status"] == "PASS" and result.get("temp_data_dir") is False:
        result.update(status="FAIL", reason="임시 데이터 폴더가 아님")
    result.update(finished_at=datetime.datetime.now().astimezone().isoformat(timespec="seconds"), log=lines)
    log("결과 %s %s" % (result["status"], result.get("reason") or ""))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    print(out)
    return {"PASS": 0, "FAIL": 1, "WAIT": 3, "PARTIAL": 6}.get(result["status"], 2)


if __name__ == "__main__":
    sys.exit(main())

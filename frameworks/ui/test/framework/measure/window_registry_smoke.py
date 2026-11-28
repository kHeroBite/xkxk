# B09 창 레지스트리 스모크(T-SMOKE) — 진단판 원격 명령으로 v5 저장본 적용·형식 6 저장·옛/새 창 열기·인자 전달·모르는 창 보존·창 덤프를 확인하고 원자료를 남긴다
"""
사용:
  python3 -B window_registry_smoke.py --host web [--bundle <진단판 wwwroot>] [--port 5834] [--fixture <저장본 JSON>] [--out logs/b09_window_smoke_web.json]
  python3 -B window_registry_smoke.py --host desktop [--conf Release] [--out logs/b09_window_smoke_desktop.json]
웹은 127.0.0.1 정적 서버 + headless Chromium(임시 프로필), 데스크톱은 tools/run_desktop_bg.sh(화면 밖·임시 데이터 폴더, 자기 PID 만 종료)로
띄운다(기동부는 perf_menu_open_hosts 재사용). 사용자 파일 가드 rc=9 이면 가드를 풀지 않고 status=WAIT(carryover UTODO-0031)다.
데스크톱 기본 구성은 진단판 Release 다. 그 구성의 dll 이 같은 구성의 최신 logs/build_desktop_*.log 시작보다 오래되면 띄우지 않고 INVALID 다.
결과: {status, host, steps:[{name, ok, problems, raw}], app_pid, …}. 원격 명령: layout.apply·layout.get·docs.open·docs.list·registry.dump.
rc: 0=PASS 1=FAIL 2=기동 실패 3=WAIT 4=포트 거부
"""
import argparse
import base64
import contextlib
import datetime
import glob
import json
import os
import re
import shutil
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(UI, "tools"))
import host_identity  # noqa: E402 — 정체성 값 단일 출처(tools/host_identity.json, 없으면 오류 종료)
from perf_menu_open_hosts import REJECT_PORTS, launch  # noqa: E402 — 앱 없이도 불러올 수 있는 기동부

BUNDLE = host_identity.path("browser.bundle_release")
FIXTURE = os.path.join(UI, "test", "common", "testdata", "layout_v5_kinds.json")
DEMO = {"xkxk.demo.grid": "Grid", "xkxk.demo.gantt": "Gantt", "xkxk.demo.form": "Form"}
OLD_KINDS = {"grid": "xkxk.demo.grid", "0": "xkxk.demo.grid", "gantt": "xkxk.demo.gantt", "1": "xkxk.demo.gantt",
             "form": "xkxk.demo.form", "2": "xkxk.demo.form"}
FORM, GANTT, UNKNOWN, PARAM, SAVE_VERSION = "xkxk.demo.form", "xkxk.demo.gantt", "acme.none", "b09", 6
DEMO_OWNER = "xkxk.frameworks.ui.testbed"  # B10: 데모 3종 소유가 셸에서 시험대 모듈로 이동
WINDOW_KEYS = {"id", "owner", "aliases", "placement", "allowDuplicate", "hasContent"}


def expected_id(doc):
    """저장본 문서 한 건이 열려야 할 창 ID — WindowId 우선, 없으면 옛 Kind(문자열·숫자), Kind 도 없으면 Grid."""
    kind = "" if doc.get("Kind") is None else str(doc["Kind"])
    return doc.get("WindowId") or (OLD_KINDS.get(kind.lower(), kind) if kind.strip() else "xkxk.demo.grid")


def kind_of(window_id):
    """원격 문서 목록의 kind 값 — 데모 3종은 옛 이름 소문자, 그 밖은 창 ID 소문자."""
    return DEMO.get(window_id, window_id).lower()


class Step:
    """한 단계의 원자료·문제 목록. 원격 호출은 모두 raw 에 원문으로 남는다."""

    def __init__(self, call):
        self._call, self.raw, self.problems = call, {}, []

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

    def docs(self, key="docs_list"):
        return self.remote(key, "docs.list")

    def saved(self, key="layout_get"):
        return self.remote(key, "layout.get")

    def apply(self, key, text):
        return self.remote(key, "layout.apply", json=base64.b64encode(text.encode("utf-8")).decode("ascii"))

    def open_one(self, window_id, **args):
        """docs.open 1건 — 응답 문서의 창 ID·kind 와 그 문서가 목록에 있는지 본다. (응답 문서, 목록 문서)를 돌려준다."""
        before = {d.get("id") for d in self.docs("docs_list_before")}
        opened = self.remote("docs_open", "docs.open", **args)
        if not self.need(isinstance(opened, list) and len(opened) == 1, "docs.open 응답이 문서 1건이 아님"):
            return {}, {}
        doc = opened[0]
        listed = next((d for d in self.docs() if d.get("id") == doc.get("id")), {})
        self.need(doc.get("id") not in before, "새 문서가 아님: %s" % doc.get("id"))
        for where, d in (("docs.open", doc), ("docs.list", listed)):
            self.need(d.get("windowId") == window_id and d.get("kind") == kind_of(window_id),
                      "%s windowId=%s kind=%s(기대 %s·%s)" % (where, d.get("windowId"), d.get("kind"), window_id, kind_of(window_id)))
        return doc, listed


def apply_v5(s, ctx):
    fx = ctx["fixture"]
    r = s.apply("layout_apply", ctx["fixture_text"])
    s.need(r.get("applied") is True, "applied=%s" % r.get("applied"))
    s.need(r.get("version_read") == fx["Version"], "version_read=%s(기대 %s)" % (r.get("version_read"), fx["Version"]))
    s.need(r.get("docs") == len(fx["Documents"]), "docs=%s(기대 %d)" % (r.get("docs"), len(fx["Documents"])))
    time.sleep(ctx["settle"])  # 복원 뒤 활성화·보기 생성이 한 프레임 늦게 끝난다


def list_v5(s, ctx):
    listed = {d.get("id"): d for d in s.docs()}
    wanted = {d["Id"]: expected_id(d) for d in ctx["fixture"]["Documents"]}
    s.raw["expected"] = wanted
    s.need(sorted(listed) == sorted(wanted), "문서 Id %s(기대 %s)" % (sorted(listed), sorted(wanted)))
    for doc_id, win in wanted.items():
        d = listed.get(doc_id) or {}
        s.need(d.get("windowId") == win and d.get("kind") == kind_of(win), "%s windowId=%s kind=%s(기대 %s)" % (doc_id, d.get("windowId"), d.get("kind"), win))
        s.need(bool(d.get("title")), "%s 제목 없음" % doc_id)
    s.need(len({d.get("windowId") for d in listed.values()}) >= 3, "열린 창 종류 3 미만")


def save_v6(s, ctx):
    fx, saved = ctx["fixture"], s.saved()
    s.need(saved.get("Version") == SAVE_VERSION, "Version=%s" % saved.get("Version"))
    docs = {d.get("Id"): d for d in saved.get("Documents") or []}
    s.need(len(docs) == len(fx["Documents"]), "저장 문서 %d(기대 %d)" % (len(docs), len(fx["Documents"])))
    for item in fx["Documents"]:
        d, win = docs.get(item["Id"]) or {}, expected_id(item)
        s.need(d.get("WindowId") == win and d.get("Kind") == DEMO.get(win),
               "%s WindowId=%s Kind=%s(기대 %s·%s)" % (item["Id"], d.get("WindowId"), d.get("Kind"), win, DEMO.get(win)))
    dup_ids = {OLD_KINDS.get(str(k).lower(), k) for k in fx.get("DuplicateKinds") or []} | set(fx.get("DuplicateWindowIds") or [])
    s.need(dup_ids <= set(saved.get("DuplicateWindowIds") or []), "DuplicateWindowIds=%s(기대 %s 포함)" % (saved.get("DuplicateWindowIds"), sorted(dup_ids)))
    names = {DEMO[i] for i in dup_ids if i in DEMO}
    s.need(names <= set(saved.get("DuplicateKinds") or []), "DuplicateKinds=%s(기대 %s 포함)" % (saved.get("DuplicateKinds"), sorted(names)))


def open_legacy(s, ctx):
    s.open_one(GANTT, kind="gantt")  # 옛 이름 입력(D-R1: windows.open 이 아니라 docs.open)


def open_id(s, ctx):
    s.open_one(FORM, window=FORM)


def kind_compat(s, ctx):
    docs = s.docs()
    demo = [d for d in docs if d.get("windowId") in DEMO]
    for d in demo:
        s.need(d.get("kind") == kind_of(d["windowId"]), "%s kind=%s windowId=%s" % (d.get("id"), d.get("kind"), d["windowId"]))
    s.need(len(demo) == len(docs), "데모 창이 아닌 문서가 있음")
    s.need({d.get("kind") for d in demo} == {"grid", "gantt", "form"}, "kind 값 %s" % sorted({str(d.get("kind")) for d in demo}))


def param_pass(s, ctx):
    doc, listed = s.open_one(FORM, window=FORM, param=PARAM)  # title 없이 — 같은 제목 재사용이면 param 이 반영되지 않는다
    s.need(doc.get("parameter") == PARAM and listed.get("parameter") == PARAM,
           "parameter open=%s list=%s" % (doc.get("parameter"), listed.get("parameter")))
    rows = (s.raw.get("docs_list") or {}).get("result") or []  # 연 직후의 목록 원문
    s.need(bool(rows) and all("parameter" in d and (d.get("id") == doc.get("id") or d.get("parameter") is None) for d in rows),
           "다른 문서의 parameter 키 누락 또는 값 있음")
    saved = next((d for d in s.saved().get("Documents") or [] if d.get("Id") == doc.get("id")), None)
    s.need(saved is not None and not any(k.lower() == "parameter" for k in saved), "저장본에 문서가 없거나 Parameter 가 남음: %s" % saved)


def unknown_preserved(s, ctx):
    doc, _ = s.open_one(UNKNOWN, window=UNKNOWN)
    time.sleep(ctx["settle"])  # 자리표시 보기 생성

    def check(saved, where):
        rows = [d for d in saved.get("Documents") or [] if d.get("Id") == doc.get("id")]
        s.need(len(rows) == 1 and rows[0].get("WindowId") == UNKNOWN and "Kind" not in rows[0], "%s 저장 문서 %s" % (where, rows))

    first = s.saved("layout_get_first")
    check(first, "첫 저장")
    r = s.apply("layout_apply_again", json.dumps(first, ensure_ascii=False))  # 저장본을 다시 읽어 저장해도 남아야 한다
    s.need(r.get("applied") is True and r.get("docs") == len(first.get("Documents") or []), "다시 적용 %s" % r)
    time.sleep(ctx["settle"])
    check(s.saved("layout_get_second"), "다시 저장")
    again = next((d for d in s.docs() if d.get("id") == doc.get("id")), {})
    s.need(again.get("windowId") == UNKNOWN, "다시 적용 뒤 목록 windowId=%s" % again.get("windowId"))
    dump = s.remote("registry_dump", "registry.dump")
    found = [d for d in dump.get("diagnostics") or [] if str(d.get("id", "")).startswith("XKUI05")]
    s.raw["registry_dump"] = {"ok": True, "diagnostics_XKUI05": found}
    s.need(any(d.get("id") == "XKUI0505" and UNKNOWN in str(d.get("message")) for d in found), "XKUI0505 진단 없음")


def dump_windows(s, ctx):
    dump = s.remote("registry_dump", "registry.dump")
    windows = dump.get("windows") or []
    s.raw["registry_dump"] = {"ok": True, "windows": windows, "diagnostics": dump.get("diagnostics"), "runtime_errors": dump.get("runtime_errors")}
    s.need(len(windows) >= 3, "windows %d개" % len(windows))
    s.need(set(DEMO) <= {w.get("id") for w in windows}, "셸 기본 창 3종 없음")
    s.need(all(WINDOW_KEYS <= set(w) and w.get("owner") for w in windows), "원소 키 부족")
    s.need(all(w.get("owner") == DEMO_OWNER for w in windows if w.get("id") in DEMO), "데모 창 owner 가 %s 가 아님: %s" % (DEMO_OWNER, {w.get("id"): w.get("owner") for w in windows if w.get("id") in DEMO}))
    s.need(UNKNOWN not in {w.get("id") for w in windows}, "모르는 창이 등록됨")


STEPS = [apply_v5, list_v5, save_v6, open_legacy, open_id, kind_compat, param_pass, unknown_preserved, dump_windows]


def wait_ready(call, timeout_s):
    """기동 레이아웃 복원이 끝날 때까지(docs.list 가 ok) 기다린다 — 복원 중 조작은 복원이 덮어쓴다."""
    end, last = time.time() + timeout_s, None
    while True:
        last = call("docs.list")
        if isinstance(last, dict) and last.get("ok"):
            return True, last
        if time.time() >= end:
            return False, last
        time.sleep(1)


def run_steps(call, fixture_text, log, settle=1.0):
    """9단계를 차례로 실행한다. 한 단계가 실패해도 다음 단계는 실행해 원자료를 남긴다."""
    ctx = {"fixture_text": fixture_text, "fixture": json.loads(fixture_text), "settle": settle}
    out = []
    for fn in STEPS:
        s = Step(call)
        try:
            fn(s, ctx)
        except Exception as e:  # noqa: BLE001
            s.need(False, "%s: %s" % (type(e).__name__, e))
        out.append({"name": fn.__name__, "ok": not s.problems, "problems": s.problems, "raw": s.raw})
        log("%s %s %s" % ("✓" if not s.problems else "✗", fn.__name__, "; ".join(s.problems)[:400]))
    return out


def stale_desktop_build(conf, ui=UI):
    """같은 구성의 최신 빌드 로그 시작보다 오래된 dll(낡은 코드)인지 → (사유|None, 확인 기록). 로그가 없으면 확인하지 않는다."""
    starts = []
    for path in glob.glob(os.path.join(ui, "logs", "build_desktop_*.log")):
        with open(path, encoding="utf-8", errors="replace") as f:
            m = re.match(r"# build_desktop start (.{19}) conf=(\S+)", f.readline())
        starts += [(m.group(1), os.path.basename(path))] if m and m.group(2) == conf else []
    if not starts:
        return None, {"build_log": None}  # 확인 기준이 될 빌드 로그 없음
    started, name = max(starts)
    dll = os.path.join(host_identity.bin_dir("desktop", conf, "net11.0"), host_identity.get("desktop.dll_name"))
    built = os.path.getmtime(dll) if os.path.isfile(dll) else 0
    info = {"build_log": name, "build_started": started, "dll_mtime": time.strftime("%F %T", time.localtime(built)) if built else None}
    fresh = built >= time.mktime(time.strptime(started, "%Y-%m-%d %H:%M:%S")) - 5
    return (None if fresh else "실행 파일이 없거나 빌드 로그 %s 보다 오래됨(구성 %s, dll %s)" % (name, conf, info["dll_mtime"])), info


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", choices=["web", "desktop"], required=True)
    ap.add_argument("--bundle", default=BUNDLE, help="웹 진단판 wwwroot")
    ap.add_argument("--port", type=int, default=5834)
    ap.add_argument("--fixture", default=FIXTURE)
    ap.add_argument("--out")
    ap.add_argument("--conf", default="Release", help="데스크톱 구성(run_desktop_bg.sh --conf, 기본 Release=진단판)")
    ap.add_argument("--by", default=os.environ.get("PD_SLOT_OWNER", "window_registry_smoke"))
    ap.add_argument("--ready-timeout", type=int, default=90)
    ap.add_argument("--build", default="", help="빌드 식별(로그·커밋)")
    a = ap.parse_args(argv)
    if a.host == "web" and a.port in REJECT_PORTS:
        print("포트 %d 거부(사용자 인스턴스 포트)" % a.port)
        return 4
    out = os.path.abspath(a.out or os.path.join(UI, "logs", "b09_window_smoke_%s.json" % a.host))
    fixture = a.fixture if os.path.exists(a.fixture) else os.path.join(UI, a.fixture)
    with open(fixture, encoding="utf-8-sig") as f:
        fixture_text = f.read()
    lines = []

    def log(msg):
        lines.append("%s %s" % (datetime.datetime.now().strftime("%H:%M:%S"), msg))
        print(lines[-1])

    started = datetime.datetime.now().astimezone().isoformat(timespec="seconds")
    result = {"status": "INVALID", "host": a.host, "steps": [], "app_pid": None, "started_at": started, "build": a.build, "by": a.by,
              "fixture": os.path.relpath(os.path.abspath(fixture), UI), "conf": a.conf if a.host == "desktop" else None}
    stale, result["build_check"] = stale_desktop_build(a.conf) if a.host == "desktop" else (None, None)
    meta = {}
    a.shots = tempfile.mkdtemp(prefix="b09_window_shots_")  # 웹 기동부가 요구하는 폴더 — 이 스모크는 화면 캡처를 남기지 않는다
    try:
        with contextlib.nullcontext((None, {"status": "INVALID", "reason": stale})) if stale else launch(a, log) as (call, meta):
            if call is not None:
                ready, last = wait_ready(call, a.ready_timeout)
                if ready:
                    result["steps"] = run_steps(call, fixture_text, log)
                    result["status"] = "PASS" if all(s["ok"] for s in result["steps"]) else "FAIL"
                else:
                    result["reason"] = "기동 복원 대기 시간 초과: %s" % str(last)[:300]
    except Exception as e:  # noqa: BLE001 — 기동부·종료부 예외도 결과 파일로 남긴다
        result["reason"] = "%s: %s" % (type(e).__name__, str(e)[:300])
        if result["steps"]:
            result["status"] = "FAIL"
    finally:
        shutil.rmtree(a.shots, ignore_errors=True)
    meta = dict(meta or {})
    if meta.get("status") in ("WAIT", "INVALID"):  # 기동부가 앱을 못 띄운 사유
        result.update(status=meta.pop("status"), reason=meta.pop("reason", None))
    result.update(meta)
    result["app_pid"] = meta.get("pid")  # 웹은 이 프로세스 안의 headless 브라우저라 앱 PID 가 없다
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
    return {"PASS": 0, "FAIL": 1, "WAIT": 3}.get(result["status"], 2)


if __name__ == "__main__":
    sys.exit(main())

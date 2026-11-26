#!/usr/bin/env python3
# B07 메뉴 레지스트리 스모크(T-SMOKE) — 진단판 원격 명령으로 registry.dump·menu.list·메뉴/아이콘 추가·제거 왕복·command.run·nav.menu 원문을 남기고 요약 JSON 을 쓴다
"""
사용:
  python3 -B menu_registry_smoke.py --host web --bundle <진단판 wwwroot> [--port 5831] [--out logs/b07_smoke_web.json]
  python3 -B menu_registry_smoke.py --host desktop [--conf Debug] [--out logs/b07_smoke_desktop.json]
  python3 -B menu_registry_smoke.py --self-test        (앱 없음 — 요약 계산부만 확인)
웹은 127.0.0.1 정적 서버 + headless Chromium(ui28_web.Web, otest_playwright_bg 방식)이라 사용자 Chrome·화면을 건드리지 않는다.
데스크톱은 tools/run_desktop_bg.sh(화면 밖·임시 데이터 폴더)로 띄우고 자기 PID 만 종료한다. 사용자 파일 가드 rc=9 이면 가드를 풀지 않고
result=WAIT(carryover UTODO-0031)로 남긴다. 원문은 <out 이름>_raw/<키>.json 고정 경로에 덮어쓴다.
rc: 0=PASS 1=FAIL 2=WAIT·기동 실패 3=거부(포트 5180·5220)
"""
import argparse
import datetime
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(UI, "test", "common"))
BASE = os.environ.get("B07_BASE") or "e8950ea"  # B07 착수 기준 커밋(B06 완료)
OLD_MENUS = ["FileMenu", "WindowMenu", "ViewMenu", "SystemMenuItem"]
ROOTS = ["shell.mnuFile", "shell.mnuWindow", "shell.mnuView", "shell.mnuSys"]
NAV_TOTAL = 63
SMOKE_ID = "smoke.b07"  # AutomationId = menu.smoke.b07
SMOKE_AID = "menu." + SMOKE_ID
SMOKE_ICON = "smoke.b07.path"
SMOKE_PATH = "M2,2 L14,2 L14,14 L2,14 Z M5,5 L11,11"
# 조건에 따라 만들어지지 않거나 숨는 기준 항목 — 사유가 있을 때만 빠져도 된다(판정기 CONDITIONAL 과 같은 범위)
CONDITIONAL = [
    (r"shell\.mnuOpenBench", "벤치 실행 파일이 없으면 숨김"),
    (r"shell\.mnuSysGpuRestart", "재시작 필요 상태가 아니면 숨김"),
    (r"shell\.mnuShowLog", "로그 보기 조건 미충족 시 숨김"),
    (r"shell\.mnuExit", "호스트가 종료를 지원하지 않으면 숨김(웹)"),
    (r"shell\.mnuWinMultiMonitor|shell\.mnuWinMonitorNone", "모니터 수에 따라 둘 중 하나만 만든다"),
    (r"shell\.mnuWinList\d|shell\.mnuWinListMore", "열린 창 수만큼만 칸을 만든다"),
    (r"shell\.mnuWinSnap\w*", "켜진 스냅 배치만 칸을 만든다"),
]
RAW_KEYS = ["registry_dump", "menu_list", "menu_add", "menu_list_after_add", "menu_remove", "menu_list_after_remove", "icon_add",
            "menu_icons_after_icon_add", "icon_remove", "menu_icons_after_icon_remove", "command_run", "nav_menu"]


def menu_ids(v):
    s = json.dumps(v, ensure_ascii=False)
    return set(re.findall(r'"(?:automationId|AutomationId|id)"\s*:\s*"((?:shell\.mnu|menu\.)[A-Za-z0-9._-]+)"', s))


def list_ids(v):
    """menu.list 응답(또는 {root,response} 묶음 목록)에서 앱이 실제로 찾은 id 만 센다.
    items 의 id 와, ok 응답에 items 가 1개 이상 있을 때의 result.id(루트를 찾았다는 증거 — 못 찾으면 menu.list 가 ok=false)를 센다.
    요청 묶음 키·실패 응답의 id 는 세지 않는다."""
    resps = [e.get("response") for e in v if isinstance(e, dict)] if isinstance(v, list) else [v]
    ids = set()
    for r in resps:
        res = result_of(r)
        if isinstance(res, dict):
            items = res.get("items")
            ids |= menu_ids(items or [])
            if isinstance(items, list) and items:
                ids |= menu_ids({"id": res.get("id")})
    return ids


def icon_item(v, aid):
    for m in (v or {}).get("menus") or []:
        for it in m.get("items") or []:
            if it.get("id") == aid:
                return it
    return None


def result_of(resp):
    return resp.get("result") if isinstance(resp, dict) and resp.get("ok") else None


def summarize(raw, base_aids, base_nav, host):
    """raw = {키: 원격 응답 원문}. 판정 목록과 빠진 ID·허용 예외를 돌려준다(앱 없이 시험 가능한 순수 함수)."""
    checks = []

    def ck(name, ok, detail=""):
        checks.append({"name": name, "pass": bool(ok), "detail": str(detail)[:300]})

    rg = result_of(raw.get("registry_dump")) or {}
    menus = rg.get("menus") or []
    ck("registry.dump menus 70개 이상·owner 있음", len(menus) >= 70 and all(m.get("owner") for m in menus), len(menus))
    shell_cmds = [c for c in rg.get("commands") or [] if c.get("owner") == "xkxk.shell"]
    ck("xkxk.shell 명령 61개", len(shell_cmds) == 61, len(shell_cmds))
    ck("icons.explicit 목록", isinstance((rg.get("icons") or {}).get("explicit"), list))
    got = list_ids(raw.get("menu_list"))
    missing = sorted(base_aids - got)
    allowed, bad = [], []
    for m in missing:
        reason = next((r for p, r in CONDITIONAL if re.fullmatch(p, m)), None)
        (allowed if reason else bad).append({"id": m, "reason": reason} if reason else m)
    ck("menu.list(메뉴 열기 전)에 기준 AutomationId 포함(조건부만 예외)", not bad and bool(got), bad[:8])
    add = result_of(raw.get("menu_add")) or {}
    ck("registry.menu.add ok", add.get("ok") is True, add)
    after, gone = list_ids(raw.get("menu_list_after_add")), list_ids(raw.get("menu_list_after_remove"))
    ck("추가 메뉴 표시", SMOKE_AID in after, SMOKE_AID)
    ck("registry.menu.remove ok", (result_of(raw.get("menu_remove")) or {}).get("ok") is True, raw.get("menu_remove"))
    ck("제거 뒤 사라짐", SMOKE_AID not in gone, SMOKE_AID)
    ck("registry.icon.add ok", (result_of(raw.get("icon_add")) or {}).get("ok") is True, raw.get("icon_add"))
    ia = icon_item(result_of(raw.get("menu_icons_after_icon_add")), SMOKE_AID) or {}
    ck("아이콘 추가 뒤 추가 항목이 레지스트리 PathData 로 그려짐", ia.get("iconKey") == SMOKE_ICON and ia.get("geometryFound") is True, ia)
    ck("registry.icon.remove ok", (result_of(raw.get("icon_remove")) or {}).get("ok") is True, raw.get("icon_remove"))
    ir = icon_item(result_of(raw.get("menu_icons_after_icon_remove")), SMOKE_AID) or {}
    ck("아이콘 제거 뒤 다시 그림(geometryFound=false)", ir.get("iconKey") == SMOKE_ICON and ir.get("geometryFound") is False, ir)
    cr = result_of(raw.get("command_run")) or {}
    ck("command.run 신규 명령 실행", cr.get("executed") is True and str(cr.get("id", "")).startswith("xkxk."), cr)
    if host == "web":
        nv = result_of(raw.get("nav_menu")) or {}
        drawer = [e.get("id") for e in nv.get("drawer") or [] if isinstance(e, dict)]
        ck("nav.menu drawer id 가 기준 63행과 같은 순서(D1)", drawer == base_nav and bool(nv.get("tabs")),
           "빠짐 %s 더함 %s" % (sorted(set(base_nav) - set(drawer))[:4], sorted(set(drawer) - set(base_nav))[:4]))
    return checks, missing, allowed


def git_show(rel):
    r = subprocess.run(["git", "-c", "core.quotepath=off", "show", "%s:./%s" % (BASE, rel)], cwd=UI, capture_output=True, text=True)
    if r.returncode != 0:
        raise SystemExit("기준 커밋 %s 의 %s 를 읽지 못함" % (BASE, rel))
    return re.sub(r"<!--.*?-->", "", r.stdout, flags=re.S)


def base_aids():
    ids = set()
    for m in OLD_MENUS:
        ids |= set(re.findall(r'AutomationId="(shell\.[A-Za-z0-9]+)"', git_show("src/xkxk.frameworks.ui.views/Menus/%s.axaml" % m)))
    return ids


def base_nav_ids():
    s = re.sub(r"//[^\n]*", "", git_show("src/xkxk.frameworks.ui.data/Navigation/NavMenuCatalog.cs"))
    ids = re.findall(r'^\s*(?:Tab\()?[GIX]\(\s*[FWVS]\s*,\s*\d+\s*,\s*"(\w+)"', s, re.M)
    if len(ids) != NAV_TOTAL:
        raise SystemExit("기준 NavMenuCatalog 행 %d개(%d 기대)" % (len(ids), NAV_TOTAL))
    return ids


def scenario(call, run_cmd, log):
    """원격 호출 순서. 첫 호출들은 메뉴를 열지 않는 menu.list(4 루트) — 어떤 메뉴도 열기 전 원문(D4)."""
    raw = {"menu_list": [{"root": r, "response": call("menu.list", id=r)} for r in ROOTS]}  # 묶음 키는 root(id 아님 — 요청 id 가 AutomationId 로 세이지 않게)
    log("menu.list 4 루트(메뉴 열기 전) 완료")
    raw["registry_dump"] = call("registry.dump")
    raw["nav_menu"] = call("nav.menu")
    raw["menu_add"] = call("registry.menu.add", id=SMOKE_ID, parent="xkxk.file", command="xkxk.window.cascade", icon=SMOKE_ICON, title="B07 스모크")
    raw["menu_list_after_add"] = call("menu.list", id="shell.mnuFile")
    raw["icon_add"] = call("registry.icon.add", key=SMOKE_ICON, path=SMOKE_PATH)
    raw["menu_icons_after_icon_add"] = call("menu.icons", id="shell.mnuFile")
    raw["icon_remove"] = call("registry.icon.remove", key=SMOKE_ICON)
    raw["menu_icons_after_icon_remove"] = call("menu.icons", id="shell.mnuFile")
    raw["menu_remove"] = call("registry.menu.remove", id=SMOKE_ID)
    raw["menu_list_after_remove"] = call("menu.list", id="shell.mnuFile")
    raw["command_run"] = call("command.run", id=run_cmd)
    log("시나리오 호출 %d건 완료" % len(raw))
    return raw


def write_json(p, v):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(v, f, ensure_ascii=False, indent=1)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", choices=["web", "desktop"])
    ap.add_argument("--out")
    ap.add_argument("--bundle", help="웹 진단판 wwwroot")
    ap.add_argument("--port", type=int, default=5831)
    ap.add_argument("--shots", default=os.path.join(UI, "logs", "b07_smoke_shots"))
    ap.add_argument("--conf", default="Debug", help="데스크톱 구성(run_desktop_bg.sh --conf)")
    ap.add_argument("--run-command", default="xkxk.view.showExplorer", help="command.run 으로 실행할 B07 신규 명령")
    ap.add_argument("--by", default=os.environ.get("PD_SLOT_OWNER", "menu_registry_smoke"))
    ap.add_argument("--build", default="", help="빌드 식별(로그·커밋)")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        from menu_registry_smoke_selftest import self_test
        return self_test()
    if not a.host or (a.host == "web" and not a.bundle):
        ap.error("--host 와(웹이면) --bundle 이 필요하다")
    if a.port in (5180, 5220):
        print("포트 %d 거부(사용자 인스턴스 포트)" % a.port)
        return 3
    out = os.path.abspath(a.out or os.path.join(UI, "logs", "b07_smoke_%s.json" % a.host))
    stem = os.path.splitext(out)[0]
    lines = []
    logp = stem + ".log"

    def log(s):
        lines.append("%s %s" % (datetime.datetime.now().strftime("%H:%M:%S"), s))
        print(lines[-1])
        write_json_text(logp, "\n".join(lines) + "\n")

    def write_json_text(p, t):
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f:
            f.write(t)

    log("시작 host=%s base=%s build=%s" % (a.host, BASE, a.build))
    import menu_registry_smoke_hosts as hosts
    raw, meta = (hosts.run_web if a.host == "web" else hosts.run_desktop)(a, log)
    summary = {"host": a.host, "log": logp, "by": a.by, "build": a.build, "base": BASE, "menu_list_before_open": raw is not None,
               "raw": {}, "missing_automation_ids": [], "allowed_missing": [], "checks": []}
    summary.update({k: v for k, v in meta.items()})
    if raw is not None:
        for k in RAW_KEYS:
            p = os.path.join(stem + "_raw", k + ".json")
            write_json(p, raw.get(k))
            summary["raw"][k] = p
        checks, missing, allowed = summarize(raw, base_aids(), base_nav_ids(), a.host)
        summary.update(checks=checks, missing_automation_ids=missing, allowed_missing=allowed)
        summary["result"] = "PASS" if checks and all(c["pass"] for c in checks) and summary.get("pageerror", 0) == 0 else "FAIL"
        for c in checks:
            log("%s %s %s" % ("✓" if c["pass"] else "✗", c["name"], "" if c["pass"] else c["detail"]))
    log("결과 %s" % summary["result"])
    write_json(out, summary)
    print(out)
    return {"PASS": 0, "FAIL": 1}.get(summary["result"], 2)


if __name__ == "__main__":
    sys.exit(main())

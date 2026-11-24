# Q17 데스크톱 언어 전환 실측 — lang.set en 유지·[loc] 재진입 경고·재시작 뒤 유지·en 상태 i18n.scan 한글 잔존(lead 01:3x 원인 확정용)
"""
사용: python3 test/desktop/measure/retest_lang_desktop.py --conf <Work|Release> [--out logs/odev6r/lang_desktop.json]
전제: 진단 빌드(PdDiagnostics=true), 데스크톱 실행 슬롯 확보. 사용자 설정 파일(settings.json·layout.json)은 백업 후 원복한다.
기동: run_desktop_bg.sh --kill 의 종료 대상은 그 스크립트가 기록한 자기 PID 뿐이다. 다른 Pd 가 떠 있으면 rc=11 로 바로 실패한다.
"""
import argparse
import datetime
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
TOOLS = _paths.TOOLS  # 옛 위치(poc/ui/tools) — run_desktop_bg.sh·pdctl.py·logs 경로 기준 그대로
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import pdctl  # noqa: E402
from retest_remote import Remote  # noqa: E402
from retest_user_files import wait_app_exit  # noqa: E402

HANGUL = re.compile(r"[가-힣]")


def user_dir():
    """사용자 설정 폴더(%LOCALAPPDATA%\\PdPoc). 앱이 꺼져 있으면 연결 정보 파일이 없으므로 pdctl.info_path(없으면 sys.exit)를 쓰지 않는다."""
    if os.environ.get("PD_REMOTE_INFO"):
        return os.path.dirname(os.environ["PD_REMOTE_INFO"])
    found = sorted(glob.glob("/mnt/c/Users/*/AppData/Local/PdPoc"), key=os.path.getmtime)
    if not found:
        raise RuntimeError("사용자 설정 폴더(PdPoc)를 찾지 못함")
    return found[-1]


def launch(conf):
    """사용자 조건(--remote-control 만 — 종료 자동 저장·기동 자동 복원)으로 기동하고 앱 로그 경로를 돌려준다."""
    old = None
    try:
        old = pdctl.load_info().get("pid")
    except SystemExit:
        pass
    run = subprocess.run(["bash", os.path.join(TOOLS, "run_desktop_bg.sh"), "--conf", conf, "--kill", "--", "--remote-control"],
                         capture_output=True, timeout=80)
    out = run.stdout.decode("utf-8", "replace")
    if run.returncode == 11:
        raise RuntimeError("기동 거부(rc=11): 다른 Pd 가 떠 있다. --kill 은 기록된 자기 PID 만 끄므로 그 Pd 는 그대로 둔다")
    m = re.search(r"로그=(\S+)", out)
    for _ in range(40):
        try:
            info = pdctl.load_info()
            if info.get("pid") != old:
                r = Remote()
                if r.call("status") is not None:
                    return r, (m.group(1) if m else None)
        except SystemExit:
            pass
        time.sleep(1)
    raise RuntimeError("기동 실패: " + out[-300:])


def classify(hits, r):
    """
    en 상태 한글 잔존을 화면 문구와 데이터로 나눈다(lead 01:5x — Q17 판정 근거). 분류는 규칙 기반 추정이므로 목록 전체를 남긴다.
    data: 그리드 셀(DataGridCell)과 같은 글자의 TextBlock(셀 안 글자) / log: 로그 목록 줄(ListBoxItem·같은 글자) — 남는 과거 기록
    title: 열린 문서 제목(docs.list — 열 때 그 언어로 지은 이름) / ui: 그 밖(열 머리글·버튼·라벨·자리표시·알림 문구 등 — 번역 대상 후보)
    """
    cells = {h.get("text") for h in hits if h.get("type") == "DataGridCell"}
    logs = {h.get("text") for h in hits if h.get("type") == "ListBoxItem"}
    titles = {d.get("title") for d in r.call("docs.list") or []}
    out = {"data": [], "log": [], "title": [], "ui": []}
    for h in hits:
        t = h.get("text")
        kind = ("data" if t in cells else "log" if t in logs else "title" if t in titles else "ui")
        out[kind].append({"type": h.get("type"), "id": h.get("id"), "name": h.get("name"), "text": t})
    out["counts"] = {k: len(v) for k, v in out.items() if k != "counts"}
    out["ui_unique"] = sorted({x["text"] for x in out["ui"]})
    return out


def lang(r):
    g = r.call("lang.get") or {}
    return {"language": g.get("language"), "culture": g.get("culture")}


def shutdown(r):
    r.call("app.shutdown")
    if not wait_app_exit(30):  # 고정 대기 대신 프로세스 0개 확인(종료 중 layout.json 재기록이 복원본을 덮은 02:15 사고)
        raise RuntimeError("앱이 30초 안에 끝나지 않음 — 사용자 파일 복원 전 확인 필요")


def main(argv):
    p = argparse.ArgumentParser(description="Q17 데스크톱 언어 전환 실측")
    p.add_argument("--conf", default="Release")
    p.add_argument("--out", default=os.path.join(ROOT, "logs", "odev6r", "lang_desktop_%s.json" % datetime.datetime.now().strftime("%H%M%S")))
    a = p.parse_args(argv)
    ud = user_dir()
    backup = a.out + ".backup"
    os.makedirs(backup, exist_ok=True)
    saved = {f: os.path.exists(os.path.join(ud, f)) for f in ("settings.json", "layout.json")}
    for f, ok in saved.items():
        if ok:
            shutil.copy2(os.path.join(ud, f), os.path.join(backup, f))
    md5 = lambda f: (__import__("hashlib").md5(open(os.path.join(ud, f), "rb").read()).hexdigest()[:8]
                     if os.path.exists(os.path.join(ud, f)) else None)
    rep = {"started": datetime.datetime.now().isoformat(timespec="seconds"), "conf": a.conf, "backup": saved,
           "md5_before": {f: md5(f) for f in saved}}
    try:
        r, log = launch(a.conf)
        rep["boot"] = lang(r)
        # odev-2 01:3x 요청: 언어 전환 전후 테마·창 스타일 등 다른 콤보 값이 바뀌지 않는가(목록 재알림 되쓰기 위험)
        # 설정값(theme·themeId·wheelMode) + 설정 패널 콤보 선택 순번(창 스타일은 settings.get 에 없어 콤보로 본다). 콤보가 화면에 없으면 None.
        combos = ("shell.cmbTheme", "shell.cmbWheelMode", "shell.cmbWindowStyle")
        keep = lambda: dict({k: v for k, v in (r.call("settings.get") or {}).items() if k in ("themeId", "theme", "wheelMode")},
                            **{c: (r.call("ui.find", id=c) or {}).get("selectedIndex") for c in combos})
        rep["settings_before"] = keep()
        rep["set_response"] = (lambda s: {"language": (s or {}).get("language"), "culture": (s or {}).get("culture")})(r.call("lang.set", code="en"))
        waits = []
        for _ in range(3):
            time.sleep(1)
            waits.append(lang(r))
        rep["after_waits"] = waits
        rep["settings_after"] = keep()
        rep["combo"] = (r.call("ui.find", id="shell.cmbLanguage") or {}).get("text")  # 설정 패널 언어 콤보 표시(English 여야)
        scan = r.call("i18n.scan") or {}
        hits = [h for h in scan.get("hits") or [] if HANGUL.search(h.get("text") or "")]
        rep["scan_en"] = {"language": scan.get("language"), "count": scan.get("count"), "hangul_left": len(hits),
                          "sample": [h.get("text") for h in hits[:15]], "classified": classify(hits, r)}
        rep["missing_en"] = (r.call("i18n.missing", lang="en") or {}).get("count")
        shutdown(r)
        text = open(log, encoding="utf-8", errors="replace").read() if log and os.path.exists(log) else ""
        rep["loc_log"] = {"path": log, "reentrant": [l for l in text.splitlines() if "재진입" in l][:5],
                          "loc_lines": [l for l in text.splitlines() if "[loc]" in l][:10]}
        r2, _ = launch(a.conf)
        rep["after_restart"] = lang(r2)
        r2.call("lang.set", code="ko")
        shutdown(r2)
    except Exception as e:  # 실패해도 사용자 파일 원복은 반드시 한다
        rep["error"] = repr(e)
    finally:
        wait_app_exit(30)  # 예외로 빠졌어도 앱이 끝난 뒤에 복원
        for f, ok in saved.items():
            if ok:
                shutil.copy2(os.path.join(backup, f), os.path.join(ud, f))
        rep["restored"] = saved
        rep["md5_after"] = {f: md5(f) for f in saved}  # lead 01:4x: 사용자 조건 시험은 시작 전·종료 후 md5 기록
        rep["user_files_same"] = rep["md5_after"] == rep["md5_before"]
    en = lambda d: (d or {}).get("language") == "en" and (d or {}).get("culture") == "en-US"
    rep["checks"] = {
        "set_en": en(rep.get("set_response")),
        "en_after_waits": all(en(w) for w in rep.get("after_waits") or [None]),
        "persist_after_restart": en(rep.get("after_restart")),
        "other_settings_unchanged": rep.get("settings_before") == rep.get("settings_after"),
    }
    # 재진입 경고는 판정이 아니라 원인 확정 증거: 나오면 '콤보 되쓰기 재진입'이 실제로 있었고 가드가 막은 것이다
    rep["cause"] = ("재진입 확인(가드가 차단)" if (rep.get("loc_log") or {}).get("reentrant")
                    else "재진입 경고 없음 — 원인 미확정(되쓰기가 비동기이거나 다른 경로)")
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(rep, f, ensure_ascii=False, indent=2)
    cl = (rep.get("scan_en") or {}).get("classified") or {}
    brief = {k: rep.get(k) for k in ("boot", "set_response", "after_waits", "settings_before", "settings_after", "combo", "after_restart", "checks", "cause", "error")}
    brief["hangul_left"] = {"total": (rep.get("scan_en") or {}).get("hangul_left"), "counts": cl.get("counts"), "ui_unique": cl.get("ui_unique")}
    print(json.dumps(brief, ensure_ascii=False, indent=1))  # 전체 목록(data·log·title·ui)은 결과 JSON scan_en.classified
    print("→", a.out)
    return 0 if all(rep["checks"].values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

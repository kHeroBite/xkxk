#!/usr/bin/env python3
# 테마 요청·적용 동기 측정 — 테마 편집기 삭제·설정 창 색 테마 콤보·다음 테마 연속 호출·기반 변경이 실제 적용 테마와 맞는지 원격 명령만으로 재어 JSON 에 저장한다(판정은 judge 규칙이 낸다)
"""
사용: PD_REMOTE_INFO=<data-dir>/remote-control.json python3 -B retest_theme_sync.py --out <logs/<에이전트>/<회차>/theme_sync.json> [--rounds 12]
앱은 먼저 run_desktop_bg.sh 로 진단 빌드를 임시 --data-dir·--profile-dir 와 --remote-control 로 띄워 둔다.
격리되지 않은 앱(사용자 폴더 사용)에 붙으면 rc=3 으로 거부한다. poc/ui/logs/ui28/ 에는 쓰지 않는다(otest 전용).
키 주입은 하지 않는다. 사용자 테마는 측정 안에서 만들고 지운다.
값: 각 사례의 consistent=콤보·목록 표시가 적용된 테마와 같은가, nextBurst.deltas=다음 테마 2회 동시 요청이 옮긴 칸 수(2가 정답).
다음 테마 연속 호출은 HTTP 2건을 동시에 보낸다. 같은 프레임에 겹칠 때만 옛 결함이 드러나므로 --rounds 만큼 반복해 분포를 남긴다.
종료 코드: 0=측정 완료, 2=연결 실패, 3=실행 거부. PASS/FAIL 은 판정 도구가 낸다.
"""
import argparse
import json
import os
import sys
import threading
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402

SETTLE_S = 1.2  # 테마 적용은 프레임 뒤로 미뤄지고 스피너 해제까지 기다린다
COMBO = "settings.cmbColorTheme"
EDITOR_LIST = "theme.cboTheme"


def safe(r, cmd, **kw):
    """명령 실패도 측정값이다 — 예외 대신 {"error": 문구} 로 남긴다."""
    try:
        result = r.call(cmd, **kw)
    except Exception as ex:  # noqa: BLE001 — 측정 도구는 멈추지 않고 실패를 기록한다
        return {"error": str(ex)}
    return result if result is not None else {"error": "응답 없음"}


def theme_ids(state):
    """콤보·목록의 항목 순서 = 내장 먼저, 이어서 사용자 테마(ThemeCatalog.Themes)."""
    return list(state.get("builtinThemes") or []) + [t["id"] for t in state.get("userThemes") or []]


def snapshot(r, control):
    """지금 적용 테마와 콤보·목록 표시를 함께 읽는다. consistent = 표시 칸이 적용 테마 칸과 같다."""
    state = safe(r, "themes.get")
    node = safe(r, "ui.find", id=control)
    ids = theme_ids(state) if isinstance(state, dict) else []
    active = state.get("activeThemeId") if isinstance(state, dict) else None
    index = node.get("selectedIndex") if isinstance(node, dict) else None
    return {
        "activeThemeId": active,
        "requestedThemeId": state.get("requestedThemeId") if isinstance(state, dict) else None,
        "mode": state.get("mode") if isinstance(state, dict) else None,
        "selectedIndex": index,
        "selectedText": node.get("text") if isinstance(node, dict) else None,
        "expectedIndex": ids.index(active) if active in ids else None,
        "consistent": active in ids and index == ids.index(active),
    }


def after(r, control, action):
    """동작 직후와 적용이 끝난 뒤를 각각 읽는다. 직후 값은 재진입으로 튀는지 보는 표본이다."""
    result = action()
    immediate = snapshot(r, control)
    time.sleep(SETTLE_S)
    return {"result": result, "immediate": immediate, "settled": snapshot(r, control)}


def measure_settings_combo(r):
    """I-2: 설정 창 모양 페이지를 연 채 테마를 바꾸면 색 테마 콤보가 따라오는가."""
    safe(r, "sys.open", view="Settings")
    safe(r, "settings.window", category="Appearance")
    time.sleep(0.3)
    state = safe(r, "themes.get")
    builtin = state.get("builtinThemes") or []
    out = {"open": snapshot(r, COMBO), "builtin": builtin}
    out["themeNext"] = after(r, COMBO, lambda: safe(r, "theme.next"))
    out["toggleTheme"] = after(r, COMBO, lambda: safe(r, "ui.invoke", command="ToggleTheme"))
    out["modeComboDark"] = after(r, COMBO, lambda: safe(r, "ui.select", id="shell.cmbTheme", index=2))
    out["modeComboLight"] = after(r, COMBO, lambda: safe(r, "ui.select", id="shell.cmbTheme", index=1))
    # 콤보에서 직접 고르기: 칸마다 한 번씩(모드가 다른 테마로 넘어가는 경우 포함)
    picks = []
    for index, theme_id in enumerate(builtin):
        step = after(r, COMBO, lambda i=index: safe(r, "ui.select", id=COMBO, index=i))
        step["picked"] = theme_id
        step["pickApplied"] = step["settled"]["activeThemeId"] == theme_id
        step["noJump"] = step["immediate"]["selectedIndex"] == index
        picks.append(step)
    out["directPick"] = picks
    return out


def measure_next_burst(r, rounds):
    """M-3: 다음 테마 2건을 동시에 보내면 두 칸 옮겨 가는가."""
    deltas = []
    for _ in range(rounds):
        before = safe(r, "themes.get")
        ids = theme_ids(before)
        start = ids.index(before["activeThemeId"])
        threads = [threading.Thread(target=lambda: Remote().call("theme.next")) for _ in range(2)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()
        time.sleep(SETTLE_S)
        now = safe(r, "themes.get")
        deltas.append((ids.index(now["activeThemeId"]) - start) % len(ids))
    return {"deltas": deltas, "two": deltas.count(2), "one": deltas.count(1), "other": len(deltas) - deltas.count(2) - deltas.count(1)}


def measure_editor_delete(r):
    """I-1: 적용 중인 사용자 테마를 편집기에서 지우면 편집기가 남은 테마로 옮겨 가는가."""
    safe(r, "theme.openEditor")
    time.sleep(0.4)
    safe(r, "settings.set", themeId=(safe(r, "themes.get").get("builtinThemes") or ["light"])[0])
    time.sleep(SETTLE_S)
    safe(r, "ui.click", id="theme.btnClone")  # 편집기 경로로 복제 → 편집기 선택 = 새 사용자 테마, 즉시 적용
    time.sleep(SETTLE_S)
    state = safe(r, "themes.get")
    created = (state.get("userThemes") or [{}])[-1].get("id")
    out = {"created": created, "beforeDelete": snapshot(r, EDITOR_LIST), "nameBefore": safe(r, "ui.find", id="theme.txtName").get("text")}
    out["delete"] = after(r, EDITOR_LIST, lambda: safe(r, "ui.click", id="theme.btnDelete"))
    time.sleep(0.2)
    out["nameAfter"] = safe(r, "ui.find", id="theme.txtName").get("text")
    out["deleteButton"] = safe(r, "ui.find", id="theme.btnDelete").get("enabled")
    out["status"] = safe(r, "ui.find", id="theme.lblStatus").get("text")
    out["remaining"] = [t["id"] for t in safe(r, "themes.get").get("userThemes") or []]
    return out


def measure_editor_rebase(r):
    """기반 변경: 적용 중인 사용자 테마의 기반을 바꾸면 모드가 따라가는가(ThemeCatalog.Rename)."""
    safe(r, "theme.openEditor")
    time.sleep(0.3)
    safe(r, "settings.set", themeId=(safe(r, "themes.get").get("builtinThemes") or ["light"])[0])
    time.sleep(SETTLE_S)
    safe(r, "ui.click", id="theme.btnClone")
    time.sleep(SETTLE_S)
    state = safe(r, "themes.get")
    created = (state.get("userThemes") or [{}])[-1].get("id")
    before = safe(r, "themes.get")
    safe(r, "ui.toggle", id="theme.chkDark")
    safe(r, "ui.click", id="theme.btnSave")
    time.sleep(SETTLE_S)
    now = safe(r, "themes.get")
    out = {"created": created, "modeBefore": before.get("mode"), "modeAfter": now.get("mode"), "activeAfter": now.get("activeThemeId")}
    safe(r, "ui.click", id="theme.btnDelete")
    time.sleep(SETTLE_S)
    return out


def measure_editor_rename(r):
    """이름만 저장: 목록 항목을 교체해도 편집기 목록·설정 창 콤보의 선택이 남는가(ThemeCatalog.Rename → Themes[i] = theme)."""
    safe(r, "sys.open", view="Settings")
    safe(r, "settings.window", category="Appearance")
    safe(r, "theme.openEditor")
    time.sleep(0.4)
    safe(r, "settings.set", themeId=(safe(r, "themes.get").get("builtinThemes") or ["light"])[0])
    time.sleep(SETTLE_S)
    safe(r, "ui.click", id="theme.btnClone")
    time.sleep(SETTLE_S)
    safe(r, "ui.setText", id="theme.txtName", text="sync-rename")
    safe(r, "ui.click", id="theme.btnSave")
    time.sleep(SETTLE_S)
    out = {"editor": snapshot(r, EDITOR_LIST), "combo": snapshot(r, COMBO), "status": safe(r, "ui.find", id="theme.lblStatus").get("text")}
    safe(r, "ui.click", id="theme.btnDelete")
    time.sleep(SETTLE_S)
    out["remaining"] = [t["id"] for t in safe(r, "themes.get").get("userThemes") or []]
    return out


def measure_clone_then_set(r, rounds):
    """자동화: 복제 직후 색 지정이 복제본에 들어가는가(적용 대기 중 옛 활성 테마를 읽으면 '내장이라 편집 불가'로 실패)."""
    results = []
    for i in range(rounds):
        safe(r, "settings.set", themeId=(safe(r, "themes.get").get("builtinThemes") or ["light"])[0])
        time.sleep(SETTLE_S)
        calls = len(r.calls)
        safe(r, "theme.clone", name="sync-probe-%d" % i)
        safe(r, "theme.set", colors="Accent=#123456")
        results.append(all(c["ok"] for c in r.calls[calls:]))
        time.sleep(SETTLE_S)
        now = safe(r, "themes.get")
        for t in now.get("userThemes") or []:
            safe(r, "theme.delete", id=t["id"])
        time.sleep(SETTLE_S)
    return {"ok": results.count(True), "failed": results.count(False)}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--rounds", type=int, default=12)
    args = p.parse_args()
    if os.path.normpath(args.out).replace("\\", "/").find("logs/ui28/") >= 0:
        print("logs/ui28/ 는 otest 결과 전용이다 — 원시 측정은 logs/<에이전트>/<회차>/ 에 쓴다")
        return 3
    r = Remote()
    status = r.call("status")
    if not status:
        print("앱 연결 실패")
        return 2
    pid = status.get("pid")
    iso = retest_user_files.isolation(pid)
    if not (iso and iso[0]):
        print("격리되지 않은 앱이라 거부한다: %s" % (iso,))
        return 3
    started = time.time()
    raw = {"pid": pid}
    raw["settingsCombo"] = measure_settings_combo(r)
    raw["nextBurst"] = measure_next_burst(r, args.rounds)
    raw["cloneThenSet"] = measure_clone_then_set(r, 3)
    raw["editorDelete"] = measure_editor_delete(r)
    raw["editorRebase"] = measure_editor_rebase(r)
    raw["editorRename"] = measure_editor_rename(r)
    raw["elapsed_ms"] = int((time.time() - started) * 1000)
    raw["failedCalls"] = [c for c in r.calls if not c["ok"]]
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(raw, f, ensure_ascii=False, indent=1)
    print("저장: %s" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

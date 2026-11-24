#!/usr/bin/env python3
# 요건 7 통합 설정 창 측정 — 원격 명령만으로 창 열기·인벤토리 대조·즉시 반영·저장·기본값·검색·재시작 사유 값을 모아 JSON 에 저장한다(판정은 judge 규칙이 낸다)
"""
사용: python3 -B retest_settings_window.py --out <logs/<에이전트>/<회차>/settings.json> [--settings-file <임시 프로필의 settings.json>]
앱은 먼저 run_desktop_bg.sh 로 진단 빌드를 임시 --data-dir·--profile-dir 와 --remote-control 로 띄워 둔다.
--settings-file: WSL 경로. 주면 변경 뒤 파일에 저장됐는지도 읽는다(없으면 saved 측정 생략).
격리되지 않은 앱(사용자 폴더 사용)에 붙으면 rc=3 으로 거부한다. logs/ui28/ 에는 쓰지 않는다(otest 전용).
키 주입은 하지 않는다. Esc 는 진단 명령 settings.esc 가 있을 때만 측정한다.
종료 코드: 0=측정 완료, 2=연결 실패, 3=실행 거부. PASS/FAIL 은 판정 도구가 낸다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402

SAVE_WAIT_S = 1.5  # 저장은 비동기라 파일 대조 전에 기다린다
PAGE_WAIT_S = 0.3
CATEGORY_IDS = ("Appearance", "Window", "Language", "Layout", "System")
DIALOG = "settings"


def safe(r, cmd, **kw):
    """명령 실패도 측정값이다 — 예외 대신 {"error": 문구} 로 남긴다."""
    try:
        return r.call(cmd, **kw)
    except Exception as ex:  # noqa: BLE001 — 측정 도구는 멈추지 않고 실패를 기록한다
        return {"error": str(ex)}


def is_visible(node):
    return bool(isinstance(node, dict) and node.get("effectivelyVisible"))


def open_names(r):
    return (safe(r, "sys.get") or {}).get("open") or []


def measure_open(r):
    out = {"before": open_names(r)}
    safe(r, "sys.open", view="Settings")
    out["afterFirst"] = open_names(r)
    safe(r, "sys.open", view="Settings")
    out["afterSecond"] = open_names(r)
    out["window"] = safe(r, "ui.find", id="sys.dlg." + DIALOG)
    return out


def select_category(r, index):
    """페이지를 바꾸고 레이아웃을 기다린다(행은 레이아웃 뒤에 트리에 나타난다)."""
    result = safe(r, "settings.window", category=CATEGORY_IDS[index])
    time.sleep(PAGE_WAIT_S)
    return result


def measure_inventory(r):
    listing = safe(r, "settings.list")
    entries = listing.get("entries", []) if isinstance(listing, dict) else []
    seen = {}
    for index, category in enumerate(CATEGORY_IDS):
        select_category(r, index)
        state = safe(r, "settings.window")
        for row in state.get("rows", []) if isinstance(state, dict) else []:
            seen[row["id"]] = {"category": category, "visible": row["visible"], "enabled": row["enabled"]}
    select_category(r, 0)
    expected = {e["id"]: e["category"] for e in entries}
    return {
        "listing": listing,
        "rows": seen,
        "missing": sorted(k for k in expected if not (seen.get(k) or {}).get("visible")),
        "wrongCategory": sorted(k for k, v in expected.items() if k in seen and seen[k]["category"] != v),
        "notInInventory": sorted(k for k in seen if k not in expected),
        "unmappedSettings": listing.get("unmapped") if isinstance(listing, dict) else None,
    }


def read_saved(path):
    if not path:
        return None
    time.sleep(SAVE_WAIT_S)
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError) as ex:
        return {"error": str(ex)}


def measure_apply(r, settings_file):
    select_category(r, 0)
    out = {"before": safe(r, "settings.get")}
    safe(r, "ui.select", id="shell.cmbTheme", index=2)  # 다크
    out["afterDark"] = safe(r, "settings.get")
    saved = read_saved(settings_file)
    out["savedThemeDark"] = saved.get("Theme") if isinstance(saved, dict) else None
    fonts = safe(r, "settings.fonts")
    if isinstance(fonts, list) and len(fonts) > 1:
        safe(r, "ui.select", id="shell.cmbFont", index=1)
        out["afterFont"] = safe(r, "settings.get")
        saved = read_saved(settings_file)
        out["savedFont"] = saved.get("FontName") if isinstance(saved, dict) else None
    select_category(r, 2)
    safe(r, "ui.select", id="shell.cmbLanguage", index=1)
    out["afterLanguage"] = safe(r, "settings.get")
    out["categoryTextEn"] = safe(r, "ui.find", id="settings.categories")
    saved = read_saved(settings_file)
    out["savedLanguage"] = saved.get("Language") if isinstance(saved, dict) else None
    safe(r, "ui.select", id="shell.cmbLanguage", index=0)
    return out


def measure_reset(r):
    out = {}
    select_category(r, 0)
    safe(r, "ui.select", id="shell.cmbTheme", index=2)
    select_category(r, 1)
    shadow = safe(r, "ui.toggle", id="shell.chkWindowShadow", on=False)
    out["shadowOff"] = shadow.get("checked") if isinstance(shadow, dict) else None
    # 범주 기본값: 창 범주만 되돌리면 모양(다크)은 남아야 한다
    safe(r, "ui.click", id="settings.btnResetCategory")
    out["shadowAfterCategory"] = (safe(r, "ui.find", id="shell.chkWindowShadow") or {}).get("checked")
    out["themeAfterCategory"] = safe(r, "settings.get")
    # 전체 기본값
    safe(r, "ui.click", id="shell.btnSettingsReset")
    out["themeAfterAll"] = safe(r, "settings.get")
    # 레이아웃 범주는 되돌릴 값이 없어 범주 버튼이 비활성이다
    select_category(r, 3)
    out["layoutResetButton"] = safe(r, "ui.find", id="settings.btnResetCategory")
    select_category(r, 0)
    return out


def measure_search(r):
    out = {"all": safe(r, "settings.window")}
    safe(r, "ui.setText", id="settings.search", text="zzzz-없는-항목")  # 실제 검색창 경로. i18n-ok 시험 입력
    time.sleep(PAGE_WAIT_S)
    out["none"] = safe(r, "settings.window")
    out["noneEmptyText"] = is_visible(safe(r, "ui.find", id="settings.empty"))
    safe(r, "ui.setText", id="settings.search", text="GPU")
    time.sleep(PAGE_WAIT_S)
    out["gpu"] = safe(r, "settings.window")
    safe(r, "ui.setText", id="settings.search", text="")
    time.sleep(PAGE_WAIT_S)
    out["restored"] = safe(r, "settings.window")
    select_category(r, 0)
    return out


def measure_gpu(r):
    select_category(r, 4)
    out = {"before": is_visible(safe(r, "ui.find", id="shell.txtGpuRestart"))}
    safe(r, "ui.toggle", id="shell.chkGpu")
    out["pending"] = is_visible(safe(r, "ui.find", id="shell.txtGpuRestart"))
    out["reason"] = safe(r, "ui.find", id="shell.txtGpuRestart")
    safe(r, "ui.toggle", id="shell.chkGpu")
    safe(r, "sys.close", name="restartRequired")
    out["after"] = is_visible(safe(r, "ui.find", id="shell.txtGpuRestart"))
    select_category(r, 0)
    return out


def measure_close(r):
    # Esc 위험 고정: 'Esc 창닫기' 를 켜고 문서를 연 채 Esc → 설정 창만 닫히고 문서 수는 그대로여야 한다
    safe(r, "docs.open", kind="grid", allowDuplicate=True)
    select_category(r, 1)
    safe(r, "ui.toggle", id="settings.chkEscClose", on=True)
    out = {"escCloseOn": (safe(r, "ui.find", id="settings.chkEscClose") or {}).get("checked")}
    out["esc"] = safe(r, "settings.esc")
    time.sleep(0.2)
    out["afterEsc"] = open_names(r)
    if DIALOG in out["afterEsc"]:
        safe(r, "ui.click", id="settings.btnClose")
        time.sleep(0.2)
    out["afterClose"] = open_names(r)
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--settings-file")
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
    raw = {"pid": pid, "open": measure_open(r)}
    raw["inventory"] = measure_inventory(r)
    raw["apply"] = measure_apply(r, args.settings_file)
    raw["reset"] = measure_reset(r)
    raw["search"] = measure_search(r)
    raw["gpu"] = measure_gpu(r)
    raw["close"] = measure_close(r)
    raw["elapsed_ms"] = int((time.time() - started) * 1000)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(raw, f, ensure_ascii=False, indent=1)
    print("저장: %s" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# 요건 6 도움말 투어 측정 — 원격 명령만으로 시작·입력 차단·종료·건너뛰기·상태 복원·2창 동시 진행 값을 모아 JSON 에 저장한다(판정은 judge 규칙이 낸다)
"""
사용: python3 -B retest_tour.py --out <logs/<에이전트>/<회차>/tour.json>
앱은 먼저 run_desktop_bg.sh 로 진단 빌드를 임시 --data-dir·--profile-dir 와 --remote-control 로 띄워 둔다.
격리되지 않은 앱(사용자 폴더 사용)에 붙으면 rc=3 으로 거부한다. logs/ui28/ 에는 쓰지 않는다(otest 전용).
키 주입은 하지 않는다. Esc 등 키는 진단 명령 tour.esc·tour.key 의 라우팅 이벤트(OS 입력 아님)로만 넣는다.
order 항목은 [ModuleInitializer] 등록 순서 확인이다: 세션 없이 Ctrl+0 이 배율을 되돌리는지(양성 대조)와 세션 중에는 되돌리지 않는지를 함께 잰다.
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
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I 에서도 같은 폴더 모듈을 찾게
from retest_tour_cases import (  # noqa: E402
    MAX_STEPS, SETTLE_S, doc_ids, documents, end_all, measure_hidden_main, measure_modes, measure_settings,
    safe, sessions, settle, text, visible,
)

ZOOM_MARK = 1.5
BLOCKED_TARGETS = ("shell.btnTheme", "shell.mnuFile")
BLOCKED_KEYS = (
    {"key": "F4", "ctrl": True}, {"key": "S", "ctrl": True}, {"key": "D0", "ctrl": True}, {"key": "Tab", "ctrl": True},
    {"key": "LeftAlt"}, {"key": "F10"}, {"key": "F1"}, {"key": "Space", "alt": True},
)


def walk(r):
    """버튼으로 마지막 레이어까지 간다. 단계 id·카운터·구멍 영역을 기록한다."""
    trail = []
    for _ in range(MAX_STEPS):
        if visible(r, "tour.shortcuts"):
            break
        now = sessions(r)
        props = safe(r, "ui.find", id="tour.overlay", props="SpotX,SpotY,SpotWidth,SpotHeight,IsMotionReduced,Transitions")
        trail.append({
            "stepId": now[0].get("stepId") if now else None,
            "title": text(r, "tour.title"),
            "body": text(r, "tour.body"),
            "counter": text(r, "tour.counter"),
            "overlay": props.get("props") if isinstance(props, dict) else props,
        })
        settle(r, "ui.click", id="tour.btnNext")
    return trail


def measure_start(r):
    """시스템 메뉴·문서 창 ? 로 각각 시작. 새 창 없이 오버레이만 생겨야 한다."""
    out = {"sysBefore": (safe(r, "sys.get") or {}).get("open")}
    settle(r, "sys.open", view="Help")
    out["main"] = {"overlay": visible(r, "tour.overlay"), "card": visible(r, "tour.card"), "state": sessions(r), "sysOpen": (safe(r, "sys.get") or {}).get("open")}
    end_all(r)
    docs = doc_ids(r)
    if docs:
        out["docClick"] = settle(r, "ui.click", id="chrome.btnHelp", doc=docs[0])
        out["doc"] = {"overlay": visible(r, "tour.overlay"), "state": sessions(r), "trail": walk(r)}
        end_all(r)
    return out


def measure_two_windows(r):
    """SDI 창과 메인 창에서 동시에 진행하고 서로 다른 단계에 있는지 읽는다."""
    opened = settle(r, "docs.open", kind="grid", title="tour-sdi", sdi=True, allowDuplicate=True)
    before = doc_ids(r)
    sdi_doc = next((d.get("id") for d in documents(r) if d.get("title") == "tour-sdi"), None)
    out = {"opened": opened, "docs": before, "sdiDoc": sdi_doc}
    out["sdiStart"] = settle(r, "tour.start", doc=sdi_doc) if sdi_doc else {"error": "SDI 문서 없음"}
    out["mainStart"] = settle(r, "tour.start")
    first = sessions(r)
    out["sessionsStart"] = first
    if first:
        settle(r, "tour.next", window=first[0].get("window"))
    out["sessionsAfterOne"] = sessions(r)
    end_all(r)
    if sdi_doc:
        settle(r, "docs.close", doc=sdi_doc)
    return out


def measure_blocked(r):
    """투어 중 본창 접근: 원격 클릭 거부·앱 내부 포인터·라우팅 키가 대상 상태를 바꾸지 않아야 한다."""
    settle(r, "tour.start")
    out = {"probeBefore": safe(r, "tour.snapshot"), "stepBefore": sessions(r), "clicks": {}, "keys": {}}
    for control_id in BLOCKED_TARGETS:
        out["clicks"][control_id] = {
            "click": safe(r, "tour.probe", id=control_id),
            "pointer": safe(r, "ui.pointerClick", id=control_id),
        }
    for key in BLOCKED_KEYS:
        result = safe(r, "tour.key", **key)
        now = sessions(r)
        out["keys"]["+".join(str(v) for v in key.values())] = {"result": result, "stepAfterKey": now[0].get("stepId") if now else None}
    time.sleep(SETTLE_S)
    out["probeAfter"] = safe(r, "tour.snapshot")
    out["stepAfter"] = sessions(r)
    out["sysOpen"] = (safe(r, "sys.get") or {}).get("open")
    end_all(r)
    return out


def measure_order(r):
    """모듈 초기화 등록이 SettingsInput 터널 처리기보다 먼저인지: 배율을 표식값으로 두고 Ctrl+0 효과를 본다."""
    docs = doc_ids(r)
    if not docs:
        return {"error": "문서 없음"}
    doc = docs[0]
    settle(r, "docs.activate", doc=doc)

    def zoom():
        return next((d.get("zoom") for d in documents(r) if d.get("id") == doc), None)

    out = {"wheelMode": safe(r, "settings.set", wheelMode="WindowZoom")}  # 기본 FontSize 모드의 Ctrl+0 은 배율을 건드리지 않아 대조가 되지 않는다
    safe(r, "docs.zoom", doc=doc, zoom=ZOOM_MARK)
    out["zoomMarked"] = zoom()
    settle(r, "tour.start")
    out["keyInSession"] = safe(r, "tour.key", key="D0", ctrl=True)
    out["zoomAfterKeyInSession"] = zoom()  # 표식값 유지 = 투어 처리기가 먼저 받았다
    end_all(r)
    safe(r, "docs.zoom", doc=doc, zoom=ZOOM_MARK)
    out["keyNoSession"] = safe(r, "tour.key", key="D0", ctrl=True)
    out["zoomAfterKeyNoSession"] = zoom()  # 양성 대조: 같은 경로로 세션 없이는 되돌아가야 한다
    safe(r, "docs.zoom", doc=doc, step="reset")
    safe(r, "settings.set", wheelMode="FontSize")
    return out


def measure_esc_control(r):
    """Esc 창 닫기를 켠 상태: 투어 중 Esc 는 문서를 닫지 않고, 세션 없는 Esc 는 닫는다(양성 대조)."""
    out = {"escCloseOn": safe(r, "window.escClose.set", on=True)}
    settle(r, "docs.open", kind="form", title="tour-esc", allowDuplicate=True)
    out["docsBefore"] = doc_ids(r)
    settle(r, "tour.start")
    out["escInSession"] = settle(r, "tour.esc")
    out["docsAfterTourEsc"] = doc_ids(r)
    out["escNoSession"] = settle(r, "tour.key", key="Escape")
    out["docsAfterPlainEsc"] = doc_ids(r)
    safe(r, "window.escClose.set", on=False)
    return out


def measure_exit(r):
    """Esc·X·마지막 닫기로 언제나 끝나고 문서 수가 변하지 않는다."""
    out = {"docsBefore": doc_ids(r)}
    settle(r, "tour.start")
    out["esc"] = settle(r, "tour.esc")
    out["escOverlayGone"] = not visible(r, "tour.overlay")
    out["docsAfterEsc"] = doc_ids(r)
    settle(r, "tour.start")
    out["x"] = settle(r, "ui.click", id="tour.btnClose")
    out["xOverlayGone"] = not visible(r, "tour.overlay")
    settle(r, "tour.start")
    out["trail"] = walk(r)
    out["layer"] = {
        "visible": visible(r, "tour.shortcuts"),
        "state": sessions(r),
        "counter": text(r, "tour.layer.counter"),
        "keyRow": visible(r, "tour.key.next"),
        "featureRow": visible(r, "tour.feature.autosave"),
        "contact": text(r, "tour.layer.contact"),
        "webNote": visible(r, "tour.keyNote.next"),
        "focus": safe(r, "tour.snapshot"),
    }
    out["lastClose"] = settle(r, "ui.click", id="tour.layer.btnClose")
    out["lastOverlayGone"] = not visible(r, "tour.overlay")
    settle(r, "tour.start")
    out["skip"] = settle(r, "tour.skip")
    out["skipOnLayer"] = visible(r, "tour.shortcuts")
    out["layerFocus"] = safe(r, "tour.snapshot")
    out["layerEnter"] = settle(r, "tour.key", key="Enter")  # 초점이 닫기 버튼에 있으면 Enter 로 끝난다
    out["layerEnterGone"] = not visible(r, "tour.overlay")
    end_all(r)
    out["docsAfter"] = doc_ids(r)
    return out


def measure_restore(r):
    """종료 전후 활성 문서·내용 루트 IsEnabled·포커스 대조."""
    docs = doc_ids(r)
    if docs:
        settle(r, "docs.activate", doc=docs[-1])
    out = {"noFocus": {"before": safe(r, "tour.snapshot")}}
    settle(r, "tour.start")
    out["noFocus"]["during"] = safe(r, "tour.snapshot")
    settle(r, "tour.esc")
    out["noFocus"]["after"] = safe(r, "tour.snapshot")
    settle(r, "tour.key", key="Tab")  # 초점을 본창 컨트롤에 둔 경우도 복원돼야 한다
    out["withFocus"] = {"before": safe(r, "tour.snapshot")}
    settle(r, "tour.start")
    out["withFocus"]["during"] = safe(r, "tour.snapshot")
    settle(r, "tour.esc")
    out["withFocus"]["after"] = safe(r, "tour.snapshot")
    return out


def measure_skip(r):
    """문서 0개면 문서 창 ? 단계는 대상이 없어 건너뛴다. 전체 수가 문서가 있을 때보다 작아야 한다."""
    settle(r, "tour.start")
    out = {"withDocs": sessions(r), "trailWithDocs": walk(r)}
    end_all(r)
    settle(r, "docs.close", all=True)
    settle(r, "tour.start")
    out["withoutDocs"] = sessions(r)
    out["trailWithoutDocs"] = walk(r)
    end_all(r)
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
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
    safe(r, "docs.open", kind="grid", title="tour-a", allowDuplicate=True)
    settle(r, "docs.open", kind="gantt", title="tour-b", allowDuplicate=True)
    raw["start"] = measure_start(r)
    raw["twoWindows"] = measure_two_windows(r)
    raw["blocked"] = measure_blocked(r)
    raw["order"] = measure_order(r)
    raw["escControl"] = measure_esc_control(r)
    raw["exit"] = measure_exit(r)
    raw["restore"] = measure_restore(r)
    raw["hiddenMain"] = measure_hidden_main(r)
    raw["settings"] = measure_settings(r)
    raw["modes"] = measure_modes(r)
    raw["skip"] = measure_skip(r)
    raw["elapsed_ms"] = int((time.time() - started) * 1000)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(raw, f, ensure_ascii=False, indent=1)
    print("저장: %s" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

# Fold5 실기기에서 Android 앱의 ko↔en 실시간 전환(Q17)을 원격 제어로 판정하는 스크립트
"""
사용:
  python3 test/mobile/measure/retest_android_lang.py [--apk <APK>] [--serial R3CW70202FP] [--out 결과.json]
검사 3가지(펼침 1812x2176 → 접힘 904x2316 순서로 같은 검사를 반복한다).
  1) lang.set en 뒤 화면 컨트롤 트리에서 한글이 남았는지, 키 문자열(점으로 이어진 소문자 키·{중괄호})이 노출되는지.
     문서 데이터(그리드 행·간트 막대 등 사용자 데이터)는 예외 목록으로 뺀다.
  2) 영어 문자열이 넘치는지: 컨트롤 bounds 가 보이는 폭 밖으로 나가는지 본다(텍스트 길이 기반 추정도 함께 기록).
  3) 앱을 다시 시작한 뒤에도 언어가 en 으로 유지되는지. 끝나면 ko 로 되돌린다.
명령은 lang.get·lang.set lang=ko|en(별칭 settings.set language)과 i18n.missing·i18n.scan 이다. 형태별 캡처를 logs/otest_m1/lang_en_*.png 로 남긴다.
원복(finally): 언어 ko·화면 크기 reset·forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값. density 는 건드리지 않는다.
종료 코드: 0=전부 PASS, 1=FAIL 있음, 2=준비·실행 오류.
"""
import argparse
import datetime
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402  설치·연결·원복 함수를 재사용한다

HANGUL = re.compile(r"[가-힣ㄱ-ㆎ]")
KEY_LIKE = re.compile(r"^(?:[a-z][a-z0-9_]*\.){1,}[a-z0-9_]+$|\{[A-Za-z0-9_.]+\}")
DATA_IDS = ("grid.dgMain", "gantt.canvas")  # 사용자 데이터 영역 — 언어 검사에서 제외
FORMS = [("unfolded", None), ("folded", "904x2316")]


def walk(node, out, skip=False, parent_vis=True, path=""):
    """컨트롤 트리를 순회해 (id, name, text, bounds) 목록을 만든다. 데이터 영역은 건너뛴다.
    visible 은 IsVisible 과 IsEffectivelyVisible 이 모두 true 일 때만 true 다(effectivelyVisible 필드가 없으면 부모 체인의 visible 을 AND 한다).
    각 항목에는 type·원본 visible·effectivelyVisible·isVisible·부모 경로(path)도 남긴다(숨김 노드 확인용)."""
    if isinstance(node, dict):
        cid = str(node.get("id") or "")
        here = (path + "/" if path else "") + (cid or str(node.get("type") or "?"))
        skip_here = skip or any(cid.startswith(d) for d in DATA_IDS)
        vis = parent_vis and node.get("visible", True) is not False
        eff = node.get("effectivelyVisible", node.get("isEffectivelyVisible"))
        if eff is not None:
            vis = vis and bool(eff)
        if node.get("isVisible") is not None:  # i0156 판부터: 자기 IsVisible 과 IsEffectivelyVisible 을 따로 낸다
            vis = vis and bool(node["isVisible"])
        if not skip_here:
            text = node.get("text") or node.get("name") or ""
            if text or node.get("bounds"):
                out.append({"id": cid, "text": str(text), "bounds": node.get("bounds"), "type": node.get("type"), "visible": vis,
                            "raw_visible": node.get("visible"), "effectivelyVisible": eff, "isVisible": node.get("isVisible"), "path": here[-160:]})
        for v in node.values():
            if isinstance(v, (list, dict)):
                walk(v, out, skip_here, vis, here)
    elif isinstance(node, list):
        for v in node:
            walk(v, out, skip, parent_vis, path)


def set_lang(r, code):
    """lang.set 을 인자 이름 후보로 시도하고 성공한 형태를 돌려준다."""
    for key in ("code", "lang"):  # 실측: 앱은 인자 'code' 를 요구한다(계획서 lang= 와 다름)
        res = r.call("lang.set", **{key: code})
        if res is not None and r.calls[-1]["ok"]:
            return key
    res = r.call("settings.set", language=code)  # 별칭
    if res is not None and r.calls[-1]["ok"]:
        return "settings.set language"
    return None


def current_lang(r):
    for cmd in ("lang.get", "settings.get"):
        res = r.call(cmd)
        if isinstance(res, dict):
            for k in ("lang", "language", "code", "current"):
                if res.get(k):
                    return str(res[k])
        elif isinstance(res, str) and res:
            return res
    return None


def screen_width(r):
    wins = r.call("windows.list") or []
    for w in wins if isinstance(wins, list) else []:
        b = w.get("bounds") or w
        if b.get("w") or b.get("width"):
            return b.get("w") or b.get("width")
    return None


def check_language(r, width):
    """한글 잔존·키 노출·폭 초과를 검사한다."""
    tree = r.call("tree.get", named=False, depth=40) or {}  # 깊이 64 초과 시 앱이 JSON 직렬화 오류를 낸다(실측)
    items = []
    walk(tree, items)
    scan = r.call("i18n.scan") or {}  # 보이는 컨트롤의 한글 문자열 목록(앱 제공) — 트리 실패 시에도 한글 잔존 판정 가능
    for h in scan.get("hits") or []:
        if h.get("visible", True) and h.get("text"):
            items.append({"id": "scan:" + str(h.get("type")), "text": str(h["text"]), "bounds": h.get("bounds"), "type": h.get("type")})
    hangul = [i for i in items if HANGUL.search(i["text"])]
    keys = [i for i in items if KEY_LIKE.search(i["text"].strip())]
    over = []
    for i in items:
        b = i.get("bounds") or {}
        if i.get("visible") is False or not i.get("id"):  # 숨겨진 컨트롤(단일 문서 모드의 shell.cmbProject 등)과 id 없는 가상화 항목은 폭 초과 판정에서 뺀다
            continue
        if width and b.get("w") and (b.get("x", 0) + b["w"]) > width + 1:
            over.append({"id": i["id"], "text": i["text"][:40], "right": round(b.get("x", 0) + b["w"])})
    detail = lambda i: {"id": i["id"], "text": i["text"][:40], "type": i.get("type"), "visible_and": i.get("visible"), "raw_visible": i.get("raw_visible"),
                        "effectivelyVisible": i.get("effectivelyVisible"), "isVisible": i.get("isVisible"), "bounds": i.get("bounds"), "path": i.get("path")}
    # 판정 규칙(lead 승인 2026-10-29): isVisible=False 이고 effectivelyVisible=False 인 노드는 한글 잔존 판정에서 제외한다(근거: i0315 `기본 어둡게 (내장)` 이 콤보 안 w=0·h=0 숨김 입력 상자로 확인됨).
    # 제외한 노드는 hangul_excluded_hidden 에 type·부모 경로·bounds 로 남기고, bounds 가 0 이 아닌 숨김 노드는 제외하지 않고 hidden_nonzero_bounds 로 보고해 판정에 포함한다.
    def is_hidden(i):
        eff = i.get("effectivelyVisible")
        return i.get("isVisible") is False and (eff is False or (eff is None and i.get("raw_visible") is False))
    def zero_bounds(i):
        b = i.get("bounds") or {}
        return not b.get("w") and not b.get("h")
    excluded = [i for i in hangul if is_hidden(i) and zero_bounds(i) and not str(i.get("id", "")).startswith("scan:")]
    nonzero_hidden = [i for i in hangul if is_hidden(i) and not zero_bounds(i)]
    judged = [i for i in hangul if i not in excluded and not str(i.get("id", "")).startswith("scan:")]  # scan: 항목은 앱 i18n.scan(보이는 컨트롤) 결과라 정보로만 둔다(문서 샘플 데이터 포함)
    scan_info = [i for i in hangul if str(i.get("id", "")).startswith("scan:")]
    return {"controls": len(items), "hangul_left": [detail(i) for i in judged[:30]],
            "hangul_excluded_hidden": [{"type": i.get("type"), "path": i.get("path"), "bounds": i.get("bounds"), "text": i["text"][:40]} for i in excluded[:30]],
            "hidden_nonzero_bounds": [detail(i) for i in nonzero_hidden[:30]],
            "scan_hangul_info": [{"type": i.get("type"), "text": i["text"][:40]} for i in scan_info[:60]], "judgment_rule": "isVisible=False & effectivelyVisible=False 노드는 제외(목록 hangul_excluded_hidden). bounds≠0 숨김 노드는 제외하지 않음(hidden_nonzero_bounds). i18n.scan 항목은 정보용(scan_hangul_info).",
            "hangul_left_visible_only": [detail(i) for i in hangul if i.get("visible") is not False and not str(i.get("id", "")).startswith("scan:")][:30],
            "key_exposed": [{"id": i["id"], "text": i["text"][:40]} for i in keys[:30]],
            "overflow": over[:30], "width": width}


def theme_dropdown(r, serial, form):
    """테마 콤보 드롭다운을 열어 lang_en_dropdown_{형태}.png 를 남기고 닫는다. 열기에 성공한 AutomationId 를 돌려준다(없으면 None)."""
    r.call("sys.open", view="Settings")  # 요건 7: 색 테마 콤보는 설정 창 화면 페이지로 옮겼다
    time.sleep(1.5)
    for cid in ("settings.cmbColorTheme",):  # 설정 창의 색 테마 콤보. shell.cmbTheme 은 시스템·라이트·다크 설정 콤보라 테마 이름 판정 대상이 아니다
        res = r.call("ui.click", id=cid)
        if res is not None and r.calls[-1]["ok"]:
            time.sleep(1.5)
            path = capture(serial, "lang_en_dropdown_" + form)
            ra.adb(serial, "shell", "input", "keyevent", "111")  # ESC 로 닫기
            time.sleep(1)
            r.call("settings.esc")
            return {"id": cid, "capture": path}
    r.call("settings.esc")
    return {"id": None, "capture": None, "note": "settings.cmbColorTheme 클릭 실패 — 컨트롤이 없거나 보이지 않음"}


def capture(serial, name):
    """말줄임으로 잘린 텍스트는 bounds 로 못 잡으므로 형태별 캡처 1장을 증거로 남긴다(shell screencap + pull)."""
    out = os.path.join(os.path.dirname(ra.DEFAULT_OUT), name + ".png")
    ra.adb(serial, "shell", "screencap", "-p", "/sdcard/pd_lang.png")
    rc, _ = ra.adb(serial, "pull", "/sdcard/pd_lang.png", ra.win_path(out))
    ra.adb(serial, "shell", "rm", "-f", "/sdcard/pd_lang.png")
    return out if rc == 0 else None


def wm_size(serial, size):
    ra.adb(serial, "shell", "wm", "size", size or "reset")
    time.sleep(2)


def restart(serial, r_info_path):
    """앱을 완전히 종료 후 다시 시작하고 새 토큰으로 연결 정보를 갱신한다."""
    ra.adb(serial, "shell", "am", "force-stop", ra.PKG)
    ra.adb(serial, "shell", "run-as", ra.PKG, "rm", "-f", "files/PdPoc/remote-control.json")  # 이전 프로세스의 토큰 파일을 지워 새 토큰만 읽는다
    ra.adb(serial, "shell", "monkey", "-p", ra.PKG, "-c", "android.intent.category.LAUNCHER", "1")
    for _ in range(40):
        rc, out = ra.adb(serial, "exec-out", "run-as", ra.PKG, "cat", "files/PdPoc/remote-control.json")
        if rc == 0 and out.strip().startswith("{"):
            info = json.loads(out)
            info["port"] = ra.HOST_PORT
            with open(r_info_path, "w", encoding="utf-8") as f:
                json.dump(info, f)
            return True
        time.sleep(2)
    return False


def run(serial, apk, state):
    from retest_remote import Remote  # PD_REMOTE_INFO 설정 뒤에 가져온다
    info_path = ra.prepare(serial, apk, state)
    if not info_path:
        return {"error": "prepare 실패"}, 2
    r = Remote()
    for k in ("docs.open",):
        for kind in ("grid", "gantt", "form"):
            r.call(k, kind=kind)
    state["lang_before"] = current_lang(r) or "ko"
    report = {"lang_before": state["lang_before"], "forms": {}, "persist": {}}
    key = set_lang(r, "en")
    report["lang_set_arg"] = key
    fails = []
    if key is None:
        return dict(report, error="lang.set 실패(명령 미구현 가능)"), 1
    time.sleep(2)
    for name, size in FORMS:
        wm_size(serial, size)
        time.sleep(3)
        res = check_language(r, screen_width(r))
        res["i18n_missing"] = r.call("i18n.missing")  # 누락 키 목록(odev-6r 제공)
        res["i18n_scan"] = r.call("i18n.scan")  # 하드코딩 문자열 검사
        res["capture"] = capture(serial, "lang_en_" + name)
        res["theme_dropdown"] = theme_dropdown(r, serial, name)  # 영어 전환 뒤 테마 콤보 드롭다운을 실제로 열어 캡처(한국어 때 만든 항목 컨테이너 재사용 확인)
        report["forms"][name] = res
        if res["hangul_left"]:
            fails.append("%s: 한글 잔존 %d건" % (name, len(res["hangul_left"])))
        if res["key_exposed"]:
            fails.append("%s: 키 문자열 노출 %d건" % (name, len(res["key_exposed"])))
        miss = res.get("i18n_missing") or {}
        if (miss.get("count") if isinstance(miss, dict) else miss):  # count 가 0 이면 누락 없음(dict 자체는 항상 참이라 오탐이었다)
            fails.append("%s: i18n.missing 누락 키 있음" % name)
        if res["overflow"]:
            fails.append("%s: 폭 초과 %d건" % (name, len(res["overflow"])))
    wm_size(serial, None)
    if not restart(serial, info_path):
        fails.append("재시작 후 원격 제어 연결 실패")
    else:
        r2 = Remote()
        after = current_lang(r2)
        report["persist"] = {"after_restart": after}
        if (after or "").lower()[:2] != "en":
            fails.append("재시작 후 언어 유지 실패: %s" % after)
        set_lang(r2, state["lang_before"][:2])
    report["fails"] = fails
    report["verdict"] = "PASS" if not fails else "FAIL"
    return report, (0 if not fails else 1)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", default=ra.DEFAULT_APK)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--out", default=os.path.join(os.path.dirname(ra.DEFAULT_OUT), "android_lang.json"))
    a = p.parse_args()
    state, code, report = {}, 2, {"started": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk}
    try:
        res, code = run(a.serial, a.apk, state)
        report.update(res)
    except Exception as e:  # noqa: BLE001 — 오류도 보고서에 남기고 원복은 finally 가 보장
        report["error"] = repr(e)
    finally:
        try:
            ra.adb(a.serial, "shell", "wm", "size", "reset")
        except Exception as e:  # noqa: BLE001
            print("wm size 원복 실패 %s" % e)
        ra.cleanup(a.serial, state)
        os.makedirs(os.path.dirname(a.out), exist_ok=True)
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        print("결과: " + a.out)
    return code


if __name__ == "__main__":
    sys.exit(main())

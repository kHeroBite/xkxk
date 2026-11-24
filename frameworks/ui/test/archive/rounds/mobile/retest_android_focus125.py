# Fold5 실기기에서 #125(집중 해제 뒤 분할선 끌기 원격 대리)를 집중 적용 전·중·해제 뒤·panel.resize 뒤 4시점으로 나눠 joinProbe·panel.bounds·캡처·[split] 로그를 남기는 스크립트
"""
사용: python3 tools/retest_android_focus125.py --apk <진단 APK> [--serial R3CW70202FP] [--out 결과.json]
시점(각 시점마다 panel.joinProbe 전문(split=·splitLog=·docDocks= 포함)·panel.bounds·panel.list·문서별 mdiBounds·화면 캡처 1장을 남긴다):
  T1 집중 적용 전(tpl.mdi-cascade 상태), T2 집중 적용 중(tpl.focus), T3 해제 뒤(tpl.mdi-cascade 로 복귀), T4 panel.resize id=LeftPane px=<현재+40> 뒤(안정될 때까지 100ms 간격 재읽기)
logcat 의 '[split]' 줄 전부(collapse|restore …)를 결과에 첨부한다. 판정 수치는 LeftPane +40±2, Documents 폭 -40±10(Documents 측정 대상 불확실 주석 유지, docDocks 로 대조).
원복(finally): 템플릿을 mdi-cascade 로 되돌림·forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값. 종료 코드 0=완료, 2=준비 오류.
"""
import argparse
import datetime
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_lang as rl  # noqa: E402  capture() 재사용


def field(probe, name):
    """joinProbe 응답 전문 문자열에서 `name=값` 조각(다음 `|` 전까지)을 뽑는다."""
    m = re.search(re.escape(name) + r"=([^|]*)", probe or "")
    return m.group(1) if m else None


def run(serial, apk, state):
    from retest_remote import Remote
    info = ra.prepare(serial, apk, state)
    if not info:
        return {"error": "prepare 실패"}, 2
    r = Remote()
    ra.adb(serial, "logcat", "-c")
    r.call("docs.close", all=True)
    for k in ("grid", "form"):
        r.call("docs.open", kind=k)
    time.sleep(3)
    rep = {"points": {}}

    def stable_bounds():
        """panel.bounds 를 100ms 간격으로 읽어 연속 두 번 같으면 안정 값으로 돌려준다(최대 3초)."""
        prev, reads = None, []
        t0 = time.time()
        while time.time() - t0 < 3:
            b = r.call("panel.bounds")
            reads.append({"t_ms": round((time.time() - t0) * 1000), "value": b})
            if b is not None and prev == b:
                return b, True, reads
            prev = b
            time.sleep(0.1)
        return prev, False, reads

    def point(tag):
        b, stable, reads = stable_bounds()
        jp = r.call("panel.joinProbe", x=0, y=0)
        probe = json.dumps(jp, ensure_ascii=False)
        rep["points"][tag] = {"panel_bounds": b, "stable": stable, "reads": reads, "joinProbe_full": jp, "split": field(probe, "split"), "splitLog": field(probe, "splitLog"),
                              "docDocks": field(probe, "docDocks"), "panel_list": r.call("panel.list"),
                              "docs_mdiBounds": {d.get("id"): (r.call("chrome.state", doc=d.get("id")) or {}).get("mdiBounds") for d in (r.call("docs.list") or [])},
                              "capture": rl.capture(serial, "focus125_" + tag)}

    r.call("layout.template.apply", id="tpl.mdi-cascade")
    time.sleep(2)
    point("T1_before")
    r.call("layout.template.apply", id="tpl.focus")
    time.sleep(2)
    point("T2_focus_applied")
    r.call("layout.template.apply", id="tpl.mdi-cascade")
    time.sleep(2)
    point("T3_after_release")
    left_w = ((rep["points"]["T3_after_release"]["panel_bounds"] or {}).get("LeftPane") or {}).get("w")
    rep["resize_resp"] = r.call("panel.resize", id="LeftPane", px=(left_w + 40) if left_w else 154)
    time.sleep(1.5)
    point("T4_after_resize")
    p3, p4 = (rep["points"][k]["panel_bounds"] or {} for k in ("T3_after_release", "T4_after_resize"))
    g = lambda p, k: (p.get(k) or {})
    rep["judgement"] = {"LeftPane_w_delta": (g(p4, "LeftPane").get("w") or 0) - (g(p3, "LeftPane").get("w") or 0), "Documents_w_delta": (g(p4, "Documents").get("w") or 0) - (g(p3, "Documents").get("w") or 0),
                        "Documents_y_before": g(p3, "Documents").get("y"), "Documents_y_after": g(p4, "Documents").get("y"),
                        "note": "Documents 측정 대상 불확실 — docDocks 값과 캡처로 대조"}
    rc, log = ra.adb(serial, "logcat", "-d", "-v", "brief")
    rep["split_logcat"] = [ln[-200:] for ln in log.splitlines() if "[split]" in ln]  # 태그가 아니라 문자열로 거른다(콘솔 출력의 logcat 태그 미확인)
    rep["notify_logcat"] = [ln[-200:] for ln in log.splitlines() if "[notify]" in ln][:20]
    full = os.path.join(os.path.dirname(ra.DEFAULT_OUT), "focus125_logcat_full.txt")  # 0줄이면 태그 문제일 수 있어 전체 logcat 을 저장해 둔다
    open(full, "w", encoding="utf-8").write(log)
    rep["logcat_full_file"] = full
    return rep, 0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--out", default=os.path.join(os.path.dirname(ra.DEFAULT_OUT), "focus125.json"))
    a = p.parse_args()
    state, code = {}, 2
    report = {"started": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk}
    try:
        res, code = run(a.serial, a.apk, state)
        report.update(res)
    except Exception as e:  # noqa: BLE001 — 오류도 보고서에 남기고 원복은 finally 가 보장
        report["error"] = repr(e)
    finally:
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")
        os.makedirs(os.path.dirname(a.out), exist_ok=True)
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        print("결과: " + a.out)
    return code


if __name__ == "__main__":
    sys.exit(main())

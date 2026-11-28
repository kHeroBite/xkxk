#!/usr/bin/env python3
# RETEST 원격 시나리오 — 실행 중 PoC 앱을 원격 명령만으로 테마·글자 크기·창 배율·제목줄 9버튼·창 20개 배치까지 조작하고 조작별 p50/p95 표를 JSON 으로 저장
"""
사용:
  python3 test/common/measure/retest_remote.py --data-dir <임시 데이터 폴더 Windows 경로> [--out 결과.json] [--skip themes,fonts,chrome,windows] [--docs 20]
앱은 --remote-control 로 먼저 기동해 둔다(연결 정보는 pdctl.py 와 같은 파일). 종료 코드: 0=명령·검사 전부 통과, 1=실패 있음, 2=연결 실패·실행 거부.
캡처는 --data-dir(또는 PD_DATA_DIR) 아래 shots 폴더에만 쓴다. 값이 없거나 사용자 폴더면 windows 단계를 돌릴 때 실행을 거부한다(D-SHOT1).
각 명령의 ok·elapsed_ms·ui_frame_ms 와 검사(check) 결과, 마지막 perf.ops(remote.*) 표를 함께 저장한다.
"""
import argparse
import datetime
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
_USER_DATA_TAIL = "\\".join([""] + [s.lower() for s in _paths.get("data.segments")])  # 사용자 데이터 폴더 꼬리(host_identity 의 data.segments 를 \ 로 이음)
import pdctl  # noqa: E402  같은 폴더의 클라이언트 재사용(연결 정보·curl.exe 호출)


class Remote:
    """명령 1개 = 요청 1개. 응답과 소요를 calls 에 남기고 실패해도 다음 단계로 진행한다."""

    def __init__(self):
        self.info = pdctl.load_info()
        self.calls = []
        self.checks = []

    def call(self, cmd, _timeout=60, **args):
        """_timeout = HTTP 대기 초(명령 인자 timeout 과 구분)."""
        body = json.dumps({"cmd": cmd, "args": {k: v for k, v in args.items() if v is not None}}, ensure_ascii=False)
        # status=NotReady(앱 화면 준비 전 — Android 재기동 직후 등)는 결과 null 로 읽지 않고 retryAfterMs 만큼 기다렸다 다시 묻는다(최대 10초, lead 02:5x)
        waited = 0.0
        while True:
            text = pdctl.curl(self.info, "api/cmd", body, timeout=_timeout)
            try:
                resp = json.loads(text)
            except ValueError:
                resp = {"ok": False, "error": "JSON 아님: " + text[:200]}
            if resp.get("status") != "NotReady" or waited >= 10:
                break
            delay = (resp.get("retryAfterMs") or 500) / 1000.0
            time.sleep(delay)
            waited += delay
        if resp.get("status") == "NotReady":
            resp["error"] = "NotReady(%s) %.1fs 기다려도 준비 안 됨: %s" % (resp.get("reason"), waited, resp.get("error"))
        self.calls.append({"cmd": cmd, "args": args, "ok": bool(resp.get("ok")), "ms": resp.get("ms"),
                           "elapsed_ms": resp.get("elapsed_ms"), "ui_frame_ms": resp.get("ui_frame_ms"),
                           "error": resp.get("error"), "status": resp.get("status"), "notReadyWaitS": round(waited, 1) if waited else None})
        if not resp.get("ok"):
            print("  ✗ %s %s → %s" % (cmd, args, (resp.get("error") or "")[:160]))
        return resp.get("result") if resp.get("ok") else None

    def check(self, name, passed, detail=""):
        self.checks.append({"name": name, "pass": bool(passed), "detail": str(detail)})
        print("  %s %s %s" % ("✓" if passed else "✗", name, detail))


def active_doc(r):
    st = r.call("status") or {}
    return (st.get("active") or {}).get("id")


def step_themes(r):
    print("[1] 테마 전환")
    desc = r.call("themes.get") or {}
    original = desc.get("themeId")
    ids = (desc.get("builtinThemes") or [])[:6]
    r.check("내장 테마 6종 이상", len(ids) >= 6, ids)
    for tid in ids:
        r.call("theme.select", id=tid)
        s = r.call("settings.get") or {}
        r.check("테마 적용 " + tid, s.get("activeThemeId") == tid, s.get("activeThemeId"))
    if original:
        r.call("theme.select", id=original)


def font_of(r):
    return (r.call("settings.get") or {}).get("baseFontSize")


def step_fonts(r):
    print("[2] 글자 크기·Ctrl+휠 배율(문서 창 단위)")
    doc = (r.call("docs.open", kind="grid", title="RT-글자") or [{}])[0].get("id")
    orig_mode = (r.call("settings.get") or {}).get("wheelMode", "FontSize")
    r.call("settings.set", wheelMode="FontSize")
    r.call("input.ctrlWheel", delta=0, doc=doc)
    base = font_of(r)
    for _ in range(3):
        r.call("input.ctrlWheel", delta=1, doc=doc)
    up = font_of(r)
    r.check("Ctrl+휠 위 3회 → 글자 +3", base is not None and up == base + 3, "%s→%s" % (base, up))
    for _ in range(3):
        r.call("input.ctrlWheel", delta=-1, doc=doc)
    r.check("Ctrl+휠 아래 3회 → 원래 크기", font_of(r) == base, font_of(r))
    res = r.call("input.ctrlWheel", delta=1, target="main") or {}
    r.check("메인 창 위 Ctrl+휠 무시(글자)", res.get("handled") is False, res.get("handled"))
    r.call("settings.set", baseFontSize=18)
    r.check("설정 값 입력 18", font_of(r) == 18, font_of(r))
    r.call("input.ctrlWheel", delta=0, doc=doc)

    r.call("settings.set", wheelMode="WindowZoom")
    for _ in range(3):
        r.call("input.ctrlWheel", delta=1, doc=doc)
    z = next((d.get("zoom") for d in (r.call("docs.list") or []) if d.get("id") == doc), None)
    r.check("창 배율 모드 Ctrl+휠 위 3회 → 130%", z is not None and abs(z - 1.3) < 0.01, z)
    res = r.call("input.ctrlWheel", delta=1, target="main") or {}
    r.check("메인 창 위 Ctrl+휠 무시(배율)", res.get("handled") is False, res.get("handled"))
    for zoom in (0.5, 2.0):
        d = r.call("docs.zoom", doc=doc, zoom=zoom) or {}
        r.check("docs.zoom %.1f" % zoom, abs((d.get("zoom") or 0) - zoom) < 0.01, d.get("zoom"))
    r.call("input.ctrlWheel", delta=0, doc=doc)
    z = next((d.get("zoom") for d in (r.call("docs.list") or []) if d.get("id") == doc), None)
    r.check("Ctrl+0 → 100%", z == 1.0, z)
    r.call("settings.set", wheelMode=orig_mode)


def toggle_twice(r, cmd, field, doc):
    """같은 버튼 2번: 값이 바뀌고 원래대로 돌아오는지."""
    before = (r.call("chrome.state", doc=doc) or {}).get(field)
    mid = (r.call(cmd, doc=doc) or {}).get(field)
    after = (r.call(cmd, doc=doc) or {}).get(field)
    r.check("%s 토글(%s)" % (cmd, field), before is not None and mid == (not before) and after == before,
            "%s→%s→%s" % (before, mid, after))


def step_chrome(r):
    print("[3] 제목줄 9버튼(chrome.*)")
    doc = (r.call("docs.open", kind="gantt", title="RT-제목줄") or [{}])[0].get("id")
    toggle_twice(r, "chrome.sdi", "floating", doc)
    toggle_twice(r, "chrome.pin", "pinned", doc)
    toggle_twice(r, "chrome.sync", "sync", doc)
    toggle_twice(r, "chrome.dup", "allowDuplicate", doc)
    # 위치 고정: 잠그면 MDI 크기 손잡이(chrome.gripResize)가 사라지고 위치·크기 불변. 끌기 무시는 포그라운드 실입력 항목(원격 불가)
    before = (r.call("chrome.state", doc=doc) or {}).get("mdiBounds")
    locked = (r.call("chrome.lock", doc=doc) or {}).get("locked")
    gone = r.call("wait.for", _timeout=15, gone="chrome.gripResize", doc=doc, timeout=3000) or {}
    r.check("chrome.lock 잠금 → 손잡이 숨김", locked is True and gone.get("met"), gone.get("detail"))
    after = (r.call("chrome.state", doc=doc) or {}).get("mdiBounds")
    r.check("잠금 중 mdiBounds 불변", before == after, "%s / %s" % (before, after))
    r.call("chrome.lock", doc=doc)
    back = r.call("wait.for", _timeout=15, control="chrome.gripResize", doc=doc, timeout=3000) or {}
    r.check("잠금 해제 → 손잡이 복귀", back.get("met"), back.get("detail"))
    help_text = r.call("chrome.help", doc=doc)
    r.check("chrome.help 문구", bool(help_text), (help_text or "")[:40])
    res = r.call("chrome.refresh", doc=doc) or {}
    r.check("chrome.refresh 새 문서", res.get("doc") and res.get("closed") == doc, res)
    doc = res.get("doc") or active_doc(r)
    res = r.call("chrome.cache", doc=doc) or {}
    r.check("chrome.cache(=새로고침) 새 문서", res.get("doc") and res.get("closed") == doc, res)
    doc = res.get("doc") or active_doc(r)
    for on in (False, True):
        r.check("chrome.shadow on=%s" % on, r.call("chrome.shadow", on=on) is on)
    for mars in (True, False):
        r.check("chrome.frame mars=%s" % mars, r.call("chrome.frame", mars=mars) is mars)
    # 실클릭 경로(Automation Peer): 제목줄 핀 버튼 2번 → 원래 상태
    before = (r.call("chrome.state", doc=doc) or {}).get("pinned")
    r.call("ui.click", id="chrome.btnPin", doc=doc)
    mid = (r.call("chrome.state", doc=doc) or {}).get("pinned")
    r.call("ui.click", id="chrome.btnPin", doc=doc)
    r.check("ui.click chrome.btnPin 토글", mid == (not before), "%s→%s" % (before, mid))


def shot_dir_ok(d):
    """캡처 폴더 검사: 그 실행의 임시 데이터 폴더(Windows 경로)여야 한다. 비었거나 사용자 폴더(%LOCALAPPDATA% 아래 호스트 데이터 폴더)면 None(D-SHOT1 — path 없는 캡처는 진단판이 사용자 폴더에 쓴다)."""
    d = (d or "").strip().rstrip("\\")
    low = d.lower().replace("/", "\\")
    if not d or ":\\" not in d or low.endswith("appdata\\local" + _USER_DATA_TAIL) or "\\appdata\\local" + _USER_DATA_TAIL + "\\" in low + "\\":
        return None
    return d


def step_windows(r, count, data_dir=None):
    print("[4] 창 %d개 열기·배치" % count)
    r.call("docs.close", all=True)
    per = {"grid": count - 2 * (count // 3), "gantt": count // 3, "form": count // 3}
    for kind, n in per.items():
        if n > 0:
            r.call("docs.open", kind=kind, count=n)
    w = r.call("wait.for", _timeout=30, docCount=count, timeout=15000) or {}
    r.check("문서 %d개" % count, w.get("met"), w.get("detail"))
    for mode in ("cascade", "tile-h", "tile-v", "tabbed", "cascade"):
        r.call("docs.arrange", mode=mode)
    for _ in range(5):
        r.call("docs.activate", dir="next")
    r.call("docs.state", all="minimize")
    r.call("docs.state", all="restore")
    # 캡처 path 는 그 실행의 임시 데이터 폴더\shots\ 로 반드시 명시한다(team-lead 지시, D-SHOT1 — path 없이 부르면 진단판이 사용자 폴더에 쓴다).
    # 폴더가 없거나 사용자 폴더면 screenshot 을 부르지 않고 검사 실패로 남긴다(앱이 shots 폴더를 직접 만든다 — RemoteWindowsCommands.cs:229)
    d = shot_dir_ok(data_dir)
    if d is None:
        r.check("스크린샷", False, "캡처 거부: 임시 데이터 폴더(--data-dir, Windows 경로) 없음 또는 사용자 폴더 — path 없는 screenshot 금지")
        return
    shot = r.call("screenshot", window=0, path=d + "\\shots\\remote_%s.png" % time.strftime("%H%M%S")) or {}
    r.check("스크린샷", (shot.get("bytes") or 0) > 0, shot.get("path"))


def ops_table(r):
    ops = r.call("perf.ops", op="remote.") or []
    print("\n%-28s %5s %9s %9s %9s %9s" % ("조작", "횟수", "sync p50", "sync p95", "frame p50", "frame p95"))
    for o in ops:
        print("%-28s %5s %9s %9s %9s %9s" % (o.get("name"), o.get("count"), o.get("sync_p50_ms"),
                                              o.get("sync_p95_ms"), o.get("frame_p50_ms"), o.get("frame_p95_ms")))
    return ops


def main(argv):
    p = argparse.ArgumentParser(description="RETEST 원격 시나리오")
    p.add_argument("--out", default="retest_remote_%s.json" % datetime.datetime.now().strftime("%Y%m%d_%H%M%S"))
    p.add_argument("--skip", default="", help="themes,fonts,chrome,windows 중 건너뛸 단계")
    p.add_argument("--docs", type=int, default=20)
    p.add_argument("--data-dir", default=os.environ.get("PD_DATA_DIR"),
                   help="이번 실행의 임시 데이터 폴더(Windows 경로, run_desktop_bg.sh 출력 '임시 데이터 폴더:'). 캡처는 <폴더>\\shots\\ 에 쓴다. windows 단계를 돌리면 필수")
    a = p.parse_args(argv)
    skip = set(s.strip() for s in a.skip.split(",") if s.strip())
    if "windows" not in skip and shot_dir_ok(a.data_dir) is None:  # D-SHOT1: path 없는 캡처를 막으려고 실행 자체를 거부
        print("실행 거부: --data-dir(또는 PD_DATA_DIR)에 이번 실행의 임시 데이터 폴더 Windows 경로를 주라(사용자 폴더 불가). "
              "캡처 없이 돌리려면 --skip windows")
        return 2

    r = Remote()
    status = r.call("status")
    if status is None:
        print("앱 상태 조회 실패 — 기동·토큰 확인")
        return 2
    print("앱 pid=%s platform=%s 문서=%s" % (status.get("pid"), status.get("platform"), status.get("docCount")))
    r.call("perf.reset")
    r.call("docs.close", all=True)
    steps = [("themes", lambda: step_themes(r)), ("fonts", lambda: step_fonts(r)),
             ("chrome", lambda: step_chrome(r)), ("windows", lambda: step_windows(r, a.docs, a.data_dir))]
    for name, run in steps:
        if name in skip:
            continue
        try:
            run()
        except Exception as ex:  # 한 단계 오류가 나머지 단계를 막지 않게
            r.check("단계 %s 예외" % name, False, repr(ex))
    ops = ops_table(r)

    failed_calls = [c for c in r.calls if not c["ok"]]
    failed_checks = [c for c in r.checks if not c["pass"]]
    result = {"started": status, "calls": r.calls, "checks": r.checks, "operations": ops,
              "summary": {"calls": len(r.calls), "failed_calls": len(failed_calls),
                          "checks": len(r.checks), "failed_checks": len(failed_checks)}}
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print("\n명령 %d(실패 %d) · 검사 %d(실패 %d) → %s" % (len(r.calls), len(failed_calls), len(r.checks),
                                                   len(failed_checks), os.path.abspath(a.out)))
    return 0 if not failed_calls and not failed_checks else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

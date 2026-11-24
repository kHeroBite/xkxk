# Fold5 Chrome 웹에서 CDP 터치 끌기(그리드 80%→20%, touchMove 12단계)의 단계 사이 대기(0·16·33ms)별 스크롤 비율을 재는 스크립트(D-SC1, otest-m-2)
"""
사용: python3 -E -u test/web/measure/retest_web_scroll_ratio.py --url-part 127.0.0.1:5185 --tag i1705rc_U --out <json> [--waits 0,16,33] [--reps 3]
기록: 매회 first·offsetY 전(끌기 전)·끝(touchEnd 직후)·안정(2.5초 뒤), 손가락 이동(CSS px), 비율(offsetY 변화 ÷ 손가락 이동), 실제 끌기 소요(ms).
touchEnd 전 정지 60ms 는 retest_web_i1505_base.py c09 와 같게 둔다.
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from cdp_bridge import Bridge  # noqa: E402

ADB = "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"

# D-SC2·D-TP1 계수기(odev-8m dsc2_dtp1_analysis.md §3 E1 그대로, 이미 있으면 값만 0 으로)
PROBE_JS = r"""(() => {
  if (window.__pd) { const s = window.__pd; for (const k of ['down','move','up','cancel','coalesced','handlerMs','handlerMax']) s[k] = 0; s.target = null; s.touchAction = null; s.cancelTarget = null; return 'reset'; }
  const s = window.__pd = { down: 0, move: 0, up: 0, cancel: 0, coalesced: 0, raf: 0, handlerMs: 0, handlerMax: 0, target: null, touchAction: null, cancelTarget: null };
  let t0 = 0;
  const cap = e => {
    if (e.pointerType !== 'touch') return;
    if (e.type === 'pointerdown') { s.down++; s.target = e.target.tagName + '#' + e.target.id + '.' + e.target.className; s.touchAction = getComputedStyle(e.target).touchAction; }
    if (e.type === 'pointermove') { s.move++; s.coalesced += e.getCoalescedEvents ? e.getCoalescedEvents().length : 1; t0 = performance.now(); }
    if (e.type === 'pointerup') s.up++;
    if (e.type === 'pointercancel') { s.cancel++; s.cancelTarget = e.target.tagName + '#' + e.target.id; }
  };
  const bub = e => { if (e.pointerType === 'touch' && e.type === 'pointermove' && t0) { const d = performance.now() - t0; s.handlerMs += d; s.handlerMax = Math.max(s.handlerMax, d); t0 = 0; } };
  for (const t of ['pointerdown', 'pointermove', 'pointerup', 'pointercancel']) {
    document.addEventListener(t, cap, { capture: true, passive: true });
    window.addEventListener(t, bub, { capture: false, passive: true });
  }
  (function f() { s.raf++; requestAnimationFrame(f); })();
  return 'ok';
})()"""
PD_READ = "JSON.stringify(Object.assign({}, window.__pd, {now: performance.now()}))"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", default="127.0.0.1:5185")
    ap.add_argument("--tag", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--waits", default="0,16,33")
    ap.add_argument("--reps", type=int, default=3)
    ap.add_argument("--adb", action="store_true", help="CDP 대신 adb input swipe(--waits 를 지속 ms 로 사용)")
    ap.add_argument("--oy", type=float, default=321.0, help="adb 화면 y 원점 px(CSS 0 의 화면 y — retest_web_dw 보정값)")
    ap.add_argument("--serial", default="R3CW70202FP")
    ap.add_argument("--probe", action="store_true", help="D-SC2·D-TP1 계수기(__pd)·pinch.state 전후 기록(§5-1)")
    ap.add_argument("--probe-dir", default=None, help="회별 webU_sc2_*.json 을 쓸 폴더")
    ap.add_argument("--probe-prefix", default="", help="회별 파일 접두(예: i1505_)")
    ap.add_argument("--normalize", action="store_true", help="시작 전 문서를 모두 닫고 그리드 1개만 연다(판 사이 조건 맞추기)")
    ap.add_argument("--idle-raf", type=float, default=None, help="E5: 끌기 없이 정지 상태 rAF 간격을 이 초만큼 reps 회 잰다")
    ap.add_argument("--posttap", action="store_true", help="§5 r5: 끌기 뒤 보이는 행 탭 → 선택 확인")
    ap.add_argument("--H", type=float, default=32.25, help="머리글 높이(retest_web_dw 보정값)")
    ap.add_argument("--P", type=float, default=31.1, help="실제 행 간격(retest_web_dw 측정값)")
    ap.add_argument("--shot", action="store_true", help="매회 안정 뒤 캡처")
    ap.add_argument("--pretap", type=float, default=None, help="끌기 전 행 탭 후 대기 초(생략 시 탭 없음)")
    a = ap.parse_args()
    b = Bridge(a.url_part)
    sl = time.sleep
    rep = {"tag": a.tag, "url": b.url, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "runs": []}
    for _ in range(30):
        if b.eval('typeof pdAutomation !== "undefined"') is True:
            break
        sl(1)
    ds = b.call("docs.list") or []
    rep["docs_at_open"] = {"count": len(ds), "kinds": [d.get("kind") for d in ds]}
    if a.normalize:
        # 판 사이 조건 맞추기: 출처(포트)마다 저장소에 남은 레이아웃이 달라 열린 문서 수가 다르다(i1705rc 5185 는 14개 복원) — 모두 닫고 그리드 1개만 연다
        b.call("docs.close", all=True)
        sl(1.5)
        b.call("docs.open", kind="grid")
        sl(4)
        ds = b.call("docs.list") or []
        rep["docs_after_normalize"] = {"count": len(ds), "kinds": [d.get("kind") for d in ds]}
    gs = [d.get("id") for d in ds if d.get("kind") == "grid"]
    if not gs:
        b.call("docs.open", kind="grid")
        sl(2.5)
        gs = [d.get("id") for d in (b.call("docs.list") or []) if d.get("kind") == "grid"]
    G = gs[0]
    b.call("docs.activate", doc=G)
    sl(1)
    gb = (b.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
    GV = lambda: b.call("grid.visible", id="grid.dgMain", doc=G) or {}  # noqa: E731
    g = GV()
    rh = g.get("rowHeight") or 28
    colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
    rep.update(gb=gb, rowHeight=rh, doc=G, metrics=b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio,ua:navigator.userAgent})"),
               mode=b.call("ui.mode"))
    if a.idle_raf:
        # E5: 손대지 않은 정지 상태의 rAF 간격 기준선 — 계수기 설치 뒤 idle_raf 초씩 a.reps 회, 호스트 load 함께 기록
        b.eval(PROBE_JS)
        sl(2.0)
        rep["idle"] = []
        for k in range(a.reps):
            la = os.getloadavg()
            p0 = json.loads(b.eval(PD_READ))
            sl(a.idle_raf)
            p1 = json.loads(b.eval(PD_READ))
            n = p1["raf"] - p0["raf"]
            span = p1["now"] - p0["now"]
            row = {"rep": k + 1, "span_ms": round(span, 1), "raf": n, "raf_interval_ms": round(span / n, 2) if n else None, "host_load": [round(x, 2) for x in la]}
            rep["idle"].append(row)
            print("IDLE", json.dumps(row), flush=True)
        json.dump(rep, open(a.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        b.close()
        return
    y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
    finger = y0 - y1
    pick = lambda v: {k: v.get(k) for k in ("first", "offsetY", "topClip", "selectedIndex")}  # noqa: E731
    for w in [int(x) for x in a.waits.split(",")]:
        for k in range(a.reps):
            b.call("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
            sl(1.0)
            if a.pretap is not None:
                # retest_web_dw c09 재현: 끌기 직전에 행(first+2) 탭(touch_path 와 같은 hold 60ms) 뒤 pretap 초 대기
                ty = gb["y"] + 36.8 + 2 * rh + rh / 2.0
                b.touch("touchStart", [{"x": colx, "y": ty, "id": 1}])
                b.sleep_ms(60)
                b.touch("touchEnd", [])
                sl(a.pretap)
            probe = None
            if a.probe:
                # §5-1 순서: 계수기 설치(또는 0 으로) → pinch.state·grid.visible 저장 → swipe → 2.5초 → __pd·pinch.state·grid.visible
                probe = {"install": b.eval(PROBE_JS), "pinch_before": b.call("pinch.state")}
            before = GV()
            pd0 = json.loads(b.eval(PD_READ)) if a.probe else None
            t0 = time.time()
            if a.adb:
                # 실 OS 터치: adb input swipe(대기 값 w 를 swipe 지속 ms 로 쓴다). 화면 px = CSS px × dpr + (x: 0, y: oy)
                dpr = rep["metrics"]["dpr"]
                sxa, sya, syb = int(round(colx * dpr)), int(round(y0 * dpr + a.oy)), int(round(y1 * dpr + a.oy))
                subprocess.run([ADB, "-s", a.serial, "shell", "input", "swipe", str(sxa), str(sya), str(sxa), str(syb), str(w)], capture_output=True, timeout=60)
                t1 = time.time()
            else:
                b.touch("touchStart", [{"x": colx, "y": y0, "id": 1}])
                for i in range(1, 13):
                    if w:
                        b.sleep_ms(w)
                    b.touch("touchMove", [{"x": colx, "y": y0 + (y1 - y0) * i / 12, "id": 1}])
                t1 = time.time()
                b.sleep_ms(60)
                b.touch("touchEnd", [])
            pd_end = json.loads(b.eval(PD_READ)) if a.probe else None
            end = GV()
            sl(2.5)
            after = GV()
            if a.probe:
                pd1 = json.loads(b.eval(PD_READ))
                probe.update(pinch_after=b.call("pinch.state"), pd_before=pd0, pd_swipe_end=pd_end, pd_after=pd1)
                raf_sw = (pd_end["raf"] - pd0["raf"]) if pd_end and pd0 else None
                span = (pd_end["now"] - pd0["now"]) if pd_end and pd0 else None
                mv = pd1.get("move") or 0
                probe["derived"] = {"down": pd1.get("down"), "move": mv, "up": pd1.get("up"), "cancel": pd1.get("cancel"), "coalesced": pd1.get("coalesced"),
                                    "raf_during_swipe": raf_sw, "swipe_span_ms": round(span, 1) if span else None,
                                    "raf_interval_ms": round(span / raf_sw, 1) if raf_sw else None,
                                    "move_per_raf": round(mv / raf_sw, 2) if raf_sw else None,
                                    "coalesced_per_move": round(pd1["coalesced"] / mv, 2) if mv else None,
                                    "handlerMs_per_move": round(pd1["handlerMs"] / mv, 2) if mv else None, "handlerMax": round(pd1.get("handlerMax") or 0, 1),
                                    "target": pd1.get("target"), "touchAction": pd1.get("touchAction"), "cancelTarget": pd1.get("cancelTarget")}
                print("PROBE", json.dumps(probe["derived"], ensure_ascii=False), flush=True)
            post = None
            if a.posttap:
                # §5 r5: 끌기 직후 보이는 행(완전히 보이는 첫 행+3)을 탭 → 그 행이 선택돼야 한다. 행 위치 = 그리드 y + H + i×P − offsetY + P/2(D-GV1: first 안 씀)
                import math
                want = int(math.ceil(((after.get("offsetY") or 0) - 0.5) / a.P)) + 3
                tyy = gb["y"] + a.H + want * a.P - (after.get("offsetY") or 0) + a.P / 2.0
                b.touch("touchStart", [{"x": colx, "y": tyy, "id": 1}])
                b.sleep_ms(60)
                b.touch("touchEnd", [])
                sl(0.6)
                pv = GV()
                post = {"want": want, "tap_css": [round(colx, 1), round(tyy, 1)], "selectedIndex": pv.get("selectedIndex"), "ok": pv.get("selectedIndex") == want}
                print("POSTTAP", post, flush=True)
            shot = b.shot(os.path.join(os.path.dirname(a.out), "web_%s_scroll_w%d_r%d.png" % (a.tag, w, k + 1))) if a.shot else None
            ob = before.get("offsetY") or 0
            de = (end.get("offsetY") or 0) - ob
            ds_ = (after.get("offsetY") or 0) - ob
            row = {"wait_ms": w, "rep": k + 1, "drag_wall_ms": round((t1 - t0) * 1000), "finger_css": round(finger, 1),
                   "before": pick(before), "end": pick(end), "after": pick(after),
                   "d_offset_end": round(de, 1), "d_offset_settled": round(ds_, 1),
                   "ratio_end": round(de / finger, 3), "ratio_settled": round(ds_ / finger, 3),
                   "ratio_first": round(((after.get("first") or 0) - (before.get("first") or 0)) / (finger / rh), 3),
                   "after_rows": {k2: after.get(k2) for k2 in ("last", "rows", "firstFull", "lastFull")}, "shot": shot,
                   "mode": "adb" if a.adb else "cdp", "probe": probe, "posttap": post, "host_load": [round(x, 2) for x in os.getloadavg()]}
            if a.probe and a.probe_dir:
                nm = "%swebU_sc2_%s_r%d.json" % (a.probe_prefix, ("adb%d" % w) if a.adb else "cdp", k + 1)
                json.dump(dict(row, tag=a.tag, before_full=before, after_full=after), open(os.path.join(a.probe_dir, nm), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
            rep["runs"].append(row)
            print("RUN", w, k + 1, row["drag_wall_ms"], row["d_offset_settled"], row["ratio_settled"], flush=True)
            json.dump(rep, open(a.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    b.call("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
    json.dump(rep, open(a.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    b.close()


if __name__ == "__main__":
    main()

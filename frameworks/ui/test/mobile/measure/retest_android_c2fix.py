# Android 실기 재측정 구동기(측정 계층) — 설정 줄 라벨·컨트롤 배치(D21)·패널 본문 빈 곳 터치 활성(D22)·SDI 2개 합류 탭 줄(U15) 원자료를 모은다. 판정은 test/mobile/judge/c2fix_rules.py
"""
사용(python3 -B, 기기 사용권을 먼저 받는다):
  retest_android_c2fix.py --apk <APK> --md5 <기대 md5> --serial <기기> --out <logs 결과 폴더> [--only d21,d22,u15] [--fold 904x2316]
- 실행마다 설치→원격 연결→측정→원복(wm size reset·앱 제거·forward 해제·stay_on 원값). wm density 는 건드리지 않는다(사용자 값 보존).
- 터치는 실입력(adb input)이다. 좌표는 원격 bounds(DIP)×(화면 px÷viewWidth)로 바꾼다.
- 값만 모은다. PASS/FAIL 은 내지 않는다.
"""
import argparse
import hashlib
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402

A = None
K = [2.25]


def adb(*a, **kw):
    return ra.adb(A.serial, *a, **kw)


def walk(n, out):
    if isinstance(n, dict):
        out.append(n)
        for c in n.get("children") or n.get("tree") or []:
            walk(c, out)
    elif isinstance(n, list):
        for c in n:
            walk(c, out)
    return out


def shot(name):
    rc, png = adb("exec-out", "screencap", "-p", binary=True, timeout=60)
    i = png.find(b"\x89PNG")
    p = os.path.join(A.out, name + ".png")
    if i >= 0:
        with open(p, "wb") as f:
            f.write(png[i:])
        return p
    return None


def tap(x, y):
    return adb("shell", "input", "tap", str(round(x * K[0])), str(round(y * K[0])))[0]


def swipe(x1, y1, x2, y2, ms):
    return adb("shell", "input", "swipe", *[str(round(v * K[0])) for v in (x1, y1, x2, y2)], str(ms))[0]


def fold(on):
    adb("shell", "wm", "size", A.fold if on else "reset")
    time.sleep(4)


def scale(r):
    vw = (r.call("ui.mode") or {}).get("viewWidth") or 805.3
    rc, out = adb("shell", "wm", "size")
    px = [l.split(":")[1].strip() for l in out.splitlines() if "size" in l]
    w = int(px[-1].split("x")[0]) if px else 1812
    K[0] = w / vw
    return {"viewWidth": vw, "pxWidth": w, "k": K[0]}


def docs(r):
    return r.call("docs.list") or []


def base(r, kinds=("grid", "gantt", "form")):
    r.call("docs.close", all=True)
    for k in kinds:
        r.call("docs.open", kind=k)
    time.sleep(1.5)


# 새 설정 창(요건 7)은 범주별 페이지다 — 짝 6개는 Language(언어)·Appearance(테마·폰트·문서 글자·Ctrl+휠)·Window(창 스타일)에 나뉜다
D21_CATEGORIES = ("Language", "Appearance", "Window")


def d21(r, st):
    """설정 창(sys.open view=Settings)을 열고 범주마다 SettingsView 안 TextBlock·컨트롤 사각형을 ko·en 으로 모은다. 각 노드에 category 를 붙인다."""
    out = {}
    for lang in ("ko", "en"):
        r.call("lang.set", code=lang)
        time.sleep(1.2)
        r.call("sys.open", view="Settings")
        time.sleep(1.5)
        nodes, found, shots = [], False, []
        for cat in D21_CATEGORIES:
            r.call("settings.window", category=cat)
            time.sleep(1.0)  # 페이지를 바꾼 뒤 레이아웃이 끝나야 행이 트리에 나타난다
            tree = r.call("tree.get", named=False, depth=60)
            panel = [n for n in walk(tree, []) if n.get("type") == "SettingsView"]
            found = found or bool(panel)
            for n in walk(panel[0] if panel else {}, []):
                if n.get("type") == "TextBlock" or (n.get("id") or "").startswith("shell."):
                    d = {k: n.get(k) for k in ("type", "id", "text", "bounds", "effectivelyVisible")}
                    d["category"] = cat
                    nodes.append(d)
            shots.append(shot("d21_%s_%s_%s" % (st, lang, cat)))
        out[lang] = {"found_panel": found, "nodes": nodes, "shot": shots[0] if shots else None, "shots": shots}
        r.call("settings.esc")  # 요건 7: 설정 창은 Esc 로 닫는다(문서는 닫히지 않아야 한다)
        time.sleep(0.8)
    r.call("lang.set", code="ko")
    return out


def blank_point(tree, pane_id):
    """패널 사각형 안, 머리줄(30DIP) 아래에서 버튼·목록 항목·입력 컨트롤과 겹치지 않는 점(가장 먼 점)."""
    pane = [n for n in walk(tree, []) if n.get("id") == pane_id]
    if not pane or not pane[0].get("bounds"):
        return None, None
    b = pane[0]["bounds"]
    hits = [n["bounds"] for n in walk(pane[0], []) if n.get("bounds") and n.get("effectivelyVisible") is not False
            and n.get("type") in ("Button", "ListBoxItem", "TextBox", "ComboBox", "ToggleButton", "CheckBox", "DataGridRow")]
    best = None
    for yy in range(int(b["y"] + 34), int(b["y"] + b["h"] - 6), 6):
        for xx in range(int(b["x"] + 6), int(b["x"] + b["w"] - 6), 6):
            d = min([max(h["x"] - xx, xx - h["x"] - h["w"], h["y"] - yy, yy - h["y"] - h["h"]) for h in hits] or [99])
            if d > 0 and (best is None or d > best[0]):
                best = (d, xx, yy)
    return b, (best[1:] if best else None)


def kb():
    """소프트 키보드 표시 여부(true|false|unknown) — dumpsys input_method 키 이름 차이를 흡수."""
    rc, out = adb("shell", "dumpsys", "input_method")
    for key in ("mInputShown=", "mIsInputViewShown=", "isInputViewShown="):
        for line in out.splitlines():
            if key in line:
                return "true" if (key + "true") in line else "false"
    return "unknown"


def kbhide():
    """키보드가 떠 있을 때만 ESC(111) → 남으면 BACK(4) 1회. 없으면 아무것도 보내지 않는다(화면 이탈 방지)."""
    log = [kb()]
    if log[-1] == "true":
        adb("shell", "input", "keyevent", "111")
        time.sleep(0.8)
        log.append(kb())
        if log[-1] == "true":
            adb("shell", "input", "keyevent", "4")
            time.sleep(0.8)
            log.append(kb())
    alive = adb("shell", "pidof", ra.PKG)[1].strip()
    return {"kb": log, "app_pid": alive}


def md5f(p):
    return hashlib.md5(open(p, "rb").read()).hexdigest() if p else None


def doc_title_point(r):
    """활성 MDI 문서 제목 글자 자리(문서 기호 오른쪽 70 DIP) — 문서 본문 입력칸을 누르지 않고 문서를 활성하는 점."""
    tree = r.call("tree.get", named=False, depth=60)
    o = [n["bounds"] for n in walk(tree, []) if n.get("type") == "MdiDocumentControl" and n.get("effectivelyVisible") and n.get("bounds")]
    act = [d["id"] for d in docs(r) if d.get("active")] or [d["id"] for d in docs(r)]
    mb = (r.call("chrome.state", doc=act[0]) or {}).get("mdiBounds") if act else None
    return (o[0]["x"] + mb["x"] + 70, o[0]["y"] + mb["y"] + 16) if o and mb else None


def d22(r):
    """패널 본문 빈 곳 실터치 → 머리줄 반전. 순서: 문서 제목 터치(패널 비활성) → 키보드 닫기 → 전 캡처 → 빈 곳 터치 → 후 캡처 → 문서 제목 터치."""
    base(r, ("grid", "gantt"))  # 입력칸이 있는 입력폼은 열지 않는다(키보드가 떠 좌표를 덮는 일 방지 — lead 05:0x 지시)
    r.call("docs.arrange", mode="cascade")
    time.sleep(1)
    tree = r.call("tree.get", named=False, depth=60)
    dp = doc_title_point(r)
    out = {"panels": r.call("panel.list"), "doc_point": dp}
    for pid in ("LeftPane", "BottomPane", "NotificationPane"):
        b, pt = blank_point(tree, pid)
        rec = {"bounds": b, "blank": pt}
        if b and pt and dp:
            tap(*dp)
            time.sleep(1)
            rec["hide_before"] = kbhide()
            rec["before"] = shot("d22_%s_before" % pid)
            rec["kb_before_tap"] = kb()
            if rec["kb_before_tap"] == "true":
                rec["skipped"] = "키보드가 닫히지 않아 빈 곳 터치 생략"
                out[pid] = rec
                continue
            rec["tap_rc"] = tap(*pt)
            time.sleep(1)
            rec["after"] = shot("d22_%s_after_blank" % pid)
            rec["kb_after_tap"] = kb()
            rec["changed"] = md5f(rec["before"]) != md5f(rec["after"])
            rec["docs_after"] = [(d["id"], d.get("active")) for d in docs(r)]
            tap(*dp)
            time.sleep(1)
            rec["after_doc"] = shot("d22_%s_after_doc" % pid)
            rec["kb_after_doc"] = kb()
            rec["app_pid_end"] = adb("shell", "pidof", ra.PKG)[1].strip()
        out[pid] = rec
    # 패널 A 본문 → 패널 B 본문 이동: 활성 머리줄이 하나뿐인지(각 단계 캡처 + 패널 사각형)
    out["switch"] = []
    for a_id, b_id in (("LeftPane", "BottomPane"), ("BottomPane", "NotificationPane")):
        ba, pa = blank_point(tree, a_id)
        bb_, pb = blank_point(tree, b_id)
        rec = {"a": a_id, "b": b_id, "a_bounds": ba, "b_bounds": bb_}
        if ba and bb_ and pa and pb and dp:
            tap(*dp)
            time.sleep(1)
            rec["hide"] = kbhide()
            rec["kb_before"] = kb()
            rec["start"] = shot("d22_sw_%s_%s_0" % (a_id, b_id))
            tap(*pa)
            time.sleep(1)
            rec["after_a"] = shot("d22_sw_%s_%s_a" % (a_id, b_id))
            rec["kb_a"] = kb()
            tap(*pb)
            time.sleep(1)
            rec["after_b"] = shot("d22_sw_%s_%s_b" % (a_id, b_id))
            rec["kb_b"] = kb()
            tap(*dp)
            time.sleep(1)
            rec["app_pid_end"] = adb("shell", "pidof", ra.PKG)[1].strip()
        out["switch"].append(rec)
    # 목록 행 터치 회귀: 패널 안 첫 목록 행(ListBoxItem·DataGridRow)을 눌러 선택 표시·문서 불변을 본다
    out["row_tap"] = []
    for pid in ("BottomPane", "LeftPane", "NotificationPane"):
        pane = [n for n in walk(tree, []) if n.get("id") == pid]
        rows_ = [n for n in walk(pane, []) if n.get("type") in ("ListBoxItem", "DataGridRow") and n.get("bounds")
                 and n.get("effectivelyVisible") is not False]
        rec = {"pane": pid, "rows": len(rows_)}
        if rows_ and dp:
            row = rows_[min(1, len(rows_) - 1)]
            rb = row["bounds"]
            tap(*dp)
            time.sleep(1)
            rec["hide"] = kbhide()
            rec["row"] = {k: row.get(k) for k in ("type", "id", "text", "bounds", "selected", "isSelected")}
            rec["docs_before"] = [d["id"] for d in docs(r)]
            rec["before"] = shot("d22_row_%s_before" % pid)
            rec["kb_before"] = kb()
            rec["tap_rc"] = tap(rb["x"] + rb["w"] / 2, rb["y"] + rb["h"] / 2)
            time.sleep(1)
            rec["after"] = shot("d22_row_%s_after" % pid)
            rec["kb_after"] = kb()
            rec["docs_after"] = [d["id"] for d in docs(r)]
            t2 = r.call("tree.get", named=False, depth=60)
            same = [n for n in walk(t2, []) if n.get("type") == row.get("type") and n.get("bounds") == rb]
            rec["row_after"] = {k: same[0].get(k) for k in ("type", "id", "text", "selected", "isSelected")} if same else None
            rec["app_pid_end"] = adb("shell", "pidof", ra.PKG)[1].strip()
        out["row_tap"].append(rec)
    # 본문 안 컨트롤 터치의 기존 동작: 탐색기 첫 버튼 → 문서 1개 열림
    left = [n for n in walk(tree, []) if n.get("id") == "LeftPane"]
    btn = [n for n in walk(left, []) if n.get("type") == "Button" and n.get("bounds")]
    n0 = len(docs(r))
    if btn:
        bb = btn[0]["bounds"]
        out["control_tap"] = {"button": bb, "rc": tap(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)}
        time.sleep(1.5)
        out["control_tap"].update({"docs_before": n0, "docs_after": len(docs(r))})
    return out


def strips(r):
    tree = r.call("tree.get", named=False, depth=60)
    return [{"type": n.get("type"), "bounds": n.get("bounds"), "vis": n.get("effectivelyVisible"),
             "texts": [x.get("text") for x in walk(n, []) if x.get("text")]}
            for n in walk(tree, []) if n.get("type") in ("DocumentTabStrip", "DocumentTabStripItem")]


def inside(b, wb):
    return wb["x"] <= b["x"] <= wb["x"] + wb["w"] and wb["y"] <= b["y"] <= wb["y"] + wb["h"]


def u15(r):
    """SDI 1개(탭 줄 숨김) → 본창 탭을 SDI 창 가운데로 실터치 끌기(합류) → 탭 줄 표시."""
    base(r, ("grid", "gantt"))
    ids = [d["id"] for d in docs(r)]
    sdi, other = ids[0], ids[1]
    out = {"sdi_doc": sdi, "other": other, "sdi_on": r.call("docs.sdi", doc=sdi, on=True)}
    time.sleep(1.5)
    win = [w for w in (r.call("windows.list") or []) if not w.get("main")]
    out["win1"] = win
    out["one"] = {"strips": strips(r), "shot": shot("u15_one")}
    wb = win[0].get("bounds") if win else None
    out["attempts"] = []
    if wb:
        cx, cy = wb["x"] + wb["w"] / 2, wb["y"] + wb["h"] / 2
        r.call("docs.arrange", mode="tabbed")
        time.sleep(1.5)
        items = [s for s in strips(r) if s["type"] == "DocumentTabStripItem" and s.get("vis") and s.get("bounds")]
        src = [s["bounds"] for s in items if not inside(s["bounds"], wb)]
        if not src:
            out["attempts"].append({"name": "tab_swipe", "skipped": "본창 문서 탭 없음"})
        else:
            sx, sy = src[0]["x"] + src[0]["w"] / 2, src[0]["y"] + src[0]["h"] / 2
            rc = swipe(sx, sy, cx, cy, 1500)
            time.sleep(2)
            out["attempts"].append({"name": "tab_swipe", "from": (sx, sy), "to": (cx, cy), "rc": rc,
                                    "windows": r.call("windows.list"), "strips": strips(r), "shot": shot("u15_after_tab_swipe")})
    out["redock"] = r.call("docs.redock", all=True)
    return out


def u19(r):
    """패널 머리줄 활성 반전 — 라이트·다크 각각: 문서 제목 터치(패널 비활성) → 키보드 닫기 → 전 캡처 → 머리줄 터치 → 후 캡처 → 문서 터치 → 복귀 캡처."""
    out = {}
    for tid in ("pd-light", "pd-dark"):
        rec = {"theme": tid, "cmd": r.call("cmd.run", command="theme:" + tid)}
        time.sleep(2)
        base(r, ("grid", "gantt"))
        r.call("docs.arrange", mode="cascade")
        time.sleep(1)
        lb = [p.get("bounds") for p in (r.call("panel.list") or []) if p.get("id") == "LeftPane"]
        dp = doc_title_point(r)
        rec["left"] = lb[0] if lb else None
        if not lb or not dp:
            out[tid] = rec
            continue
        tap(*dp)
        time.sleep(1)
        rec["hide"] = kbhide()
        rec["before"] = shot("u19_%s_before" % tid)
        rec["kb_before"] = kb()
        rec["tap_header"] = tap(lb[0]["x"] + 20, lb[0]["y"] + 10)
        time.sleep(1)
        rec["after"] = shot("u19_%s_after_header" % tid)
        rec["kb_after"] = kb()
        tap(*dp)
        time.sleep(1)
        rec["after_doc"] = shot("u19_%s_after_doc" % tid)
        rec["app_pid_end"] = adb("shell", "pidof", ra.PKG)[1].strip()
        out[tid] = rec
    r.call("cmd.run", command="theme:pd-light")
    return out


def main(argv):
    global A
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--md5", required=True)
    p.add_argument("--serial", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--only", default="d21,d22,u15,u19")
    p.add_argument("--fold", default="904x2316")
    A = p.parse_args(argv)
    os.makedirs(A.out, exist_ok=True)
    md5 = hashlib.md5(open(A.apk, "rb").read()).hexdigest()
    if md5 != A.md5:
        print("APK md5 불일치 %s" % md5)
        return 3
    only = set(A.only.split(","))
    state, res = {}, {"apk_md5": md5, "serial": A.serial, "started": time.strftime("%Y-%m-%dT%H:%M:%S")}
    try:
        if not ra.prepare(A.serial, A.apk, state):
            res["error"] = "prepare 실패"
            return 2
        from retest_remote import Remote
        r = Remote()
        for _ in range(30):
            if r.call("status") is not None:
                break
            time.sleep(1)
        res["scale_open"] = scale(r)
        for key, fn in (("d21_open", lambda: d21(r, "open")), ("d22", lambda: d22(r)), ("u15", lambda: u15(r)), ("u19", lambda: u19(r))):
            if key.split("_")[0] in only:
                try:
                    res[key] = fn()
                except Exception as e:  # noqa: BLE001 — 한 항목 오류가 나머지를 막지 않게
                    res[key] = {"exception": repr(e)}
        if "d21" in only:
            fold(True)
            res["scale_fold"] = scale(r)
            try:
                res["d21_fold"] = d21(r, "fold")
            except Exception as e:  # noqa: BLE001
                res["d21_fold"] = {"exception": repr(e)}
            fold(False)
        rc, log = adb("logcat", "-d", "-v", "brief", "*:E", timeout=60)
        res["fatal_lines"] = [l for l in log.splitlines() if "FATAL" in l or re.search(r"ANR in " + re.escape(_paths.get("android.package")) + r"(\s|$)", l)]
        res["call_errors"] = [c for c in r.calls if not c.get("ok")][:40]
        return 0
    finally:
        adb("shell", "wm", "size", "reset")
        ra.cleanup(A.serial, state)
        rc, out = adb("shell", "wm size; wm density; settings get global stay_on_while_plugged_in")
        res["device_after"] = out.strip()
        with open(os.path.join(A.out, "c2fix_measure.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

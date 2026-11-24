# i2000rc Fold5 기기 Chrome 웹 시험(GV1-W1·W2, RH1-W, UIM1-W)을 CDP 로 재고 케이스별 원문·기대·판정을 남기는 스크립트(odev-1s i2000rc_test_plan.md e46d7a9f, otest-m-3)
"""
사용: python3 -E -u tools/retest_web_i2000rc.py --url-part 127.0.0.1:5192 --pf F|U [--only gv1,rh1,uim1] [--out-dir logs/otest_m2/i2000rc]
폼팩터(wm size)는 실행 전에 맞춘다(접힘 = wm size 904x2316 대용). 회마다 Page.reload 뒤 문서를 그리드 1개로 맞추고(docs_at_open 기록) 시작한다.
GV1-W1: 행 2 탭(CDP 터치) → c09 CDP 끌기(80%→20%, touchMove 12단계·16ms) → grid.select index=30 scroll=true → grid.visible·캡처
GV1-W2: c09 끌기만 → grid.visible·캡처
RH1-W: tree.get 의 DataGridRow·DataGridCell bounds 높이와 연속 행 y 차이, 30행 스크롤 검산
UIM1-W: ui.mode set=auto → 켜진 칸 다시 탭(펼침=태블릿 칸, 접힘=폰 칸) → ui.mode(저장값 Auto 유지). 끝에 auto 원복
"""
import argparse
import hashlib
import json
import math
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cdp_bridge import Bridge  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
MANIFEST = "ad839c229056"


def md5(p):
    return hashlib.md5(open(p, "rb").read()).hexdigest()


def walk(n, out):
    if isinstance(n, dict):
        out.append(n)
        for ch in n.get("children") or n.get("tree") or []:
            walk(ch, out)
    elif isinstance(n, list):
        for ch in n:
            walk(ch, out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--pf", required=True, choices=["U", "F"])
    ap.add_argument("--only", default="gv1,rh1,uim1")
    ap.add_argument("--out-dir", default=os.path.join(ROOT, "logs", "otest_m2", "i2000rc"))
    a = ap.parse_args()
    only = set(a.only.split(","))
    O = a.out_dir
    os.makedirs(O, exist_ok=True)
    tools_md5 = {"ui20_check.py": md5(os.path.join(HERE, "ui20_check.py")), "ui20_lib.py": md5(os.path.join(HERE, "ui20_lib.py")),
                 "retest_web_i2000rc.py": md5(os.path.abspath(__file__))}
    b = Bridge(a.url_part)
    sl = time.sleep
    pf = "web" + a.pf
    sumf = os.path.join(O, "web_i2000rc_%s_summary.json" % a.pf)
    summ = {"build": "i2000rc", "manifest": MANIFEST, "url": b.url, "pf": pf, "tools_md5": tools_md5, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "cases": []}

    def save_summary():
        with open(sumf, "w", encoding="utf-8") as f:
            json.dump(summ, f, ensure_ascii=False, indent=1)

    def jd(name, obj):
        with open(os.path.join(O, name), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)

    def shot(name):
        try:
            b.shot(os.path.join(O, name + ".png"))
            return name + ".png"
        except Exception:  # noqa: BLE001
            return None

    def case(cid, expect, actual, verdict, files=None, note=None, cond=None):
        c = {"id": cid, "build": "i2000rc", "manifest": MANIFEST, "platform": pf, "at": time.strftime("%Y-%m-%d %H:%M:%S"), "tools_md5": tools_md5,
             "condition": cond, "expect": expect, "actual": actual, "verdict": verdict, "files": [f for f in (files or []) if f], "note": note}
        jd("web_%s_%s.json" % (cid, a.pf), c)
        summ["cases"].append({k: c[k] for k in ("id", "verdict", "note")})
        save_summary()
        print("CASE", cid, a.pf, verdict, json.dumps(actual, ensure_ascii=False)[:240], flush=True)

    def wait_ready():
        for _ in range(40):
            try:
                if b.eval('typeof pdAutomation !== "undefined"') is True and isinstance(b.call("docs.list"), list):
                    return True
            except Exception:  # noqa: BLE001
                pass
            sl(1)
        return False

    def fresh():
        b.cdp("Page.reload", ignoreCache=False)
        sl(3)
        ready = wait_ready()
        sl(2)
        ds = b.call("docs.list") or []
        at_open = {"count": len(ds), "kinds": [d.get("kind") for d in ds]}
        normalized = False
        if not (len(ds) == 1 and ds[0].get("kind") == "grid"):
            b.call("docs.close", all=True)
            sl(1.5)
            b.call("docs.open", kind="grid")
            sl(4)
            ds = b.call("docs.list") or []
            normalized = True
        G = [d.get("id") for d in ds if d.get("kind") == "grid"][0]
        b.call("docs.activate", doc=G)
        sl(1)
        gb = (b.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
        return G, gb, {"ready": ready, "docs_at_open": at_open, "normalized": normalized, "docs_after": len(ds),
                       "metrics": b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio})"), "host_load": [round(x, 2) for x in os.getloadavg()]}

    def ttap(x, y):
        b.touch("touchStart", [{"x": x, "y": y, "id": 1}])
        b.sleep_ms(60)
        b.touch("touchEnd", [])

    def c09(colx, gb):
        y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
        b.touch("touchStart", [{"x": colx, "y": y0, "id": 1}])
        for i in range(1, 13):
            b.sleep_ms(16)
            b.touch("touchMove", [{"x": colx, "y": y0 + (y1 - y0) * i / 12, "id": 1}])
        b.sleep_ms(60)
        b.touch("touchEnd", [])
        return y0 - y1

    pick = lambda v: {k: v.get(k) for k in ("first", "firstFull", "lastFull", "offsetY", "topClip", "staleRows", "selectedIndex", "rowHeight")}  # noqa: E731
    try:
        # ---------- RH1-W ----------
        P = 31.05
        if "rh1" in only:
            G, gb, cond = fresh()
            GV = lambda: b.call("grid.visible", id="grid.dgMain", doc=G) or {}  # noqa: E731
            tree = b.call("tree.get", doc=G, named=False, depth=90) or {}
            nodes = walk(tree, [])
            rows = [n for n in nodes if n.get("type") == "DataGridRow" and (n.get("bounds") or {}).get("h", 0) > 0 and n.get("isVisible", n.get("visible", True)) is not False]
            cells = [n for n in nodes if n.get("type") == "DataGridCell" and (n.get("bounds") or {}).get("h", 0) > 0]
            rb = sorted([(n["bounds"].get("y"), n["bounds"].get("h")) for n in rows])
            hs = [h for _, h in rb]
            diffs = [round(y1 - y0, 2) for (y0, _), (y1, _) in zip(rb, rb[1:])]
            b.call("grid.select", id="grid.dgMain", doc=G, index=30, scroll=True)
            sl(0.8)
            g30 = GV()
            P_scroll = round(((g30.get("offsetY") or 0) - (g30.get("topClip") or 0)) / g30["first"], 3) if g30.get("first") else None
            med = sorted(hs)[len(hs) // 2] if hs else None
            P = med or P_scroll or P
            jd("web_RH1-W%s_tree.json" % a.pf, tree)
            s = shot("web_RH1-W%s" % a.pf)
            case("RH1-W", "실측만(판정 밖). i1720rc 웹 31.1, RowHeight 28",
                 {"row_bounds_h": hs[:20], "row_h_median": med, "row_y_diffs": diffs[:20], "cell_bounds_h_set": sorted(set(round(n["bounds"]["h"], 2) for n in cells))[:10],
                  "P_scroll": P_scroll, "scroll30": pick(g30), "rows_found": len(rows)},
                 "기록", ["web_RH1-W%s_tree.json" % a.pf, s], "접힘은 wm size 904x2316 대용" if a.pf == "F" else None, cond)

        # ---------- GV1-W1 / W2 ----------
        if "gv1" in only:
            for cid, pre_tap in (("GV1-W1", True), ("GV1-W2", False)):
                G, gb, cond = fresh()
                GV = lambda: b.call("grid.visible", id="grid.dgMain", doc=G) or {}  # noqa: E731
                colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
                tapinfo = None
                if pre_tap:
                    H = 32.25
                    tries = []
                    for _ in range(4):
                        g = GV()
                        y = gb["y"] + H + 2.5 * P - (g.get("offsetY") or 0)
                        ttap(colx, y)
                        sl(0.6)
                        got = GV().get("selectedIndex")
                        tries.append({"y_css": round(y, 1), "got": got, "H": round(H, 2)})
                        if got == 2 or got is None or got < 0:
                            break
                        H += (2 - got) * P * 0.9
                    tapinfo = {"tries": tries, "ok": tries[-1]["got"] == 2}
                before = GV()
                finger = c09(colx, gb)
                sl(2.5)
                mid = GV()
                sel = None
                if pre_tap:
                    sel = b.call("grid.select", id="grid.dgMain", doc=G, index=30, scroll=True)
                    sl(1.0)
                after = GV()
                jd("web_%s_%s_before.json" % (cid, a.pf), before)
                jd("web_%s_%s_after.json" % (cid, a.pf), after)
                s = shot("web_%s_%s_after" % (cid, a.pf))
                off = after.get("offsetY") or 0
                want = math.floor(off / 31.05)
                st = after.get("staleRows") or []
                ok_rows = after.get("first") is not None and abs(after["first"] - want) <= 1 and abs((after.get("firstFull") if after.get("firstFull") is not None else -9) - want) <= 1
                if pre_tap:
                    exp = {"staleRows": "포커스 행 2 포함", "first≈firstFull≈floor(offsetY/31.05)": want, "참고(i1720rc 접힘)": "first=firstFull=2·offsetY 240.4(오염)"}
                    ok = ok_rows and 2 in st
                else:
                    exp = {"staleRows": [], "first·firstFull": "i1720rc 와 같음(floor(offsetY/31.05) 근처)", "floor": want}
                    ok = ok_rows and st == []
                ratio = round(((mid.get("offsetY") or 0) - (before.get("offsetY") or 0)) / finger, 3) if finger else None
                case(cid, exp, {"tap_row2": tapinfo, "before": pick(before), "after_drag": pick(mid), "select30": sel, "after": pick(after), "floor_offset_P": want,
                                "finger_css": round(finger, 1), "ratio_offset": ratio},
                     "PASS(캡처 확인 필요)" if ok else "FAIL", ["web_%s_%s_before.json" % (cid, a.pf), "web_%s_%s_after.json" % (cid, a.pf), s],
                     ("접힘은 wm size 904x2316 대용. " if a.pf == "F" else "") + "판정 기준 first≈firstFull≈floor(offsetY/31.05)(odev-8n)", cond)

        # ---------- UIM1-W ----------
        if "uim1" in only:
            G, gb, cond = fresh()
            m_set = b.call("ui.mode", set="auto")
            sl(1)
            m0 = b.call("ui.mode")
            if a.pf == "U":
                f = b.call("ui.find", id="shell.btnUiModeTablet") or {}
                bb = f.get("bounds")
                tapped = None
                if bb and bb.get("w") and f.get("isVisible") is not False:
                    x, y = bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2
                    ttap(x, y)
                    sl(1.5)
                    tapped = {"css": [round(x, 1), round(y, 1)], "bounds": bb}
                m1 = b.call("ui.mode")
                s = shot("web_UIM1-W_U_after_tap")
                ok = tapped is not None and (m1 or {}).get("uiMode") == "Auto" and (m1 or {}).get("isCompact") is False
                case("UIM1-W-U1", {"uiMode": "Auto 유지", "isCompact": False, "짝": "UIM1-U1"}, {"set_auto": m_set, "start": m0, "find": f, "tapped": tapped, "after": m1},
                     "PASS" if ok else ("판정필요(칸 미노출)" if tapped is None else "FAIL"), [s], None, cond)
            else:
                sw = b.call("ui.find", id="shell.uiModeSwitch")
                s = shot("web_UIM1-W_F_folded")
                hidden = (sw is None) or (isinstance(sw, dict) and (sw.get("effectivelyVisible") is False or sw.get("isVisible") is False))
                ok = hidden and (m0 or {}).get("isNarrow") is True and (m0 or {}).get("isCompact") is True and (m0 or {}).get("uiMode") == "Auto"
                case("UIM1-W-F", {"folded": "스위치 숨김·isNarrow=true·isCompact=true·uiMode Auto", "짝": "UIM1-F(교체)"}, {"set_auto": m_set, "folded_ui_mode": m0, "switch_find": sw},
                     "PASS" if ok else "FAIL", [s], "접힘은 wm size 904x2316 대용. uiModeEffective 는 값만 기록", cond)
            b.call("ui.mode", set="auto")
            summ["mode_after"] = b.call("ui.mode")
    except Exception as e:  # noqa: BLE001
        summ["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
    finally:
        try:
            b.call("ui.mode", set="auto")
        except Exception:  # noqa: BLE001
            pass
        save_summary()
        b.close()
        print("EXIT", flush=True)


if __name__ == "__main__":
    main()

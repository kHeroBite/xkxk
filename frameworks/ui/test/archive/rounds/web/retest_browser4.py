# 브라우저(WASM) 새 번들 재시험 — docs.sdi floating·관리형 창, 잠금 시 손잡이 IsVisible, 새로고침 인라인 확인, MDI-Z(ZOrder 저장·복원)를 Playwright 로 점검한다
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:300]), flush=True)


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b)
        ids = [d["id"] for d in r.call("docs.open", kind="grid", count=2)]
        time.sleep(2)
        d = ids[0]
        # docs.sdi floating
        r.call("docs.sdi", doc=d, on=True)
        time.sleep(1.5)
        x = t.doc(r, d)
        wl = r.call("windows.list") or []
        rec("MDI-09 docs.sdi on → floating/managedWindow", "floating=true, managedWindow=true, windows.list 에 ManagedWindow",
            {"floating": x.get("floating"), "managed": x.get("managedWindow"), "windows": [(w.get("type"), w.get("managed")) for w in wl]},
            "PASS" if x.get("floating") and x.get("managedWindow") and any(w.get("type") == "ManagedWindow" for w in wl) else "FAIL")
        page.screenshot(path=os.path.join(OUT, "mdi09_sdi_v2.png"))
        # 잠금 시 손잡이
        g0 = r.call("ui.find", id="chrome.gripResize", doc=d) or r.call("ui.find", id="chrome.gripResize", window=0)
        r.call("chrome.lock", doc=d)
        time.sleep(1)
        g1 = r.call("ui.find", id="chrome.gripResize", doc=d) or r.call("ui.find", id="chrome.gripResize", window=0)
        rec("UI-09 gripResize 잠금 전/후 보임", "전 보임 → 후 IsVisible=false",
            {"before": g0 and g0.get("visible"), "after": g1 and g1.get("visible"), "found": [bool(g0), bool(g1)]},
            "PASS" if g0 and g0.get("visible") and g1 and not g1.get("visible") else ("미검증(손잡이 못 찾음)" if not g0 else "FAIL"))
        r.call("chrome.lock", doc=d)
        r.call("docs.sdi", doc=d, on=False)
        time.sleep(1)
        x = t.doc(r, d)
        rec("MDI-09 docs.sdi off → 원복", "floating=false, managedWindow=false", {"floating": x.get("floating"), "managed": x.get("managedWindow")},
            "PASS" if not x.get("floating") and not x.get("managedWindow") else "FAIL")
        # 새로고침 인라인 확인
        before = [y["id"] for y in r.call("docs.list")]
        r.call("ui.click", id="chrome.btnCache", doc=d)
        time.sleep(1)
        ok = r.call("ui.find", id="chrome.refreshOk", doc=d) or r.call("ui.find", id="chrome.refreshOk", window=0)
        page.screenshot(path=os.path.join(OUT, "refresh_inline.png"))
        rec("새로고침 인라인 확인(chrome.btnCache → chrome.refreshOk)", "확인 UI 표시", ok, "PASS" if ok else "FAIL(미표시 또는 id 상이)")
        if ok:
            r.call("ui.click", id="chrome.refreshOk", doc=d)
            time.sleep(1)
            after = [y["id"] for y in r.call("docs.list")]
            rec("새로고침 확인 클릭 → 새 문서", "문서 교체", {"before": before, "after": after}, "PASS" if before != after else "FAIL")
        ctx.close()

        # MDI-Z
        ctxz, pz, rz = t.boot(b)
        zid = [x["id"] for x in rz.call("docs.open", kind="grid", count=5)]
        time.sleep(2)
        lst = rz.call("docs.list")
        has_z = "zIndex" in lst[0]
        rec("MDI-Z zIndex 필드", "있음", has_z, "PASS" if has_z else "미검증")
        if has_z:
            rz.call("chrome.pin", doc=zid[1])
            for i in (3, 0, 4, 2):
                rz.call("docs.activate", doc=zid[i])
            time.sleep(1)
            def snap(rr):
                return [(y["id"], y.get("zIndex"), y.get("childIndex"), y.get("active")) for y in rr.call("docs.list")]
            before = snap(rz)
            pinned_before = {y: (rz.call("chrome.state", doc=y) or {}).get("pinned") for y in zid}
            rz.call("layout.save")
            pz.screenshot(path=os.path.join(OUT, "mdiz_before.png"))
            pz.close()
            ctx2, pz2, rz2 = t.boot(b, "?load-layout", ctx=ctxz)
            time.sleep(3)
            after = snap(rz2)
            pinned_after = {y: (rz2.call("chrome.state", doc=y) or {}).get("pinned") for y in zid}
            pz2.screenshot(path=os.path.join(OUT, "mdiz_after.png"))
            same = [(a[0], a[1], a[3]) for a in before] == [(a[0], a[1], a[3]) for a in after]
            rec("MDI-Z 저장→새로고침→복원: zIndex·활성·핀", "동일", {"before": before, "after": after, "pin": [pinned_before, pinned_after]},
                "PASS" if same and pinned_before == pinned_after else "FAIL")
        ctxz.close()
        with open(os.path.join(OUT, "retest_web4.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()

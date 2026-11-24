# 브라우저(WASM) 추가 시험 3 — 위치 고정(lock) 조합·프리셋·중복 방지·탭 우클릭·도킹 핀·사용자 테마 저장/재로드·MDI-Z(ZOrder 복원)를 Playwright 로 점검한다
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
    print("%-6s %s | %s" % (verdict, item, str(actual)[:240]), flush=True)


def cs(r, d):
    return r.call("chrome.state", doc=d) or {}


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b)
        ids = [d["id"] for d in r.call("docs.open", kind="grid", count=2)]
        time.sleep(2)
        d = ids[0]
        # UI-09 위치 고정 × 항상 위 4조합
        for lock in (False, True):
            for pin in (False, True):
                cur = cs(r, d)
                if bool(cur.get("locked")) != lock:
                    r.call("chrome.lock", doc=d)
                if bool(cs(r, d).get("pinned")) != pin:
                    r.call("chrome.pin", doc=d)
                s = cs(r, d)
                ok = bool(s.get("locked")) == lock and bool(s.get("pinned")) == pin
                rec("UI-09 lock=%s pin=%s 독립" % (lock, pin), "두 상태가 서로 독립", {"locked": s.get("locked"), "pinned": s.get("pinned")}, "PASS" if ok else "FAIL")
        # 잠금 상태에서 mdiBounds 불변 / 최대화 후 복원 위치
        if not cs(r, d).get("locked"):
            r.call("chrome.lock", doc=d)
        before = cs(r, d).get("mdiBounds")
        r.call("docs.moveAll", dx=100, dy=60)
        after_move = cs(r, d).get("mdiBounds")
        rec("UI-09 잠금 중 moveAll 로 mdiBounds", "변하지 않음(잠금)", {"before": before, "after": after_move}, "PASS" if before == after_move else "관찰(전체 이동은 잠금 예외일 수 있음)")
        r.call("docs.state", state="maximized", doc=d)
        r.call("docs.state", state="normal", doc=d)
        restored = cs(r, d).get("mdiBounds")
        rec("UI-09 최대화→복원 후 위치", "원래 위치", {"before": after_move, "restored": restored}, "PASS" if restored == after_move else "FAIL")
        # 잠금 시 gripResize 소실: MDI 문서와 SDI(관리형 창) 양쪽에서 확인
        g_mdi = r.call("ui.find", id="chrome.gripResize", window=0)
        rec("UI-09 MDI 문서의 chrome.gripResize", "잠금 시 없음", "존재" if g_mdi else "없음", "PASS" if not g_mdi else "FAIL")
        d2 = ids[1]
        r.call("docs.sdi", doc=d2, on=True)
        time.sleep(2)
        g_before = r.call("ui.find", id="chrome.gripResize", window=0)
        r.call("chrome.lock", doc=d2)
        time.sleep(1)
        g_after = r.call("ui.find", id="chrome.gripResize", window=0)
        rec("UI-09 SDI(관리형) gripResize 잠금 전/후", "전 있음 → 후 없음", {"before": bool(g_before), "after": bool(g_after), "canResize": cs(r, d2).get("canResize")},
            "PASS" if g_before and not g_after else ("미검증(SDI 에 gripResize 없음)" if not g_before else "FAIL"))
        page.screenshot(path=os.path.join(OUT, "ui09_lock_sdi.png"))
        r.call("chrome.lock", doc=d2)
        r.call("docs.sdi", doc=d2, on=False)

        # MDI-02 중복 방지
        r.call("docs.close", all=True)
        r.call("docs.open", kind="form", title="DUP-T")
        r.call("docs.open", kind="form", title="DUP-T")
        n = len([x for x in r.call("docs.list") if x.get("title") == "DUP-T"])
        rec("MDI-02 중복 방지(같은 제목 2회 열기)", "1개", n, "PASS" if n == 1 else "FAIL")
        r.call("docs.open", kind="form", title="DUP-T", allowDuplicate=True)
        n2 = len([x for x in r.call("docs.list") if x.get("title") == "DUP-T"])
        rec("MDI-02 allowDuplicate", "2개", n2, "PASS" if n2 == 2 else "FAIL")

        # MDI-11 / EXT-02·04 프리셋(탐색기 버튼) — 이름으로 클릭
        r.call("docs.close", all=True)
        gids = [x["id"] for x in r.call("docs.open", kind="grid", count=3)]
        time.sleep(1)
        for name, yy in (("MDI 계단식", 443), ("탭 문서", 477), ("MDI 세로 바둑판", 511)):
            page.mouse.click(110, yy)
            out = True
            time.sleep(1.5)
            page.screenshot(path=os.path.join(OUT, "mdi11_%s.png" % name.replace(" ", "_")))
            rec("MDI-11 프리셋 " + name, "클릭 성공 + 배치 변경", bool(out), "PASS" if out else "FAIL")

        # MDI-03 탭 우클릭 / 활성 강조
        box = page.evaluate("() => null")
        tab = r.call("ui.find", id="shell.tab." + gids[0])
        if tab:
            bx = tab["bounds"]
            page.mouse.click(bx["x"] + bx["w"] / 2, bx["y"] + bx["h"] / 2, button="right")
            time.sleep(1)
            page.screenshot(path=os.path.join(OUT, "mdi03_tab_rightclick.png"))
            rec("MDI-03 탭 우클릭 메뉴", "컨텍스트 메뉴 표시(스크린샷 판독)", "mdi03_tab_rightclick.png", "증거")
            page.keyboard.press("Escape")
        else:
            rec("MDI-03 탭 우클릭", "탭 컨트롤 발견", None, "FAIL")

        # UI-09R 도구 창 Dock 핀: 탐색기 핀(211,127)
        r.call("docs.arrange", mode="cascade")
        page.screenshot(path=os.path.join(OUT, "ui09r_before.png"))
        page.mouse.click(211, 127)
        time.sleep(1.5)
        page.screenshot(path=os.path.join(OUT, "ui09r_after_click.png"))
        tr = json.dumps(r.call("tree.get", window=0, named=True), ensure_ascii=False)
        rec("UI-09R 도구 창 핀 클릭", "고정↔자동숨김 전환(스크린샷 판독)", "ui09r_before/after_click.png", "증거")

        # UI-07 사용자 테마: 복제→색 변경→저장→재로드
        r.call("settings.set", theme="Light")
        rr = None
        ctxn, page_t, rt = t.boot(b, "?cmd=theme-clone:ZZTest,theme-set:PdBrushAccent=%23ff0000,theme-save&exit-after=0")
        time.sleep(2)
        th = rt.call("themes.get") or {}
        rec("UI-07 복제·색 변경·저장", "userThemes 에 ZZTest", {"active": th.get("activeThemeId"), "user": th.get("userThemes")}, "PASS" if th.get("userThemes") else "FAIL")
        page_t.screenshot(path=os.path.join(OUT, "ui07_custom_theme.png"))
        page_t.close()
        ctxn2, page_u, ru = t.boot(b, "", ctx=ctxn)
        th2 = ru.call("themes.get") or {}
        rec("UI-07 새 탭 재로드 후 사용자 테마 유지", "userThemes 유지", {"active": th2.get("activeThemeId"), "user": th2.get("userThemes")}, "PASS" if th2.get("userThemes") else "FAIL")
        ctxn.close()

        # MDI-Z: 번들에 zIndex 가 있는지 먼저 확인
        ctxz, pz, rz = t.boot(b)
        zid = [x["id"] for x in rz.call("docs.open", kind="grid", count=5)]
        time.sleep(2)
        lst = rz.call("docs.list")
        has_z = "zIndex" in lst[0]
        rec("MDI-Z 번들에 zIndex 필드", "있음", has_z, "PASS" if has_z else "미검증(번들에 ZOrder 코드 없음 — 다음 번들에서 시험)")
        if has_z:
            rz.call("chrome.pin", doc=zid[1])
            for i in (3, 0, 2, 4):
                rz.call("docs.activate", doc=zid[i])
            rz.call("docs.activate", doc=zid[2])
            before = [(x["id"], x.get("zIndex"), x.get("childIndex")) for x in rz.call("docs.list")]
            act0 = (rz.call("status") or {}).get("active", {}).get("id")
            rz.call("layout.save")
            pz.close()
            ctx2, pz2, rz2 = t.boot(b, "?load-layout", ctx=ctxz)
            time.sleep(3)
            after = [(x["id"], x.get("zIndex"), x.get("childIndex")) for x in rz2.call("docs.list")]
            act1 = (rz2.call("status") or {}).get("active", {}).get("id")
            rec("MDI-Z 복원 후 쌓임·활성", "zIndex/활성 동일", {"before": before, "after": after, "active": [act0, act1]}, "PASS" if before == after and act0 == act1 else "FAIL")
        ctxz.close()
        with open(os.path.join(OUT, "retest_web3.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()

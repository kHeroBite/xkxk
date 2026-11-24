# 웹 Dock 메뉴 다국어 시험(UI-16) — dock.menuHeaders 로 도구 ▾(toolchrome)·문서 탭(tab)·MDI 창(mdi) 우클릭 메뉴 Header 를 ko·en 에서 읽어 언어 반영 여부를 판정
# 사용: PD_URL=http://127.0.0.1:5204/ python3 tools/retest_browser21.py   (진단판·헤드리스 전용)
"""
판정: 각 대상·언어마다 항목 Header 를 모은다.
  ko: 키가 있는 항목(resolved_via=header|resource|mapped)이 한글을 포함해야 함(literal=테마 고정 영어 항목은 제외·기록)
  en: 같은 항목이 한글 없이 영문이어야 함
  ko 와 en 의 Header 가 항목별로 달라야 함(literal 제외)
도구 Id 는 확정 값을 몰라 후보를 차례로 시도하고 성공한 Id 를 기록한다.
"""
import json
import os
import re
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
HANGUL = re.compile(r"[가-힣]")
TOOL_IDS = ["LeftPane", "BottomPane", "NotificationPane", "Explorer", "Log", "Notifications", "LeftPane.Tabs", "BottomPane.Tabs"]
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:500]), flush=True)


def headers(r, target, ids):
    for i in ids:
        v = r.call("dock.menuHeaders", target=target, doc=i)
        if v is not None:
            return i, v
    return None, {"error": (r.calls[-1].get("error") if r.calls else None)}


def keyed(items):
    return [(x.get("index"), x.get("header"), x.get("resolved_via")) for x in (items or []) if x.get("resolved_via") not in ("separator", "literal")]


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-dockmenu")
        r.call("docs.close", all=True)
        g = r.call("docs.open", kind="grid", title="DM-grid", allowDuplicate=True)[0]["id"]
        r.call("docs.open", kind="form", title="DM-form", allowDuplicate=True)
        time.sleep(1.0)
        got = {}
        for lang in ("ko", "en"):
            r.call("lang.set", code=lang)
            time.sleep(0.8)
            r.call("layout.template.apply", id="tpl.tabbed")
            time.sleep(0.6)
            got[(lang, "tab")] = headers(r, "tab", [g])
            got[(lang, "toolchrome")] = headers(r, "toolchrome", TOOL_IDS)
            got[(lang, "tool")] = headers(r, "tool", TOOL_IDS)
            r.call("layout.template.apply", id="tpl.mdi-cascade")
            time.sleep(0.6)
            got[(lang, "mdi")] = headers(r, "mdi", [g])
            page.screenshot(path=os.path.join(OUT, "dockmenu_%s.png" % lang))
        for target in ("toolchrome", "tool", "tab", "mdi"):
            (ik, vk), (ie, ve) = got[("ko", target)], got[("en", target)]
            ko, en = keyed(vk.get("items")), keyed(ve.get("items"))
            literal = [x.get("header") for x in (vk.get("items") or []) if x.get("resolved_via") == "literal"]
            ok_ko = bool(ko) and all(h and HANGUL.search(h) for _, h, _ in ko)
            ok_en = bool(en) and all(h and not HANGUL.search(h) for _, h, _ in en)
            differ = bool(ko) and len(ko) == len(en) and all(a[1] != b_[1] for a, b_ in zip(ko, en))
            rec("D%s Dock 메뉴 ko/en(%s)" % ({"toolchrome": "1", "tool": "2", "tab": "3", "mdi": "4"}[target], target),
                "ko 항목 한글·en 항목 영문·항목별 서로 다름(테마 고정 영어 literal 항목은 제외, 기록)",
                {"id": ik, "control": vk.get("control"), "source": vk.get("source"), "map": vk.get("map"), "ko": ko, "en": en, "literal_fixed_english": literal,
                 "error": vk.get("error") or ve.get("error")},
                "PASS" if ok_ko and ok_en and differ else ("판정불가(대상 못 찾음)" if ik is None else
                                                           "판정불가(메뉴가 열리기 전이라 컨트롤에 ContextMenu/Flyout 없음 — source=none)" if vk.get("source") == "none" else "FAIL"))
        r.call("lang.set", code="ko")
        with open(os.path.join(OUT, "retest_web21_dockmenu.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()

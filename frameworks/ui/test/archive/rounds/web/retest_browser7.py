# 브라우저(WASM) UI-11 레이아웃 라이브러리 보완 시험 — 기본 이름 형식·빈 이름·이름 변경·삭제·Version1 읽기를 원격 명령으로 판정한다(Q7 웹)
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
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:400]), flush=True)


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?fresh")
        r.call("docs.open", kind="grid", count=2)
        time.sleep(1.5)
        dn = r.call("layout.defaultName")
        name = dn if isinstance(dn, str) else (dn or {}).get("name") or (dn or {}).get("id")
        rec("Q7 기본 이름 형식(yyyy-MM-dd HH:mm:ss)", "정규식 일치", dn, "PASS" if isinstance(name, str) and re.fullmatch(r"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}( \(\d+\))?", name) else "FAIL")
        s1 = r.call("layout.save", name=name)
        rec("Q7 기본 이름으로 저장", "status=Saved", s1, "PASS" if (s1 or {}).get("status") == "Saved" else "FAIL")
        s2 = r.call("layout.save", name="   ")
        rec("Q7 공백 이름 저장 거부", "NameEmpty", s2, "PASS" if (s2 or {}).get("status") == "NameEmpty" else "FAIL")
        s3 = r.call("layout.save", name=name)
        rec("Q7 중복 이름 저장 거부", "NameExists", s3, "PASS" if (s3 or {}).get("status") == "NameExists" else "FAIL")
        s4 = r.call("layout.save", name=name, overwrite=True)
        rec("Q7 overwrite 로 덮어쓰기", "Saved", s4, "PASS" if (s4 or {}).get("status") == "Saved" else "FAIL")
        lst = r.call("layout.list") or []
        me = next((x for x in lst if x.get("name") == name), None)
        rec("Q7 목록에 개인 레이아웃 표시(Kind=Personal)", "목록에 있음", me, "PASS" if me and me.get("kind") == "Personal" else "FAIL")
        if me:
            rn = r.call("layout.rename", id=me["id"], name="WEB-이름변경")
            rec("Q7 이름 변경", "Saved", rn, "PASS" if (rn or {}).get("status") == "Saved" else "FAIL")
            ld = r.call("layout.load", id=me["id"])
            rec("Q7 불러오기", "복원됨", ld, "PASS" if ld else "FAIL")
            dl = r.call("layout.delete", id=me["id"], confirm=True)
            rec("Q7 삭제", "Saved", dl, "PASS" if (dl or {}).get("status") == "Saved" else "FAIL")
            gone = not any(x.get("id") == me["id"] for x in (r.call("layout.list") or []))
            rec("Q7 삭제 후 목록에서 사라짐", "True", gone, "PASS" if gone else "FAIL")
        # Version1 읽기: 현재 layout.get 의 Version 을 1 로, Panels·SidePanelsHidden·PanelId·AutoTitle 제거 후 자동 저장 슬롯 키에 넣고 인자 없이 load
        lg = r.call("layout.get")
        if isinstance(lg, dict):
            v1 = json.loads(json.dumps(lg))
            v1["Version"] = 1
            for k in ("Panels", "SidePanelsHidden"):
                v1.pop(k, None)
            def strip(o):
                if isinstance(o, dict):
                    for k in ("PanelId", "AutoTitle"):
                        o.pop(k, None)
                    for v in o.values():
                        strip(v)
                elif isinstance(o, list):
                    for v in o:
                        strip(v)
            strip(v1)
            page.evaluate("v => localStorage.setItem('pd-poc-layout', v)", json.dumps(v1))
            r.call("docs.close", all=True)
            ok = r.call("layout.load")
            n = len(r.call("docs.list") or [])
            rec("Q7 Version1 저장본 읽기", "load 성공·문서 복원", {"load": ok, "docs": n}, "PASS" if ok and n >= 1 else "FAIL")
        else:
            rec("Q7 Version1 저장본 읽기", "layout.get 응답", lg, "미검증")
        with open(os.path.join(OUT, "retest_web7.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()

# 브라우저(WASM) UI-11 레이아웃 저장·관리 대화상자를 화면 AutomationId 와 실마우스 클릭으로 시험하는 스크립트(Q7 실클릭, ?fresh 로 설정·레이아웃 격리)
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
    print("%-6s %s | %s" % (verdict, item, str(actual)[:420]), flush=True)


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?fresh")
        r.call("docs.open", kind="grid", count=2)
        time.sleep(1.5)

        def click(ident, wait=0.7):
            n = r.call("ui.find", id=ident)
            if not n or not n.get("bounds") or not n.get("visible", True):
                return None
            bb = n["bounds"]
            page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)
            time.sleep(wait)
            return n

        def text_of(ident):
            n = r.call("ui.find", id=ident) or {}
            return n.get("text")

        def names():
            return [x.get("name") for x in (r.call("layout.list") or [])]

        # 1) 저장 대화: 메뉴 → 현재 화면 저장
        m = click("shell.mnuWinMenu")
        s = click("shell.mnuLayoutSave")
        rec("Q7 클릭: 윈도우 메뉴 → 현재 화면 저장 열기", "저장 대화 표시(shell.layout.txtName)", {"menu": bool(m), "item": bool(s)}, "PASS" if m and s and r.call("ui.find", id="shell.layout.txtName") else "FAIL")
        page.screenshot(path=os.path.join(OUT, "q7_click_save_dialog.png"))
        default = text_of("shell.layout.txtName")
        rec("Q7 클릭: 저장 대화 기본 이름(yyyy-MM-dd HH:mm:ss)", "정규식 일치", default, "PASS" if isinstance(default, str) and re.fullmatch(r"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}( \(\d+\))?", default) else "FAIL")
        before = names()
        # 2) 빈 이름 저장 거부
        r.call("ui.setText", id="shell.layout.txtName", text="")
        click("shell.layout.btnSave")
        after_empty = names()
        rec("Q7 클릭: 빈 이름 저장 거부", "목록 불변·안내 문구", {"before": len(before), "after": len(after_empty), "message": text_of("shell.layout.message")}, "PASS" if len(before) == len(after_empty) else "FAIL")
        # 3) 이름 입력 후 저장
        r.call("ui.setText", id="shell.layout.txtName", text="WEB-CLICK-1")
        click("shell.layout.btnSave")
        n1 = names()
        rec("Q7 클릭: 이름 입력 후 저장", "목록에 WEB-CLICK-1", n1, "PASS" if "WEB-CLICK-1" in n1 else "FAIL")
        page.screenshot(path=os.path.join(OUT, "q7_click_saved.png"))
        # 4) 같은 이름 재저장 → 덮어쓰기 확인
        click("shell.mnuWinMenu")
        click("shell.mnuLayoutSave")
        r.call("ui.setText", id="shell.layout.txtName", text="WEB-CLICK-1")
        click("shell.layout.btnSave")
        ow = r.call("ui.find", id="shell.layout.btnOverwrite")
        rec("Q7 클릭: 중복 이름 저장 시 덮어쓰기 확인 표시", "btnOverwrite/btnOverwriteNo", bool(ow), "PASS" if ow else "FAIL")
        if ow:
            click("shell.layout.btnOverwriteNo")
            rec("Q7 클릭: 덮어쓰기 아니오 → 개수 불변", "목록 불변", len(names()), "PASS" if names().count("WEB-CLICK-1") == 1 else "FAIL")
        click("shell.layout.btnCancel")
        # 5) 관리 대화: 이름 변경·삭제
        click("shell.mnuWinMenu")
        mg = click("shell.mnuLayoutManage")
        lst = r.call("ui.find", id="shell.layout.list")
        rec("Q7 클릭: 화면 관리 열기", "목록(shell.layout.list) 표시", {"item": bool(mg), "list": bool(lst)}, "PASS" if mg and lst else "FAIL")
        page.screenshot(path=os.path.join(OUT, "q7_click_manage_dialog.png"))
        sel = r.call("ui.select", id="shell.layout.list", text="WEB-CLICK-1")
        rec("Q7 관리: 목록에서 WEB-CLICK-1 선택", "선택 성공", bool(sel), "PASS" if sel else "FAIL")
        r.call("ui.setText", id="shell.layout.txtRename", text="WEB-CLICK-2")
        click("shell.layout.btnRename")
        n2 = names()
        rec("Q7 클릭: 이름 변경", "WEB-CLICK-2 로 변경", n2, "PASS" if "WEB-CLICK-2" in n2 and "WEB-CLICK-1" not in n2 else "FAIL")
        r.call("ui.select", id="shell.layout.list", text="WEB-CLICK-2")
        click("shell.layout.btnDelete")
        yes = r.call("ui.find", id="shell.layout.btnDeleteYes")
        rec("Q7 클릭: 삭제 확인 표시", "btnDeleteYes/btnDeleteNo", bool(yes), "PASS" if yes else "FAIL")
        click("shell.layout.btnDeleteNo")
        rec("Q7 클릭: 삭제 아니오 → 유지", "WEB-CLICK-2 유지", names(), "PASS" if "WEB-CLICK-2" in names() else "FAIL")
        click("shell.layout.btnDelete")
        click("shell.layout.btnDeleteYes")
        n3 = names()
        rec("Q7 클릭: 삭제 예 → 제거", "WEB-CLICK-2 없음", n3, "PASS" if "WEB-CLICK-2" not in n3 else "FAIL")
        page.screenshot(path=os.path.join(OUT, "q7_click_deleted.png"))
        with open(os.path.join(OUT, "retest_web8.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()

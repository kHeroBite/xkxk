#!/usr/bin/env python3
# 데스크톱 패널 탭을 SDI 창에 넣은 뒤 복구 측정(D-PANELLOST) — 보기 메뉴로 되찾기, SDI 창 닫기 뒤 위치·복구, 재기동 복원을 값으로 남긴다
"""
사용: python3 -B retest_panel_in_sdi.py --run-dir <bin/run/<복사본>/net10.0> --work logs/<이름>/<회차> [--tool Notifications]
넣기는 진단 split.do(Dock 끌어 놓기와 같은 실행 경로), 닫기는 제목줄 닫기 버튼 ui.click, 보기 메뉴는 panel.show(메뉴와 같은 ShowTool)다.
재기동은 layout.get 값을 새 임시 프로필에 심어 띄운다(같은 프로필 재사용 아님). bin/run 복사본·화면 밖·가드 사용, 자기 PID 만 종료. 종료 코드 0=통과, 1=실패, 4=가드 중단.
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402,F401
import mdi_drag_app as app  # noqa: E402

CHECKS = []


def check(name, ok, detail=None):
    CHECKS.append({"name": name, "pass": bool(ok), "detail": detail})
    print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(detail, ensure_ascii=False)[:400], flush=True)


class App:
    def __init__(self, run_dir, work, seed=None):
        from retest_remote import Remote
        from offscreen_guard import OffscreenGuard
        self.pid, self.info, self.log = app.launch(run_dir, work, seed_layout=seed)
        self.r = Remote()
        self.guard = OffscreenGuard(self.pid)
        self.guard.__enter__()
        time.sleep(1.0)

    def stop(self):
        self.guard.__exit__(None, None, None)
        print(app.stop(self.pid, os.path.basename(os.path.dirname(self.info))), flush=True)

    def state(self):
        self.guard.check()
        return {
            "panels": {p["id"]: (p.get("visible"), [t.get("id") for t in p.get("tabs") or []]) for p in self.r.call("panel.list") or []},
            "docs": [(d.get("id"), d.get("floating")) for d in self.r.call("docs.list") or []],
            "windows": [(w.get("title"), w.get("docs")) for w in self.r.call("windows.list") or [] if not w.get("main")],
        }

    def setup(self, tool):
        self.r.call("docs.close", all=True)
        self.r.call("docs.open", kind="grid")
        time.sleep(1)
        doc = self.r.call("docs.list")[0]["id"]
        self.r.call("docs.sdi", doc=doc, on=True)
        time.sleep(1.2)
        result = self.r.call("split.do", target="doc:" + doc, tool=tool, op="Fill")
        time.sleep(1.5)
        self.guard.check()
        return doc, {k: result.get(k) for k in ("status", "validate")}

    def reset(self, tool):
        self.r.call("docs.redock", all=True)
        self.r.call("panel.show", tool=tool)
        self.r.call("docs.close", all=True)
        time.sleep(1)


def in_panel(st, tool):
    return any(p[0] and tool in p[1] for p in st["panels"].values())


def walk(node, out):
    if isinstance(node, list):
        for child in node:
            walk(child, out)
    elif isinstance(node, dict):
        out.append(node)
        for child in node.get("children") or []:
            walk(child, out)
    return out


def tool_title(cur, tool):
    """제목 대조값. 건수가 붙는 '알림(5건)' 은 접두로 본다."""
    return {"Notifications": "알림", "Explorer": "탐색기", "Log": "로그"}[tool]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run-dir", required=True)
    ap.add_argument("--work", required=True)
    ap.add_argument("--tool", default="Notifications")
    a = ap.parse_args()
    os.makedirs(a.work, exist_ok=True)
    from offscreen_guard import GuardBreach
    tool, rc, cur = a.tool, 0, None
    try:
        cur = App(a.run_dir, a.work)
        _, put = cur.setup(tool)
        st = cur.state()
        check("넣기 뒤 창에 있음(패널에는 없음)", not in_panel(st, tool), {"put": put, "state": st})
        cur.r.call("screenshot", window=1, path=app.win(os.path.abspath(os.path.join(a.work, "in_sdi.png"))))
        shown = cur.r.call("panel.show", tool=tool)
        time.sleep(1.2)
        st = cur.state()
        check("① 보기 메뉴로 패널에 돌아옴", in_panel(st, tool), {"shown": shown, "state": st})
        cur.reset(tool)

        cur.setup(tool)
        clicks = 0
        after = cur.state()
        title_after_doc = None
        while after["windows"] and clicks < 3:  # 창마다 제목줄 닫기를 누른다(문서를 닫아도 창이 남으면 한 번 더)
            cur.r.call("ui.click", window=1, id="chrome.btnClose")
            time.sleep(1.5)
            after = cur.state()
            clicks += 1
            if clicks == 1 and after["windows"]:
                nodes = walk(cur.r.call("tree.get", window=1, named=False, depth=60).get("tree"), [])
                title_after_doc = [x.get("text") for n in nodes if n.get("name") == "TitleText" for x in walk(n, []) if x.get("text")]
        check("② 문서를 닫으면 제목줄이 패널 제목", bool(title_after_doc) and all(tool_title(cur, tool) in t for t in title_after_doc[:1]), title_after_doc)
        check("② 패널만 남은 창도 제목줄 ✕ 로 닫힘", not after["windows"], {"clicks": clicks, "windows": after["windows"]})
        where = in_panel(after, tool)
        shown = cur.r.call("panel.show", tool=tool)
        time.sleep(1.2)
        back = cur.state()
        check("② 창을 닫은 뒤 보기 메뉴로 패널에 돌아옴", in_panel(back, tool), {"clicks": clicks, "afterClose": after, "inPanelAfterClose": where, "shown": shown, "back": back})
        cur.reset(tool)

        cur.setup(tool)
        layout = json.dumps(cur.r.call("layout.get"), ensure_ascii=False)
        before = cur.state()
        cur.stop()
        cur = App(a.run_dir, a.work, seed=layout)
        time.sleep(3)
        st = cur.state()
        shown = cur.r.call("panel.show", tool=tool)
        time.sleep(1.2)
        back = cur.state()
        check("③ 재기동 뒤 보기 메뉴로 패널에 있음", in_panel(back, tool), {"beforeRestart": before, "afterRestart": st, "shown": shown, "back": back})
    except GuardBreach as e:
        check("가드", False, str(e))
        rc = 4
    finally:
        if cur:
            cur.stop()
        with open(os.path.join(a.work, "panel_in_sdi.json"), "w", encoding="utf-8") as f:
            json.dump({"checks": CHECKS}, f, ensure_ascii=False, indent=1)
    failed = [c for c in CHECKS if not c["pass"]]
    print("결과 %d/%d" % (len(CHECKS) - len(failed), len(CHECKS)))
    return rc or (1 if failed else 0)


if __name__ == "__main__":
    sys.exit(main())

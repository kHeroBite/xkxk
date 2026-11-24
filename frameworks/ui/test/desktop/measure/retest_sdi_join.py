#!/usr/bin/env python3
# 데스크톱 SDI 창 합류·분할 측정(U15) — 진단 split.do 로 Dock 끌어 놓기와 같은 실행 경로를 불러 SDI 창 안 탭 줄·칸·제목줄 글자를 그 창 트리에서만 잰다
"""
사용: python3 -B retest_sdi_join.py --run-dir <bin/run/<복사본>/net10.0> --work logs/<이름>/<회차>
bin/run 복사본을 임시 폴더·화면 밖으로 띄우고(mdi_drag_app) 자기 PID 만 종료한다. 조작마다 OffscreenGuard 를 확인한다.
실제 포인터로 다른 OS 창까지 끄는 입력은 재지 않는다(사람 확인 항목). 결과는 <work>/sdi_join.json. 종료 코드 0=통과, 1=실패, 4=가드 중단.
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


def walk(node, out):
    if isinstance(node, list):
        for child in node:
            walk(child, out)
    elif isinstance(node, dict):
        out.append(node)
        for child in node.get("children") or []:
            walk(child, out)
    return out


class Run:
    def __init__(self, run_dir, work):
        self.run_dir, self.work, self.pid, self.items = run_dir, work, None, []

    def start(self):
        from retest_remote import Remote
        from offscreen_guard import OffscreenGuard
        self.pid, self.info, self.log = app.launch(self.run_dir, self.work)
        self.r = Remote()
        self.guard = OffscreenGuard(self.pid)
        self.guard.__enter__()
        time.sleep(1.0)

    def stop(self):
        if self.pid:
            self.guard.__exit__(None, None, None)
            print(app.stop(self.pid, os.path.basename(os.path.dirname(self.info))), flush=True)
            self.pid = None

    def check(self, name, ok, detail=None):
        self.items.append({"name": name, "pass": bool(ok), "detail": detail})
        print(("PASS " if ok else "FAIL ") + name + "  " + json.dumps(detail, ensure_ascii=False)[:260], flush=True)

    def view(self):
        """SDI 실 창 1개의 트리만 본다. 메인 창 탭 줄은 다른 TopLevel 이라 섞이지 않는다."""
        wins = [w for w in self.r.call("windows.list") or [] if not w.get("main")]
        if not wins:
            return {"docs": [], "strips": [], "items": [], "docks": [], "title": None}
        nodes = walk(self.r.call("tree.get", window=wins[0]["index"], named=False, depth=60).get("tree"), [])
        shown = [n for n in nodes if n.get("effectivelyVisible") and (n.get("bounds") or {}).get("h", 0) > 0]
        title = [x.get("text") for n in nodes if n.get("name") == "TitleText" for x in walk(n, []) if x.get("text")]
        return {
            "docs": wins[0].get("docs") or [], "client": (wins[0].get("w"), wins[0].get("h")), "title": title[0] if title else None,
            "strips": [n["bounds"] for n in shown if n.get("type") == "DocumentTabStrip"],
            "items": [n["bounds"] for n in shown if n.get("type") == "DocumentTabStripItem"],
            "docks": [n["bounds"] for n in shown if n.get("type") == "DocumentDockControl"],
        }

    def prepare(self):
        self.guard.check()
        self.r.call("docs.close", all=True)
        for kind in ("grid", "gantt", "form"):
            self.r.call("docs.open", kind=kind)
        time.sleep(1.5)
        ids = [d["id"] for d in self.r.call("docs.list")]
        self.r.call("docs.sdi", doc=ids[0], on=True)
        time.sleep(1.5)
        self.r.call("docs.arrange", mode="tabbed")
        time.sleep(1.5)
        return ids, {d["id"]: d["title"] for d in self.r.call("docs.list")}

    def drop(self, ids, op):
        result = self.r.call("split.do", target="doc:" + ids[0], doc=ids[1], op=op)
        time.sleep(1.5)
        self.guard.check()
        self.r.call("screenshot", window=1, path=app.win(os.path.abspath(os.path.join(self.work, "sdi_%s.png" % op))))
        return {"status": result.get("status"), "policy": result.get("policy"), "validate": result.get("validate")}


def fill(run):
    ids, titles = run.prepare()
    one = run.view()
    run.check("F 합류 전 문서 1개 창은 탭 줄 없음", not one["strips"], one["strips"])
    drop = run.drop(ids, "Fill")
    v = run.view()
    floating = [d["id"] for d in run.r.call("docs.list") if d.get("floating")]  # windows.list 의 docs 는 화면에 그려진 탭만 센다
    run.check("F 합류 뒤 분리 창 문서 2개", len(floating) == 2, {"drop": drop, "floating": floating})
    run.check("F 합류 뒤 탭 줄 1개·탭 2개", len(v["strips"]) == 1 and len(v["items"]) == 2, {"strips": v["strips"], "items": v["items"]})
    run.check("F 제목줄 글자가 문서 제목", v["title"] in (titles[ids[0]], titles[ids[1]]), v["title"])
    main_strips = [n["bounds"] for n in walk(run.r.call("tree.get", window=0, named=False, depth=60).get("tree"), []) if n.get("type") == "DocumentTabStrip" and n.get("effectivelyVisible") and (n.get("bounds") or {}).get("h", 0) > 0]
    run.check("F 메인 창 탭 줄은 문서 영역 1개뿐(탭 1개 측면 패널은 숨김)", len(main_strips) == 1, main_strips)
    run.r.call("docs.redock", all=True)
    time.sleep(1)


def split(run):
    ids, titles = run.prepare()
    drop = run.drop(ids, "Top")
    v = run.view()
    run.check("S 놓은 뒤 문서 2개·두 칸", len(v["docs"]) == 2 and len(v["docks"]) == 2, {"drop": drop, "docs": v["docs"], "docks": v["docks"]})
    cw, ch = v.get("client") or (0, 0)
    run.check("S 두 칸 모두 창 안", bool(v["docks"]) and all(b["x"] + b["w"] <= cw + 1 and b["y"] + b["h"] <= ch + 1 for b in v["docks"]), {"client": v.get("client"), "docks": v["docks"]})
    run.check("S 칸마다 탭 줄 보임", len(v["strips"]) == 2, v["strips"])
    run.check("S 제목줄 글자가 문서 제목", v["title"] in (titles[ids[0]], titles[ids[1]]), v["title"])
    run.r.call("docs.close", doc=ids[1])
    time.sleep(2)
    left = run.view()
    run.check("S 한 문서를 닫으면 남은 문서 제목·탭 줄 없음", left["title"] == titles[ids[0]] and not left["strips"], {"title": left["title"], "strips": left["strips"]})
    run.r.call("docs.redock", all=True)
    time.sleep(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run-dir", required=True)
    ap.add_argument("--work", required=True)
    a = ap.parse_args()
    os.makedirs(a.work, exist_ok=True)
    from offscreen_guard import GuardBreach
    run = Run(a.run_dir, a.work)
    out = {"runDir": a.run_dir}
    rc = 0
    try:
        run.start()
        fill(run)
        split(run)
        out["guard"] = run.guard.summary()
    except GuardBreach as e:
        out["breach"] = str(e)
        rc = 4
    finally:
        run.stop()
        out["items"] = run.items
        with open(os.path.join(a.work, "sdi_join.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    passed = sum(i["pass"] for i in run.items)
    print("결과 %d/%d" % (passed, len(run.items)))
    return rc or (0 if passed == len(run.items) else 1)


if __name__ == "__main__":
    sys.exit(main())

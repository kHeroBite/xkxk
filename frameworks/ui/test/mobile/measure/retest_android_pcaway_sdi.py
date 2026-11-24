#!/usr/bin/env python3
# Fold5 접힘 폰 SDI 합류·분할 측정(C1) — 펼침에서 실터치로 합류·분할한 창을 접어 폰에서 도달·표시를 재고 펼쳐 복원을 잰다
"""
사용: python3 -B retest_android_pcaway_sdi.py --apk <Debug APK> --out <폴더> [--fresh] [--measure]
폰 NAV 는 탭 줄이 없어 탭 끌기 입력이 없고(트리 DocumentTabStrip 0, 화면설계 06 §독립 창 항목 비활성), 관리형 창 bounds 가 null 이다.
retest_sdi_join_android.py 를 접힘에서 그대로 돌리면 window_view 가 box 없는 값을 돌려줘 fill 103행이 KeyError 'box' 로 죽는다(도구 결함. 폰은 SDI 끌기 입력 자체가 없다). 여기서는 합류·분할을 펼침에서 실터치로 만들고 접힘에서 잰다.
접힘의 split.do 는 phone-mode 로 거부되는 설계다(근거: 판정보고서 부록 a5_UI20_분할_1.3.11.md 16행, 화면설계 06 30·39행, 01 201행, RemoteSplitCommands.cs 135행). 값만 남기고 판정에 쓰지 않는다.
pcaway_report emit --script 로 이 파일을 지정하고 --raw 없이 --measure-file 만 넘긴다.
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android_pcaway_lib as L  # noqa: E402
import retest_sdi_join_android as J  # noqa: E402

HEAD = 33  # 창 제목줄 32 + 틀 1


def docs(c):
    return c.call("docs.list") or []


def walk_docs(c, tag):
    rows = []
    for d in docs(c):
        c.call("docs.activate", doc=d["id"])
        time.sleep(1.2)
        cur = [x["id"] for x in docs(c) if x.get("active")]
        rows.append({"doc": d["id"], "active": cur, "title_in_tree": d["title"] in c.dump()})
    c.raw.setdefault("walk", {})[tag] = rows
    return rows


def scenario(c, scale, name, dy):
    """dy=0 이면 본문 가운데(합류), dy<0 이면 위쪽 안내(분할)."""
    ids = J.prepare_docs(c)
    titles = {d["id"]: d["title"] for d in docs(c)}
    box = J.window_view(c)["box"]
    moved = J.drag_tab_to(c, titles[ids[1]], scale, (box["x"] + box["w"] / 2, box["y"] + HEAD + (box["h"] - HEAD) / 2 + dy))
    v0 = J.window_view(c)
    cap_open = c.shot("%s_open" % name)
    L.set_closed()
    L.U.style_wait(c, "Mobile", 20)
    time.sleep(1)
    st = {"state": L.dev_state(), "wm": L.wm_size(), "effective": (c.call("ui.style") or {}).get("effective"), "floating": [d["id"] for d in docs(c) if d.get("floating")]}
    rows = walk_docs(c, name)
    direct = c.call("split.do", target="doc:" + ids[0], doc=ids[1], op="Top") or {}  # 접힘 직접 경로. RemoteSplitCommands.cs 는 IsCompact 면 phone-mode 로 거부한다
    st["split_do_on_phone"] = {k: direct.get(k) for k in ("status", "reason", "side")}
    cap_closed = c.shot("%s_closed" % name)
    L.set_open()
    L.U.style_wait(c, "Tablet", 20)
    time.sleep(2)
    v1 = J.window_view(c)
    c.shot("%s_reopen" % name)
    c.call("docs.redock", all=True)
    time.sleep(1)
    res = {"moved": str(moved), "open_docs": v0.get("docs"), "open_docks": len(v0.get("docks") or []), "open_strips": len(v0.get("strips") or []), "closed": st, "walk": rows,
           "reopen_docs": v1.get("docs"), "reopen_docks": len(v1.get("docks") or []), "reopen_strips": len(v1.get("strips") or [])}
    c.raw[name] = res
    print(name, str(res)[:500], flush=True)
    return res, cap_closed, cap_open


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--measure", action="store_true")
    a = p.parse_args()
    c = L.Ctx("fold", "Debug", a.out)
    c.raw["apk"] = a.apk
    L.set_open()
    if not L.install(a.apk, fresh=a.fresh) or not L.attach(c):
        print("설치·연결 실패")
        return 1
    L.U.style_wait(c, "Tablet", 40)
    time.sleep(2)
    scale = L.wm_size()[0] / ((c.call("ui.mode") or {}).get("viewWidth") or 805.3)
    error, join, split, caps = None, {}, {}, []
    try:
        join, cap_c, cap_o = scenario(c, scale, "join", 0)
        caps += [cap_c, cap_o]
        split, cap_c, cap_o = scenario(c, scale, "split", -30)
        caps += [cap_c]
    except Exception as e:  # noqa: BLE001 — 도구 오류는 tool_error_absent 값으로 남긴다
        error = repr(e)
    n, ex = c.fatal_count()
    c.check("치명 0", n == 0, (n, ex), 0)
    raw = L.save_raw(c, "c1")
    if a.measure:
        reach = lambda r: bool(r.get("walk")) and all(x["title_in_tree"] and x["doc"] in x["active"] for x in r["walk"])
        closed = [r.get("closed", {}) for r in (join, split)]
        rows = {
            "cover_state_closed": (closed, all(s.get("state") == "CLOSED" and tuple(s.get("wm", ())) == (904, 2316) and s.get("effective") == "Mobile" for s in closed)),
            "sdi_join_phone": (join, len(join.get("open_docs") or []) == 2 and reach(join) and len(join.get("reopen_docs") or []) == 2 and join.get("reopen_strips") == 1),
            "sdi_split_phone": (split, split.get("open_docks") == 2 and reach(split) and split.get("reopen_docks") == 2),
            "tool_error_absent": (error, error is None and n == 0),
        }
        L.write_measure(a.out, "C1", rows, "retest_android_pcaway_sdi.py " + raw, caps)
    return 0


if __name__ == "__main__":
    sys.exit(main())

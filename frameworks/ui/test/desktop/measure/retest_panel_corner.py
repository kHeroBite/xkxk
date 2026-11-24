#!/usr/bin/env python3
# 패널 끝 확장(요건 3) 원격 측정 — 규칙 표본·기본 버튼·확장 왕복·알림 4방향·뷰 보존을 재서 JSON 으로 낸다. 판정 결과 파일은 만들지 않는다(otest 소관)
"""
사용: PD_REMOTE_INFO=<격리 앱 연결 정보> python3 -B test/desktop/measure/retest_panel_corner.py --out <결과.json> [--only rules,default,roundtrip,edges,preserve,split,persist]
앱은 임시 --data-dir·--profile-dir 로 --remote-control 진단 빌드를 먼저 띄워 둔다(사용자 layout.json·settings.json 을 쓰지 않는다).
persist 는 재기동이 필요해 layout.get 의 cornerOwners 값까지만 잰다. 재기동 유지는 호출자가 같은 data-dir 로 다시 띄운 뒤 --only persist-after --expect-owners '{"BottomLeft":"BottomPane"}' 로 잰다.
종료 코드: 0=측정 완료(검사 전부 참), 1=검사 거짓 있음, 2=연결 실패.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402

L, B, N = "LeftPane", "BottomPane", "NotificationPane"
TOL = 1.5  # dip 허용(분할선 끌기 없는 비율 계산 반올림 + 픽셀 맞춤). AC-3-03 의 ±1dip 은 결과 수치로 otest 가 판정한다


def rules(r, out):
    """AC-3-01·05: 순수 함수 표본."""
    samples = [
        ("right_default", "NotificationPane:Right:0,LeftPane:Left:1,BottomPane:Bottom:2", "", False,
         {("BottomPane", "BottomLeft"), ("BottomPane", "BottomRight")}),
        ("left_default", "NotificationPane:Left:0,LeftPane:Left:1,BottomPane:Bottom:2", "", False, {("BottomPane", "BottomLeft")}),
        ("top_default", "NotificationPane:Top:0,LeftPane:Left:1,BottomPane:Bottom:2", "", False,
         {("LeftPane", "TopLeft"), ("BottomPane", "BottomLeft")}),
        ("bottom_default", "NotificationPane:Bottom:0,LeftPane:Left:1,BottomPane:Bottom:2", "", False,
         {("LeftPane", "BottomLeft"), ("BottomPane", "BottomLeft")}),
        ("right_claim_bl", "NotificationPane:Right:0,LeftPane:Left:1,BottomPane:Bottom:2", "BottomLeft:BottomPane", False,
         {("LeftPane", "BottomLeft"), ("BottomPane", "BottomRight")}),
        ("hidden_rival", "NotificationPane:Right:0:hidden,LeftPane:Left:1,BottomPane:Bottom:2", "", False, {("BottomPane", "BottomLeft")}),
        ("pinwheel_4", "T:Top:0,R:Right:1,B:Bottom:2,L:Left:3", "TopLeft:T,TopRight:R,BottomRight:B,BottomLeft:L", True, None),
    ]
    rows = []
    for name, panels, owners, pin, want in samples:
        got = r.call("panel.corner.rules", panels=panels, owners=owners)
        t = {(x["panel"], x["corner"]) for x in (got or {}).get("truncated", [])}
        ok = got is not None and got["pinwheel"] == pin and (want is None or t == want)
        r.check("rules." + name, ok, sorted(t))
        rows.append({"name": name, "got": got})

    # 네 변이 모두 찬 배치에서 풍차를 만드는 클릭은 claimable 에 없어야 한다: 세 모서리가 이미 풍차 방향이면 마지막 끝은 버튼이 없다
    almost = r.call("panel.corner.rules", panels="T:Top:3,R:Right:1,B:Bottom:2,L:Left:0", owners="TopRight:R,BottomRight:B,BottomLeft:L")
    claim = {(x["panel"], x["corner"]) for x in (almost or {}).get("claimable", [])}
    trunc = {(x["panel"], x["corner"]) for x in (almost or {}).get("truncated", [])}
    r.check("rules.pinwheel_click_hidden", ("T", "TopLeft") in trunc and ("T", "TopLeft") not in claim, {"truncated": sorted(trunc), "claimable": sorted(claim)})
    out["rules"] = rows


def ensure_default(r):
    r.call("panel.dock", id=N, side="right")
    for corner in ("TopLeft", "TopRight", "BottomLeft", "BottomRight"):
        r.call("panel.corner.release", corner=corner)
    time.sleep(0.3)


def buttons(state):
    return {b["id"]: b for b in state["buttons"] if b["visible"]}


def click_button(r, button_id):
    """실제 버튼 경로(ui.click 은 IsEffectivelyEnabled 를 확인한다)."""
    res = r.call("ui.click", id=button_id)
    time.sleep(0.5)
    return res is not None


def default_buttons(r, out):
    """AC-3-02·07: 기본 배치 버튼 위치·글리프·툴팁."""
    ensure_default(r)
    s = r.call("panel.corner.state")
    b = buttons(s)
    want = {"panel.corner.BottomPane.BottomLeft", "panel.corner.BottomPane.BottomRight"}
    r.check("default.only_bottom_ends", set(b) == want, sorted(b))
    glyphs = {i: x["glyph"] for i, x in b.items()}
    r.check("default.glyphs", glyphs.get("panel.corner.BottomPane.BottomLeft") == "<" and glyphs.get("panel.corner.BottomPane.BottomRight") == ">", glyphs)
    r.check("default.tips", all(x["tip"] for x in b.values()), {i: x["tip"] for i, x in b.items()})
    r.check("default.focusable", all(x["focusable"] and x["tabStop"] for x in b.values()))
    out["default"] = s


def rect(bounds, key):
    v = bounds.get(key)
    return v if v else None


def roundtrip(r, out):
    """AC-3-03·04: 하단 < 클릭 전후 사각형, 되돌린 뒤 스냅샷 동일."""
    ensure_default(r)
    before = r.call("panel.bounds")
    layout_before = r.call("layout.get")
    click_button(r, "panel.corner.BottomPane.BottomLeft")
    after = r.call("panel.bounds")
    state = r.call("panel.corner.state")
    lb, bb, db = rect(before, L), rect(before, B), rect(before, "Documents")
    la, ba, da = rect(after, L), rect(after, B), rect(after, "Documents")
    measures = {"before": {k: before.get(k) for k in (L, B, N, "Documents")}, "after": {k: after.get(k) for k in (L, B, N, "Documents")}}
    r.check("expand.bottom_reaches_left", abs(ba["x"] - lb["x"]) <= TOL, measures)
    r.check("expand.left_height_shrinks", la["h"] < lb["h"] - TOL)
    r.check("expand.doc_width_same", abs(da["w"] - db["w"]) <= TOL, (db["w"], da["w"]))
    r.check("expand.left_button_appears", "panel.corner.LeftPane.BottomLeft" in buttons(state), sorted(buttons(state)))
    r.check("expand.glyph_down", buttons(state).get("panel.corner.LeftPane.BottomLeft", {}).get("glyph") == "∨")
    click_button(r, "panel.corner.LeftPane.BottomLeft")
    final = r.call("panel.bounds")
    layout_after = r.call("layout.get")
    same_panels = (layout_before or {}).get("Panels") == (layout_after or {}).get("Panels")
    r.check("roundtrip.snapshot_panels_equal", same_panels, {"before": (layout_before or {}).get("Panels"), "after": (layout_after or {}).get("Panels")})
    for k in (L, B, "Documents"):
        r.check("roundtrip.rect." + k, all(abs(before[k][c] - final[k][c]) <= TOL for c in ("x", "y", "w", "h")), (before[k], final[k]))
    out["roundtrip"] = measures


def edges(r, out):
    """AC-3-08: 알림 4방향마다 잘린 끝 하나를 확장·복원."""
    rows = []
    for side in ("top", "bottom", "left", "right"):
        ensure_default(r)
        r.call("panel.dock", id=N, side=side)
        time.sleep(0.5)
        s0 = r.call("panel.corner.state")
        b0 = r.call("panel.bounds")
        ids = sorted(buttons(s0))
        r.check("edges.%s.has_buttons" % side, bool(ids), ids)
        if not ids:
            continue

        for pick in ids:  # 기본 배치의 잘린 끝 전부(위쪽 끝 ∧ 포함)
            tag = "edges.%s.%s" % (side, pick.replace("panel.corner.", ""))
            click_button(r, pick)
            s1 = r.call("panel.corner.state")
            r.check(tag + ".owner_set", bool(s1["owners"]), s1["owners"])
            panel = pick.split(".")[2]
            other = [i for i in buttons(s1) if i.split(".")[3] == pick.split(".")[3] and i.split(".")[2] != panel]
            r.check(tag + ".counter_button", bool(other), sorted(buttons(s1)))
            # 알림과 다른 패널이 같은 변에 있으면 그 모서리 상태가 셋이다: 맞은편 패널 버튼은 '끝까지'라 기본을 지나친다.
            # 그때는 알림 패널 버튼으로 기본에 돌아온다(버튼 눌러 되돌리는 경로가 끊기지 않는지도 함께 잰다).
            corner = pick.split(".")[3]
            s2 = s1
            for _ in range(4):
                if not s2["owners"]:
                    break
                at = sorted(i for i in buttons(s2) if i.split(".")[3] == corner)
                nxt = [i for i in at if ".NotificationPane." in i] or [i for i in at if i != pick] or at
                if not nxt:
                    break
                click_button(r, nxt[0])
                s2 = r.call("panel.corner.state")
            b2 = r.call("panel.bounds")
            r.check(tag + ".restored_rect", all(abs(b0["Documents"][c] - b2["Documents"][c]) <= TOL for c in ("x", "y", "w", "h")), (b0["Documents"], b2["Documents"]))
            r.check(tag + ".owners_cleared", not s2["owners"], s2["owners"])
            rows.append({"side": side, "buttons": ids, "clicked": pick, "owners_after": s1["owners"], "owners_end": s2["owners"]})
    ensure_default(r)
    out["edges"] = rows


def split_area(r, out):
    """분할(UI-20) 보존: 좌 영역을 나눠도 탭·비율이 유지되고 버튼은 영역 아래 끝 칸에 놓인다."""
    ensure_default(r)
    r.call("docs.open", kind="form", count=1)
    time.sleep(0.3)
    doc = r.call("docs.list")[-1]["id"]
    done = r.call("split.do", target=L, op="Bottom", doc=doc)
    r.check("split.created", (done or {}).get("status") == "Split", (done or {}).get("status"))
    before = r.call("split.dump")["areas"]["Left"]["panes"]
    click_button(r, "panel.corner.BottomPane.BottomLeft")
    after = r.call("split.dump")["areas"]["Left"]["panes"]
    same = [(p["id"], p["tabs"], p["active"], p["proportion"]) for p in before] == [(p["id"], p["tabs"], p["active"], p["proportion"]) for p in after]
    r.check("split.panes_same", same, after)
    found = r.call("ui.find", id="panel.corner.LeftPane.BottomLeft")
    lower = after[-1]["bounds"]
    box = (found or {}).get("bounds") or {}
    r.check("split.button_on_last_pane", lower["y"] <= box.get("y", -1) <= lower["y"] + lower["h"], {"button": box, "lastPane": lower})
    click_button(r, "panel.corner.LeftPane.BottomLeft")
    final = r.call("split.dump")["areas"]["Left"]["panes"]
    r.check("split.released_same", [p["proportion"] for p in before] == [p["proportion"] for p in final], final)
    out["split"] = {"before": before, "after": after, "final": final}


def view_fingerprint(r):
    return {"%s:%s" % (c["type"], c["id"]): c["hash"] for c in r.call("panel.corner.views")["controls"] if c["id"]}


def preserve(r, out):
    """스파이크 지표: 클릭 전후 뷰 인스턴스·활성 문서·핀·탭·분할 비율."""
    ensure_default(r)
    r.call("docs.open", kind="grid", count=2)
    time.sleep(0.5)
    before = {"views": view_fingerprint(r), "docs": r.call("docs.list"), "tabsLeft": r.call("panel.tabs", id=L), "tabsBottom": r.call("panel.tabs", id=B),
              "panels": r.call("layout.get").get("Panels")}
    r.check("preserve.button_click", click_button(r, "panel.corner.BottomPane.BottomLeft"))
    after = {"views": view_fingerprint(r), "docs": r.call("docs.list"), "tabsLeft": r.call("panel.tabs", id=L), "tabsBottom": r.call("panel.tabs", id=B),
             "panels": r.call("layout.get").get("Panels")}
    changed = sorted(k for k in before["views"] if k in after["views"] and before["views"][k] != after["views"][k])
    gone = sorted(k for k in before["views"] if k not in after["views"])
    docs_before = [(d["id"], d["active"]) for d in before["docs"]]
    docs_after = [(d["id"], d["active"]) for d in after["docs"]]
    doc_keys = [k for k in before["views"] if k.split(":")[1] == "Documents" or k.split(":")[1].startswith("Grid")]
    r.check("preserve.document_views_same", not [k for k in doc_keys if k in changed or k in gone], {"changed": changed, "gone": gone})
    r.check("preserve.active_same", docs_before == docs_after, docs_after)
    r.check("preserve.tabs_same", before["tabsLeft"] == after["tabsLeft"] and before["tabsBottom"] == after["tabsBottom"])
    r.check("preserve.left_proportion_same", before["panels"]["ExplorerProportion"] == after["panels"]["ExplorerProportion"])
    r.check("preserve.bottom_proportion_same", before["panels"]["LogProportion"] == after["panels"]["LogProportion"])
    r.call("panel.pin", id=L, on="true")
    time.sleep(0.3)
    r.check("preserve.pin_on_after_move", (r.call("panel.list") or [{}]) and any(p["id"] == L and p["pinned"] for p in r.call("panel.list")))
    r.call("panel.pin", id=L, on="false")
    time.sleep(0.3)
    r.check("preserve.pin_off_after_move", any(p["id"] == L and not p["pinned"] for p in r.call("panel.list")))
    r.check("preserve.left_share_after_pin", r.call("layout.get")["Panels"]["ExplorerProportion"] == before["panels"]["ExplorerProportion"])
    out["preserve"] = {"view_changed": changed, "view_gone": gone, "after_tree": r.call("panel.corner.state")["tree"]}
    ensure_default(r)


def persist(r, out):
    """AC-3-06 앞 단계: 저장 표현에 cornerOwners 가 실린다."""
    ensure_default(r)
    click_button(r, "panel.corner.BottomPane.BottomLeft")
    got = r.call("layout.get")["Panels"]
    r.check("persist.corner_owners_saved", got.get("cornerOwners") == {"BottomLeft": B}, got.get("cornerOwners"))
    out["persist"] = got


def persist_after(r, out, expect):
    got = r.call("layout.get")["Panels"]
    r.check("persist_after.corner_owners", got.get("cornerOwners") == expect, got.get("cornerOwners"))
    state = r.call("panel.corner.state")
    r.check("persist_after.tree_follows", (state["owners"] or None) == expect, state["owners"])
    out["persist_after"] = {"panels": got, "state": state}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="rules,default,roundtrip,edges,preserve,split,persist")
    ap.add_argument("--expect-owners", default="null")
    args = ap.parse_args()
    if os.path.abspath(args.out).replace("\\", "/").find("/logs/ui28/") >= 0:
        sys.exit("logs/ui28 에는 쓰지 않는다(결과 파일은 otest 가 만든다)")
    if not os.environ.get("PD_REMOTE_INFO"):
        sys.exit("PD_REMOTE_INFO 필수(사용자 인스턴스에 붙지 않게)")

    r = Remote()
    if r.call("status") is None:
        return 2
    out = {}
    only = args.only.split(",")
    steps = {"rules": rules, "default": default_buttons, "roundtrip": roundtrip, "edges": edges, "preserve": preserve, "persist": persist, "split": split_area}
    for name in only:
        if name == "persist-after":
            persist_after(r, out, json.loads(args.expect_owners))
        else:
            steps[name](r, out)

    out["checks"] = r.checks
    out["calls"] = [c for c in r.calls if not c["ok"]]
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    failed = [c["name"] for c in r.checks if not c["pass"]]
    print("검사 %d건, 거짓 %d건 %s" % (len(r.checks), len(failed), failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

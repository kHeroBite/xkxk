#!/usr/bin/env python3
# retest_mdi_drag 의 추가 측정 — 실제 포인터 크기 조절(테두리 눌러 끌기)과 캔버스 밖 부분의 잘림을 보는 화면 캡처
"""
retest_mdi_drag.py 가 불러 쓴다. 단독 실행하지 않는다.
캡처는 앱 자체 렌더(api/screenshot/download)라 전경·커서와 무관하다. 잘림 판정은 사람이 PNG 를 본다 — 이 파일은 자리와 파일만 남긴다.
"""
import json
import os
import time

import mdi_drag_app as app
import pdctl

SETTLE_S = 0.6


def _place(run, doc, x=100, y=100, w=640, h=420):
    run.r.call("docs.activate", doc=doc)
    run.r.call("placement.markPlaced", doc=doc, x=x, y=y, w=w, h=h)
    time.sleep(SETTLE_S)


def _edge_drag(run, doc, edge, dx, dy):
    """테두리 손잡이를 눌러 끈다. 손잡이는 1px 바깥 테두리 안쪽에 있어 누름점을 3px 안으로 둔다(모서리 손잡이를 피해 변의 가운데 근처)."""
    s = run.state()
    a, b = s["area"], next(d for d in s["docs"] if d["id"] == doc)["bounds"]
    ox, oy = app.client_origin(run.hwnd)
    points = {"right": (b["x"] + b["w"] - 3, b["y"] + 200), "left": (b["x"] + 3, b["y"] + 200),
              "bottom": (b["x"] + 200, b["y"] + b["h"] - 3), "top": (b["x"] + 200, b["y"] + 3)}
    px, py = points[edge]
    sx, sy = a["x"] + px - ox, a["y"] + py - oy
    run.gate("크기 조절 전")
    app.post_drag(run.hwnd, sx, sy, sx + dx, sy + dy)
    time.sleep(SETTLE_S)
    run.gate("크기 조절 뒤")
    return run.bounds(doc)


def measure_narrow(run, doc, raw, new):
    """좁은 창(최소 폭)에서 왼쪽·오른쪽 끝까지 민 뒤 다시 잡을 수 있는가."""
    run.r.call("placement.markPlaced", doc=doc, x=200, y=100, w=120, h=200)
    time.sleep(SETTLE_S)
    run.r.call("docs.resize", doc=doc, edge="right", d=-600, steps=4)
    time.sleep(SETTLE_S)
    narrow = run.doc(doc)
    rows = {"start": narrow["bounds"], "grab": narrow.get("grab")}
    for name, dx in (("left", -3000), ("right", 3000)):
        run.drag(doc, dx, 0)
        limit = run.bounds(doc)
        again = run.drag(doc, 100 if name == "left" else -100, 0)
        rows[name] = {"limit": limit, "regrab_point": again, "regrabbed": run.bounds(doc) != limit}
        if new:
            run.check("narrow_regrab_" + name, rows[name]["regrabbed"], limit)
    raw["narrow"] = rows


def measure_resize(run, doc, raw, new):
    """크기 조절 경계(관리자 경로 — 실제 끌기와 같은 함수이고 뒤따르는 제한이 없다)."""
    area, rows = run.state()["area"], {}
    # d 는 좌표 증가 방향이 + 다(왼쪽·위 가장자리를 밖으로 밀려면 음수). 자리를 매번 되돌려 가장자리별로 잰다
    for edge, d in (("top", -3000), ("left", -3000), ("right", 3000), ("bottom", 3000), ("top_in", 3000), ("left_in", 3000)):
        _place(run, doc)
        rows[edge] = (run.r.call("docs.resize", doc=doc, edge=edge.split("_")[0], d=d, steps=4) or {}).get("final")
    raw["resize"] = rows
    print("  크기 조절:", rows)
    if new:
        run.check("resize_top_stops_at_zero", rows["top"]["y"] == 0 and rows["top"]["y"] + rows["top"]["h"] == 520, rows["top"])
        run.check("resize_top_in_title_visible", rows["top_in"]["y"] <= area["dipH"], rows["top_in"])
    _place(run, doc)


def measure_real_resize(run, doc, raw, new):
    """요청 경계를 실제 포인터 경로로 확인한다. 위쪽은 0 에서 멈추고, 끌지 않은 쪽 가장자리는 그대로여야 한다."""
    rows = {}
    for edge, dx, dy in (("right", 150, 0), ("bottom", 0, 150), ("left", -300, 0), ("top", 0, -400), ("top_in", 0, 900)):
        _place(run, doc)
        rows[edge] = _edge_drag(run, doc, edge.split("_")[0], dx, dy)
    raw["real_resize"] = rows
    print("  실제 크기 조절:", rows)
    if not new:
        return
    area = raw["area"]
    r = rows
    run.check("real_resize_right", r["right"]["w"] > 640 and r["right"]["x"] == 100, r["right"])
    run.check("real_resize_bottom", r["bottom"]["h"] > 420 and r["bottom"]["y"] == 100, r["bottom"])
    run.check("real_resize_left_past_canvas", r["left"]["x"] < 0 and r["left"]["x"] + r["left"]["w"] == 740, r["left"])
    run.check("real_resize_top_stops", r["top"]["y"] == 0 and r["top"]["h"] == 520, r["top"])
    run.check("real_resize_top_in_keeps_title", r["top_in"]["y"] <= area["dipH"] - raw["grab"]["titleBottom"], r["top_in"])


def measure_redock_left(run, doc, raw, new):
    """요건 4: SDI 가 캔버스 왼쪽에 60% 걸친 채 재결합하면 자리를 지키고, 메인 크기가 바뀌어도 그대로다(기존 retest_redock_policy 는 오른쪽만 본다)."""
    r = run.r
    r.call("placement.force", on=True)
    r.call("docs.sdi", doc=doc, on=True)
    time.sleep(SETTLE_S)
    area = run.state()["area"]
    frame = run.doc(doc)["sdiFrame"]
    target_x, target_y = area["x"] - round(frame["w"] * 0.4), area["y"] + 60
    for _ in range(3):  # 창 사각형과 보이는 프레임의 여백은 앱만 안다 — 재서 맞춘다
        row = next(s for s in r.call("window.snapshot")["sdi"] if s.get("doc") == doc)
        r.call("sdi.setBounds", doc=doc, x=row["x"] + target_x - frame["x"], y=row["y"] + target_y - frame["y"])
        time.sleep(0.3)
        frame = run.doc(doc)["sdiFrame"]
    r.call("docs.redock", doc=doc)
    time.sleep(SETTLE_S)
    placed = run.doc(doc)
    size = r.call("window.snapshot")["main"]
    run.resize_main(w=size["clientPxW"] + 40, h=size["clientPxH"])
    time.sleep(1.2)
    run.resize_main(w=size["clientPxW"], h=size["clientPxH"])
    time.sleep(1.2)
    held = run.doc(doc)
    r.call("placement.force", on=False)
    raw["redock_left_60"] = {"frame": frame, "screen": placed["screen"], "bounds": placed["bounds"], "after_main_resize": held["bounds"]}
    print("  왼쪽 60%% 재결합: frame=%s screen=%s 뒤=%s" % (frame, placed["screen"], held["bounds"]))
    if new:
        scr = placed.get("screen")  # Snap 켬 등으로 재결합 직후 화면 좌표가 없으면(None) 크래시하지 않고 FAIL 로 남긴다
        run.check("redock_left_60_keeps", scr is not None and abs(scr["x"] - frame["x"]) <= 2 and abs(scr["y"] - frame["y"]) <= 2, scr)
        run.check("redock_left_60_holds_on_resize", held["bounds"] == placed["bounds"], held["bounds"])


def capture_limits(run, doc, raw, work):
    """왼쪽·오른쪽·아래 한계 자리 캡처. 캔버스 밖 부분이 옆 패널 위에 그려지면 안 된다."""
    shots = {}
    for name, dx, dy in (("left", -3000, 0), ("right", 3000, 0), ("down", 0, 3000)):
        _place(run, doc, x=200, y=100)
        run.drag(doc, dx, dy)
        data = pdctl.curl(run.r.info, "api/screenshot/download", json.dumps({}), out_binary=True)
        path = os.path.join(work, "limit_%s.png" % name)
        if data.startswith(b"\x89PNG"):
            with open(path, "wb") as f:
                f.write(data)
            shots[name] = {"file": path, "bounds": run.bounds(doc)}
        else:
            shots[name] = {"error": data[:200].decode("utf-8", "replace")}
    raw["shots"] = shots


def effective_min_grab(run, default=50):
    """MDI 잡을 곳 현재값(mdiMinGrabDip) — MDI 문서 grab 에는 없어 SDI 문서 grab.minGrab(MdiDragLimit.CurrentMinGrab 와 같은 값)에서 읽는다. 못 읽으면 기본 50."""
    new = None
    try:
        ids = [d["id"] for d in run.r.call("docs.list")]
        run.r.call("docs.open", kind="grid", allowDuplicate=True)
        time.sleep(0.4)
        new = next(d["id"] for d in run.r.call("docs.list") if d["id"] not in ids)
        run.r.call("docs.sdi", doc=new, on=True)
        time.sleep(0.8)
        v = ((next(d for d in run.state()["docs"] if d["id"] == new)).get("grab") or {}).get("minGrab")
        return v if isinstance(v, (int, float)) else default
    except (StopIteration, KeyError, TypeError):
        return default
    finally:
        if new:
            run.r.call("docs.close", doc=new)
            time.sleep(0.4)

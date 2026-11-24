# ui28_web.py 보조 모듈 — 최대화(max)·끌기 중 모서리(sqcorner)·그리드 셀 현재·포커스 표시(cell) 헤드리스 캡처 단계(눈으로 보는 판정용, 시간 측정 없음, OS 실입력 없음)
"""ui28_web.py --only max,sqcorner,cell 로 부른다(FNS 를 ui28_web.main 이 합친다). 모두 Playwright 페이지 안 포인터·원격 명령만 쓴다.
- max: 그리드 문서를 열고 docs.state state=maximized 로 최대화·캡처, state=normal 로 해제·캡처(해제까지 되돌린다).
- sqcorner: 끌기 중 창 모서리 모양. 옵션 MdiMoveLightSquareCorners 를 원격으로 바꾸는 명령이 없어(소스 확인) 현재(기본 꺼짐) 상태에서 끄는 도중 모서리를 캡처하고, 옵션 켬 상태는 '원격 설정 불가'로 적는다.
- cell: 그리드 셀(3행 2열)을 grid.clickRow 로 클릭하고 현재 셀·포커스 표시를 캡처한다.
각 단계는 {captures:[경로...], ...원격 응답 요약} 을 돌려준다. 판정은 캡처를 눈으로 본다."""
import time

from PIL import Image


def _open_grid(w):
    w.res("docs.open", kind="grid")
    time.sleep(1.8)


def _doc(w):
    s = w.res("placement.state") or {}
    d = next((x for x in s.get("docs", [])), None)
    return s, d


def measure_max(w):
    out = {"captures": []}
    _open_grid(w)
    s, d = _doc(w)
    out["doc_before"] = d and d.get("bounds")
    w.shot("r5vis_max_before"); out["captures"].append(w.shots + "/r5vis_max_before.png")
    r = w.call("docs.state", state="maximized")
    out["docs_state_maximized"] = {"ok": r.get("ok"), "error": r.get("error"), "state": ((r.get("result") or {}).get("state"))}
    time.sleep(1.2)
    w.shot("r5vis_max_maximized"); out["captures"].append(w.shots + "/r5vis_max_maximized.png")
    s2, d2 = _doc(w)
    out["doc_maximized"] = d2 and d2.get("bounds"); out["area"] = s2.get("area")
    r = w.call("docs.state", state="normal")
    out["docs_state_normal"] = {"ok": r.get("ok"), "error": r.get("error"), "state": ((r.get("result") or {}).get("state"))}
    time.sleep(1.0)
    w.shot("r5vis_max_restored"); out["captures"].append(w.shots + "/r5vis_max_restored.png")
    s3, d3 = _doc(w)
    out["doc_restored"] = d3 and d3.get("bounds")
    return out


def measure_sqcorner(w):
    out = {"captures": [], "option_on": "원격 설정 불가 — MdiMoveLightSquareCorners 를 바꾸는 원격 명령·설정 화면 id 없음(소스 확인)"}
    _open_grid(w)
    s, d = _doc(w)
    a, b, g = s["area"], d["bounds"], d["grab"]
    out["moveLight"] = w.res("chrome.moveLight")
    x0 = a["x"] + b["x"] + b["w"] / 2
    y0 = a["y"] + b["y"] + g["titleBottom"] / 2
    m = w.page.mouse
    m.move(x0, y0); m.down()
    for i in range(1, 9):
        m.move(x0 - 18 * i, y0 + 10 * i); time.sleep(0.04)
    time.sleep(0.15)   # 끄는 중(QuietMs 300ms 안)에 캡처
    cur = (w.res("placement.state") or {}).get("docs", [{}])[0].get("bounds") or b
    shot = w.shot("r5vis_sqcorner_during_drag"); out["captures"].append(w.shots + "/r5vis_sqcorner_during_drag.png")
    x, y = int(a["x"] + cur["x"]), int(a["y"] + cur["y"])
    # 창 왼쪽 위·오른쪽 위 모서리를 4배로 키워 눈으로 보기 쉽게 저장한다
    for nm, (cx, cy) in {"tl": (x, y), "tr": (x + int(cur["w"]) - 28, y)}.items():
        box = (max(cx - 4, 0), max(cy - 4, 0), cx + 28, cy + 28)
        shot.crop(box).resize((128, 128), Image.NEAREST).save(w.shots + "/r5vis_sqcorner_corner_%s.png" % nm)
        out["captures"].append(w.shots + "/r5vis_sqcorner_corner_%s.png" % nm)
    m.up(); time.sleep(0.9)
    w.shot("r5vis_sqcorner_after_quiet"); out["captures"].append(w.shots + "/r5vis_sqcorner_after_quiet.png")
    return out


def measure_cell(w):
    out = {"captures": []}
    _open_grid(w)
    w.shot("r5vis_cell_before"); out["captures"].append(w.shots + "/r5vis_cell_before.png")
    r = w.call("grid.clickRow", id="grid.dgMain", doc="Grid-1", index=2, col=1)
    out["grid_clickRow"] = {"ok": r.get("ok"), "error": r.get("error"), "result": str(r.get("result"))[:200]}
    time.sleep(0.8)
    w.shot("r5vis_cell_after_click"); out["captures"].append(w.shots + "/r5vis_cell_after_click.png")
    for nm in ("CurrencyVisual", "FocusVisual"):
        f = w.call("ui.find", id=nm)
        out["find_" + nm] = {"ok": f.get("ok"), "error": f.get("error"), "bounds": (f.get("result") or {}).get("bounds")}
    return out


def measure_sqopt(w):
    """사각 모서리 옵션(MdiMoveLightSquareCorners)을 설정 화면에서 켤 수 있는지 확인한다(값은 바꾸지 않는다): 설정 화면을 열어 settings.list 의 entries(화면 항목)·unmapped(화면 항목 없는 설정)에서 옵션 위치를 찾는다."""
    out = {"captures": []}
    w.res("sys.open", view="Settings"); time.sleep(0.8)
    w.res("settings.window", category="Window"); time.sleep(0.5)
    lst = w.res("settings.list") or {}
    ent = [e for e in lst.get("entries", []) if "quare" in str(e)]
    out["entries_with_square"] = ent
    out["unmapped_move"] = [x for x in lst.get("unmapped", []) if "Move" in x]
    out["uiSettings_square"] = [x for x in lst.get("uiSettings", []) if "quare" in x]
    w.shot("r5vis_sqopt_settings_window"); out["captures"].append(w.shots + "/r5vis_sqopt_settings_window.png")
    out["esc"] = (w.res("settings.esc") or {}).get("viewOpen")
    return out


FNS = {"max": measure_max, "sqcorner": measure_sqcorner, "cell": measure_cell, "sqopt": measure_sqopt}

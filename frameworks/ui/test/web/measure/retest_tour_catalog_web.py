#!/usr/bin/env python3
# 도움말 카탈로그 트리밍 웹 측정 — 진단 번들은 원격 명령으로, 제품 번들은 실제 키(Chromium headless)와 화면으로 내장 카탈로그 로드·빈 카탈로그·투어 표시를 값으로 남긴다
"""
사용: python3 -B retest_tour_catalog_web.py --diag <진단 wwwroot> --product <제품 wwwroot> --out <결과.json> --shots <폴더> [--port 5831]
규칙(결과 보기 전에 고정):
  진단: 내장 카탈로그 오류 0·단계 수 일치, PC 폭 메인 투어 진행, 폰 폭 Nav 단계, 없는 카탈로그에서 레이어 표시, reset 복귀.
  제품: window.pdAutomation 없음, 번들에 카탈로그 내용 있음·진단 명령 문자열 없음, 새 기동 뒤 실제 F1 로 화면 변화, 시스템→도움말 클릭 뒤 화면 변화, 다음 버튼 클릭 뒤 변화, Esc 뒤 원래 화면 복귀.
  F1 과 메뉴 경로는 따로 판정한다. 메뉴가 통과해도 F1 통과가 아니다. 좌표는 같은 폭의 진단 번들에서 읽는다.
종료 코드: 0=전부 참, 1=거짓 있음. headless 브라우저라 사용자 화면에 창이 뜨지 않는다.
"""
import argparse
import http.server
import json
import os
import socketserver
import sys
import threading
import time
from functools import partial

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "tools"))
from ui28_web706 import ARGS, W  # noqa: E402

CATALOG = os.path.join(HERE, "..", "..", "..", "src", "Pd.Poc.Shell", "Tour", "tour_catalog.json")
SIZES = {"pc": (1400, 900), "phone": (390, 844)}


def serve(directory, port):
    http.server.SimpleHTTPRequestHandler.log_message = lambda *x: None
    socketserver.TCPServer.allow_reuse_address = True
    server = socketserver.TCPServer(("127.0.0.1", port), partial(http.server.SimpleHTTPRequestHandler, directory=directory))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def walk(w):
    w.call("tour.start")
    time.sleep(1.0)
    steps = []
    for _ in range(40):
        state = (w.res("tour.state") or [{}])[0]
        if state.get("onShortcutLayer") or not state:
            return steps, state
        steps.append((state.get("stepId"), state.get("index"), state.get("count")))
        w.call("tour.next")
        time.sleep(0.5)
    return steps, {}


def center(w, control_id):
    bounds = (w.find(control_id) or {}).get("bounds")
    return (bounds["x"] + bounds["w"] / 2, bounds["y"] + bounds["h"] / 2) if bounds else None


def read_points(w, checks):
    """실제 마우스로 시스템→도움말을 눌러 투어를 열고 제품판에 쓸 좌표를 읽는다. 진단판의 양성 대조를 겸한다."""
    points = {"sys": center(w, "shell.mnuSys")}
    w.page.mouse.click(*points["sys"])
    time.sleep(1.0)
    points["help"] = center(w, "shell.mnuSysHelp")
    w.page.mouse.click(*points["help"])
    time.sleep(1.5)
    state = (w.res("tour.state") or [{}])[0]
    points["next"] = center(w, "tour.btnNext")
    checks["진단판 메뉴 클릭으로 투어 시작"] = (state.get("scope") == "MainWindow" and bool(points["next"]), {"state": state, "points": points})
    w.call("tour.end")
    time.sleep(0.5)
    return points


def measure_diag(p, port, bundle, shots, catalog, checks, errors):
    w = W(p, port, bundle, SIZES["pc"], shots, errors)
    w.page.mouse.click(700, 500)
    time.sleep(0.5)
    w.page.keyboard.press("F1")
    time.sleep(1.0)
    fresh = w.res("tour.state") or []
    checks["진단판 새 기동 뒤 실제 F1 시작"] = (len(fresh) == 1 and fresh[0].get("scope") == "MainWindow", fresh)
    w.call("tour.end")
    time.sleep(0.5)
    points = read_points(w, checks)
    info = w.res("tour.catalog.info") or {}
    checks["내장 카탈로그 로드(트리밍)"] = (info.get("errors") == [] and len(info.get("stepIds") or []) == len(catalog["steps"]) and info.get("shortcuts") == len(catalog["shortcuts"]), info)
    steps, last = walk(w)
    main_ids = [s["id"] for s in catalog["steps"] if s["scope"] == "MainWindow"]
    checks["PC 폭 메인 투어 진행"] = (len(steps) >= 5 and all(s[0] in main_ids for s in steps) and last.get("onShortcutLayer") is True, {"steps": steps, "last": last})
    rows = {n: bool((w.res("ui.find", id="tour.feature.menu." + n) or {}).get("effectivelyVisible")) for n in ("File", "Window", "View", "Sys")}
    tray = w.call("ui.find", id="tour.feature.tray")
    checks["메뉴 줄 생성, 트레이 줄 없음(웹)"] = (all(rows.values()) and not tray.get("ok"), {"rows": rows, "tray": tray.get("ok")})
    w.shot("diag_pc_layer")
    w.call("tour.end")
    time.sleep(0.5)
    empty = w.res("tour.catalog.load", path="/no/such/tour_catalog.json") or {}
    w.call("tour.start")
    time.sleep(1.0)
    state = (w.res("tour.state") or [{}])[0]
    checks["없는 카탈로그: 오류 기록 후 레이어 표시"] = (len(empty.get("errors") or []) >= 1 and state.get("onShortcutLayer") is True and bool((w.res("ui.find", id="tour.shortcuts") or {}).get("effectivelyVisible")), {"errors": empty.get("errors"), "state": state})
    w.shot("diag_pc_empty")
    w.call("tour.end")
    reset = w.res("tour.catalog.reset") or {}
    checks["내장 카탈로그 복귀"] = (reset.get("errors") == [] and len(reset.get("stepIds") or []) == len(catalog["steps"]), {"steps": len(reset.get("stepIds") or [])})
    w.b.close()
    w = W(p, port, bundle, SIZES["phone"], shots, errors)
    steps, last = walk(w)
    nav_ids = [s["id"] for s in catalog["steps"] if s["scope"] == "Nav"]
    checks["폰 폭 Nav 단계(트리밍)"] = (len(steps) >= 1 and all(s[0] in nav_ids for s in steps) and last.get("scope") == "Nav", {"steps": steps})
    w.shot("diag_phone_layer")
    w.call("tour.end")
    w.b.close()
    return points


def changed_ratio(a, b):
    """두 스크린샷 파일이 얼마나 다른지 대략 본다(바이트 비교가 아니라 크기 차이를 함께 쓴다)."""
    from PIL import Image, ImageChops
    with Image.open(a) as x, Image.open(b) as y:
        diff = ImageChops.difference(x.convert("RGB"), y.convert("RGB")).convert("L").point(lambda v: 255 if v > 8 else 0)
        hist = diff.histogram()
        return hist[255] / float(sum(hist))


def measure_product(p, port, shots, points, checks, errors):
    browser = p.chromium.launch(headless=True, args=ARGS)
    page = browser.new_page(viewport={"width": SIZES["pc"][0], "height": SIZES["pc"][1]})
    page.on("pageerror", lambda e: errors.append("product pageerror: " + str(e)[:300]))
    page.goto("http://127.0.0.1:%d/?fresh" % port, wait_until="load", timeout=90000)
    time.sleep(25)  # 제품판은 진단 신호가 없어 시간으로 기다린다
    has_auto = page.evaluate("() => typeof window.pdAutomation !== 'undefined'")
    checks["제품판에 자동화 없음"] = (not has_auto, has_auto)
    names = {k: os.path.join(shots, "product_%s.png" % k) for k in ("before", "f1", "tour", "next", "after")}
    page.mouse.click(700, 500)
    time.sleep(1.5)
    page.screenshot(path=names["before"])
    page.keyboard.press("F1")
    time.sleep(2.0)
    page.screenshot(path=names["f1"])
    f1 = changed_ratio(names["before"], names["f1"])
    checks["제품판 새 기동 뒤 실제 F1 로 투어 표시(화면 변화)"] = (f1 > 0.05, f1)
    page.keyboard.press("Escape")
    time.sleep(1.5)
    page.mouse.click(*points["sys"])
    time.sleep(1.0)
    page.mouse.click(*points["help"])
    time.sleep(2.0)
    page.screenshot(path=names["tour"])
    page.mouse.click(*points["next"])
    time.sleep(1.5)
    page.screenshot(path=names["next"])
    page.keyboard.press("Escape")
    time.sleep(1.5)
    page.screenshot(path=names["after"])
    tour = changed_ratio(names["before"], names["tour"])
    moved = changed_ratio(names["tour"], names["next"])
    back = changed_ratio(names["before"], names["after"])
    checks["제품판 시스템→도움말로 투어 표시(화면 변화)"] = (tour > 0.05, tour)
    checks["제품판 다음 단계로 이동(화면 변화)"] = (moved > 0.001, moved)
    checks["제품판 Esc 로 복귀"] = (back < 0.02, back)
    browser.close()


def scan_bundle(bundle, checks, label):
    import check_product_web as cp
    markers = {"catalog": b'"main.uiStyle"', "load": "tour.catalog.load".encode("utf-16le"), "remoteType": b"RemoteTourCatalogCommands"}
    counts = dict.fromkeys(markers, 0)
    for _, data, kind in cp.collect_blobs(bundle, "web", {}):
        if kind == "asm":
            for name, needle in markers.items():
                counts[name] += data.count(needle)
    return counts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--diag", required=True)
    ap.add_argument("--product", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5831)
    a = ap.parse_args()
    if "/logs/ui28/" in os.path.abspath(a.out).replace("\\", "/"):
        sys.exit("logs/ui28 쓰기 금지")
    os.makedirs(a.shots, exist_ok=True)
    with open(CATALOG, encoding="utf-8") as f:
        catalog = json.load(f)
    checks, errors = {}, []
    diag_scan = scan_bundle(a.diag, checks, "diag")
    product_scan = scan_bundle(a.product, checks, "product")
    checks["번들 정적: 진단판에 카탈로그·로드 명령 있음"] = (diag_scan["catalog"] >= 1 and diag_scan["load"] >= 1, diag_scan)
    checks["번들 정적: 제품판에 카탈로그 있음, 로드 명령 없음"] = (product_scan["catalog"] >= 1 and product_scan["load"] == 0 and product_scan["remoteType"] == 0, product_scan)
    diag_server = serve(a.diag, a.port)
    product_server = serve(a.product, a.port + 1)
    try:
        with sync_playwright() as p:
            points = measure_diag(p, a.port, a.diag, a.shots, catalog, checks, errors)
            measure_product(p, a.port + 1, a.shots, points, checks, errors)
    finally:
        diag_server.shutdown()
        product_server.shutdown()
    results = {k: {"ok": bool(v[0]), "value": v[1]} for k, v in checks.items()}
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"results": results, "errors": errors}, f, ensure_ascii=False, indent=1)
    for name, row in results.items():
        print("%s %s %s" % ("PASS" if row["ok"] else "FAIL", name, json.dumps(row["value"], ensure_ascii=False)[:260]))
    print("콘솔 오류 %d건: %s" % (len(errors), errors[:3]))
    return 0 if all(r["ok"] for r in results.values()) else 1


if __name__ == "__main__":
    sys.exit(main())

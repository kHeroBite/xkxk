# otest-w-14 D1 웹 실창 측정 — 자기 프로필 headed Chrome 에 web_diag 를 띄우고 safe_click·safe_key 실입력으로 스피너 숨김·패널 끌기·우클릭 메뉴·F1 을 잰다
# 사용: python3 -B d1_web.py <출력폴더> <서버 URL> <CDP 포트>
# 판정 규칙(측정 전 고정):
#   real_chrome_headed: 헤드리스 아님(PDCHROME_HEADLESS 없음) + 문서 visibilityState=visible + 실입력 이벤트가 isTrusted=true 로 페이지에 도착
#   own_profile_only: 자기 임시 프로필(--user-data-dir dc_<tag>)로 띄운 Chrome, 포트 리스너 PID == 자기 Chrome PID
#   real_mouse_leave_hides_spinner: busy 중 실커서를 창 안 → 창 밖으로 옮기면 followerVisible 이 true → false
#   real_drag_panel: LeftPane 제목 줄을 실끌기(SendInput)로 다른 패널 가운데(가운데 도킹 표적 자리)에 놓으면 패널 side 또는 bounds 가 바뀐다
#     (w-15 변경: 놓는 자리만 '문서 영역 오른쪽' → '다른 패널 가운데'. 문서 영역 오른쪽은 도킹 표적이 없는 자리로 추정되고, 문서 영역에 놓인 도구 패널은 설계상 제자리다(PanelDropService.CanDrop/Begin).
#      참거짓 조건(side 또는 bounds 변화)은 그대로다. 끌기 도중 실이벤트 수·중간 캡처는 판정에 쓰지 않는 원인 분리용 기록이다)
#   real_rclick_menu: 탐색기 패널 제목 줄 실우클릭(웹 메인 제목 줄에는 ContextMenu 호스트가 없다) → ui.contextMenu 호스트 목록에 isOpen=true 인 Pd 메뉴가 1개 이상, contextmenu 이벤트가 isTrusted 이고 기본 동작 막힘(defaultPrevented=true, 브라우저 메뉴 안 뜸)
#   real_f1: 실 F1 키 → tour.state sessions 1개 이상, Esc 로 종료
#   console_error_zero: Chrome 콘솔 오류 0건(console.error·예외·렌더러 충돌. pdchrome 의 탐색 기록 NAVIGATED·CONTEXTS_CLEARED 는 오류가 아니라 제외)
import json, os, sys, time
UI = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
ROOTD = os.path.abspath(os.path.join(UI, "..", ".."))
sys.path.insert(0, os.path.join(UI, "test/web/measure"))
sys.path.insert(0, os.path.join(UI, "test/desktop/measure"))
import dc_common as dc  # noqa: E402
import retest_real_input as ri  # noqa: E402
away = ri.away
SAFE_CLICK, SAFE_KEY = ri.SAFE_CLICK, ri.SAFE_KEY

out, url, cport = sys.argv[1], sys.argv[2], int(sys.argv[3])
def real_errors(raw):
    """pdchrome errors 는 페이지 탐색 기록(NAVIGATED·CONTEXTS_CLEARED)을 함께 담는다. 콘솔 오류(console.error·예외)와 렌더러 충돌만 센다."""
    return [e for e in (raw or []) if not str(e).startswith(("NAVIGATED ", "CONTEXTS_CLEARED "))]
def open_menus(hosts):
    return [h for h in (hosts or []) if h.get("isOpen") is True]
DRY = "--dry-run" in sys.argv[4:]  # 입력 주입 없이 포트·창 기하·원격 상태 읽기만 한다(헤드리스, 다른 축 실입력과 겹치지 않게)
os.makedirs(out, exist_ok=True)
if DRY:
    c = dc.Chrome(cport, "w15d1dry_" + time.strftime("%H%M%S"), headless=True)  # w-15: 매 실행 새 프로필
    pid = c.hello.get("pid"); rep = {"chrome": c.hello, "listener": dc.port_listener(cport)}
    try:
        rep["nav"] = c.q(op="nav", url=url, timeout=120000, needAuto=True); c.q(op="sleep", ms=3000)
        rep["geom"] = c.q(op="eval", expr="({sx:screenX,sy:screenY,ow:outerWidth,oh:outerHeight,iw:innerWidth,ih:innerHeight,dpr:devicePixelRatio,vis:document.visibilityState})").get("value")
        for aid in ("shell.titleDragArea", "shell.dock"):
            n = c.call("ui.find", id=aid) or {}; rep[aid] = {"bounds": n.get("bounds"), "visible": n.get("effectivelyVisible")}
        rep["LeftPane"] = next((p for p in c.call("panel.list") or [] if p.get("id") == "LeftPane"), None)
        rep["panels"] = [{"id": p.get("id"), "side": p.get("side"), "bounds": p.get("bounds")} for p in (c.call("panel.list") or [])]
        rep["busy.state"] = c.call("busy.state")
        rep["contextMenu.hosts"] = (c.call("ui.contextMenu") or {}).get("hosts")
        rep["tour.state"] = c.call("tour.state")
        rep["errors_raw"] = c.q(op="errors").get("value")
        rep["errors"] = real_errors(rep["errors_raw"])
        rep["guard"] = "check 미호출(드라이런은 입력 없음)"
        need = [rep["listener"] == [pid], bool(rep["geom"]), bool(rep["shell.titleDragArea"]["bounds"]), bool(rep["LeftPane"]), isinstance(rep["busy.state"], dict),
                bool(rep["contextMenu.hosts"]), rep["tour.state"] is not None]
        rep["checks"] = need
        rc = 0 if all(need) else 1
    finally:
        c.close()
    json.dump(rep, open(os.path.join(out, "D1_dryrun.json"), "w"), ensure_ascii=False, indent=1, default=str)
    print("DRYRUN rc=%d checks=%s" % (rc, need), flush=True)
    sys.exit(rc)
rows, caps, log = {}, [], []
def P(*a):
    s = time.strftime("%H:%M:%S") + " " + " ".join(str(x) for x in a); print(s, flush=True); log.append(s)
def add(name, value, ok):
    rows[name] = {"name": name, "value": bool(ok), "pass": bool(ok), "source": "d1_web.py: " + json.dumps(value, ensure_ascii=False, default=str)[:1500]}
    P("PASS" if ok else "FAIL", name, json.dumps(value, ensure_ascii=False, default=str)[:300])

def guard():
    away.check()
    l = dc.load1()
    if l > 50: raise SystemExit("부하 %.1f > 50 — 중단" % l)
    return l

PROF = "w15d1_" + time.strftime("%H%M%S")  # w-15: 매 실행 새 프로필(앞 실행에서 패널 배치가 바뀐 프로필을 이어 쓰지 않는다, team-lead 지시)
c = dc.Chrome(cport, PROF, headless=False)
pid = c.hello.get("pid")
P("chrome", c.hello)
def call(cmd, **a): return c.call(cmd, **a)
def ev(expr): return c.q(op="eval", expr=expr).get("value")
def shot(tag):
    p = os.path.join(out, "d1_%s.png" % tag); c.q(op="shot", file=dc.win(p)); caps.append(p); return p
def click(x, y, button="Left", drag=None, no_click=False, expected=None):
    guard()
    exp = expected if (no_click and expected is not None) else pid
    args = ["-ScreenX", int(x), "-ScreenY", int(y), "-ExpectedProcessId", exp, "-Button", button]
    if drag: args += ["-DragToX", int(drag[0]), "-DragToY", int(drag[1])]
    if no_click: args.append("-NoClick")
    end = drag or (x, y)
    with away.injection("d1_" + ("move" if no_click else ("drag" if drag else "click")), int(end[0]), int(end[1])):
        d = ri.run_ps_file(SAFE_CLICK, *args)
    if d.returncode != 0: raise ri.InputRefused("safe_click rc=%s %s" % (d.returncode, (d.stderr or d.stdout)[-300:]))
    time.sleep(0.4)
def key(name):
    guard()
    d = ri.run_ps_file(SAFE_KEY, "-Key", name, "-ExpectedProcessId", pid, "-ForbiddenProcessId", 4)
    if d.returncode != 0: raise ri.InputRefused("safe_key rc=%s %s" % (d.returncode, (d.stderr or d.stdout)[-300:]))
    time.sleep(0.5)

rec = {"started_at": ri.now_iso(), "load_start": dc.load1(), "chrome": c.hello, "url": url}
try:
    guard()
    r = c.q(op="nav", url=url, timeout=120000, needAuto=True); P("nav", r)
    c.q(op="sleep", ms=3000)
    ev("window.__d1=[];['pointerdown','pointermove','pointerup','contextmenu','keydown'].forEach(t=>addEventListener(t,e=>{if(window.__d1.length<400)window.__d1.push({t,trusted:e.isTrusted,prevented:e.defaultPrevented,key:e.key||null})},{capture:false}));true")
    g = ev("({sx:screenX,sy:screenY,ow:outerWidth,oh:outerHeight,iw:innerWidth,ih:innerHeight,dpr:devicePixelRatio,vis:document.visibilityState})")
    bx = (g["ow"] - g["iw"]) // 2
    ox, oy = g["sx"] + bx, g["sy"] + g["oh"] - g["ih"] - bx
    dpr = g["dpr"] or 1
    rec["geom"] = dict(g, ox=ox, oy=oy); P("geom", rec["geom"])
    def scr(x, y): return ox + x * dpr, oy + y * dpr
    def bnd(aid):
        n = call("ui.find", id=aid) or {}
        b = n.get("bounds") or {}
        return b if n.get("effectivelyVisible") is not False and b.get("w") else None
    # 전경 확보: 상태 표시 없는 빈 문서 영역을 실클릭한다(앱 상태를 바꾸지 않는 자리)
    tb = bnd("shell.titleDragArea") or {"x": 600, "y": 4, "w": 200, "h": 24}
    tx, ty = scr(tb["x"] + tb["w"] / 2, tb["y"] + tb["h"] / 2)
    click(tx, ty)
    trusted0 = ev("window.__d1.filter(e=>e.t==='pointerdown')")
    add("real_chrome_headed", {"headless_env": bool(os.environ.get("PDCHROME_HEADLESS")), "visibility": g["vis"], "pointerdown": trusted0, "pid": pid},
        (not os.environ.get("PDCHROME_HEADLESS")) and g["vis"] == "visible" and any(e.get("trusted") for e in (trusted0 or [])))
    lst = dc.port_listener(cport)
    add("own_profile_only", {"profile": r"C:\Users\rioky\AppData\Local\Temp\pdwebperf\dc_" + PROF, "listener": lst, "pid": pid}, pid in lst and len(lst) >= 1 and all(p == pid for p in lst))
    # 1) 스피너: busy 중 창 안 → 창 밖
    def spinner():
        d = bnd("shell.dock") or {"x": 400, "y": 200, "w": 600, "h": 400}
        ix, iy = scr(d["x"] + d["w"] / 2, d["y"] + d["h"] / 2)
        click(ix, iy, no_click=True)
        st = call("busy.begin", scope="main", reason="d1") or {}
        time.sleep(1.0)
        click(ix + 12, iy + 12, no_click=True); time.sleep(0.4)
        s_in = call("busy.state") or {}
        shot("spinner_inside")
        # 창 밖 점: Chrome 창 오른쪽 빈 화면(모니터 1920x1200 안, 가장자리 100px 밖)
        wx1 = g["sx"] + g["ow"]
        outp = (min(wx1 + 150, 1800), iy)
        owner = None  # 창 밖 점은 커서 이동만 하므로 소유자 검사를 건너뛴다(-ExpectedProcessId 0)
        click(outp[0], outp[1], no_click=True, expected=0)
        time.sleep(0.6)
        s_out = call("busy.state") or {}
        shot("spinner_outside")
        call("busy.end", token=st.get("token"))
        v = {"begin": st, "in": s_in, "out": s_out, "outside_at": outp, "owner": owner}
        return v, bool(s_in.get("followerVisible")) and s_out.get("followerVisible") is False
    v, ok = spinner(); add("real_mouse_leave_hides_spinner", v, ok)
    # 3) 실우클릭 메뉴 — 웹은 제목 줄(titleDragArea)에 ContextMenu 호스트가 없다(드라이런 ui.contextMenu 목록 실측). 호스트가 있는 탐색기 패널 제목 줄(PART_Grip)을 우클릭한다 (w-15: 끌기보다 먼저 한다 — 끌기 놓기가 패널 배치를 바꾸고 docs.resetLayout 은 MDI 창 크기·위치만 초기화해 패널 배치를 되돌리지 않는다)
    lpr = next((p for p in call("panel.list") or [] if p.get("id") == "LeftPane"), {}).get("bounds") or {}
    rx, ry = scr(lpr.get("x", 0) + lpr.get("w", 200) / 2, lpr.get("y", 51) + 10)
    ev("window.__d1=[]")
    click(rx, ry, button="Right"); time.sleep(0.6)
    opened = open_menus((call("ui.contextMenu") or {}).get("hosts"))
    evs = ev("window.__d1.filter(e=>e.t==='contextmenu')")
    shot("rclick_menu")
    key("Esc")
    add("real_rclick_menu", {"at": (rx, ry), "target": "LeftPane 제목 줄", "open_hosts": opened, "contextmenu_events": evs},
        bool(opened) and bool(evs) and all(e.get("trusted") and e.get("prevented") for e in evs))
    # 2) 패널 실끌기
    pl0 = call("panel.list") or []
    lp0 = next((p for p in pl0 if p.get("id") == "LeftPane"), {})
    b = lp0.get("bounds") or {}
    sx, sy = scr(b.get("x", 0) + b.get("w", 200) / 2, b.get("y", 51) + 10)
    # 놓는 자리: LeftPane 이 아닌 보이는 패널 가운데(Q3 CDP 끌기와 같은 방식 — 다른 패널 가운데). RightPane 우선
    others = [p for p in pl0 if p.get("id") != "LeftPane" and (p.get("bounds") or {}).get("w")]
    tgt = next((p for p in others if p.get("id") == "RightPane"), others[0] if others else None)
    if tgt:
        tb2 = tgt["bounds"]; ex, ey = scr(tb2["x"] + tb2["w"] / 2, tb2["y"] + tb2["h"] / 2)
    else:
        ex, ey = scr(1140 - 40, (56 + 655) / 2)
    ev("window.__d1=[]")
    import threading
    th_err = []
    def _drag():
        try: click(sx, sy, drag=(ex, ey))
        except Exception as e: th_err.append(e)  # noqa: BLE001
    th = threading.Thread(target=_drag); th.start()
    mid = None; t_end = time.time() + 30
    while th.is_alive() and time.time() < t_end:  # 끌기 도중: 실 pointermove 가 10개 넘으면 1회 캡처(입력 없음, CDP 읽기·캡처만)
        n = ev("window.__d1.filter(e=>e.t==='pointermove').length") or 0
        if mid is None and n >= 10:
            mid = {"moves_at_shot": n, "shot": shot("panel_dragging")}
        time.sleep(0.1)
    th.join(60)
    if th_err: raise th_err[0]
    time.sleep(1.0)
    evs_drag = ev("window.__d1.filter(e=>e.t!=='keydown')") or []
    cnt = {t: sum(1 for e in evs_drag if e.get("t") == t) for t in ("pointerdown", "pointermove", "pointerup")}
    cnt["all_trusted"] = all(e.get("trusted") for e in evs_drag)
    lp1 = next((p for p in call("panel.list") or [] if p.get("id") == "LeftPane"), {})
    shot("panel_dragged")
    add("real_drag_panel", {"before": {"side": lp0.get("side"), "bounds": b}, "after": {"side": lp1.get("side"), "bounds": lp1.get("bounds")}, "from": (sx, sy), "to": (ex, ey),
                            "target_panel": (tgt or {}).get("id"), "drag_events": cnt, "mid_drag": mid},
        bool(lp0) and (lp1.get("side") != lp0.get("side") or lp1.get("bounds") != b))
    call("docs.resetLayout")
    time.sleep(1.0)
    # 4) 실 F1
    click(tx, ty)  # 전경·포커스 확보
    key("F1"); time.sleep(0.8)
    ts = call("tour.state")
    sess = (ts.get("sessions") or []) if isinstance(ts, dict) else (ts or [])
    shot("f1_tour")
    key("Esc"); time.sleep(0.6)
    ts2 = call("tour.state")
    sess2 = (ts2.get("sessions") or []) if isinstance(ts2, dict) else (ts2 or [])
    keys = ev("window.__d1.filter(e=>e.t==='keydown')")
    add("real_f1", {"sessions": sess, "after_esc": sess2, "keydown": keys}, bool(sess))
    raw_errs = c.q(op="errors").get("value") or []
    errs = real_errors(raw_errs)
    add("console_error_zero", {"errors": errs[:20], "nav_events_excluded": [e for e in raw_errs if e not in errs][:20]}, len(errs) == 0)
except (away.UserReturned, away.GuardUnavailable) as e:
    rec["stopped"] = "guard: %r" % (e,); P("STOP", e)
except Exception as e:  # noqa: BLE001
    rec["stopped"] = repr(e); P("ERROR", repr(e))
finally:
    rec["finished_at"] = ri.now_iso(); rec["load_end"] = dc.load1()
    c.close()
    rec["chrome_pid"] = pid
    doc = {"measured_started_at": rec["started_at"], "measured_finished_at": rec["finished_at"], "measurements": list(rows.values()),
           "raw": rec, "log": log}
    json.dump(doc, open(os.path.join(out, "D1_measure.json"), "w"), ensure_ascii=False, indent=1, default=str)
    P("END", os.path.join(out, "D1_measure.json"))

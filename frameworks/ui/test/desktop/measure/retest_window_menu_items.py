# Mars 윈도우 메뉴 13항목 시험의 항목 정의 — 항목별 원격 호출·실행 전후 대조·재기동 유지 등록·되돌림 예약(실행기는 retest_window_menu.py)
import datetime

from retest_window_menu_focus import TEMPLATE_IDS, it_focus_apply, it_focus_leave, it_focus_persist, it_focus_pinned
from retest_window_menu_body import it_template_order, with_body  # 템플릿 단계별 활성 문서 본문 확인(lead 06:3x)
from retest_window_menu_magnet import ITEMS_Q8


class Pending(Exception):
    """원격 명령 미구현(셸 API 대기) — 판정 PENDING."""


class NotApplicable(Exception):
    """플랫폼이 그 명령을 못 함(원격 응답 status=Unavailable — 메뉴도 비활성) — 판정 N/A."""


def it_autosave(ctx):
    """
    §3.5 ①(리드 결정): 자동 저장을 끈 채 종료해도 데이터를 지우지 않는다(저장만 안 함).
    켜진 상태에서 현재 화면을 저장 → 끔 → 재기동 뒤 ⓐ 꺼짐 유지 ⓑ 설정(themeId) 보존 ⓒ 이전 저장 레이아웃을 불러오면 같은 문서.
    """
    v0 = ctx.call("window.autosave.get")
    ctx.restore.append(("window.autosave.set", {"on": v0}))
    ctx.call("window.autosave.set", on=True)
    ctx.call("docs.close", all=True)
    for t in ("AS-1", "AS-2"):
        ctx.call("docs.open", kind="grid", title=t, allowDuplicate=True)
    ctx.call("layout.save")
    v1 = ctx.call("window.autosave.set", on=False)
    theme = (ctx.call("settings.get") or {}).get("themeId")
    ctx.persist.append(("window.autosave.get", None, False))
    ctx.persist.append(("settings.get", None, ("field", "themeId", theme)))
    ctx.persist.append(("layout.load", None, ("titles", ["AS-1", "AS-2"])))
    return v1 is False, {"initial": v0, "afterOff": v1, "themeId": theme, "saved": ["AS-1", "AS-2"]}


def it_escclose(ctx):
    """§3.5 ②: 켜면 Esc 경로(EscCloseCommand — 키 바인딩과 같은 명령)로 활성 문서가 닫히고, 끄면 닫히지 않는다."""
    v0 = ctx.call("window.escClose.get")
    ctx.restore.append(("window.escClose.set", {"on": v0}))
    ctx.call("docs.close", all=True)
    for t in ("ESC-1", "ESC-2"):
        ctx.call("docs.open", kind="form", title=t, allowDuplicate=True)
    ctx.call("window.escClose.set", on=False)
    ctx.r.call("ui.invoke", command="EscClose")
    off = len(ctx.call("docs.list") or [])
    ctx.call("window.escClose.set", on=True)
    ctx.call("ui.invoke", command="EscClose")
    on = len(ctx.call("docs.list") or [])
    ctx.persist.append(("window.escClose.get", None, True))
    return off == 2 and on == 1, {"docsAfterEscOff": off, "docsAfterEscOn": on, "default": v0}


def it_snap(ctx):
    s0 = ctx.call("snap.get") or {}
    want = 12 if s0.get("range") != 12 else 20
    s1 = ctx.call("snap.set", on=not s0.get("on", True), range=want) or {}
    s2 = ctx.call("snap.set", range=13) or {}  # 홀수 → 셸이 2px 단위로 맞춘다
    ctx.restore.append(("snap.set", {"on": s0.get("on"), "range": s0.get("range")}))
    ctx.persist.append(("snap.get", None, s2))
    ok = s1.get("on") == (not s0.get("on", True)) and s1.get("range") == want and s2.get("range") in (12, 14)
    return ok, {"before": s0, "set": s1, "odd13": s2}


def it_capture(ctx):
    """§3.5 ⑬(PD 결정): 메인과 모든 SDI 창에 적용 — SDI 1개를 열어 둔 채 토글하고, probe(전 창 적용 여부)로 실제 반영을 본다."""
    if ctx.target == "desktop":
        open_sdi(ctx, 1)
    c0 = ctx.call("window.captureProtect.get") or {}
    c1 = ctx.call("window.captureProtect.set", on=not c0.get("setting")) or {}
    ctx.restore.append(("window.captureProtect.set", {"on": c0.get("setting")}))
    if c1.get("applied") is None:
        return "N/A" if ctx.target == "web" else None, {"before": c0, "after": c1, "note": "실제 적용 probe 없음"}
    ctx.persist.append(("window.captureProtect.get", None, c1))
    return c1.get("setting") == (not c0.get("setting")) and c1.get("applied") == c1.get("setting"), {"before": c0, "after": c1}


def open_sdi(ctx, n=2):
    ctx.call("docs.close", all=True)
    for i in range(n):
        ctx.call("docs.open", kind="form", title="WM-SDI%d" % (i + 1), sdi=True, allowDuplicate=True)
    ctx.call("docs.open", kind="grid", title="WM-MDI", allowDuplicate=True)


def it_sdi_minmax(ctx):
    """§3.5 ⑧⑨: 모두 최소화 → 모두 이전 크기로(OS 복원 경계, Normal). MDI·메인은 그대로, 복원 경계 = 최소화 직전 경계."""
    open_sdi(ctx)
    a = ctx.call("sdi.minimizeAll") or {}
    b = ctx.call("sdi.restoreAll") or {}
    mins, rest = ([s.get("state") for s in (x.get("after") or {}).get("sdi", [])] for x in (a, b))
    geo = lambda snap: [(s.get("x"), s.get("y"), s.get("w"), s.get("h")) for s in (snap or {}).get("sdi", [])]
    same_main = a.get("before", {}).get("main") == b.get("after", {}).get("main")
    ok = len(mins) == 2 and all(s == "Minimized" for s in mins) and all(s == "Normal" for s in rest) \
        and geo(a.get("before")) == geo(b.get("after")) and same_main
    # 기준(lead 01:3x, odev-2 SdiState API): 관리형 SDI 상태는 window.snapshot 의 state(stateSource=SdiState)로 읽는다 — 옛 MdiState 는 늘 Normal
    src = sorted({s.get("stateSource") or "MdiState/OS" for x in (a, b) for s in (x.get("after") or {}).get("sdi", [])})
    return ok, {"basis": "lead 01:3x·odev-2 WindowCommands.SdiState", "stateSource": src,
                "afterMin": mins, "afterRestore": rest, "boundsBack": geo(a.get("before")) == geo(b.get("after")), "mainSame": same_main}


def it_sdi_close(ctx):
    open_sdi(ctx)
    a = ctx.call("sdi.closeAll") or {}
    before, after = a.get("before", {}), a.get("after", {})
    ok = len(before.get("sdi", [])) == 2 and len(after.get("sdi", [])) == 0 and after.get("docCount") == before.get("docCount", 0) - 2
    return ok, {"before": before.get("docCount"), "after": after.get("docCount"), "sdiAfter": len(after.get("sdi", []))}


def it_sdi_reset(ctx):
    """§3.5 ⑦ SDI 위치 초기화 — 판정은 retest_window_menu_sdi(lead 04:5x 보강·05:0x 정정: 크기 유지·공식 계산값 일치·대상 작업 영역·MDI·메인 불변)."""
    from retest_window_menu_sdi import it_sdi_reset as run  # 300줄 상한으로 분리
    return run(ctx, open_sdi)


def desktop_hold(ctx):
    """lead 06:1x: 데스크톱 ④⑤⑥⑦ 은 창을 주 모니터로 옮기거나 키워 사용자 화면을 덮는다(odev-4b q6_i0451 실측) — 명령 전에 HOLD."""
    from retest_window_menu_sdi import HOLD, HOLD_REASON
    return (HOLD, {"reason": HOLD_REASON, "evidence": "Q6 근거는 기존 i0339 결과로 대신(lead 06:1x)"}) if ctx.target == "desktop" else None


def it_main_reset(ctx):
    """§3.5 ④: 기본 크기·주 모니터 중앙, Normal, 화면 안."""
    if ctx.target != "desktop":
        return "N/A", "메인 창이 OS 창이 아님(단일 뷰) — MDI 재배치만 해당"
    hold = desktop_hold(ctx)
    if hold:
        return hold
    a = ctx.call("window.main.reset") or {}
    m = a.get("after", {}).get("main", {})
    p = next((s for s in m.get("screens") or [] if s.get("primary")), {})
    cx, cy = m.get("x", 0) + m.get("pxW", 0) / 2, m.get("y", 0) + m.get("pxH", 0) / 2
    centered = p and abs(cx - (p["wx"] + p["ww"] / 2)) <= 24 and abs(cy - (p["wy"] + p["wh"] / 2)) <= 24
    return m.get("state") == "Normal" and bool(centered), {"before": a.get("before", {}).get("main"), "after": m}


def fits(m, rect, tol=2):
    """§3.5 ⑤⑥(Mars 동일): Normal 상태에서 클라이언트 크기(물리 px)가 rect(모니터 Bounds) 크기와 같고, 위치가 (X−7±tol, Y±tol).
    tol=2 의 근거(lead 04:4x 승인, 16→2 — 판정 강화): 문서(판정보고서 §⑤⑥·합격기준 Mars 실측) 기준은 '같다'(client=Bounds, X−7)이고 허용 오차 수치는 없다.
    남는 차는 반올림뿐이다 — ① 원격 clientPxW/H = round(ClientSize×RenderScaling) 반올림 ≤1px ② 호스트 맞춤 계산(DIP↔px) 반올림 ≤1px → 상한 2px.
    ※ '반올림 ≤1px 두 번' 은 코드 기반 추정이며 실측이 아니다(lead 04:5x). i0339 실측 차는 0px(그때 화면 배율은 기록하지 않음)이고, 다른 해상도·배율 실측이 생기면 다시 판단한다.
    x·y 는 OS 정수 px 다. 예전 16 은 근거 없는 여유값이었다(첫 작성자 odev-6r, 의도 기록 없음 — 테두리 차까지 통과시켜 '같다' 보다 느슨했다)."""
    return (m.get("state") == "Normal" and abs(m.get("clientPxW", 0) - rect[2]) <= tol and abs(m.get("clientPxH", 0) - rect[3]) <= tol
            and abs(m.get("x", 0) - (rect[0] - 7)) <= tol and abs(m.get("y", 0) - rect[1]) <= tol)


def main_fit(ctx, mode):
    if ctx.target != "desktop":
        return "N/A", "단일 뷰(브라우저 탭·Android 액티비티)는 메인 창 크기 맞춤이 없음"
    hold = desktop_hold(ctx)
    if hold:
        return hold
    a = ctx.call("window.main.maximize", mode=mode) or {}
    m = a.get("after", {}).get("main", {})
    sc = m.get("screens") or []
    if mode == "single":
        p = next((s for s in sc if s.get("primary")), sc[0] if sc else {})
        rect = (p.get("x", 0), p.get("y", 0), p.get("w", 0), p.get("h", 0))
    else:
        if len(sc) < 2:
            ctx.call("window.main.reset")
            return "N/A", "모니터 1대 — 멀티 모니터 맞춤 판정 불가"
        x0, y0 = min(s["x"] for s in sc), min(s["y"] for s in sc)
        rect = (x0, y0, max(s["x"] + s["w"] for s in sc) - x0, max(s["y"] + s["h"] for s in sc) - y0)
    ctx.call("window.main.reset")
    return fits(m, rect), {"mode": mode, "main": {k: m.get(k) for k in ("x", "y", "clientPxW", "clientPxH", "state")}, "target": rect}


def it_save_now(ctx):
    """⑪ 현재 화면 수동 저장: 인자 없는 layout.save(자동 저장 슬롯) → 전부 닫기 → layout.load 로 그대로 돌아오는가."""
    open_sdi(ctx, 1)
    want = sorted(d["title"] for d in ctx.call("docs.list") or [])
    ctx.call("layout.save")
    ctx.call("docs.close", all=True)
    ok = ctx.call("layout.load") is True
    got = sorted(d["title"] for d in ctx.call("docs.list") or [])
    # 자동 저장 슬롯을 덮어썼으므로 ①이 등록한 재기동 대조 기대값(AS-1·AS-2)도 이 화면으로 바꾼다.
    # (Android 01:2x 실측: 재기동 뒤 WM-MDI·WM-SDI1 이 정확히 복원됐는데 옛 기대값 때문에 FAIL — 앱 결함 아닌 시험 설계 오류)
    ctx.persist[:] = [p for p in ctx.persist if p[0] != "layout.load"] + [("layout.load", None, ("titles", want))]
    return ok and got == want, {"want": want, "got": got}


def it_layout_save_load(ctx):
    name = "WM-%s" % datetime.datetime.now().strftime("%H%M%S")
    open_sdi(ctx, 1)
    s = ctx.call("layout.save", name=name) or {}
    ctx.call("docs.close", all=True)
    ok_load = ctx.call("layout.load", id=s.get("id")) is True
    docs = ctx.call("docs.list") or []
    ctx.layout_id = s.get("id")
    ctx.persist.append(("layout.list", None, ("contains", s.get("id"))))
    return s.get("status") == "Saved" and ok_load and len(docs) == 2, {"save": s, "loaded": len(docs)}


def it_layout_manage(ctx):
    lid = getattr(ctx, "layout_id", None)
    if not lid:
        return False, "앞 항목(저장)이 실패해 대상 없음"
    entries = ctx.call("layout.list") or []
    # 기준(lead 01:3x, odev-2 LayoutLibrary 089b23 결정): 중복 이름 시험은 예약 이름(자동 저장 슬롯)이 아닌 사용자 이름으로 한다.
    # 예전에는 layout.list[0](Android 에선 자동 슬롯 id=auto)의 이름을 썼다 — 사용자 레이아웃 중복 판정이 아니었다.
    mine = next((e for e in entries if e.get("id") == lid), {})
    dup = ctx.call("layout.save", name=mine.get("name", "x")) or {}
    # 예약 이름: 첫 저장 NameExists, overwrite=true 재시도는 ReadOnly(자동 슬롯은 덮어쓸 수 없다) — 자동 슬롯이 목록에 있을 때만 본다
    auto = next((e for e in entries if e.get("id") == "auto"), None)
    reserved = None
    if auto:
        r1 = ctx.call("layout.save", name=auto.get("name")) or {}
        r2 = ctx.call("layout.save", name=auto.get("name"), overwrite=True) or {}
        reserved = {"first": r1.get("status"), "overwrite": r2.get("status")}
    rn = ctx.call("layout.rename", id=lid, name="WM-이름변경") or {}
    no_confirm = ctx.r.call("layout.delete", id=lid)  # 의도된 거부 확인: confirm 없으면 오류여야 한다(바로 아래에서 confirm=True 로 실제 삭제)
    print("  (위 ✗ layout.delete 는 #121 의 의도된 거부 확인 — confirm 없는 삭제는 막혀야 PASS. 실제 삭제는 다음 호출 confirm=true)")
    de = ctx.call("layout.delete", id=lid, confirm=True) or {}
    gone = all(e.get("id") != lid for e in ctx.call("layout.list") or [])
    ctx.persist[:] = [p for p in ctx.persist if p[0] != "layout.list"]
    reserved_ok = reserved is None or reserved == {"first": "NameExists", "overwrite": "ReadOnly"}
    ok = dup.get("status") == "NameExists" and reserved_ok and rn.get("status") == "Saved" and no_confirm is None \
        and de.get("status") == "Saved" and gone
    return ok, {"basis": "lead 01:3x·odev-2 LayoutLibrary 089b23 — 사용자 이름 중복=NameExists, 예약 이름 overwrite=ReadOnly",
                "dupName": dup.get("status"), "reserved": reserved if auto else "자동 슬롯 없음(미대조)",
                "rename": rn, "deleteNoConfirm": no_confirm, "delete": de}


def it_layout_template(ctx):
    ts = ctx.call("layout.template.list") or []
    if not ts:
        return False, "템플릿 0개"
    ok = ctx.call("layout.template.apply", id=ts[0].get("id")) is True
    # 기준(lead 01:3x, odev-2 LayoutLibrary 089b23): 템플릿은 읽기 전용 — rename·delete 는 ReadOnly 거부가 PASS(예전 NotFound 는 FAIL).
    # 판정 핵심은 적용이 동작하는 것(ok)과 5종 존재다.
    ro = ctx.call("layout.rename", id=ts[0].get("id"), name="x") or {}
    rd = ctx.call("layout.delete", id=ts[0].get("id"), confirm=True) or {}
    ids = sorted(t.get("id") for t in ts)
    return ok and ro.get("status") == "ReadOnly" and rd.get("status") == "ReadOnly" and ids == TEMPLATE_IDS, \
        {"basis": "lead 01:3x·odev-2 089b23 — 템플릿 읽기 전용(ReadOnly)", "applied": ok, "templates": ids,
         "renameTemplate": ro.get("status"), "deleteTemplate": rd.get("status")}


def it_full_backup(ctx):
    """§3.5 ⑫(PD 결정): 전체 백업 → 설정 변경 → 복원하면 설정이 돌아오고, 복원 전 현재 상태가 자동 백업된다(재시작 없음)."""
    preexisting = {e.get("id") for e in ctx.call("backup.list") or []}  # 시험 전부터 있던 백업 — 절대 지우지 않는다(리드 00:2x)
    t0 = (ctx.call("settings.get") or {}).get("themeId")
    b = ctx.call("backup.create", name="WM-백업-%s" % datetime.datetime.now().strftime("%H%M%S")) or {}
    ctx.call("theme.next")
    before_restore = ctx.call("backup.list") or []
    restored = ctx.call("backup.restore", id=b.get("id"), confirm=True) or {}
    t1 = (ctx.call("settings.get") or {}).get("themeId")
    after_restore = ctx.call("backup.list") or []
    autos = [e for e in after_restore if e.get("id") not in {x.get("id") for x in before_restore} and e.get("isAuto")]
    # 정리: 이번 시험이 만든 백업(직접 만든 것 + 복원 직전 자동 백업)만 지운다
    for e in after_restore:
        if e.get("id") not in preexisting:
            ctx.call("backup.delete", id=e.get("id"), confirm=True)
    ok = restored.get("status") == "Saved" and t1 == t0 and len(autos) == 1
    return ok, {"theme": (t0, t1), "restore": restored.get("status"), "autoBackup": [e.get("name") for e in autos]}


# (번호, 이름, 함수) — Mars 윈도우 메뉴 13항목 순서(odev-1 1차 사양 23:5x). ⑫ 화면 백업·복원·삭제는 PD UI-11 개인 레이아웃으로 대응.
ITEMS = [
    (1, "윈도우 자동저장(끈 채 종료해도 데이터 유지)", it_autosave),
    (2, "Esc 창닫기(활성 문서만)", it_escclose),
    (3, "자석 범위(0~48·2단위)", it_snap),
    (4, "메인 창 초기화", it_main_reset),
    (5, "싱글 모니터 최대화(주 모니터 맞춤)", lambda c: main_fit(c, "single")),
    (6, "멀티 모니터 최대화(전체 모니터 맞춤)", lambda c: main_fit(c, "multi")),
    (7, "SDI 위치 초기화", it_sdi_reset),
    (8, "SDI 모두 최소화·(9) 이전 크기로", it_sdi_minmax),
    (10, "SDI 모두 닫기", it_sdi_close),
    (11, "현재 화면 수동 저장", it_save_now),
    (12, "화면 백업·복원·삭제(개인 레이아웃 저장·불러오기)", it_layout_save_load),
    (121, "화면 백업 이름 변경·삭제", it_layout_manage),
    # 템플릿 적용 단계마다 활성 문서 본문 보임 확인(with_body — lead 06:3x D-2)
    (122, "레이아웃 템플릿 적용(5종 존재·이름변경 거부)", with_body(it_layout_template)),
    (124, "집중 템플릿: 탐색기·로그 숨김, 알림 보임", with_body(it_focus_apply)),
    (125, "집중 해제: 다른 템플릿 적용 시 탐색기·로그 복귀", with_body(it_focus_leave)),
    (126, "집중 숨김 상태 저장·불러오기 유지", with_body(it_focus_persist)),
    (127, "집중 왕복 핀 회차(핀 풀림·복원·위치·크기)", with_body(it_focus_pinned)),
    (128, "템플릿 순서 조합(집중→계단식 등) 단계마다 활성 문서 본문 보임", it_template_order),
    (123, "전체 백업·복원(설정+개인 레이아웃, 복원 전 자동 백업)", it_full_backup),
    (13, "화면 캡처 방지", it_capture),
] + ITEMS_Q8  # 31~36: Q8 자석 흡착(docs.move·docs.resize — 진단 빌드, 없으면 PENDING)

# 윈도우 메뉴 ⑦ SDI 위치 초기화 판정(lead 04:5x 보강·05:0x 정정) — 크기 유지·SdiResetLayout 공식 일치·대상 작업 영역 안·MDI 불변·메인 불변, 시작점(40)은 '구현 일관성' 기록만
"""
문서 기준(판정보고서 §⑦, Mars 원문): 기본 위치, 겹치면 +30 비켜 놓기, 화면 밖 보정, 크기 그대로.
준비(lead 05:2x 무동작 통과 차단): sdi.setBounds 로 두 SDI 를 공식과 다른 위치(PREP — 30 계단 아님·시작점 40 아님)로 옮기고 SDI1 크기를 바꾼다.
PASS 필수: ⓪ 준비 적용(초기화 전 위치 = 준비 위치) ⑤ 초기화로 각 창 위치가 바뀜(위치 고정 창 제외)
          ① 크기 유지(초기화 전 SDI1 크기를 기본과 다르게 바꾼 뒤 그대로인지)
          ② 위치 = 공식 계산값(lead 05:0x 정정 — '정확히 +30' 이 아니라 SdiResetLayout 규칙: 기본 위치에서 시작, 이미 놓인 창·위치 고정 창과
             좌상단이 같을 때만 +30 반복, 어느 작업 영역과도 100×100 겹치지 않으면 첫 작업 영역 가운데로 보정 후 다시 비켜 놓기).
             고정 창 없는 기본 장면에서는 결과가 (+30,+30) 계단과 같다.
          ③ 초기화 위치가 대상 작업 영역 안(웹 관리형 창은 메인 창 클라이언트 안) ④ MDI 문서 bounds 불변 + 메인 창 불변.
기록만(PASS 조건 아님): 시작점 40(구현 상수 SdiCascadeOrigin — 구현 일관성). ② 는 시작점 40 으로 먼저 계산하고, 안 맞으면
          실측 첫 창 위치를 시작점으로 다시 계산해 맞으면 통과(시작점 차이만으로 FAIL 하지 않게) — matchedWith=origin40|fallback 기록, fallback 이면 '구현 일관성 미확인'
          이고 판정은 PASS 가 아니라 'PASS(조건부)'(기본 위치 미검증 — 러너 종료 코드 2, gate 근거 아님).
대상 작업 영역: 실행기 인자 --primary-override x,y,w,h(물리 px, odev-4b Program.cs 진단 전용과 같은 형식)가 있으면 그 사각형,
          없으면 주 모니터 작업 영역(공식 입력은 주 모니터 먼저 + 나머지 모니터 작업 영역 — DesktopWindowHost.ResetSdi 와 같음).
웹 기대값(lead 새 규칙): 공식 위치(xf,yf)를 메인 클라이언트 W×H 안으로 당긴다 — x=min(xf,W−w), y=min(yf,H−h), 최소 0, 크기 유지.
          추가 PASS 조건 ⑥ 창끼리 좌상단이 달라야 한다(당긴 결과가 한 점에 겹치면 FAIL).
"""
GAP = 30          # Mars 원문 계단 간격(SdiResetLayout.Gap)
MIN_VISIBLE = 100  # SdiResetLayout.MinVisible
SAME_TOL = 0.5     # SdiResetLayout.SameTolerance
MAX_SHIFTS = 1000  # SdiResetLayout.MaxShifts
ORIGIN = 40        # WindowCommands.SdiCascadeOrigin(구현 상수 — 기록만)
PREP = ((210, 170), (530, 250))  # 준비 위치(대상 영역 원점 기준): 서로 차 (320,80) — 30 계단 아님, 시작점 40 아님
ROUND = 1         # 좌표 반올림 허용(px·DIP)
SIZE_TOL = 2      # 크기 반올림 허용(fits 와 같은 근거: 반올림 2회 — 코드 기반 추정)
TEST_SIZE = (500, 380)  # 초기화 전에 SDI1 에 줄 크기(DIP — 기본 640 과 다름)
COND_PASS = "PASS(조건부)"  # 공식은 맞으나 시작점이 40 이 아님(fallback) — 구현 일관성 미확인, gate 근거 아님
TEST_SIZE2 = (560, 340)  # 초기화 전에 SDI2 에 줄 크기(DIP — 기본·SDI1 과 다름)


def _entry(snap, doc_id):
    return next((s for s in (snap or {}).get("sdi") or [] if s.get("doc") == doc_id), {})


def _size(e):
    """창 크기: OS 창은 물리 px(pxW·pxH), 관리형 창은 DIP(w·h)."""
    return (e.get("pxW"), e.get("pxH")) if e.get("pxW") is not None and not e.get("managed") else (e.get("w"), e.get("h"))


def _rect(e):
    w, h = _size(e)
    return (e.get("x"), e.get("y"), w, h)


def parse_override(v):
    """--primary-override 'x,y,w,h' → (x,y,w,h) 정수, 형식이 틀리거나 w·h ≤ 0 이면 None(앱 Program.cs 와 같은 규칙)."""
    if v is None:
        return None
    p = [s.strip() for s in str(v).split(",")]
    try:
        r = tuple(int(s) for s in p)
    except ValueError:
        return None
    return r if len(r) == 4 and r[2] > 0 and r[3] > 0 else None


def _areas(ctx, snap):
    """공식 입력 작업 영역 목록(첫 항목 = 보정 기준·③ 대상)과 종류. 데스크톱: override 사각형 하나 또는 주 모니터 먼저 + 나머지(물리 px). 웹: 메인 클라이언트(DIP)."""
    main = (snap or {}).get("main") or {}
    screens = main.get("screens") or []
    ov = parse_override(getattr(ctx.args, "primary_override", None))
    if screens:
        if ov:
            return [ov], "override(%d,%d,%dx%d)" % ov
        ordered = sorted(screens, key=lambda s: 0 if s.get("primary") else 1)
        return [(s.get("wx"), s.get("wy"), s.get("ww"), s.get("wh")) for s in ordered], "monitor-work-area(primary)"
    return [(0, 0, main.get("w"), main.get("h"))], "main-client"


def _visible(r, areas):
    """SdiResetLayout.IsVisible: 어느 영역과든 min(100, 창 크기) 이상 겹치면 화면 안."""
    x, y, w, h = r
    nw, nh = min(MIN_VISIBLE, w), min(MIN_VISIBLE, h)
    for ax, ay, aw, ah in areas:
        iw = min(x + w, ax + aw) - max(x, ax)
        ih = min(y + h, ay + ah) - max(y, ay)
        if iw > 0 and ih > 0 and iw >= nw and ih >= nh:
            return True
    return False


def _avoid(p, occ):
    for _ in range(MAX_SHIFTS):
        if not any(abs(o[0] - p[0]) <= SAME_TOL and abs(o[1] - p[1]) <= SAME_TOL for o in occ):
            break
        p = (p[0] + GAP, p[1] + GAP)
    return p


def compute(areas, default, wins):
    """SdiResetLayout.Compute 이식: wins=[(pos,(w,h),locked)] → 새 좌상단 목록(고정 창은 None)."""
    occ = [w[0] for w in wins if w[2]]
    out = []
    for pos, size, locked in wins:
        if locked:
            out.append(None)
            continue
        p = _avoid(default, occ)
        if areas and not _visible((p[0], p[1], size[0], size[1]), areas):
            a = areas[0]
            p = _avoid((a[0] + (a[2] - size[0]) / 2, a[1] + (a[3] - size[1]) / 2), occ)
        occ.append(p)
        out.append(p)
    return out


def clamp_web(placed, sizes, area):
    """웹 관리형 창 새 규칙(lead ⑦ 웹 기대값): x=min(xf, W−w), y=min(yf, H−h), 최소 0, 크기 유지 — xf,yf 는 공식 위치, W·H 는 메인 클라이언트."""
    _, _, W, H = area
    return [None if p is None else (max(0, min(p[0], W - s[0])), max(0, min(p[1], H - s[1]))) for p, s in zip(placed, sizes)]


def _pos(e):
    """공식 좌표계 위치: 관리형 창은 레이어 기준 inContainer, OS 창은 화면 x,y."""
    ic = e.get("inContainer") or {}
    return (ic.get("x"), ic.get("y")) if ic else (e.get("x"), e.get("y"))


def _inside(r, area):
    x, y, w, h = r
    ax, ay, aw, ah = area
    return None not in (x, y, w, h) and x >= ax and y >= ay and x + w <= ax + aw and y + h <= ay + ah


HOLD = "HOLD"  # 보류(실행 안 함) — 러너 종료 코드 2, gate 근거 아님
HOLD_REASON = "포그라운드 노출 불가피(override 미동작) — lead 06:0x·06:1x 보류 결정, odev-4b q6_i0451 실측"


def it_sdi_reset(ctx, open_sdi):
    # lead 06:0x 결정: 데스크톱 #7 은 --primary-override 원인 수정 전까지 실행하지 않는다(사용자 화면에 시험 창 노출 방지).
    # 창을 열기 전에 돌려보내야 노출이 없다. override 가 고쳐지면 이 분기와 준비 좌표(화면 밖 → MDI-12 정상화 보정)를 다시 본다.
    if getattr(ctx, "target", None) == "desktop":
        return HOLD, {"reason": HOLD_REASON,
                      "resume": "override 수정 통지 뒤 이 분기 제거·준비 좌표 재검토"}
    open_sdi(ctx)
    docs = {d.get("title"): d for d in ctx.call("docs.list") or []}
    s1, s2, mdi = (docs.get(t, {}).get("id") for t in ("WM-SDI1", "WM-SDI2", "WM-MDI"))
    mdi_before = (ctx.call("chrome.state", doc=mdi) or {}).get("mdiBounds")
    # 준비(lead 05:2x 무동작 통과 차단): 두 SDI 를 공식과 다른 위치(서로 30 계단 아님·시작점 40 아님, 대상 영역 안)로 옮기고 SDI1 크기를 기본과 다르게
    pre_area = _areas(ctx, ctx.call("window.snapshot") or {})[0][0]
    prep = {s1: (pre_area[0] + PREP[0][0], pre_area[1] + PREP[0][1]), s2: (pre_area[0] + PREP[1][0], pre_area[1] + PREP[1][1])} if None not in pre_area else {}
    if not prep:
        return False, {"why": "대상 영역 미상 — 준비 위치를 정할 수 없음", "area": pre_area}
    # ① 두 창 모두 기본과 다른(서로도 다른) 크기로 — 모든 창 크기 불변 확인(otest-1 권고)
    sp = ctx.call("sdi.setBounds", doc=s2, x=prep[s2][0], y=prep[s2][1], w=TEST_SIZE2[0], h=TEST_SIZE2[1]) or {}
    sb = ctx.call("sdi.setBounds", doc=s1, x=prep[s1][0], y=prep[s1][1], w=TEST_SIZE[0], h=TEST_SIZE[1]) or {}
    if sb.get("status") != "Done" or sp.get("status") != "Done":
        return False, {"why": "sdi.setBounds 실패 — 준비 불가", "resp": {"s1": sb, "s2": sp}}
    sized = {d: _size(_entry(sb.get("after"), d)) for d in (s1, s2)}  # 마지막 setBounds 직후 스냅샷(px) — 같은 단위끼리 비교
    a = ctx.call("sdi.resetPositions") or {}
    bf, af = a.get("before") or {}, a.get("after") or {}
    e1, e2 = _entry(af, s1), _entry(af, s2)
    mdi_after = (ctx.call("chrome.state", doc=mdi) or {}).get("mdiBounds")
    # ① 크기 유지(두 창 모두)
    kept = {d: _size(e) for d, e in ((s1, e1), (s2, e2))}
    size_ok = all(None not in kept[d] and None not in sized[d] and all(abs(k - s) <= SIZE_TOL for k, s in zip(kept[d], sized[d])) for d in (s1, s2))
    # ② 공식 계산값과 일치: 스냅샷의 SDI 전부(순서 = 앱 입력 순서로 가정 — 비교는 순서 무관 집합)를 SdiResetLayout 규칙으로 계산
    areas, area_kind = _areas(ctx, af)
    area = areas[0]
    sdi_b = {s.get("doc"): s for s in bf.get("sdi") or []}
    entries = af.get("sdi") or []
    wins = [(_pos(sdi_b.get(e.get("doc"), e)), _size(e), bool(e.get("locked"))) for e in entries]
    actual = sorted(_pos(e) for e, w in zip(entries, wins) if not w[2])
    defaults = [("origin40", (area[0] + ORIGIN, area[1] + ORIGIN))] if None not in area else []
    if actual and None not in actual[0]:
        defaults.append(("fallback", actual[0]))  # 실측 첫 창 위치를 시작점으로 대체 계산(시작점만 다른 경우 — 구현 일관성 미확인)
    match, expected, exp40 = None, None, None
    for name, d in defaults:
        if any(None in w[1] for w in wins) or any(None in a for a in actual):
            break
        placed = compute([a for a in areas if None not in a], d, wins)
        if area_kind == "main-client":  # 웹: 공식 위치를 메인 클라이언트 안으로 당긴다(새 규칙)
            placed = clamp_web(placed, [w[1] for w in wins], area)
        exp = sorted(p for p in placed if p is not None)
        expected = expected or exp
        if name == "origin40":
            exp40 = exp  # 자기 공식(시작점 +40) 기대 위치 — 조건부·FAIL 원인 추적용(lead 05:4x)
        if len(exp) == len(actual) and all(abs(a[0] - b[0]) <= ROUND and abs(a[1] - b[1]) <= ROUND for a, b in zip(exp, actual)):
            match, expected = name, exp
            break
    gap_ok = match is not None and len(actual) >= 2
    p = actual
    gap = (p[1][0] - p[0][0], p[1][1] - p[0][1]) if len(p) >= 2 else None  # 기록: 기본 장면이면 (+30,+30)
    # ⓪ 준비 적용(초기화 전 위치 = 준비 위치) ⑤ 초기화로 위치가 바뀜(고정 창 제외) — 무동작 통과 차단
    pre = {d: _pos(sdi_b.get(d, {})) for d in (s1, s2)}
    near = lambda a, b: None not in a and None not in b and abs(a[0] - b[0]) <= ROUND and abs(a[1] - b[1]) <= ROUND
    prep_ok = all(near(pre[d], prep[d]) for d in (s1, s2))
    moved = {e.get("doc"): not near(_pos(sdi_b.get(e.get("doc"), {})), _pos(e)) for e, w in zip(entries, wins) if not w[2]}
    # ③ 대상 영역 안
    inside = {s1: _inside(_rect(e1), area), s2: _inside(_rect(e2), area)}
    # ④ MDI·메인 불변
    mdi_ok = mdi_before is not None and mdi_before == mdi_after
    main_ok = bf.get("main") == af.get("main")
    checks = {"⓪_준비적용": prep_ok, "⑤_위치변경": bool(moved) and all(moved.values()), "①_크기유지": size_ok, "②_공식일치": gap_ok, "③_작업영역안": all(inside.values()), "④_MDI불변": mdi_ok, "④_메인불변": main_ok}
    if area_kind == "main-client":  # 웹 새 규칙: 당긴 뒤에도 창끼리 좌상단이 달라야 한다(겹쳐 쌓임 금지)
        tl = [(round(q[0]), round(q[1])) for q in actual if None not in q]
        checks["⑥_좌상단상이"] = len(tl) >= 2 and len(set(tl)) == len(tl)
    # 구현 일관성(기록만): 첫 창이 시작점 40 인가(관리형은 레이어 기준 inContainer, OS 창은 대상 영역 기준)
    first = min((e1, e2), key=lambda e: (e.get("x") is None, e.get("x") or 0))
    ic = first.get("inContainer") or {}
    origin = (ic.get("x"), ic.get("y")) if ic else (None if first.get("x") is None else first.get("x") - (area[0] or 0),
                                                 None if first.get("y") is None else first.get("y") - (area[1] or 0))
    detail = {"basis": "lead 04:5x·05:0x — 문서 §⑦: 기본 위치·겹칠 때만 +30·화면 밖 보정·크기 그대로(SdiResetLayout 공식)", "checks": checks,
              "size": {"set": sized, "afterReset": kept}, "gap": gap, "positions": p,
              "formula": {"expected": expected, "matchedWith": match, "areas": areas,
                          "expectedOrigin40": exp40, "actual": actual,
                          "diffFromOrigin40": [(a[0] - b[0], a[1] - b[1]) for a, b in zip(actual, exp40)] if exp40 and len(exp40) == len(actual) else None, "note": "웹 영역은 메인 클라이언트 근사(레이어 크기 미노출)"},
              "area": {"kind": area_kind, "rect": area, "inside": inside},
              "mdi": {"before": mdi_before, "after": mdi_after}, "settled": a.get("settled"), "eventSeen": a.get("eventSeen"),
              "prep": {"intended": prep, "beforeReset": pre, "moved": moved},
              "implementationConsistency": {"origin": origin, "expected": 40, "status": {"origin40": "일치", "fallback": "구현 일관성 미확인"}.get(match, "판정 불가"),
                                            "note": "구현 상수 SdiCascadeOrigin — PASS 조건 아님. matchedWith=fallback(대체 계산)면 '구현 일관성 미확인'"}}
    ok = all(v is True for v in checks.values())
    # fallback 으로만 맞으면 '기본 위치'를 검증하지 못한 것 — PASS 와 합치지 않고 조건부로 구분(otest-1 지적, 러너 종료 코드 2)
    return (COND_PASS if ok and match == "fallback" else ok), detail

# AC-010·AC-022 실제 닫기 효과의 판정 규칙(순수 함수) — 웹 motion_close_judge 와 Fold5 측정이 같은 기준을 쓴다
"""
재생 0 은 FAIL, 표본 없음은 INVALID 다. motion.play 는 전환도 올리므로 닫기 재생의 근거로 쓰지 않는다(lastClose 로 본다).
단독 실행 없음. 자기시험은 motion_close_judge.py --self-test 가 돌린다.
"""
PLAYED_MID_MIN = 400  # 처음·끝 화면 둘 다와 달라야 하는 화소 수
PIXEL = 8             # 이보다 큰 밝기 차만 다른 화소로 센다
ORDER = ("FAIL", "INVALID", "PASS")
SAMPLE = (64, 48)     # alpha·off 를 셀 축소 크기
TC_ALPHA, TC_OFF = 0.001, 0.02  # 첫 변화로 보는 값
OFF_MAX = 1.0         # 5차 실측: MDI fade 는 0.51 이하, 바탕 없는 DOCK 은 3.08
BARE_ALPHA = 0.5      # 첫 변화가 이만큼 끝 모습 쪽이면서 선 밖이면 뒤 내용이 비친 것(5차 DOCK 0.809)
# 되돌림(flicker) 문턱. 실측값에서 고르지 않고 아래 이론으로 정한다.
#  - fade 의 불투명도는 표의 가속 곡선(CubicEaseOut·CubicEaseIn, MotionEffects.cs)이라 시간에 대해 단조다. 참 alpha 는 줄지 않는다.
#  - 한 장의 alpha 측정 오차: alpha = Σ(x-s)·k / Σk² (k = 끝-시작, SAMPLE 축소 회색). 화소마다 화면 혼합 결과의 8bit 반올림과
#    축소 회색의 반올림이 서로 독립인 균등 오차(±0.5, 분산 1/12 씩)로 더해져 분산 1/6 → 한 장의 alpha 표준편차 σ = 1/√(6·Σk²).
#    두 장의 차는 √2·σ 이고, 6 표준편차를 측정 한계로 잡아 q = 6·√2·σ = 2·√3/√(Σk²) 이다(최악 합 2·Σ|k|/Σk² 는 반올림 오차가 모든
#    화소에서 같은 부호일 때만이라 쓰지 않는다 — 그러면 문서 1개 닫기처럼 바뀌는 영역이 좁은 사례를 늘 판정하지 못한다).
#  - 촬영 구간이 겹치는 두 장은 찍힌 순서를 알 수 없으므로 비교하지 않는다(시각을 모르면 표본 순서를 그대로 쓴다).
#  - 따라서 순서가 확실한 두 장에서 q 보다 큰 감소는 측정으로 설명할 수 없는 실제 되돌림이다. DROP_MAX 는 판정 분해능이고,
#    사례의 q 가 DROP_MAX 를 넘으면(대비 부족 — Σk² < 1200, 즉 바뀐 회색 합이 거의 없음) 판정을 INVALID 로 둔다. 그래서 DROP_MAX 보다 큰 감소는 늘 q 보다 크다.
#    0.1 은 '진행도 10% 되돌림'을 가려낼 분해능으로 정한 값이고, 같은 회색 단계 기준인 OFF_MAX(1.0)와 함께 쓴다.
DROP_MAX = 0.1


def result(verdict, why, **data):
    return dict(data, verdict=verdict, why=why)


def css_scale(viewport_w, layout_w):
    """앱 사각형(ui.find bounds = 메인 TopLevel DIP)을 화면 표본·마우스(CSS) 좌표로 바꿀 배율 = 뷰포트 CSS 너비 ÷ 같은 TopLevel 의 ClientSize 너비(DIP, 같은 회차 실측).
    ui.style width(기기 분류용 창 폭 dp — 웹은 CSS ÷ 기기 배율)는 이 배율의 근거가 아니다(r11: 그 값으로 2.25 배 하자 누름이 전부 빗나감, r10 배율 1 누름은 전부 닫힘).
    실측값이 없으면 None(호출 쪽이 INVALID 로 둔다)."""
    try:
        v, w = float(viewport_w), float(layout_w)
    except (TypeError, ValueError):
        return None
    return v / w if v > 0 and w > 0 else None


def frame_evidence(phases, ms):
    """
    기록 전용(판정에 쓰지 않는다): 재생 중 앱 프레임과 판정기가 받은 화면 프레임 간격.
    phases = 닫기 뒤 perf.get 의 phases(motion.play 구간의 phase_raw.dt_ms), ms = 조작 시각부터 화면 표본 시각(ms).
    motion.play 구간은 재생 중 프레임 표본이 0장이면 버려지므로(PerfFrameLog) '없음'은 재생 없음 또는 그 사이 앱 프레임 0장(UI 스레드 정체)이다.
    """
    dt = [v for p in (phases or []) if p.get("name") == "motion.play" for v in ((p.get("phase_raw") or {}).get("dt_ms") or [])]
    app = {"frames": len(dt), "dt_max_ms": max(dt), "dt_ms": dt} if dt else {"frames": 0, "note": "motion.play 구간 없음(재생 없음 또는 재생 중 앱 프레임 0장)"}
    marks = [0] + [m for m in (ms or []) if m >= 0]
    gaps = [b - a for a, b in zip(marks, marks[1:])]
    return {"app_play": app, "screen_frames": len(ms or []), "screen_gap_max_ms": max(gaps) if gaps else None}


def to_css(bounds, k):
    """DIP 사각형 {x,y,w,h} 를 배율 k 로 CSS 사각형으로."""
    return {key: bounds[key] * k for key in ("x", "y", "w", "h")}


def judge_close(before, after, expect):
    """before·after = 닫기 앞·재생이 끝난 뒤의 lastClose. expect = {placement, effect, play, closed[, stage, reason]}."""
    if not isinstance(before, dict) or not isinstance(after, dict):
        return result("INVALID", "lastClose 가 없다(이 판에 진단점 없음)")
    data = {"lastClose": after}
    if not expect.get("closed"):
        return result("FAIL", "닫히지 않았다", **data)
    if after.get("seq", 0) <= before.get("seq", 0):
        return result("FAIL", "실제 닫기 알림이 오지 않았다", **data)
    for key in ("placement", "effect"):
        want, got = expect.get(key), after.get(key)
        if want and got and want != got:
            return result("INVALID", "%s 가 시험 조건과 다르다: %s != %s" % (key, got, want), **data)
    started = after.get("started", 0) - before.get("started", 0)
    if expect.get("play"):
        if started < 1:
            return result("FAIL", "재생 0 (stage=%s)" % after.get("stage"), **data)
        if after.get("stage") != "ended" or after.get("ended") != after.get("started"):
            return result("FAIL", "재생이 끝나지 않고 남았다", **data)
    elif started:
        return result("FAIL", "재생되면 안 되는데 재생됐다", **data)
    for key in ("stage", "reason"):
        if expect.get(key) and expect[key] != after.get(key):
            return result("FAIL", "%s=%s (기대 %s)" % (key, after.get(key), expect[key]), **data)
    return result("PASS", "재생·정리" if expect.get("play") else "재생 없음", **data)


LAYOUTS = {  # 기대 배치 → (ui.style effective, 활성 문서 zReason, 활성 문서 state)
    "overlap": ("pc", "ok", "normal"),
    "max": ("pc", "ok", "maximized"),
    "dock": ("tablet", "notmdi", None),
    "nav": ("mobile", None, None),
}


def layout_problem(place, seen, has_doc=True):
    """같은 회차에 읽은 {effective, zReason, state} 가 기대 배치와 다르면 이유, 맞으면 None."""
    style, reason, state = LAYOUTS[place]
    wants = {"effective": style}
    if has_doc:
        wants.update(zReason=reason, state=state)
    bad = ["%s=%s(기대 %s)" % (k, seen.get(k), v) for k, v in wants.items() if v and str(seen.get(k)).lower() != v]
    return ", ".join(bad) or None


def judge_frames(sampled, mid):
    """sampled = 재생 시간 안에 받은 프레임 수, mid = 그중 처음·끝 화면 둘 다와 다른 프레임 수."""
    data = {"sampled": sampled, "mid": mid}
    if not sampled:
        return result("INVALID", "재생 시간 안의 화면 표본이 없다", **data)
    if not mid:
        return result("FAIL", "중간 프레임 0", **data)
    return result("PASS", "중간 프레임 있음", **data)


def trace(frames, start, end, box):
    """프레임마다 처음·끝과 다른 화소 수(s·e)와 처음→끝 선 위의 자리 alpha(0=닫기 전, 1=끝)·선에서 벗어난 정도 off."""
    def small(im):
        return im.crop(box).convert("L").resize(SAMPLE).tobytes()

    first, last = small(start), small(end)
    line = [b - a for a, b in zip(first, last)]
    size = sum(v * v for v in line)
    q = round(2.0 * 3 ** 0.5 / size ** 0.5, 4) if size else None  # 두 장 alpha 차의 측정 한계(DROP_MAX 주석)
    points = []
    for frame in frames:
        now, alpha, off = small(frame), None, None
        if size:
            a = sum((f - s) * v for f, s, v in zip(now, first, line)) / float(size)
            alpha = round(a, 3)
            off = round(sum(abs(f - (s + a * v)) for f, s, v in zip(now, first, line)) / float(len(line)), 2)
        points.append({"s": diff_count(frame, start, box), "e": diff_count(frame, end, box), "alpha": alpha, "off": off, "q": q})
    return points


def flicker(points):
    """순서가 확실한 두 장(앞 장 촬영 끝 < 뒤 장 촬영 시작, t0·t1 ms 가 없으면 표본 순서)에서 alpha 가 가장 크게 준 값과 그 두 장."""
    worst = (0.0, None, None)
    for j, b in enumerate(points):
        for i in range(j):
            a = points[i]
            sure = "t1" not in a or "t0" not in b or a["t1"] < b["t0"]
            if sure and a["alpha"] is not None and b["alpha"] is not None and a["alpha"] - b["alpha"] > worst[0]:
                worst = (round(a["alpha"] - b["alpha"], 3), i, j)
    return worst


def first_change(points, fade):
    """첫 변화(Tc) 표본 번호(없으면 None). judge_trace 와 기록 전용 차이 지도(first_frame_map)가 같은 기준을 쓴다."""
    def moved(p):
        if fade and p["alpha"] is not None:
            return p["alpha"] > TC_ALPHA or p["off"] > TC_OFF
        return p["s"] >= PLAYED_MID_MIN

    return next((i for i, p in enumerate(points) if moved(p)), None)


STALL_RULE_BASIS = "21§7 안A(10차 관측 뒤 도입)"  # 시간 기준 재생 — 정체면 끝 모습으로 건너뛴다


PLAY_MS_RESOURCE = "PdStyleMotionMs"  # DesignEffectsApplier: "motion" => DesignThemeResolver.MotionMs() — 앱 재생 시간과 같은 출처


def play_ms_of(state):
    """
    앱이 쓰는 재생 시간(ms). 1순위 style.state.resources["PdStyleMotionMs"](= DesignThemeResolver.MotionMs).
    실제 state.effective 에는 Choice·effect. 접두 키만 들어가 motion.<수준>.ms(Number 키)가 없다(11차 dark_2 에서 play_ms=null 확인).
    resources 가 없을 때만 effective 의 motion.<수준>.ms 를 본다. 둘 다 없거나 0 이하이면 None(정체 규칙 미적용 → FAIL 유지).
    """
    state = state or {}
    for raw in ((state.get("resources") or {}).get(PLAY_MS_RESOURCE),
                (lambda eff: eff.get("motion.%s.ms" % eff.get("motion")))(state.get("effective") or {})):
        try:
            ms = float(raw)
        except (TypeError, ValueError):
            continue
        return ms if ms > 0 else None
    return None


def stall_override(frames, evidence, play_ms):
    """
    21 §7 안 A: 재생은 시간 기준이라 UI 스레드가 재생 시간 이상 멈추면 중간 모습 없이 끝 모습으로 건너뛴다.
    '중간 프레임 0' FAIL 이고 재생 중 앱 최대 프레임 간격(evidence.app_play.dt_max_ms) ≥ play_ms 면 INVALID(정체)로 바꾼다.
    근거 기록이 없거나(구간 없음 포함) play_ms 를 못 읽으면 그대로 FAIL. 다른 판정은 손대지 않는다. stall_rule 에 근거를 남긴다.
    """
    gap = ((evidence or {}).get("app_play") or {}).get("dt_max_ms")
    hit = frames.get("verdict") == "FAIL" and frames.get("why") == "중간 프레임 0"
    applied = bool(hit and isinstance(gap, (int, float)) and play_ms and gap >= play_ms)
    out = dict(frames, stall_rule={"rule_basis": STALL_RULE_BASIS, "applied": applied, "dt_max_ms": gap, "play_ms": play_ms})
    if applied:
        out.update(verdict="INVALID", why="정체 — 재생 중 앱 최대 프레임 간격 %s ms ≥ 재생 시간 %s ms(중간 프레임 0)" % (gap, play_ms))
    return out


def scale_problem(mode, k, dpr_seen, dpr_want):
    """
    배율 구성 점검(구성 이상이면 이유, 맞으면 None). force(Chrome 실제 배율)는 앱 DIP = CSS 라 k ≈ 1 이어야 하고 devicePixelRatio 가 요청 배율이어야 한다
    (r11 scale_probe: force main.w 1400·k 1.0, emulation main.w 622.2·k 2.25 — 흉내는 앱 창이 줄고 그리기·누름이 어긋난다).
    """
    bad = []
    if mode == "force":
        if k is None or abs(k - 1) > 0.01:
            bad.append("실제 배율인데 k=%s(기대 1)" % k)
        if dpr_seen is None or abs(float(dpr_seen) - float(dpr_want)) > 0.01:
            bad.append("devicePixelRatio=%s(기대 %s)" % (dpr_seen, dpr_want))
    return ", ".join(bad) or None


def css_box(bounds, k, limit):
    """DIP 사각형 → CSS 정수 사각형(x0,y0,x1,y1), 화면(limit=(w,h)) 안으로 자른다. 없거나 비면 None."""
    if not bounds or not bounds.get("w") or not bounds.get("h"):
        return None
    x0, y0 = max(0, round(bounds["x"] * k)), max(0, round(bounds["y"] * k))
    x1, y1 = min(limit[0], round((bounds["x"] + bounds["w"]) * k)), min(limit[1], round((bounds["y"] + bounds["h"]) * k))
    return (x0, y0, x1, y1) if x0 < x1 and y0 < y1 else None


def split_counts(total, in_cover, in_window):
    """기록 전용: 첫 변화 장의 처음 대비 차이 화소를 덮개 안·덮개 밖 창 안·창 밖으로 나눈다(None 은 사각형 없음)."""
    out = {"total": total, "cover": in_cover}
    if in_window is not None:
        out["window_outside_cover"] = in_window - (in_cover or 0)
        out["outside_window"] = total - in_window
    elif in_cover is not None:
        out["outside_cover"] = total - in_cover
    return out


def judge_trace(points, fade):
    """
    첫 변화(Tc)부터 본다. 조작 시각으로 자르지 않는다.
    fade 는 처음·끝 그림의 혼합이라 선에서 벗어나면(off) 바탕이 없거나 색이 다르거나 다른 내용이 그려진 것이다.
    """
    tc = first_change(points, fade)
    if tc is None:
        return result("INVALID", "닫힌 뒤 바뀐 화면 표본이 없다", sampled=0)
    seen = points[tc:]
    head = {"sampled": len(seen), "first": {k: seen[0][k] for k in ("alpha", "off")}}
    lit = fade and seen[0]["off"] is not None and seen[0]["off"] > OFF_MAX
    if lit and seen[0]["alpha"] >= BARE_ALPHA:
        return result("FAIL", "바탕 없는 덮개(첫 변화부터 뒤 내용이 비친다)", **head)
    if lit:
        return result("FAIL", "덮개 첫 화면이 닫기 전 모습과 어긋난다(확대·바탕색·내용)", **head)
    if fade and seen[0]["alpha"] is not None:
        if (seen[0].get("q") or 0) > DROP_MAX:
            return result("INVALID", "대비 부족 — 되돌림 분해능 q=%s > %s" % (seen[0]["q"], DROP_MAX), **head)
        fall, i, j = flicker(seen)
        if fall > DROP_MAX:
            return result("FAIL", "되돌림(flicker) — 진행도 %s 감소(%s → %s)" % (fall, seen[i]["alpha"], seen[j]["alpha"]), drop=fall, **head)
    if seen[-1]["e"] >= PLAYED_MID_MIN:
        return result("INVALID", "관찰 시간 안에 끝 모습이 되지 않았다", **head)
    mid = sum(1 for p in seen if p["s"] >= PLAYED_MID_MIN and p["e"] >= PLAYED_MID_MIN
              and not (fade and p["off"] is not None and p["off"] > OFF_MAX))
    return dict(judge_frames(len(seen), mid), first=head["first"])


def judge_cover(cover, dpr=None):
    """lastClose.cover(7차 진단): 배율 변환 경로·k=1·화소 ≥ 원본(1 미만 여유)·배율이 기기 배율과 같음. 진단이 없으면 INVALID."""
    if not isinstance(cover, dict):
        return result("INVALID", "lastClose.cover 진단이 없다(7차 전 판)")
    px, src = cover.get("px") or [0, 0], cover.get("src") or [0, 0]
    bad = []
    if cover.get("mode") != "scaled":
        bad.append("찍기 경로 %s(예비 경로는 그림자 뒤 배율 이중)" % cover.get("mode"))
    if abs(float(cover.get("k", 0)) - 1) > 0.001:
        bad.append("k=%s" % cover.get("k"))
    if not all(0 <= p - s < 1 for p, s in zip(px, src)):
        bad.append("화소 %s 대 원본 %s" % (px, src))
    if dpr and abs(float(cover.get("scale", 0)) - dpr) > 0.01:
        bad.append("배율 %s ≠ 기기 %s" % (cover.get("scale"), dpr))
    return result("FAIL" if bad else "PASS", "; ".join(bad) or "1:1 진단", cover=cover)


def with_control(case, control):
    """재생 없음 사례는 같은 회차의 재생 대조군이 PASS 일 때만 인정한다."""
    if case["verdict"] == "PASS" and (control or {}).get("verdict") != "PASS":
        return dict(case, verdict="INVALID", why="같은 회차의 현재 문서 재생(대조군)이 PASS 가 아니다")
    return case


def unseen(case):
    """화면을 보지 않은 사례 표기 — 재생이 보였다는 근거로 쓰지 않는다."""
    return dict(case, visibility="unchecked")


def judge_occlusion(inside, outside):
    """inside = 프레임별 앞 창 사각형 안 변화 화소, outside = 그 밖 변화 화소."""
    data = {"inside": inside, "outside": outside}
    if not inside:
        return result("INVALID", "재생 시간 안의 화면 표본이 없다", **data)
    if max(inside) > 0:
        return result("FAIL", "덮개가 앞 창을 침범했다", **data)
    if max(outside or [0]) < PLAYED_MID_MIN:
        return result("FAIL", "앞 창 밖에서도 재생이 보이지 않는다", **data)
    return result("PASS", "앞 창 안 변화 0·밖 재생", **data)


def judge_residue(released):
    if released is None:
        return result("INVALID", "남은 재생을 읽지 못했다")
    return result("PASS" if released == 0 else "FAIL", "남은 재생 %s" % released)


def combine(**parts):
    worst = min((p["verdict"] for p in parts.values()), key=ORDER.index) if parts else "INVALID"
    why = "; ".join("%s: %s" % (k, p["why"]) for k, p in parts.items() if p["verdict"] != "PASS")
    return result(worst, why or "모두 통과", parts=parts)


def diff_count(a, b, box=None, outside=None):
    """다른 화소 수. box 안만 세고 outside 사각형은 뺀다."""
    from PIL import ImageChops

    d = ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > PIXEL else 0)
    if outside:
        d.paste(0, outside)
    if box:
        d = d.crop(box)
    return d.histogram()[255]


def rule_cases():
    """자기시험 사례(이름, 맞음)."""
    zero = {"seq": 1, "stage": "ended", "started": 0, "ended": 0}
    def last(stage="ended", started=1, ended=1, placement="DockTab", effect="fade", seq=2, reason=None):
        return dict(seq=seq, stage=stage, started=started, ended=ended, placement=placement, effect=effect, reason=reason)

    def points(*rows):  # (alpha, off) → 처음·끝과 다른 화소 수는 alpha 로 만든다
        return [dict(alpha=a, off=o, s=0 if a < 0.005 else 5000, e=0 if a > 0.99 else 5000) for a, o in rows]

    def shown(rows, fade=True):
        return judge_trace(points(*rows), fade)["verdict"]

    ok = result("PASS", "")
    cover = {"mode": "scaled", "scale": 2.25, "px": [905, 583], "src": [904.05, 582.075], "dest": [401.8, 258.7], "k": 1}
    mdi = [(0.0, 0.0), (0.003, 0.1), (0.2, 0.4), (0.95, 0.36), (1.0, 0.0)]
    want = dict(placement="DockTab", effect="fade", play=True, closed=True)
    good = judge_close(zero, last(), want)
    lost = judge_close(zero, last("canceled", 0, 0), want)
    return [
        ("재생 PASS", good["verdict"] == "PASS"),
        ("닫혔는데 재생 0 FAIL(이번 증거)", lost["verdict"] == "FAIL"),
        ("MDI 층 없음 FAIL", judge_close(zero, last("noLayer", 0, 0), want)["verdict"] == "FAIL"),
        ("NAV 대상 없음 FAIL", judge_close(zero, last("noTarget", 0, 0, placement=None, effect=None), want)["verdict"] == "FAIL"),
        ("DOCK 마지막만 성공이면 전체 FAIL", combine(left=lost, last=good)["verdict"] == "FAIL"),
        ("전환 재생만 있음 FAIL", judge_close(zero, last("none", 0, 0, effect=None), want)["verdict"] == "FAIL"),
        ("일반 사례 busy FAIL", judge_close(zero, last("busy", 0, 0), want)["verdict"] == "FAIL"),
        ("닫기 알림 없음 FAIL", judge_close(zero, last(seq=1), want)["verdict"] == "FAIL"),
        ("안 닫힘 FAIL", judge_close(zero, last(), dict(want, closed=False))["verdict"] == "FAIL"),
        ("잔류 FAIL", judge_close(zero, last("played", 1, 0), want)["verdict"] == "FAIL"),
        ("배치 다름 INVALID", judge_close(zero, last(placement="Nav"), want)["verdict"] == "INVALID"),
        ("진단 없음 INVALID", judge_close(None, None, want)["verdict"] == "INVALID"),
        ("none PASS", judge_close(zero, last("none", 0, 0, effect="none"), dict(want, effect="none", play=False, stage="none"))["verdict"] == "PASS"),
        ("none 인데 재생 FAIL", judge_close(zero, last(), dict(want, play=False))["verdict"] == "FAIL"),
        ("사유 다름 FAIL", judge_close(zero, last(reason="done"), dict(want, reason="busy"))["verdict"] == "FAIL"),
        ("중간 프레임 PASS", judge_frames(5, 2)["verdict"] == "PASS"),
        ("중간 프레임 0 FAIL", judge_frames(5, 0)["verdict"] == "FAIL"),
        ("표본 없음 INVALID", judge_frames(0, 0)["verdict"] == "INVALID"),
        ("5차 MDI fade PASS", shown(mdi) == "PASS"),
        ("5차 DOCK 바탕 없음 FAIL", shown([(0.0, 0.0), (0.809, 3.08), (0.95, 1.2), (1.0, 0.0)]) == "FAIL"),
        ("바탕색 어긋남 FAIL", shown([(0.05, 2.5), (0.5, 1.5), (1.0, 0.0)]) == "FAIL"),
        ("늦게 잡힌 첫 표본은 바탕 FAIL 아님", shown([(0.0, 0.0), (0.7, 0.3), (1.0, 0.0)]) == "PASS"),
        ("한 번에 끝 모습 FAIL", shown([(0.0, 0.0), (1.0, 0.0)]) == "FAIL"),
        ("닫기 전 프레임뿐 INVALID", shown([(0.0, 0.0), (0.0, 0.0)]) == "INVALID"),
        ("관찰 안에 안 끝남 INVALID", shown([(0.003, 0.1), (0.4, 0.3)]) == "INVALID"),
        ("선 밖 프레임은 중간 아님", shown([(0.002, 0.1), (0.5, 2.0), (1.0, 0.0)]) == "FAIL"),
        ("이동 효과는 선 밖이어도 PASS", shown([(0.4, 9.0), (1.0, 0.0)], False) == "PASS"),
        ("대조군 실패면 gap INVALID", with_control(ok, result("FAIL", ""))["verdict"] == "INVALID"),
        ("대조군 없으면 gap INVALID", with_control(ok, None)["verdict"] == "INVALID"),
        ("대조군 PASS 면 gap PASS", with_control(ok, ok)["verdict"] == "PASS"),
        ("화면 미검사 표기", unseen(ok)["visibility"] == "unchecked"),
        ("되돌림 FAIL(r7 콤보: 1.0 → 0.28 → 0.86)", shown([(0.0, 0.0), (1.0, 0.0), (0.28, 3.0), (0.86, 1.6), (1.0, 0.0)]) == "FAIL"),
        ("0.1 이하 감소는 통과", shown([(0.0, 0.0), (0.3, 0.2), (0.25, 0.2), (0.6, 0.2), (1.0, 0.0)]) == "PASS"),
        ("구간 겹친 두 장은 순서 불명이라 안 셈", flicker([dict(alpha=0.9, t0=0, t1=130), dict(alpha=0.3, t0=20, t1=150)])[0] == 0),
        ("구간 안 겹치면 셈", flicker([dict(alpha=0.9, t0=0, t1=130), dict(alpha=0.3, t0=140, t1=260)])[0] == 0.6),
        ("대비 부족 INVALID", judge_trace([dict(p, q=0.2) for p in points((0.0, 0.0), (0.5, 0.2), (1.0, 0.0))], True)["verdict"] == "INVALID"),
        ("배율 2.25 확대 첫 화면 FAIL(Fold5 6차 실측)", shown([(0.0, 0.0), (0.243, 34.57), (0.9, 4.56), (1.0, 0.0)]) == "FAIL"),
        ("덮개 진단 1:1 PASS", judge_cover(cover, 2.25)["verdict"] == "PASS"),
        ("덮개 진단 없음 INVALID", judge_cover(None)["verdict"] == "INVALID"),
        ("예비 경로 FAIL", judge_cover(dict(cover, mode="dpi"))["verdict"] == "FAIL"),
        ("k≠1 FAIL", judge_cover(dict(cover, k=2.25))["verdict"] == "FAIL"),
        ("화소 부족 FAIL", judge_cover(dict(cover, px=[903, 582]))["verdict"] == "FAIL"),
        ("기기 배율 다름 FAIL", judge_cover(cover, 1.0)["verdict"] == "FAIL"),
        ("앞 창 불변 PASS", judge_occlusion([0, 0], [900, 20])["verdict"] == "PASS"),
        ("앞 창 침범 FAIL", judge_occlusion([0, 12], [900, 20])["verdict"] == "FAIL"),
        ("바깥 재생 없음 FAIL", judge_occlusion([0, 0], [3, 2])["verdict"] == "FAIL"),
        ("가림 표본 없음 INVALID", judge_occlusion([], [])["verdict"] == "INVALID"),
        ("남은 재생 FAIL", judge_residue(1)["verdict"] == "FAIL"),
        ("사례 0 INVALID", combine()["verdict"] == "INVALID"),
        ("기대 NAV 인데 PC형", layout_problem("nav", {"effective": "Pc"}) is not None),
        ("실효값 없음", layout_problem("nav", {}) is not None),
        ("NAV 맞음", layout_problem("nav", {"effective": "Mobile"}) is None),
        ("기대 DOCK 인데 MDI 창", layout_problem("dock", {"effective": "Tablet", "zReason": "ok"}) is not None),
        ("DOCK 맞음", layout_problem("dock", {"effective": "Tablet", "zReason": "notMdi"}) is None),
        ("기대 최대화인데 겹침", layout_problem("max", {"effective": "Pc", "zReason": "ok", "state": "Normal"}) is not None),
        ("문서 0개는 UI 형만", layout_problem("dock", {"effective": "Tablet"}, False) is None),
        ("INVALID 가 PASS 에 묻히지 않음", combine(a=judge_frames(0, 0), b=good)["verdict"] == "INVALID"),
        ("배율 계산 2.25(DIP 622.2·CSS 1400 가정 — 식 검사)",
         abs(css_scale(1400, 622.2) - 2.25) < 0.001
         and [round(v) for v in to_css({"x": 100, "y": 40, "w": 200, "h": 120}, css_scale(1400, 622.2)).values()] == [225, 90, 450, 270]),
        ("배율 1 은 사각형 그대로(DPR 1 판정 불변)", css_scale(1400, 1400) == 1.0
         and to_css({"x": 12.5, "y": 7, "w": 301, "h": 99}, 1.0) == {"x": 12.5, "y": 7, "w": 301, "h": 99}),
        ("앱 프레임 근거: motion.play 구간 dt 모음",
         frame_evidence([{"name": "other", "phase_raw": {"dt_ms": [99]}}, {"name": "motion.play", "phase_raw": {"dt_ms": [16.7, 340.2]}}], [15, 42, 604, 810])
         == {"app_play": {"frames": 2, "dt_max_ms": 340.2, "dt_ms": [16.7, 340.2]}, "screen_frames": 4, "screen_gap_max_ms": 562}),
        ("앱 프레임 근거: 구간 없음·화면 표본 없음", frame_evidence(None, [])["app_play"]["frames"] == 0
         and frame_evidence([], None)["screen_gap_max_ms"] is None),
        ("정체: 간격 < 재생 시간이면 FAIL 유지", stall_override(result("FAIL", "중간 프레임 0"), {"app_play": {"dt_max_ms": 319.9}}, 320.0)["verdict"] == "FAIL"),
        ("정체: 간격 = 재생 시간이면 INVALID", stall_override(result("FAIL", "중간 프레임 0"), {"app_play": {"dt_max_ms": 320.0}}, 320.0)["verdict"] == "INVALID"),
        ("정체: 간격 > 재생 시간이면 INVALID·근거 기록", stall_override(result("FAIL", "중간 프레임 0"), {"app_play": {"dt_max_ms": 340.2}}, 320.0)["stall_rule"]
         == {"rule_basis": STALL_RULE_BASIS, "applied": True, "dt_max_ms": 340.2, "play_ms": 320.0}),
        ("정체: 근거 기록 없음이면 FAIL", stall_override(result("FAIL", "중간 프레임 0"), {"app_play": {"frames": 0}}, 320.0)["verdict"] == "FAIL"
         and stall_override(result("FAIL", "중간 프레임 0"), None, 320.0)["verdict"] == "FAIL"),
        ("정체: 재생 시간 못 읽으면 FAIL", stall_override(result("FAIL", "중간 프레임 0"), {"app_play": {"dt_max_ms": 999}}, None)["verdict"] == "FAIL"),
        ("정체: 다른 FAIL 은 그대로", stall_override(result("FAIL", "바탕 없는 덮개(첫 변화부터 뒤 내용이 비친다)"), {"app_play": {"dt_max_ms": 999}}, 320.0)["verdict"] == "FAIL"),
        ("재생 시간 출처: effective motion.<수준>.ms", play_ms_of({"effective": {"motion": "cinematic", "motion.cinematic.ms": "320"}}) == 320.0
         and play_ms_of({"effective": {"motion": "cinematic"}}) is None and play_ms_of(None) is None),
        ("재생 시간 출처: 실제 state 모양(effective 에 motion.*.ms 없음, resources 에 있음)",
         play_ms_of({"effective": {"motion": "cinematic", "shadow": "inherit"}, "resources": {"PdStyleMotionMs": "320"}}) == 320.0),
        ("재생 시간 출처: 둘 다 없으면 None → 정체 규칙 FAIL 유지",
         play_ms_of({"effective": {"motion": "cinematic"}, "resources": {"PdStyleShadow": "x"}}) is None
         and stall_override(result("FAIL", "중간 프레임 0"), {"app_play": {"dt_max_ms": 676.6}},
                            play_ms_of({"effective": {"motion": "cinematic"}, "resources": {}}))["verdict"] == "FAIL"),
        ("재생 시간 출처: resources 0(inherit·none) 이면 None",
         play_ms_of({"effective": {"motion": "inherit"}, "resources": {"PdStyleMotionMs": "0"}}) is None),
        ("정체: 11차 dark_2 모양(간격 676.6, resources 320)이면 INVALID",
         stall_override(result("FAIL", "중간 프레임 0"), {"app_play": {"dt_max_ms": 676.6}},
                        play_ms_of({"effective": {"motion": "cinematic"}, "resources": {"PdStyleMotionMs": "320"}}))["verdict"] == "INVALID"),
        ("실제 배율 k=1·dpr 맞음은 정상", scale_problem("force", 1.0, 2.25, 2.25) is None),
        ("실제 배율인데 k 2.25 면 구성 이상", scale_problem("force", 2.25008, 2.25, 2.25) is not None),
        ("실제 배율인데 dpr 1 이면 구성 이상", scale_problem("force", 1.0, 1, 2.25) is not None),
        ("흉내·배율 1 은 점검 안 함", scale_problem("emulation", 2.25, 2.25, 2.25) is None and scale_problem("none", 1.0, 1, 1) is None),
        ("첫 변화 번호(판정과 같은 기준)", first_change(points((0.0, 0.0), (0.002, 0.1), (0.5, 0.2)), True) == 1
         and first_change(points((0.0, 0.0), (0.0, 0.0)), True) is None),
        ("DIP→CSS 정수 사각형·화면 자르기", css_box({"x": 100, "y": 40, "w": 200, "h": 120}, 2.25, (1400, 900)) == (225, 90, 675, 360)
         and css_box({"x": 600, "y": 0, "w": 100, "h": 10}, 2.25, (1400, 900)) == (1350, 0, 1400, 22) and css_box(None, 1, (10, 10)) is None),
        ("덮개 안·밖 나누기", split_counts(1000, 600, 900) == {"total": 1000, "cover": 600, "window_outside_cover": 300, "outside_window": 100}
         and split_counts(1000, 600, None) == {"total": 1000, "cover": 600, "outside_cover": 400}),
        ("레이아웃 너비 없음이면 배율 없음", css_scale(1400, None) is None and css_scale(1400, 0) is None),
    ]

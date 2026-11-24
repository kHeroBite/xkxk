# AC-010·AC-022 판정기(웹) — 실제 닫기(✕·명령·탭)를 배치·남은 수마다 걸어 덮개 재생·바탕, 앞 창 불침범, 진행 표시 중 생략을 판정한다
"""
사용
  motion_close_judge.py --bundle <wwwroot> --port <포트> --out <결과.json> [--only <사례 이름 앞부분,…>]
  motion_close_judge.py --self-test

실제 DockableClosing → DockableClosed 경로만 쓴다(motion.probe hold 로 만든 재생은 쓰지 않는다).
한 칸이라도 재생 0·중간 프레임 0·바탕 없는 덮개면 전체 FAIL, 화면 표본이 없거나 관찰 안에 끝나지 않으면 INVALID 다.
표본은 조작 시각으로 자르지 않고 첫 변화부터 끝 모습까지 본다(motion_close_rig.py).
close_all·busy_during 은 화면을 보지 않으므로 visibility=unchecked 로 남기고 재생이 보였다는 근거로 쓰지 않는다.
--only 는 선확인용이라 전체 PASS 를 내지 않는다(다 통과해도 RECORDED).
데스크톱은 실입력 누름을 쓰지 않으므로 이 판정기가 다루지 않는다(DEFERRED). 사례가 많아 몇 분 걸린다.
21 §4.1 정본 공백(NAV 비현재·DOCK 비활성)은 현행대로 재생 없음을 본다.
"""
import sys
import time

import judge_session as session
import motion_close_rules as rules
from motion_close_rig import PLACES, Rig

PLAY_MS = 320        # theme.style.motion.cinematic.ms 기본값(기록용) — 표본을 자르는 데 쓰지 않는다
GROUP_PLACEMENT = "MdiOverlap"  # MDI 안 탭 묶음 닫기의 기대 배치(11차 설계 21 §4.1: 묶음 창 전체·MDI 효과 키 — src GroupWindowOf → MdiPlacement(창), 이 사례는 겹침)
SETTLE_MS = 1500     # 바탕 사례의 효과 키·테마 적용 뒤 대기(종전 값 그대로). --settle-ms 로 바꿀 수 있다(확인 실험용)
EFFECTS = ("pushRight", "zoomIn", "tilt3d")
BASE_LOOKS = {       # 덮개 바탕 색 사례. 이름 → 효과 키
    "dark": {},
    "decor_grid": {"decor": "grid"},
    "decor_gradient": {"decor": "gradient"},
    "glass": {"surface": "glass"},
}
DARK = "pd-dark"
NOT_APPLICABLE = {"dock.button": "DOCK 문서에는 창 ✕ 가 없다(탭 닫기로 잰다)", "nav.tab": "NAV 에는 제목 탭이 없다",
                  "cancel": "닫기를 취소시키는 원격 경로가 없다(코드 경로만)",
                  "base.image": "웹 판정기는 로컬 파일 가져오기 경로가 없어 사용자 이미지를 넣지 않는다(무늬 grid·gradient 로 잰다)"}


def close_seen(rig, place, doc, act, effect, play=True, stage=None, label="close", enum=None, cover_window=False):
    """한 번 닫고 재생·잔류·화면을 판정한다."""
    box = rig.area(place, doc)
    rig.effect("motion.close", effect)
    rig.call("perf.reset")  # 재생 중 앱 프레임 근거(기록 전용) — 이 닫기의 motion.play 구간만 남게
    rects = doc_rects(rig, doc) if play else {}  # 기록 전용(첫 변화 차이 지도) — 닫기 전 문서 화면·창 사각형
    play_ms = rules.play_ms_of(rig.call("style.state")) if play else None  # 앱이 쓰는 재생 시간(정체 규칙 기준, 하드코딩 아님)
    seen = rig.observe(label, act)
    evidence = rules.frame_evidence(rig.call("perf.get").get("phases"), seen["ms"])  # 판정에 쓰지 않는다
    left = rig.ids()
    before, after = seen["before"], seen["after"]
    if doc in left and before and after and before.get("seq") == after.get("seq"):
        return rules.result("INVALID", "누름이 닫기에 닿지 않았다")
    shown = "fade" if place == "nav" and effect != "none" else effect  # 21 §4 NAV 닫기는 fade
    expect = dict(placement=enum or PLACES[place]["enum"], effect=shown, play=play, closed=doc not in left, stage=stage)
    parts = {"close": rules.judge_close(before, after, expect), "residue": rules.judge_residue(seen["released"])}
    if play:
        points = rules.trace(seen["frames"], seen["start"], seen["end"], box)
        parts["frames"] = dict(rules.judge_trace(points, fade=shown == "fade"), raw_sizes=seen["raw_sizes"], ref_source=seen.get("ref_source"),
                               ms=seen["ms"], points=[{k: p[k] for k in ("alpha", "off", "s", "e")} for p in points])
        parts["frames"] = rules.stall_override(parts["frames"], evidence, play_ms)  # 21 §7 안 A — 정체면 '중간 프레임 0' 을 INVALID 로
        parts["scale"] = rules.judge_cover((after or {}).get("cover"), rig.s.dpr)  # 첫 화면 1:1(7차)
        parts["frames"]["first_frame_map"] = first_frame_map(rig, seen, points, shown == "fade", rects, after, label,
                                                             parts["frames"]["verdict"] != "PASS", cover_window)
    return dict(rules.combine(**parts), left=len(left), evidence=evidence)


def doc_rects(rig, doc):
    """기록 전용: 닫기 전 문서 화면(tree.get doc 의 맨 위 노드, DIP)과 창 바깥 테두리(CSS). 못 읽으면 빈 값."""
    out = {}
    try:
        tree = rig.call("tree.get", doc=doc, named=False, depth=1)
        node = (tree.get("tree") or [{}])[0]
        out.update(view_type=tree.get("root"), view_dip=node.get("bounds"))
    except session.Abort as e:
        out["view_error"] = str(e)[:200]
    out["window_css"] = rig.rect("PART_OuterBorder", doc=doc)
    return out


def first_frame_map(rig, seen, points, fade, rects, after, label, save, cover_window=False):
    """
    기록 전용(판정 불변): 첫 변화 장(판정과 같은 Tc)의 처음 기준 그림 대비 차이 화소를 덮개 안·덮개 밖 창 안·창 밖으로 나눠 센다.
    덮개 사각형은 닫기 전 문서 화면(view_dip × k) — 덮개는 그 화면을 찍은 것이다(크기는 lastClose.cover.dest 와 대조해 남긴다).
    save 면 첫 변화 장과 차이 지도(흰색 = 밝기 차 > PIXEL)를 그림으로 남긴다(<label>.first.png·<label>.diff.png).
    """
    from PIL import ImageChops

    tc = rules.first_change(points, fade)
    if tc is None:
        return {"tc": None}
    frame, start = seen["frames"][tc], seen["start"]
    limit = start.size
    window = rects.get("window_css")
    window = session.box(window, 1.0) if window else None
    # 덮개 사각형: 보통은 닫기 전 문서 화면, MDI 안 탭 묶음은 묶음 창 전체(11차 설계 21 §4.1 — GroupWindowOf)
    cover = window if cover_window else rules.css_box(rects.get("view_dip"), rig.k, limit)
    counts = rules.split_counts(rules.diff_count(frame, start), cover and rules.diff_count(frame, start, cover),
                                window and rules.diff_count(frame, start, window))
    out = {"tc": tc, "ms": seen["ms"][tc], "k": rig.k, "cover_basis": "window" if cover_window else "view",
           "view_type": rects.get("view_type"), "view_dip": rects.get("view_dip"),
           "cover_css": cover, "cover_dest_dip": ((after or {}).get("cover") or {}).get("dest"), "window_css": window, "counts": counts}
    if rects.get("view_error"):
        out["view_error"] = rects["view_error"]
    if save:
        base = "%s/%s" % (rig.s.shots, label)
        frame.save(base + ".first.png")
        ImageChops.difference(frame, start).convert("L").point(lambda v: 255 if v > rules.PIXEL else 0).save(base + ".diff.png")
        out["pngs"] = [base + ".first.png", base + ".diff.png"]
    return out


def close_case(rig, place, count, path, effect="fade", play=True, stage=None):
    doc = rig.setup(place, count)
    if not doc:
        return rules.result("INVALID", "활성 문서가 없다")
    act = rig.closer(place, path, doc)
    if not act:
        return rules.result("INVALID", "누를 자리를 찾지 못했다: %s" % path)
    return close_seen(rig, place, doc, act, effect, play, stage, "%s.%s.%d.%s" % (place, path, count, effect))


def base_case(rig, where, look):
    """간트 위 입력폼을 닫는다. 첫 화면에 간트가 비치거나 바탕색이 어긋나면 FAIL. where = dock·nav·group(MDI 안 탭 묶음)."""
    theme = rig.call("settings.get").get("themeId")
    try:
        place = "overlap" if where == "group" else where
        doc = rig.setup(place, 2)
        if where == "group":
            other = next((d for d in rig.ids() if d != doc), None)
            rig.call("window.tabJoin", source=doc, target=other)
            time.sleep(1.5)
            if rig.active() != doc:
                return rules.result("INVALID", "묶음에서 입력폼이 활성이 아니다")
        for key, value in BASE_LOOKS[look].items():
            rig.effect(key, value)
        if look == "dark":
            rig.call("settings.set", themeId=DARK)
        time.sleep(rig.settle_ms / 1000.0)  # 효과 키·테마 적용 뒤 안정 대기(--settle-ms, 기본 1500 — 확인 실험용 옵션)
        return close_seen(rig, where, doc, lambda: rig.call("docs.close", doc=doc),
                          "fade", label="base.%s.%s" % (where, look), enum=GROUP_PLACEMENT if where == "group" else None,
                          cover_window=where == "group")
    finally:
        for key in BASE_LOOKS[look]:
            rig.s.web.call("style.effect.set", key=key, value="")
        if theme and look == "dark":
            rig.s.web.call("settings.set", themeId=theme)


def open_control(rig, place):
    rig.setup(place, 0)
    rig.effect("motion.open", "fade")
    rig.call("perf.reset")
    rig.call("docs.open", kind="form")
    time.sleep(3)
    names = [p.get("name") for p in rig.call("perf.get").get("phases") or []]
    ok = "motion.play" in names
    return rules.result("PASS" if ok else "INVALID", "열기 대조군 재생" if ok else "열기 대조군도 재생 0(측정기 고장)")


def gap_case(rig, place):
    active = rig.setup(place, 2)
    other = next((d for d in rig.ids() if d != active), None)
    if not other:
        return rules.result("INVALID", "현재가 아닌 문서가 없다")
    rig.effect("motion.close", "fade")
    seen = rig.observe(place + ".gap", lambda: rig.call("docs.close", doc=other))
    expect = dict(play=False, closed=other not in rig.ids())
    expect.update({"stage": "skipped", "reason": "gap"} if place == "nav" else {"stage": "noTarget"})
    kept = rules.result("PASS" if rig.active() == active else "FAIL", "현재 문서 유지")
    return rules.combine(close=rules.judge_close(seen["before"], seen["after"], expect), active=kept)


def occlusion_case(rig):
    front = rig.setup("overlap", 2)
    back = next((d for d in rig.ids() if d != front), None)
    b = rig.rect("PART_OuterBorder", doc=front)
    behind = rig.body(back) if back else None
    if not behind or not b:
        return rules.result("INVALID", "겹친 두 창을 만들지 못했다")
    x0, y0, x1, y1 = session.box(b, 1.0)
    m = round(4 * rig.k)  # 둥근 모서리·테두리 번짐(4 DIP)은 빼고 센다 — 사각형은 rig.rect 가 이미 CSS 로 바꿨다
    inner = (x0 + m, y0 + m, x1 - m, y1 - m)
    rig.effect("motion.close", "fade")
    seen = rig.observe("overlap.occlusion", lambda: rig.call("docs.close", doc=back))
    inside = [rules.diff_count(f, seen["start"], inner) for f in seen["frames"]]
    # '밖' = 뒤 창 − 앞 창. 로그·상태 줄 변화를 재생으로 세지 않는다
    outside = [min(rules.diff_count(f, seen["start"], behind, (x0, y0, x1, y1)),
                   rules.diff_count(f, seen["end"], behind, (x0, y0, x1, y1))) for f in seen["frames"]]
    expect = dict(placement="MdiOverlap", effect="fade", play=True, closed=back not in rig.ids())
    return rules.combine(close=rules.judge_close(seen["before"], seen["after"], expect),
                         cover=rules.judge_occlusion(inside, outside), residue=rules.judge_residue(seen["released"]))


def busy_case(rig, during):
    doc = rig.setup("overlap", 2)
    rig.effect("motion.close", "fade")
    try:
        if during:
            def act():
                rig.call("docs.close", doc=doc)
                rig.call("busy.begin", scope="main", reason="motion_close_judge")  # 덮개 시작 바로 뒤 — 표시 지연 150ms < 재생 320ms
        else:
            rig.call("busy.begin", scope="main", reason="motion_close_judge")
            time.sleep(1)  # 표시 지연(기본 150ms) 뒤 보이는 상태
            def act():
                rig.call("docs.close", doc=doc)
        seen = rig.observe("overlap.busy.%s" % ("during" if during else "before"), act)
        if during and (seen["after"] or {}).get("reason") == "done":
            return rules.result("INVALID", "진행 표시가 뜨기 전에 재생이 끝나 중단을 재지 못했다", lastClose=seen["after"])
        expect = dict(play=during, closed=doc not in rig.ids(), stage="ended" if during else "busy")
        if during:
            expect["reason"] = "busy"
        out = rules.combine(close=rules.judge_close(seen["before"], seen["after"], expect),
                            residue=rules.judge_residue(seen["released"]))
        return rules.unseen(out) if during else out
    finally:
        rig.s.web.call("busy.end", scope="main")


def close_all_case(rig):
    rig.setup("overlap", 3)
    rig.effect("motion.close", "fade")
    seen = rig.observe("overlap.all", lambda: rig.call("docs.close", all=True))
    expect = dict(play=True, closed=not rig.ids())
    return rules.unseen(rules.combine(close=rules.judge_close(seen["before"], seen["after"], expect),
                                      residue=rules.judge_residue(seen["released"])))


def judge(s, only=(), settle_ms=SETTLE_MS):
    if s.scope != "web":
        raise session.Abort("DEFERRED", "웹 전용이다(데스크톱 실입력 누름은 쓰지 않는다)")
    rig, cases = Rig(s), {}
    rig.scale_mode = s.scale_mode  # judge_session.Web 이 연 배율 방식(none·force·emulation)
    rig.settle_ms = settle_ms
    s.meta.update(settle_ms=settle_ms)
    if rig.last() is None:
        return {"lastClose": rules.result("INVALID", "이 판에 lastClose 진단점이 없다")}
    rig.effect("motion", "cinematic")
    rig.effect("motion.switch", "none")
    s.meta.update(not_applicable=NOT_APPLICABLE, play_ms=PLAY_MS, only=list(only))

    def run(name, case, *args, **kw):
        if only and not name.startswith(tuple(only)):
            return
        rig.case, rig.layout, rig.k = name, None, None  # 배율은 사례마다 그 회차 check_layout 이 새로 잰다
        try:
            cases[name] = case(rig, *args, **kw)
        except session.Abort as e:  # 한 칸의 실패가 나머지 칸을 가리지 않게
            cases[name] = rules.result("INVALID", str(e))
        cases[name]["layout"] = rig.layout
        print(name, cases[name]["verdict"], cases[name].get("why", ""), flush=True)

    for place, spec in PLACES.items():
        run(place + ".control_open", open_control, place)
        for count in (3, 2, 1):
            run("%s.left%d.command" % (place, count - 1), close_case, place, count, "command")
        for path in spec["paths"][1:]:
            run("%s.left1.%s" % (place, path), close_case, place, 2, path)
            run("%s.left0.%s" % (place, path), close_case, place, 1, path)
        for effect in EFFECTS:
            run("%s.effect.%s" % (place, effect), close_case, place, 2, "command", effect)
        run(place + ".none", close_case, place, 2, "command", "none", False, "none")
    for where in ("dock", "nav"):
        for look in ("dark", "decor_grid", "decor_gradient"):
            run("base.%s.%s" % (where, look), base_case, where, look)
    for look in ("dark", "glass"):
        run("base.group." + look, base_case, "group", look)
    run("nav.other", gap_case, "nav")
    if "nav.other" in cases:  # 전부 gap 이어도 PASS 가 되지 않게 같은 회차의 현재 문서 재생과 묶는다
        cases["nav.other"] = rules.with_control(cases["nav.other"], cases.get("nav.left1.command"))
    run("dock.inactive", gap_case, "dock")
    run("overlap.occlusion", occlusion_case)
    run("overlap.busy_before", busy_case, False)
    run("overlap.busy_during", busy_case, True)
    run("overlap.close_all", close_all_case)
    if only:
        cases["_selection"] = rules.result("RECORDED", "--only 선확인이라 전체 판정이 아니다")
    return cases


def main():
    ap = session.parser(__doc__)
    ap.add_argument("--only", default="", help="사례 이름 앞부분(쉼표) — 선확인용")
    ap.add_argument("--settle-ms", type=int, default=SETTLE_MS, help="바탕 사례(base.*)에서 효과 키·테마를 바꾼 뒤 닫기 전 대기 ms(기본 %d)" % SETTLE_MS)
    args = ap.parse_args()  # --scale-mode 는 judge_session.parser 가 준다(기본 force)
    if args.self_test:
        return session.run_cases(rules.rule_cases() + session_cases(ap))
    only = tuple(p for p in args.only.split(",") if p)
    return session.run(args, "motion_close_judge", lambda s: judge(s, only, args.settle_ms))


def session_cases(ap):
    """judge_session 배율 방식 자기시험(브라우저 없이)."""
    return [
        ("배율 1 은 none", session.scale_mode_of(1.0, "force") == "none"),
        ("배율 2.25 기본은 force", ap.parse_args(["--dpr", "2.25"]).scale_mode == "force"
         and session.scale_mode_of(2.25, "force") == "force"),
        ("흉내는 옵션으로만", session.scale_mode_of(2.25, "emulation") == "emulation"
         and ap.parse_args(["--dpr", "2.25", "--scale-mode", "emulation"]).scale_mode == "emulation"),
    ]


if __name__ == "__main__":
    sys.exit(main())

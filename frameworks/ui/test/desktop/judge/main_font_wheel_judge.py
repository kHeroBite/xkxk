# B4 판정기 — 메인 창 크롬(메뉴 줄·탐색기·MDI 머리) 위 Ctrl+휠이 시스템 글자 크기를 ±1 하고 저장되며, 문서·SDI 위 동작은 그대로인지 판정한다
"""
사용
  main_font_wheel_judge.py --info <격리>/remote-control.json --approved '<누가·언제>' --out <c27>/b4.json [--results <c27>/RESULTS.json] [--keep N]
  main_font_wheel_judge.py --info … --approved … --out <c27>/b4_restart.json --expect-shell <값>   (재기동 뒤 저장값 유지 확인)
  main_font_wheel_judge.py --self-test
기본 remote_only(input.wheel = 앱 내부 휠 이벤트, 실입력 없음 → PC 비움 불요). 깨끗한 격리 인스턴스(문서 0개)에서 돈다.
보는 값  화면: 메뉴 '파일'(shell.mnuFile) FontSize(ui.find). 저장: 격리 data-dir settings.json 의 ShellFontSize. 문서: settings.get baseFontSize·activeDocZoom.
사례  메뉴 줄·탐색기·MDI 머리 위 = 글자 정확히 ±1·저장 / MDI·SDI 문서 위 = 문서만 변하고 시스템 글자 불변 / SDI 머리 위 = 무시 /
      경계(24·10)에서 더 굴려도 고정 / 끝나면 원래 값으로 되돌린다(--keep N 이면 +N 을 남기고 결과 meta.kept_shell 에 적는다).
성능은 perf.ops 의 ui.font.shellSize 를 meta.perf_shell_font 에 남긴다(판정 아님).
"""
import sys
import time

import judge_session as session
from c27_judge_common import add_c27_args, run_c27, saved_shell_size, shown_font, verdict

SAVE_WAIT_S = 2.0  # 저장 합류(지연 저장) 뒤 파일을 읽을 때까지
METHOD = ("격리 진단 Release 에 원격 input.wheel(Ctrl, 앱 내부 휠 이벤트)을 메뉴 줄·탐색기·MDI 머리·MDI/SDI 문서·SDI 머리에 보내 "
          "메뉴 FontSize(ui.find)·settings.json ShellFontSize·문서 글자/배율 전후 비교, 경계 고정·원복 — remote_only")


def judge_step(before, after, want):
    """화면 글자가 정확히 want(±1)만큼 변했는가."""
    if before is None or after is None:
        return {"verdict": "INVALID", "why": "글자 크기를 읽지 못했다", "before": before, "after": after}
    return verdict({} if after - before == want else {"변화": [before, after, want]}, "시스템 글자 %+d" % want, before=before, after=after)


def judge_regress(shell_before, shell_after, doc_before, doc_after, doc_should_move):
    """문서·SDI 위: 시스템 글자 불변, 문서 값은 doc_should_move 대로."""
    bad = {}
    if shell_before != shell_after:
        bad["시스템 글자 변함"] = [shell_before, shell_after]
    if (doc_before != doc_after) != doc_should_move:
        bad["문서 값"] = [doc_before, doc_after, "변해야 함" if doc_should_move else "그대로여야 함"]
    return verdict(bad, "문서 위 동작 무변경", shell=[shell_before, shell_after], doc=[doc_before, doc_after])


def judge_saved(saved_before, saved_after, want):
    if saved_after is None:
        return {"verdict": "FAIL", "why": "settings.json 에 ShellFontSize 가 저장되지 않았다"}
    base = 14.0 if saved_before is None else saved_before  # 첫 저장 전이면 기본값 14(UiSettings.DefaultFontSize)
    return verdict({} if saved_after - base == want else {"저장값": [saved_before, saved_after, want]}, "저장값 %+d" % want)


def judge_clamp(values):
    """끝까지 굴린 값 열. 마지막 두 값이 같고(고정) 그 전까지는 1씩 변했어야 한다."""
    if len(values) < 3:
        return {"verdict": "INVALID", "why": "표본 부족", "values": values}
    steps = [b - a for a, b in zip(values, values[1:])]
    moving, last = steps[:-1], steps[-1]
    ok = last == 0 and all(abs(x) in (0, 1) for x in moving) and any(moving)
    return verdict({} if ok else {"값 열": values}, "경계에서 고정", values=values)


def doc_state(s):
    g = s.call("settings.get")
    return [g.get("baseFontSize"), g.get("activeDocZoom")]


def wheel(s, dy, **target):
    r = s.call("input.wheel", ctrl="true", dy=dy, **target)
    time.sleep(0.4)
    return r


def step_case(s, d, **target):
    """target 위 Ctrl+휠 d → 화면 ±d 판정 후 되돌린다."""
    before = shown_font(s)
    try:
        wheel(s, d, **target)
    except session.Abort as e:
        return {"verdict": "INVALID", "why": "휠을 보낼 수 없다: %s" % e, "target": target}
    after = shown_font(s)
    wheel(s, -d, **target)
    return dict(judge_step(before, after, d), target=target)


def regress_case(s, d, should_move, **target):
    shell0, doc0 = shown_font(s), doc_state(s)
    try:
        wheel(s, d, **target)
    except session.Abort as e:
        return {"verdict": "INVALID", "why": "휠을 보낼 수 없다: %s" % e, "target": target}
    out = dict(judge_regress(shell0, shown_font(s), doc0, doc_state(s), should_move), target=target)
    wheel(s, -d, **target)
    return out


def pick_explorer(panels):
    """panel.list 결과에서 보이는 탐색기 패널 Id — 탭에 Explorer 가 있거나 왼쪽 패널. 못 찾으면 None."""
    rows = panels if isinstance(panels, list) else (panels or {}).get("panels") or []
    rows = [p for p in rows if isinstance(p, dict) and p.get("visible") is not False and p.get("id")]
    hit = next((p for p in rows if "explorer" in str(p.get("tabs", "")).lower()), None)
    hit = hit or next((p for p in rows if str(p.get("side", "")).lower() == "left"), None)
    return hit.get("id") if hit else None


def explorer_dock(s):
    """panel.list 로 탐색해 찾고, 못 찾으면 기본 배치 Id(DockLayoutBuilder 의 LeftPane)를 쓴다(없으면 input.wheel 이 INVALID 로 드러낸다)."""
    try:
        return pick_explorer(s.call("panel.list")) or "LeftPane"
    except session.Abort:
        return "LeftPane"


def first_found(s, names):
    for name in names:
        try:
            s.call("ui.find", id=name)
            return name
        except session.Abort:
            continue
    return None


def judge_expect(s, args):
    saved = saved_shell_size(args.info)
    return {"b4.restart_saved": verdict({} if saved == float(args.expect_shell) else {"저장값": [saved, args.expect_shell]},
                                        "재기동 뒤 시스템 글자 유지", saved=saved, shown=shown_font(s))}


def judge(s, args):
    if args.expect_shell is not None:
        return judge_expect(s, args)
    if s.call("window.load.state").get("docs"):
        raise session.Abort("INVALID", "문서가 열려 있다(깨끗한 격리 폴더로 다시)")
    cases, f0, saved0 = {}, shown_font(s), saved_shell_size(args.info)
    d = 1 if (f0 or 14) < 23 else -1
    try:
        cases["b4.main.menu_bar"] = step_case(s, d, id="shell.mnuFile")
        left = explorer_dock(s)
        cases["b4.main.explorer"] = step_case(s, d, dock=left) if left else {"verdict": "INVALID", "why": "보이는 탐색기(왼쪽) 패널 없음(panel.list)"}
        wheel(s, d, id="shell.mnuFile")  # 저장 확인: 순변화 d 를 남기고 잠시 기다린다
        time.sleep(SAVE_WAIT_S)
        cases["b4.saved"] = judge_saved(saved0, saved_shell_size(args.info), d)
        s.capture("after_step")
        wheel(s, -d, id="shell.mnuFile")
        time.sleep(SAVE_WAIT_S)
        cases["b4.saved_revert"] = judge_saved(saved0, saved_shell_size(args.info), 0)
        s.call("docs.open", kind="grid")
        time.sleep(2.5)
        mdi = s.call("window.load.state")["docs"][-1]["id"]
        head = first_found(s, ("PdDocIcon",))
        cases["b4.main.mdi_header"] = step_case(s, d, id=head) if head else {"verdict": "INVALID", "why": "MDI 머리 식별자 없음"}
        cases["b4.doc.mdi"] = regress_case(s, d, True, id="grid.dgMain", doc=mdi)
        s.call("docs.open", kind="grid", sdi="true")
        time.sleep(3)
        sdi = s.call("window.load.state")["docs"][-1]["id"]
        cases["b4.doc.sdi"] = regress_case(s, d, True, id="grid.dgMain", doc=sdi)
        sdi_head = first_found(s, ("DragArea", "DocIcon", "PdSdiTitleIcon"))  # DragArea 우선 — 아이콘·제목은 Border 에 가려 히트가 안 닿는다
        cases["b4.sdi.header_ignored"] = (regress_case(s, d, False, id=sdi_head) if sdi_head
                                          else {"verdict": "INVALID", "why": "SDI 머리 식별자 없음"})
        s.capture("docs_open")
        s.call("docs.close", all=True)
        time.sleep(1)
        values, edge = [shown_font(s)], 1 if d > 0 else -1
        for _ in range(16):  # 경계까지 굴린 뒤 한 번 더
            wheel(s, edge, id="shell.mnuFile")
            values.append(shown_font(s))
            if len(values) >= 3 and values[-1] == values[-2]:
                break
        cases["b4.clamp"] = judge_clamp(values)
        while shown_font(s) is not None and f0 is not None and shown_font(s) != f0 and len(values) < 40:
            wheel(s, -edge, id="shell.mnuFile")  # 원복
            values.append(shown_font(s))
        try:
            ops = s.rig.ops("ui.font.shellSize")
        except Exception as e:  # 계측 이름이 없어도 판정에는 영향 없음
            ops = repr(e)
        s.meta["perf_shell_font"] = ops  # 참고 기록(판정 아님) — 사례에 넣으면 RECORDED 가 전체 판정을 덮는다
        if args.keep:
            for _ in range(args.keep):
                wheel(s, 1, id="shell.mnuFile")
            time.sleep(SAVE_WAIT_S)
            s.meta["kept_shell"] = saved_shell_size(args.info)  # 재기동 뒤 --expect-shell 에 줄 값
    finally:
        s.call("docs.close", all=True)
    return cases


def self_test():
    return session.run_cases([
        ("+1 PASS", judge_step(14, 15, 1)["verdict"] == "PASS"),
        ("+2 FAIL", judge_step(14, 16, 1)["verdict"] == "FAIL"),
        ("무변화 FAIL", judge_step(14, 14, 1)["verdict"] == "FAIL"),
        ("못 읽음 INVALID", judge_step(None, 14, 1)["verdict"] == "INVALID"),
        ("문서 회귀 PASS", judge_regress(14, 14, [14, 1], [15, 1], True)["verdict"] == "PASS"),
        ("문서 위인데 시스템 변함 FAIL", judge_regress(14, 15, [14, 1], [15, 1], True)["verdict"] == "FAIL"),
        ("문서가 안 변함 FAIL", judge_regress(14, 14, [14, 1], [14, 1], True)["verdict"] == "FAIL"),
        ("SDI 머리 무시 PASS", judge_regress(14, 14, [14, 1], [14, 1], False)["verdict"] == "PASS"),
        ("저장 PASS", judge_saved(14, 15, 1)["verdict"] == "PASS"),
        ("첫 저장 기본 14 기준", judge_saved(None, 15, 1)["verdict"] == "PASS"),
        ("저장 안 됨 FAIL", judge_saved(14, None, 1)["verdict"] == "FAIL"),
        ("되돌림 저장 PASS", judge_saved(14, 14, 0)["verdict"] == "PASS"),
        ("경계 고정 PASS", judge_clamp([22, 23, 24, 24])["verdict"] == "PASS"),
        ("경계 넘음 FAIL", judge_clamp([23, 24, 25, 26])["verdict"] == "FAIL"),
        ("한 번에 2 FAIL", judge_clamp([20, 22, 24, 24])["verdict"] == "FAIL"),
        ("처음부터 고정 FAIL", judge_clamp([24, 24, 24])["verdict"] == "FAIL"),
        ("탐색기 패널: 탭에 Explorer", pick_explorer([{"id": "BottomPane", "side": "Bottom", "tabs": ["Log"]},
                                                     {"id": "LeftPane", "side": "Left", "tabs": ["Explorer"]}]) == "LeftPane"),
        ("탐색기 패널: 왼쪽 대체·숨김 제외", pick_explorer({"panels": [{"id": "X", "side": "Left", "visible": False},
                                                                   {"id": "L2", "side": "Left"}]}) == "L2"),
        ("탐색기 패널 없음 None", pick_explorer(None) is None),
    ])


def main():
    ap = add_c27_args(session.parser(__doc__))
    ap.add_argument("--keep", type=int, default=0, help="끝에 +N 을 남긴다(재기동 유지 확인용, 결과 meta.kept_shell)")
    ap.add_argument("--expect-shell", type=float, help="재기동 뒤 확인 전용: 저장값이 이 값인지만 본다(휠 없음)")
    ap.set_defaults(remote_only=True)
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    item = "B4_restart" if args.expect_shell is not None else "B4"
    return run_c27(args, "main_font_wheel_judge", lambda s: judge(s, args), {item: ("b4.", METHOD)})


if __name__ == "__main__":
    sys.exit(main())

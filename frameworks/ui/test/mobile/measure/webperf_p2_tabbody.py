# P2(탭 본문 반응) 측정 래퍼 — webperf_startup.py 를 고치지 않고 import 해 tab_body 의 두 번째 문서만 간트로 바꿔 webperf_fold5 를 돌리고 emit 한다
"""
사용:
  python3 webperf_p2_tabbody.py run <webperf_fold5.py 인자...>      tab_body 만 덮어쓴 채 webperf_fold5.main() 을 돌린다(나머지 동작은 원본 그대로)
  python3 webperf_p2_tabbody.py emit <webperf_startup.py emit 인자...>  원본 emit(build_result 포함)을 그대로 쓰고 결과 script 를 이 래퍼로 남긴다
  python3 webperf_p2_tabbody.py --self-test

왜 래퍼인가:
  원본 tab_body 는 그리드 두 개(Grid-1·Grid-2)를 열고 탭을 바꾼다. 두 문서의 본문 화소가 같아 ROI 변화가 없고 '반응 없음'이 된다
  (otest-m-8 11-08 녹화 실측: 선택 탭·상태 줄은 정상 전환). P1·P3 는 원본 sha 로 판정됐으므로 원본 파일은 바꾸지 않는다.
두 번째 문서를 간트로 고른 근거:
  P2 정의(goal.json P2 desc, oplan r2 §5 P2, pcaway_gate.py SPEC P2)는 '탭 전환을 본문 영역 ROI 로 잰다'만 요구하고 문서 종류를 정하지 않는다.
  같은 종류 문서를 요구하지 않으므로 본문 모양이 확실히 다른 간트를 쓴다(그리드 두 개는 데이터가 같아 화소 차이가 없다).
  잰 전환은 매번 간트(활성)→그리드(Grid-1) 한 방향이고, 측정 뒤 간트로 되돌린다.
고정:
  원본 두 모듈의 sha256 을 아래에 고정하고 다르면 실행을 거부한다. 따라서 이 래퍼의 sha 가 측정 코드 전체를 대표한다.
"""
import hashlib
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

PINNED = {
    "webperf_startup.py": "7a9362a02cdf21f5a874343048cb7c74f05bef2e3cc60dc3732fe326bf0e0601",
    "webperf_fold5.py": "837430ee1a2bc325b62782fe5b660775b05faa0bd74833c153bcc9d1ac441c57",
}
SECOND_KIND = "gantt"
TAB1_TITLE, TAB2_TITLE = "Grid-1", "Gantt-1"


def _sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def check_pins():
    bad = {n: _sha(os.path.join(HERE, n)) for n, want in PINNED.items() if _sha(os.path.join(HERE, n)) != want}
    if bad:
        raise SystemExit("거부: 원본 모듈 sha 가 고정값과 다르다 %s" % json.dumps(bad))


def open_second(c, tap, find, dump_nodes):
    """메뉴 → 간트 항목으로 두 번째 문서를 연다(재지 않음). 웹은 discover 가 모은 앱 덤프 좌표 item_gantt 를 쓴다."""
    import webperf_startup as ws
    tap(*c.pt(c.ids["opener"]))
    time.sleep(2.5 if c.web else 1.8)
    b = (c.ids.get("item_" + SECOND_KIND) if c.web else None) or find(dump_nodes(), ws._open_id(c, SECOND_KIND))
    if not b:
        tap(*c.pt(c.ids["opener"]))
        raise RuntimeError("간트 메뉴 항목 좌표 없음")
    tap(*c.pt(b))
    time.sleep(5 if c.web else 3)


def make_do_ext(orig):
    import webperf_startup as ws

    def do_ext(c, act, keep, measure, tap, tap_hold, swipe, fg_guard, find, dump_nodes, center):
        if act != "tab_body":
            return orig(c, act, keep, measure, tap, tap_hold, swipe, fg_guard, find, dump_nodes, center)
        ws._ensure_web_settings(c)
        fg_guard(c)
        if c.a.mode != "open":
            return {"name": act, "skipped": "펼침 전용"}

        def one(cond):
            # 원본 one() 과 같은 순서다. 다른 점은 두 번째 문서가 간트라는 것과 탭 노드 이름(Gantt-1)뿐이다.
            if cond:
                c.cdp.eval("(()=>{const k=%s;const o=JSON.parse(localStorage.getItem(k)||'{}');o.TabHeaderFirst=%s;localStorage.setItem(k,JSON.stringify(o))})()"
                           % (json.dumps(ws.SETTINGS_KEY), "true" if cond == "on" else "false"))
                c.cdp.call("Page.reload")
                time.sleep(22)
                c.open_doc()
                open_second(c, tap, find, dump_nodes)
                n = dump_nodes()
                c.ids["tab1"] = find(n, TAB1_TITLE, cls="DocumentTabStripItem") or c.ids["tab1"]
                c.ids["tab2"] = find(n, TAB2_TITLE, cls="DocumentTabStripItem") or c.ids["tab2"]
                fg_guard(c)
            x, y = c.pt(c.ids["tab1"])
            row = measure(c.rec, c.tmp, "tab_body_" + cond if cond else act, lambda: tap_hold(x, y), c.doc_area(), (x, y), 2.5, keep)
            tap_hold(*c.pt(c.ids["tab2"]))
            time.sleep(1.5)
            row["tab_header_first"] = cond
            row["second_doc"] = SECOND_KIND
            return row
        if not c.a.tab_cross:
            return one(None)
        i = c.run["i"] if getattr(c, "run", None) else 0
        order = ("on", "off") if i % 2 == 0 else ("off", "on")
        first = one(order[0])
        first["order"] = "%s>%s" % order
        c.run["rows"].append(first)
        last = one(order[1])
        last["order"] = "%s>%s" % order
        return last
    return do_ext


def run(argv):
    check_pins()
    import webperf_startup as ws
    import webperf_fold5 as wf
    ws.do_ext = make_do_ext(ws.do_ext)
    sys.argv = [os.path.join(HERE, "webperf_fold5.py")] + argv
    return wf.main()


def emit(argv):
    check_pins()
    import webperf_startup as ws
    ws.__file__ = os.path.abspath(__file__)  # 결과 script 를 이 래퍼(path·sha)로 남긴다 — 판정 로직(build_result)은 원본 그대로
    sys.argv = [os.path.abspath(__file__), "emit"] + argv
    return ws.main()


def self_test():
    check_pins()
    import webperf_startup as ws
    calls = []
    orig = lambda *a: calls.append(a[1]) or {"name": a[1]}  # noqa: E731
    f = make_do_ext(orig)
    assert f(None, "open_grid", None, None, None, None, None, None, None, None, None) == {"name": "open_grid"} and calls == ["open_grid"]

    class A:
        mode, tab_cross, web_setting, web_url_query = "closed", True, "", ""

    class C:
        a, web = A(), True
    r = f(C(), "tab_body", None, None, None, None, None, lambda c: None, None, None, None)
    assert r.get("skipped"), r
    assert ws._self_test() == 0
    print("wrapper self-test OK")
    return 0


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--self-test":
        sys.exit(self_test())
    if len(sys.argv) > 1 and sys.argv[1] in ("run", "emit"):
        sys.exit((run if sys.argv[1] == "run" else emit)(sys.argv[2:]))
    print(__doc__)
    sys.exit(2)

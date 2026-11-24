#!/usr/bin/env python3
# D-13 테마 전환 뒤 글자색 시험 — 시작 테마별 새 격리 프로필로 진단판을 띄우고 theme.select 뒤 요소의 실제 글자색이 대상 테마 색과 같은지 본다
"""
사용:
  python3 -B d13_theme_text_color.py --run-dir <bin/run/<이름>/net11.0> --work <결과 폴더> --out d13_fix.json [--src-root poc/ui/src] [--src-hash <동결 해시>]
  python3 -B d13_theme_text_color.py --run-dir … --work … --out d16_fix.json --d16 [--src-hash …]   (D-16 menuBar 색 지정·대비 경고)
  python3 -B d13_theme_text_color.py --self-test
사례: 시작 테마 × 대상 테마 × 요소(CASES 표). 사례 하나마다(시작·대상 짝마다) 새 프로필(settings.json 의 ThemeId=시작 테마)로 새로 띄운다.
기대값: 대상 테마의 그 요소 글자색 역할 토큰 색(themedesign.elements 의 foreground 역할 → themedesign.get 의 templateColors).
새 프로필이라 요소별 사용자 지정이 없으므로 기대값은 템플릿 색이다(REQ-101 사용자 값 우선은 K8·K9 user_override_wins 가 본다).
판정: computed.Foreground == expected(대소문자 무시). 선택 직후 프레임 지연을 감안해 WAIT_S 뒤 읽고, 다르면 SETTLE_S 까지 0.5초 간격으로 다시 읽는다.
대상 컨트롤이 화면에 없으면(no-target) 그 사례는 blocked 로 따로 적고 cases 에 넣지 않는다(PASS 로 치지 않는다).
안전: mdi_drag_app 과 같은 격리 기동(--multi-instance on, 임시 data·profile, 화면 밖 --bg-pos, 연결 정보는 임시 폴더 파일만).
     사용자 Pd 앱·포트 5180·5220 을 건드리지 않는다. 종료는 명령줄에 자기 임시 폴더가 있는 PID 만(taskkill /PID). pkill·/IM 없음.
결과: {src_hash, run_dir, verdict(PASS|FAIL|UNDET), fails, cases:[{start,target,element,computed,expected,settle_s}], blocked:[…]}
rc: 0=전 사례 일치, 1=불일치·blocked 있음, 2=기동·연결 실패·인자 오류.
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()

# 시작 테마 → [(대상 테마, [요소…])]. 같은 밝기 변형(밝게→밝게)과 다른 변형(밝게↔어둡게)을 모두 넣는다.
CASES = [
    # menuBar 는 넣지 않는다: 첫 MenuItem 이 제목줄(MainTitleBar) 안에 있어 글자색이 PdBrushMainTitleText 다(카탈로그 foreground 역할 Text 와 다름, D-13 과 무관).
    ("pd-ocean", "pd-light", ["button", "input", "gridHeader", "gridCell"]),
    ("pd-light", "pd-ocean", ["button", "input", "gridHeader"]),
    ("pd-dark", "pd-light", ["button", "input"]),
    ("pd-ocean", "pd-dark", ["button", "input"]),
    ("pd-dark", "pd-contrast", ["button", "input"]),
]
WAIT_S = 1.5
SETTLE_S = 5.0
EXTRA_ARGS = ["--open", "grid:1"]  # gridCell 대상(그리드 문서 1개). 없으면 gridCell 은 blocked
USER_PORTS = (5180, 5220)
DARK_IDS = ("pd-dark", "pd-contrast", "pd-ember", "pd-aurora")  # BuiltinThemes 의 ThemeBase.Dark 내장 테마(시작 프로필 모드 결정용)


def same(a, b):
    return bool(a) and str(a).lower() == str(b or "").lower()


def foreground_token(elements, el):
    """themedesign.elements 결과에서 요소의 foreground 역할 토큰을 templateColors 키(PdBrush 접두 제거)로 돌려준다."""
    for e in elements or []:
        if e.get("el") == el:
            tok = (e.get("colors") or {}).get("foreground")
            if tok:
                return tok[len("PdBrush"):] if tok.startswith("PdBrush") else tok
    return None


def verdict_of(cases, blocked):
    if not cases:
        return "UNDET"
    return "PASS" if all(same(c["computed"], c["expected"]) for c in cases) and not blocked else "FAIL"


def self_test():
    els = [{"el": "button", "colors": {"foreground": "PdBrushText", "background": "PdBrushButton"}}]
    assert foreground_token(els, "button") == "Text"
    assert foreground_token(els, "input") is None
    assert same("#10263b", "#10263B") and not same(None, "#000000") and not same("#10263B", "#000000")
    ok = [{"computed": "#000000", "expected": "#000000"}]
    assert verdict_of(ok, []) == "PASS"
    assert verdict_of(ok + [{"computed": "#10263B", "expected": "#000000"}], []) == "FAIL"
    assert verdict_of(ok, [{"element": "gridCell"}]) == "FAIL"
    assert verdict_of([], []) == "UNDET"
    assert sum(len(t[2]) for t in CASES) >= 9
    assert ("pd-ocean", "pd-light") in {(s, t) for s, t, _ in CASES}
    assert abs(wcag_ratio("#000000", "#FFFFFF") - 21.0) < 0.01 and abs(wcag_ratio("#777777", "#777777") - 1.0) < 1e-9
    assert wcag_ratio(*reversed(D16_PAIRS[0])) >= 4.5 > wcag_ratio(*reversed(D16_PAIRS[1]))  # 경고 기대 거짓·참 1쌍씩
    print("self-test OK")
    return 0


def launch(app, run_dir, work, start, tag):
    """mdi_drag_app.launch 와 같은 격리 기동에 시작 테마 settings.json 을 미리 넣는다. (pid, data 폴더) 를 돌려준다."""
    import subprocess
    run_dir = os.path.abspath(run_dir)
    exe = os.path.join(run_dir, "Pd.Poc.Desktop.exe")
    if "/bin/run/" not in run_dir.replace("\\", "/") or not os.path.exists(exe):
        sys.exit("bin/run 복사본만 실행한다: %s" % run_dir)
    base = os.path.abspath(os.path.join(work, tag))
    data, profile = os.path.join(base, "data"), os.path.join(base, "profile")
    os.makedirs(data, exist_ok=True)
    os.makedirs(profile, exist_ok=True)
    with open(os.path.join(profile, "settings.json"), "w", encoding="utf-8") as f:
        json.dump({"Theme": "Dark" if start in DARK_IDS else "Light", "ThemeId": start}, f)  # 모드가 테마 밝기와 다르면 짝 테마로 바뀐다(ThemeCatalog.Resolve)
    log_path = os.path.join(base, "app.log")
    args = [exe, "--multi-instance", "on", "--remote-control", "--data-dir", app.win(data), "--profile-dir", app.win(profile),
            "--bg-pos", app.BG_POS, "--primary-override", app.PRIMARY, "--offscreen-guard", "on", "--log", app.win(log_path)] + EXTRA_ARGS
    with open(os.path.join(base, "stdio.log"), "ab") as out:
        subprocess.Popen(args, cwd=run_dir, stdin=subprocess.DEVNULL, stdout=out, stderr=out, start_new_session=True)
    info = os.path.join(data, "remote-control.json")
    for _ in range(90):
        bad = app.log_violation(log_path)
        if bad:
            sys.exit("단일 인스턴스 흔적 — 즉시 중단: " + bad)
        if os.path.exists(info) and os.path.getsize(info) > 0:
            break
        time.sleep(1)
    else:
        sys.exit("연결 정보 파일이 생기지 않았다: " + info)
    with open(info, encoding="utf-8") as f:
        port = (json.load(f) or {}).get("port")
    if port in USER_PORTS:
        sys.exit("사용자 포트에 붙으려 함 — 중단: %s" % port)
    os.environ["PD_REMOTE_INFO"] = info
    from retest_remote import Remote
    remote = Remote()
    status = None
    for _ in range(60):
        status = remote.call("status")
        if status and status.get("pid"):
            break
        time.sleep(1)
    pid = (status or {}).get("pid")
    cmd = app.ps("(Get-CimInstance Win32_Process -Filter \"ProcessId=%s\").CommandLine" % pid) if pid else ""
    if app.win(data) not in cmd:
        sys.exit("연결된 앱의 명령줄에 임시 폴더가 없다 — 남의 프로세스일 수 있어 중단: pid=%s" % pid)
    return remote, pid, app.win(data)


def read_fg(remote, el):
    r = remote.call("themedesign.get", el=el)
    if r is None:
        return None, None, None
    return ((r.get("computed") or {}).get("Foreground"), r.get("templateColors") or {}, r.get("themeId"))


def run_pair(app, a, start, target, elements, stamp):
    tag = "%s_%s__%s" % (stamp, start, target)
    remote, pid, marker = launch(app, a.run_dir, a.work, start, tag)
    cases, blocked = [], []
    try:
        time.sleep(2)  # 첫 화면·문서 열림 대기
        catalog = remote.call("themedesign.elements") or []
        cur = (remote.call("settings.get") or {}).get("themeId")
        if cur != start:
            blocked.append({"start": start, "target": target, "element": "*", "why": "시작 테마가 %s 가 아님(%s)" % (start, cur)})
            return cases, blocked
        remote.call("theme.select", id=target)
        time.sleep(WAIT_S)
        for el in elements:
            tok = foreground_token(catalog, el)
            t0 = time.time()
            fg, tpl, tid = read_fg(remote, el)
            while fg is not None and tok and not same(fg, tpl.get(tok)) and time.time() - t0 < SETTLE_S:
                time.sleep(0.5)
                fg, tpl, tid = read_fg(remote, el)
            if fg is None or not tok:
                blocked.append({"start": start, "target": target, "element": el, "why": "대상 컨트롤 없음 또는 조회 실패(tok=%s)" % tok})
                continue
            cases.append({"start": start, "target": target, "element": el, "computed": fg, "expected": tpl.get(tok),
                          "token": tok, "themeId": tid, "settle_s": round(WAIT_S + time.time() - t0, 2)})
            print("  %s %s→%s %s computed=%s expected=%s" % ("✓" if same(fg, tpl.get(tok)) else "✗", start, target, el, fg, tpl.get(tok)))
    finally:
        print("  " + app.stop(pid, marker))
    return cases, blocked


# D-16(--d16): menuBar 색 역할 토큰에 색 쌍을 지정해 메뉴 글자색 반영과 대비 경고를 본다. 경고 기대는 지정한 두 색의 WCAG 비율 < min.
D16_PAIRS = [("#1F2937", "#F9FAFB"), ("#6B7280", "#7C838F")]  # (바탕, 글자) — 높은 대비 1쌍(경고 없음 기대)·낮은 대비 1쌍(경고 기대)


def wcag_ratio(fg, bg):
    def lum(h):
        h = h.lstrip("#")[-6:]
        c = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
        c = [x / 12.92 if x <= 0.03928 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
        return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]
    a, b = sorted((lum(fg), lum(bg)), reverse=True)
    return (a + 0.05) / (b + 0.05)


def run_d16(app, a, stamp):
    """새 프로필(pd-ocean) → theme.clone → 쌍마다 theme.set <바탕 역할 토큰>=bg,<글자 역할 토큰>=fg → menuBar computed·contrast."""
    remote, pid, marker = launch(app, a.run_dir, a.work, "pd-ocean", stamp + "_d16")
    cases = []
    try:
        time.sleep(2)
        roles = next((e.get("colors") or {} for e in (remote.call("themedesign.elements") or []) if e.get("el") == "menuBar"), {})
        strip = lambda t: t[len("PdBrush"):] if t and t.startswith("PdBrush") else t  # noqa: E731
        bg_tok, fg_tok = strip(roles.get("background")), strip(roles.get("foreground"))
        remote.call("theme.clone", name="d16")
        for bg, fg in D16_PAIRS:
            remote.call("theme.set", colors="%s=%s,%s=%s" % (bg_tok, bg, fg_tok, fg))
            time.sleep(WAIT_S)
            comp = ((remote.call("themedesign.get", el="menuBar") or {}).get("computed") or {}).get("Foreground")
            c = remote.call("themedesign.contrast", el="menuBar") or {}
            exp_warn = wcag_ratio(fg, bg) < (c.get("min") or 0)
            cases.append({"bg_token": bg_tok, "fg_token": fg_tok, "bg": bg, "fg": fg, "computed": comp, "ratio": c.get("ratio"),
                          "min": c.get("min"), "warn": c.get("warn"), "expected_ratio": round(wcag_ratio(fg, bg), 2), "expected_warn": exp_warn})
            print("  %s menuBar %s/%s computed=%s warn=%s 기대 %s" % ("✓" if same(comp, fg) and c.get("warn") is exp_warn else "✗",
                                                                     bg, fg, comp, c.get("warn"), exp_warn))
    finally:
        print("  " + app.stop(pid, marker))
    return cases


def main(argv):
    p = argparse.ArgumentParser()
    p.add_argument("--run-dir")
    p.add_argument("--work")
    p.add_argument("--out")
    p.add_argument("--src-root", default=os.path.join(HERE, "..", "..", "src"))
    p.add_argument("--src-hash")
    p.add_argument("--d16", action="store_true", help="D-16 menuBar 색 지정 반영·대비 경고 사례만 돌려 d16_fix.json 형식으로 쓴다")
    p.add_argument("--self-test", action="store_true")
    a = p.parse_args(argv)
    if a.self_test:
        return self_test()
    if not (a.run_dir and a.work and a.out):
        p.error("--run-dir·--work·--out 필요")
    import mdi_drag_app as app
    import theme_custom_check as T
    os.makedirs(a.work, exist_ok=True)
    stamp = time.strftime("%H%M%S")
    if a.d16:
        cs = run_d16(app, a, stamp)
        good = len(cs) == len(D16_PAIRS) and all(same(c["computed"], c["fg"]) and c["warn"] is c["expected_warn"] for c in cs)
        result = {"src_hash": a.src_hash or T.src_hash(os.path.abspath(a.src_root)), "run_dir": os.path.abspath(a.run_dir),
                  "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "verdict": "PASS" if good else "FAIL", "cases": cs}
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=1)
        print("d16 verdict=%s cases=%d → %s" % (result["verdict"], len(cs), a.out))
        return 0 if good else 1
    cases, blocked = [], []
    for start, target, elements in CASES:
        c, b = run_pair(app, a, start, target, elements, stamp)
        cases += c
        blocked += b
    v = verdict_of(cases, blocked)
    result = {"src_hash": a.src_hash or T.src_hash(os.path.abspath(a.src_root)), "run_dir": os.path.abspath(a.run_dir),
              "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "verdict": v,
              "fails": sum(1 for c in cases if not same(c["computed"], c["expected"])), "cases": cases, "blocked": blocked}
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    print("verdict=%s cases=%d fails=%d blocked=%d → %s" % (v, len(cases), result["fails"], len(blocked), a.out))
    return 0 if v == "PASS" else 1


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except SystemExit:
        raise
    except Exception as e:  # 연결·기동 실패
        print("오류:", e)
        sys.exit(2)

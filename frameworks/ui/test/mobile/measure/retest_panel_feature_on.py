# C1·F07 '패널 기능 켬' 조건 측정 래퍼 — 원본 측정 도구를 고치지 않고 import 해 시험 앱 연결 직후 시험 앱 설정에서만 패널 기능을 '켬'으로 바꾼 뒤 원본 main 을 돌린다
"""
사용:
  python3 -B retest_panel_feature_on.py sdi   <retest_android_pcaway_sdi.py 인자...>     C1
  python3 -B retest_panel_feature_on.py panel <retest_android_pcaway_panel.py 인자...>   F07·F09
  python3 -B retest_panel_feature_on.py --self-test

왜: r2 에서 패널 기능 기본값이 Auto(PC형만 켬)라 태블릿형에서 도킹 놓기·탭 합류가 꺼진다(사용자 요구).
  기본값 결과(합류 없음)는 그대로 남기고, 계획서 oplan r2 §6 위험 2 에 따라 '패널 기능 켬' 조건 결과를 따로 잰다.
방법: retest_android_pcaway_lib.attach 를 감싸 연결 성공 직후 설정 창 '창' 페이지 콤보(settings.cmbPanelFeature)를 index 1(켬)로 고르고
  settings.esc 로 닫는다(retest_r2_web.set_feature 와 같은 화면 경로). 값은 다시 읽어 c.raw['panel_feature_on'] 에 남긴다.
  시험 앱(com.pd.poc) 자기 저장소만 바뀐다. 사용자 앱·사용자 설정은 건드리지 않는다.
  F07 의 '탭 두 개' 사전 조건은 원본 도구 tab_move 가 panel.join(LeftPane, tool=Log)로 만든다. 켬 조건에서 그 결과를 c.raw['join_precheck'] 로 따로 남긴다.
고정: 원본 도구·라이브러리 sha256 을 아래에 고정하고 다르면 실행을 거부한다.
"""
import hashlib
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

PINNED = {
    "retest_android_pcaway_sdi.py": "30828629793aaffe482b43533c666941cae8eb2a029c5feeb9f7d27c2e6df2f9",
    "retest_android_pcaway_panel.py": "5f1880cb6141f8be202ad811dd2e9cc03b87979209d4e6847cec9b0e6e3e3d04",
}
COMBO = "settings.cmbPanelFeature"
ON = 1


def _sha(p):
    with open(p, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def check_pins():
    bad = {n: _sha(os.path.join(HERE, n)) for n, w in PINNED.items() if _sha(os.path.join(HERE, n)) != w}
    if bad:
        raise SystemExit("거부: 원본 도구 sha 가 고정값과 다르다 %s" % json.dumps(bad))


def _open_settings(c):
    if c.call("ui.find", id="shell.mnuSys"):
        c.call("ui.click", id="shell.mnuSys")
        time.sleep(0.6)
        c.call("ui.click", id="shell.mnuSysSettings")
    else:
        c.call("ui.click", id="shell.navTab.Settings")
    time.sleep(1.5)


def set_feature_on(c):
    """설정 '창' 페이지 콤보를 켬으로 고르고 다시 읽은 값을 돌려준 뒤 설정 화면을 닫는다."""
    rec = {}
    if not c.call("settings.window", category="Window"):
        _open_settings(c)
        c.call("settings.window", category="Window")
    time.sleep(0.8)
    rec["select"] = c.call("ui.select", id=COMBO, index=ON)
    time.sleep(0.8)
    node = c.call("ui.find", id=COMBO, props="SelectedIndex") or {}
    raw = (node.get("props") or {}).get("SelectedIndex")
    try:
        rec["selected_index"] = int(raw)
    except (TypeError, ValueError):
        rec["selected_index"] = raw
    rec["esc"] = c.call("settings.esc")
    time.sleep(1.2)
    rec["ok"] = rec["selected_index"] == ON
    return rec


def patch(L, mod_name):
    orig = L.attach

    def attach(c, *a, **kw):
        ok = orig(c, *a, **kw)
        if ok:
            rec = set_feature_on(c)
            c.raw.setdefault("panel_feature_on", []).append(rec)
            print("panel_feature_on", json.dumps(rec, ensure_ascii=False, default=str)[:300], flush=True)
            if not rec["ok"]:
                print("패널 기능 켬 설정 실패 — 측정 중단", flush=True)
                return False
        return ok
    L.attach = attach
    if mod_name == "panel":
        import retest_android_pcaway_panel as P
        orig_tm = P.tab_move

        def tab_move(r):
            pre = r.c.call("panel.join", id="LeftPane", tool="Log")  # 원본 tab_move 의 첫 줄과 같은 명령 — 결과를 남기려고 먼저 부른다(이미 합류면 원본 호출은 같은 상태 유지)
            time.sleep(1.5)
            r.c.raw["join_precheck"] = {"join": pre, "panels": r.c.call("panel.list")}
            print("join_precheck", json.dumps(r.c.raw["join_precheck"], ensure_ascii=False, default=str)[:400], flush=True)
            return orig_tm(r)
        P.tab_move = tab_move
        return P
    import retest_android_pcaway_sdi as S
    return S


def run(which, argv):
    check_pins()
    import retest_android_pcaway_lib as L
    mod = patch(L, which)
    sys.argv = [os.path.join(HERE, "retest_android_pcaway_%s.py" % which)] + argv
    return mod.main()


def self_test():
    check_pins()

    class C:
        def __init__(self):
            self.raw, self.idx, self.calls = {}, 2, []

        def call(self, cmd, **a):
            self.calls.append(cmd)
            if cmd == "settings.window":
                return {"selected": "Window"}
            if cmd == "ui.select":
                self.idx = a["index"]
                return {"ok": True}
            if cmd == "ui.find":
                return {"bounds": {}, "props": {"SelectedIndex": str(self.idx)}}
            return {}
    c = C()
    r = set_feature_on(c)
    assert r["ok"] and r["selected_index"] == 1 and "settings.esc" in c.calls, r
    print("self-test OK")
    return 0


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--self-test":
        sys.exit(self_test())
    if len(sys.argv) > 1 and sys.argv[1] in ("sdi", "panel"):
        sys.exit(run(sys.argv[1], sys.argv[2:]))
    print(__doc__)
    sys.exit(2)

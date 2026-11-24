#!/usr/bin/env python3
# 디자인 테마 축(REQ-104) 시험 진입점 — 대상(desktop·web·android)에 원격 명령으로 케이스를 돌려 결과 JSON 을 남긴다
"""
사용:
  python3 -B design_theme_check.py --self-test
  python3 -B design_theme_check.py --target desktop --out feat_desktop.json [--restart-cmd "<앱 재기동 셸 명령>"] \\
      [--legacy-fixture dtheme_legacy_settings.json] [--classic-baseline <변경 전 ds-classic 계산값 표.json>] [--probe-el button]
  python3 -B design_theme_check.py --target web --bundle <진단판 wwwroot> --port <서빙 포트> --out feat_web.json [--shots <폴더>]
      (정적 서버 + headless Chromium, prefers-reduced-motion 은 CDP Emulation.setEmulatedMedia 로 모의, 재시작 = 페이지 다시 읽기)
  python3 -B design_theme_check.py --target android --out feat_android.json --restart-cmd "<adb 재기동>" --apk-md5 <md5> --ui-style Mobile
      (otest_mobile_emulator_bg 방식 — adb forward 된 원격 연결 정보를 retest_remote.Remote 가 읽는다)
안전: 사용자 Pd 앱·포트 5180·5220 을 건드리지 않는다(웹 포트는 인자로만, 기본값 없음). 격리 진단판(임시 --data-dir·--profile-dir)에만 연결한다.
      pkill·taskkill /IM 없음.
rc: 0=전 케이스 PASS, 1=FAIL/BLOCKED/ERROR 있음, 2=연결 실패·인자 오류.
결과 형식(계획서 §7.2): {verdict, fails(정수), fail_cases, src_hash, evidence, cases:{<케이스>:{verdict, actual, expected}}}, android 는 apk_md5·ui_style 추가.
"""
import argparse
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))  # poc/ui
sys.path.insert(0, HERE)
import design_theme_cases as C  # noqa: E402
import theme_custom_check as T  # noqa: E402  src_hash·RemoteBackend 재사용


def run_cases(backend, names, ctx):
    ctx["backend"] = backend
    cases, fails = {}, []
    for n in names:
        try:
            r = getattr(C, n)(backend.b, ctx)
        except C.Blocked as e:
            r = {"verdict": "BLOCKED", "actual": str(e), "expected": None}
        except Exception as e:  # 시험 대상 오류도 숨기지 않고 기록
            r = {"verdict": "ERROR", "actual": "%s: %s" % (type(e).__name__, e), "expected": None}
        cases[n] = r
        if r["verdict"] != "PASS":
            fails.append(n)
        print("  %s %s" % ("✓" if r["verdict"] == "PASS" else "✗", n), "" if r["verdict"] == "PASS" else str(r["actual"])[:160])
    return cases, fails


def write_result(a, target, cases, fails, extra=None):
    out = {"verdict": "PASS" if not fails else "FAIL", "fails": len(fails), "fail_cases": fails, "target": target,
           "src_hash": T.src_hash(a.src_root), "evidence": a.evidence or (a.out + ".log"), "cases": cases}
    out.update(extra or {})
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    with open(out["evidence"], "w", encoding="utf-8") as f:  # 증거 파일 = 케이스별 실측값 기록(판정기가 존재를 확인)
        json.dump({k: v for k, v in cases.items()}, f, ensure_ascii=False, indent=1)
    print("verdict=%s fails=%s → %s" % (out["verdict"], fails, a.out))
    return 0 if not fails else 1


class Fake:
    """계약을 흉내 내는 가짜 앱 — 케이스 로직 자체의 검증용. bug= 로 결함을 주입하면 해당 케이스가 FAIL 이어야 한다."""
    BG = {"pd-light": "#FFFFFF", "pd-dark": "#202020", "pd-ocean": "#0A2540"}

    def __init__(self, bug=None, nav=False):
        self.bug, self.nav = bug, nav
        self.sid, self.locked, self.level, self.soft, self.emul = "ds-classic", False, "system", False, False
        self.ov, self.theme, self.udes, self.wins, self.active, self.ovopen = {}, "pd-light", {}, ["w1"], "w1", False

    def restart(self):
        pass

    @property
    def b(self):
        return self

    def eff(self):
        e = dict(C.PRESETS[self.sid])
        if not self.locked:
            e.update(self.ov)
        low = self.soft or self.emul or self.level in ("reduced", "off")
        if low:
            e.update({"depth.open": "none", "depth.overview": "flat", "depth.page": "inherit"})
            e["motion"] = "none" if self.level == "off" or self.soft else "subtle"
        if self.nav:
            e["depth.overview"] = "flat"
        return e

    def reason(self):
        return "소프트웨어 렌더" if self.soft else ("움직임 줄이기" if (self.emul or self.level in ("reduced", "off")) else "")

    def cards(self):
        mode = "carousel3d" if self.eff()["depth.overview"] == "carousel3d" else "flat"
        return mode, [{"windowId": w, "x": 100 * i, "y": 50} for i, w in enumerate(self.wins)]

    def call(self, cmd, **a):
        e = self.eff()
        if cmd == "style.set":
            if not (self.locked and self.bug != "no_lock"):
                self.sid = a["id"]
            return "ok"
        if cmd == "style.effect.set":
            k, v = a["key"], a["value"]
            if self.locked and self.bug != "no_lock":
                return "ok"
            if v == "":
                self.ov.pop(k, None)
            elif k in C.PRESETS["ds-classic"] and v != "zzz-허용밖" and (k != "shadow" or v in ("none", "soft", "float", "inherit")):
                self.ov[k] = v
            return "ok"
        if cmd in ("style.state", "style.get"):
            mode, cards = self.cards()
            return {"id": self.sid, "locked": self.locked, "motionLevel": self.level, "fallbackReason": self.reason() if self.bug != "no_fallback" or not self.soft else "",
                    "suggestedColor": C.SUGGEST[self.sid], "glassOpacity": 0.5 if self.bug == "low_glass" else 0.9,
                    "effective": e if not (self.bug == "no_fallback" and self.soft) else dict(C.PRESETS[self.sid]), "overrides": dict(self.ov),
                    "resources": {"PdStyleShadow": "" if e["shadow"] in ("none", "inherit") else "BoxShadow", "PdStyleDecor": "" if e["decor"] == "none" else "Brush",
                                  "PdStyleSurface": e["surface"], "PdStyleMotionMs": 220},
                    "windows": [{"id": w} for w in self.wins], "active": self.active, "overview": {"open": self.ovopen, "mode": mode, "cards": cards}}
        if cmd == "settings.set":  # 실제 앱과 같은 이름 인자 형식(theme.motion.level=…) — key=/value= 는 무시
            if "theme.style.locked" in a:
                self.locked = a["theme.style.locked"] == "true"
            if "theme.motion.level" in a:
                self.level = a["theme.motion.level"]
            return "ok"
        if cmd == "settings.get":
            return {"themeId": self.theme}
        if cmd == "theme.select":
            self.theme = a["id"]
            return "ok"
        if cmd == "docs.open":
            self.wins.append("w%d" % (len(self.wins) + 1))
            return "ok"
        if cmd == "overview.open":
            self.ovopen = True
            mode, cards = self.cards()
            return {"open": True, "mode": mode, "cards": cards}
        if cmd == "overview.state":
            mode, cards = self.cards()
            return {"open": self.ovopen, "mode": mode, "cards": cards}
        if cmd == "overview.select":
            self.ovopen = False
            _, cards = self.cards()
            if "index" in a:
                self.active = self.wins[a["index"]]
                return {"hit": self.active}
            hit = next((c["windowId"] for c in cards if c["x"] == a["x"]), None)
            return {"hit": self.wins[0] if self.bug == "hit_wrong" else hit}
        if cmd == "depth.probe":
            on = e["depth.open"] == "tilt" if a["kind"] == "open" else e["depth.page"] == "flip3d"
            return {"samples": [{"angleY": v} for v in ((0, 7, 14, 3) if on else (0, 0))], "restored": self.bug != "no_restore"}
        if cmd == "gpu.simulateSoftware":
            self.soft = bool(a["on"])
            return "ok"
        if cmd == "themedesign.get":
            cr = "12" if self.sid == "ds-cute" else "4"
            cr = self.udes.get(a["el"], cr)
            return {"computed": {"CornerRadius": cr, "Background": self.BG[self.theme]}}
        if cmd == "themedesign.set":
            self.udes[a["el"]] = a["value"]
            return "ok"
        if cmd == "themedesign.reset":
            self.udes.pop(a["el"], None)
            return "ok"
        if cmd == "themedesign.contrast":
            return {"ratio": 7.0, "warn": False, "min": 4.5}
        return None


def self_test():
    d = tempfile.mkdtemp(prefix="dtheme_st_")
    try:
        fx, bl = os.path.join(d, "fx.json"), os.path.join(d, "bl.json")
        json.dump({"ThemeId": "pd-light"}, open(fx, "w"))
        json.dump({"_themeId": "pd-light", "_generator": "self-test", "button": {"CornerRadius": "4"}}, open(bl, "w"))  # 밑줄 키 = 메타(비교 제외) 경로도 검증
        rc = 0
        for tag, names, nav, bugs in (
                ("desktop", C.COMMON + C.DESKTOP_ONLY, False, ((None, set()), ("no_lock", {"company_lock"}), ("no_restore", {"depth_tilt_transition"}),
                                                              ("hit_wrong", {"hit_test_3d"}), ("no_fallback", {"fallback_software"}), ("low_glass", {"contrast_ok"}))),
                ("android", C.ANDROID, True, ((None, set()), ("no_restore", {"page_flip"})))):
            for bug, want in bugs:
                f = Fake(bug, nav)
                print("self-test %s bug=%s" % (tag, bug))
                ctx = {"legacy_fixture": fx, "classic_baseline": bl, "nav": nav}
                cases, fails = run_cases(f, names, ctx)
                got = set(fails)
                ok = (not got) if bug is None else want <= got
                if bug is None and any(set(c) != {"verdict", "actual", "expected"} for c in cases.values()):
                    ok = False  # 결과 형식 검증
                print("  → %s (fails=%s)" % ("OK" if ok else "불일치", sorted(got)))
                rc |= 0 if ok else 1
        f = Fake()
        ctx = {"emulate_reduced": lambda on: setattr(f, "emul", on)}
        _, fails = run_cases(f, C.WEB_ONLY, ctx)
        print("self-test web prefers_reduced_motion → %s" % ("OK" if not fails else "불일치"))
        rc |= 1 if fails else 0
        a = argparse.Namespace(out=os.path.join(d, "r.json"), evidence="", src_root=os.path.join(UI, "src"))
        rc |= write_result(a, "desktop", {"x": C.res(True, 1, 1)}, [])
        r = json.load(open(a.out, encoding="utf-8"))
        shape = {"verdict", "fails", "src_hash", "evidence", "cases"} <= set(r) and isinstance(r["fails"], int) and os.path.exists(r["evidence"])
        print("self-test 결과 JSON 형식 →", "OK" if shape else "불일치")
        rc |= 0 if shape else 1
    finally:
        import shutil
        shutil.rmtree(d, ignore_errors=True)
    print("SELF-TEST", "PASS" if rc == 0 else "FAIL")
    return rc


def run_web(a, ctx):
    sys.path.insert(0, os.path.join(UI, "test", "web", "measure"))
    import theme_custom_web as W
    from ui28_web import Web
    from playwright.sync_api import sync_playwright
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.shots)
        W.open_without_fresh(w, p)
        cdp = w.page.context.new_cdp_session(w.page)

        def emulate(on):  # CDP Emulation.setEmulatedMedia — 값이 없는 features 는 모의 해제
            feats = [{"name": "prefers-reduced-motion", "value": "reduce"}] if on else []
            cdp.send("Emulation.setEmulatedMedia", {"features": feats})
        ctx["emulate_reduced"] = emulate
        be = W.Backend(w)
        w.call("docs.open", kind="grid")
        cases, fails = run_cases(be, C.COMMON + C.WEB_ONLY, ctx)
        errs = list(w.errors)
        w.close()
    return cases, fails, errs


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--target", choices=["desktop", "web", "android"])
    ap.add_argument("--out")
    ap.add_argument("--evidence", default="")
    ap.add_argument("--restart-cmd")
    ap.add_argument("--legacy-fixture")
    ap.add_argument("--classic-baseline")
    ap.add_argument("--probe-el", default="button")
    ap.add_argument("--bundle")
    ap.add_argument("--port", type=int)
    ap.add_argument("--shots")
    ap.add_argument("--src-root", default=os.path.join(UI, "src"))
    ap.add_argument("--apk-md5", default="")
    ap.add_argument("--ui-style", default="")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if not a.target or not a.out:
        print("--target 과 --out 필요 (또는 --self-test)")
        return 2
    ctx = {"legacy_fixture": a.legacy_fixture, "classic_baseline": a.classic_baseline, "probe_el": a.probe_el, "nav": a.target == "android"}
    try:
        if a.target == "web":
            if not a.bundle or not a.port:
                print("web 은 --bundle 과 --port 필요(포트 기본값 없음, 5180·5220 금지)")
                return 2
            cases, fails, errs = run_web(a, ctx)
            return write_result(a, "web", cases, fails, {"console_errors": errs[:20]})
        be = T.RemoteBackend(a.restart_cmd)
    except Exception as e:
        print("연결 실패:", e)
        return 2
    if a.target == "android":
        cases, fails = run_cases(be, C.ANDROID, ctx)
        return write_result(a, "android", cases, fails, {"apk_md5": a.apk_md5, "ui_style": a.ui_style})
    cases, fails = run_cases(be, C.COMMON + C.DESKTOP_ONLY, ctx)
    return write_result(a, "desktop", cases, fails)


if __name__ == "__main__":
    sys.exit(main())

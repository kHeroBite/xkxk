#!/usr/bin/env python3
# 테마 요소별 사용자 지정(REQ-101) 시험 진입점 — 대상(desktop·web·android)에 원격 명령으로 케이스를 돌려 결과 JSON 을 남긴다
"""
사용:
  python3 -B theme_custom_check.py --self-test
  python3 -B theme_custom_check.py --target desktop --out feat_desktop.json --exchange-dir <앱이 쓸 수 있는 폴더> \
      [--restart-cmd "<앱 재기동 셸 명령>"] [--settings-file <settings.json>] [--builtin-colors f.json] \
      [--legacy-fixture f.json --legacy-baseline b.json] [--src-root poc/ui/src]
  --target web: --url-part 127.0.0.1:5185 (CDP 다리, 재시작 = 페이지 다시 읽기)
  --target android: desktop 과 같은 원격(adb forward 된 pdctl 연결 정보), 재시작은 --restart-cmd
안전: 사용자 Pd 앱·포트 5180·5220 을 건드리지 않는다. 격리 진단판(임시 --data-dir)에만 연결한다. pkill·taskkill /IM 없음.
rc: 0=전 케이스 PASS, 1=FAIL/BLOCKED 있음, 2=연결 실패·인자 오류. 결과 형식은 계획서 §5.2(verdict·fails·src_hash·evidence·cases).
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import theme_custom_cases as C  # noqa: E402


def src_hash(root):
    """poc/ui/src 아래(bin·obj 제외) (상대경로, sha256) 목록의 sha256 — 동결 판 식별."""
    items = []
    for d, ds, fs in os.walk(root):
        ds[:] = [x for x in ds if x not in ("bin", "obj")]
        for f in fs:
            p = os.path.join(d, f)
            items.append((os.path.relpath(p, root).replace(os.sep, "/"), hashlib.sha256(open(p, "rb").read()).hexdigest()))
    items.sort()
    return hashlib.sha256(json.dumps(items).encode()).hexdigest()


class RemoteBackend:
    """desktop·android: retest_remote.Remote(pdctl) 를 쓴다. 재시작은 외부 명령."""

    def __init__(self, restart_cmd=None):
        import retest_remote as rr
        self.rr, self.restart_cmd = rr, restart_cmd
        self.b = rr.Remote()

    def restart(self):
        if not self.restart_cmd:
            raise C.Blocked("--restart-cmd 없음 — 재기동 케이스 판정 불가")
        subprocess.run(self.restart_cmd, shell=True, check=True, timeout=120)
        time.sleep(3)
        self.b = self.rr.Remote()


class WebBackend:
    """web: CDP 다리 Bridge.call. 재시작 = 페이지 다시 읽기."""

    def __init__(self, url_part):
        import cdp_bridge
        self.cb, self.url_part = cdp_bridge, url_part
        self.b = cdp_bridge.Bridge(url_part)

    def restart(self):
        self.b.eval("location.reload()")
        time.sleep(4)
        self.b.close()
        self.b = self.cb.Bridge(self.url_part)


def read_saved_factory(settings_file, backend_getter):
    """저장 JSON 의 사용자 테마 Design 키 목록 {요소.속성}. 파일 우선, 없으면 settings.get 의 userThemesJson."""
    def fn(theme_id):
        raw = None
        if settings_file and os.path.exists(settings_file):
            raw = (json.load(open(settings_file, encoding="utf-8")) or {}).get("UserThemesJson")
        if raw is None:
            s = backend_getter().call("settings.get") or {}
            raw = s.get("userThemesJson") or s.get("UserThemesJson")
        if raw is None:
            raise C.Blocked("저장 JSON 을 읽을 수 없음(--settings-file 또는 settings.get.userThemesJson)")
        themes = json.loads(raw) if isinstance(raw, str) else raw
        for t in themes:
            if t.get("Id") == theme_id or t.get("id") == theme_id:
                return t.get("Design") or t.get("design") or {}
        raise C.Blocked("저장 JSON 에 테마 %s 없음" % theme_id)
    return fn


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


def build_ctx(a, backend_getter):
    colors = json.load(open(a.builtin_colors, encoding="utf-8")) if a.builtin_colors else {}
    return {"builtin_colors": colors, "exchange_dir": a.exchange_dir, "to_app_path": lambda p: p,
            "legacy_fixture": a.legacy_fixture, "legacy_baseline": a.legacy_baseline,
            "read_saved_design": read_saved_factory(a.settings_file, backend_getter)}


class Fake:
    """계약을 흉내 내는 가짜 앱 — 케이스 로직 자체의 검증용. bug= 로 결함을 주입하면 FAIL 이 나와야 한다."""
    BG = {"pd-light": "#FFFFFF", "pd-dark": "#202020", "pd-contrast": "#000000", "pd-ocean": "#0A2540"}
    DEF = {"cornerRadius": 4, "borderThickness": 1, "padding": "8,4", "fontSize": 0, "fontWeight": "Normal", "fontFamily": "Segoe UI"}

    def __init__(self, bug=None):
        self.bug, self.sel, self.themes, self.saved = bug, "pd-light", {}, {}

    def restart(self):
        pass

    @property
    def b(self):
        return self

    def call(self, cmd, **a):
        t = self.themes.get(self.sel)
        if cmd == "theme.select":
            self.sel = a["id"]
            return "ok"
        if cmd == "settings.get":
            return {"themeId": self.sel, "userThemesJson": json.dumps(
                [{"Id": k, "Design": v["d"]} for k, v in self.themes.items()])}
        if cmd == "themedesign.set":
            if t is None:
                nid = "user-" + self.sel
                self.themes[nid] = {"base": self.sel, "d": {}, "c": {}}
                self.sel, t = nid, self.themes[nid]
            if self.bug == "no_persist" and a["prop"] == "padding":
                return "ok"
            t["d"]["%s.%s" % (a["el"], a["prop"])] = a["value"]
            return "ok"
        if cmd == "themedesign.reset":
            if t is not None:
                for k in [k for k in t["d"] if k.startswith(a["el"] + ".") and (not a.get("prop") or k.endswith("." + a["prop"]))]:
                    del t["d"][k]
            return "ok"
        if cmd == "themedesign.resetall":
            if t is not None:
                t["d"].clear()
                t["c"].clear()
            return "ok"
        if cmd == "theme.set":
            for kv in a["colors"].split(","):
                k, v = kv.split("=")
                t["c"][k] = v
            return "ok"
        if cmd == "theme.save":
            return "ok"
        if cmd == "theme.export":
            if not a.get("path"):
                return json.dumps(t)
            self.saved[a["path"]] = json.dumps(t)
            return "ok"
        if cmd == "theme.import":
            d = json.loads(a["path"] if str(a["path"]).lstrip().startswith(("{", "[")) else self.saved[a["path"]])
            nid = self.sel + "-imp"
            self.themes[nid] = {"base": d["base"], "d": dict(d["d"]), "c": dict(d["c"])}
            self.sel = nid
            return "ok"
        if cmd == "themedesign.contrast":
            return {"ratio": 1.0, "warn": self.bug != "no_warn", "min": 7 if (t or {}).get("base") == "pd-contrast" else 4.5}
        if cmd == "themedesign.get":
            base = t["base"] if t else self.sel
            d = t["d"] if t else {}
            el = a["el"]
            eff = dict(self.DEF)
            for k, v in d.items():
                if k.startswith(el + "."):
                    eff[k.split(".", 1)[1]] = v
            pad = [float(x) for x in str(eff["padding"]).split(",")]
            bg = (t["c"].get("Button") if t and el == "button" and "Button" in t["c"] else None) or self.BG[base]
            return {"themeId": self.sel, "baseTemplateId": base if t else None, "isBuiltin": t is None,
                    "stored": {k.split(".", 1)[1]: v for k, v in d.items() if k.startswith(el + ".")},
                    "effective": eff, "templateColors": {"Background": self.BG[base]},
                    "computed": {"FontSize": 12 + int(eff["fontSize"]), "FontFamily": eff["fontFamily"], "FontWeight": eff["fontWeight"],
                                 "CornerRadius": eff["cornerRadius"], "BorderThickness": eff["borderThickness"],
                                 "Padding": "%s,%s,%s,%s" % (pad[0], pad[1], pad[0], pad[1]), "Background": bg, "Foreground": "#000000"}}
        return None


def self_test():
    ex = os.path.join(HERE, "__st_tmp")
    os.makedirs(ex, exist_ok=True)
    a = argparse.Namespace(builtin_colors=None, exchange_dir=ex, legacy_fixture=None, legacy_baseline=None, settings_file=None)
    rc = 0
    try:
        f = Fake()
        c = build_ctx(a, lambda: f)
        c["inline_json"] = True
        c["backend"] = f
        r = C.export_import(f, c)
        print("self-test inline_json export_import →", r["verdict"])
        rc |= 0 if r["verdict"] == "PASS" else 1
        for bug, want in ((None, set()), ("no_persist", {"padding_override"}), ("no_warn", {"contrast_warning"})):
            f = Fake(bug)
            print("self-test bug=%s" % bug)
            _, fails = run_cases(f, C.COMMON, build_ctx(a, lambda: f))
            got = set(fails)
            ok = (not got) if bug is None else want <= got
            print("  → %s (fails=%s)" % ("OK" if ok else "불일치", sorted(got)))
            rc |= 0 if ok else 1
    finally:
        import shutil
        shutil.rmtree(ex, ignore_errors=True)
    print("SELF-TEST", "PASS" if rc == 0 else "FAIL")
    return rc


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--target", choices=["desktop", "web", "android"])
    ap.add_argument("--out")
    ap.add_argument("--evidence", default="")
    ap.add_argument("--exchange-dir")
    ap.add_argument("--restart-cmd")
    ap.add_argument("--settings-file")
    ap.add_argument("--builtin-colors")
    ap.add_argument("--legacy-fixture")
    ap.add_argument("--legacy-baseline")
    ap.add_argument("--url-part", default="127.0.0.1:5185")
    ap.add_argument("--src-root", default=os.path.join(HERE, "..", "..", "..", "src"))
    ap.add_argument("--apk-md5", default="")
    ap.add_argument("--ui-style", default="")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if not a.target or not a.out:
        print("--target 과 --out 필요 (또는 --self-test)")
        return 2
    try:
        backend = WebBackend(a.url_part) if a.target == "web" else RemoteBackend(a.restart_cmd)
    except Exception as e:
        print("연결 실패:", e)
        return 2
    names = list(C.COMMON) + (list(C.DESKTOP_ONLY) if a.target == "desktop" else [])
    cases, fails = run_cases(backend, names, build_ctx(a, lambda: backend.b))
    out = {"verdict": "PASS" if not fails else "FAIL", "fails": len(fails), "fail_cases": fails, "target": a.target,
           "src_hash": src_hash(a.src_root), "evidence": a.evidence, "cases": cases}
    if a.target == "android":
        out.update(apk_md5=a.apk_md5, ui_style=a.ui_style)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("verdict=%s fails=%s → %s" % (out["verdict"], fails, a.out))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())

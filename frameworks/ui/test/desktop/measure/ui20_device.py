# 기기 Chrome(Fold5) 웹판에 CDP 다리로 붙어 ui20_check 의 판정(run_live)을 그대로 돌리는 UI-20 실기기 웹 판정 스크립트
"""
사용: python3 -E -u test/desktop/measure/ui20_device.py --out <결과.json> [--url-part 127.0.0.1:5185] [--areas Left,Bottom,Right]
종료 코드는 ui20_check 와 같다(0 PASS, 1 FAIL, 2 판정불가).
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import ui20_check as u  # noqa: E402
from cdp_bridge import Bridge  # noqa: E402


class DeviceRemote(u.Remote):
    def __init__(self, url_part):
        self.log = []
        self.b = Bridge(url_part)

    def send(self, body, cmd):
        try:
            return self.b.invoke_raw(body)
        except Exception as e:  # noqa: BLE001
            raise u.Undecidable("기기 웹 원격 호출 실패(%s): %s" % (cmd, str(e)[:200]))


def panel_feature_on(r, rep):
    """방향 분할(b·c·f)은 패널 기능이 켜져 있어야 하므로 이전 값을 읽어 저장하고 On 으로 켠다(설계 04·01 §4.4). 실패하면 Undecidable."""
    got = r.b.call("settings.get")
    prev = got.get("panelFeature") if isinstance(got, dict) else None
    if prev not in ("Auto", "On", "Off"):
        raise u.Undecidable("settings.get 에서 panelFeature 를 읽지 못함(진단 빌드가 옛 판일 수 있음): %r" % (prev,))
    rep["panel_feature"] = {"before": prev, "set": "On", "restored": None}
    if r.b.call("settings.set", panelFeature="On") is None:
        raise u.Undecidable("settings.set panelFeature=On 실패: %s" % (str(getattr(r.b, "last", ""))[:200],))
    return prev


def panel_feature_restore(r, rep, prev):
    """finally 에서 호출 — 원래 값으로 되돌리고 결과를 보고서에 남긴다(예외를 삼켜 본 판정 결과를 가리지 않는다)."""
    info = rep.setdefault("panel_feature", {})
    try:
        res = r.b.call("settings.set", panelFeature=prev)
        now = res.get("panelFeature") if isinstance(res, dict) else None
        info["restored"] = (now == prev)
        info["after_restore"] = now
        if now != prev:
            info["restore_error"] = "원복 후 값 불일치: %r != %r" % (now, prev)
    except Exception as e:  # noqa: BLE001
        info["restored"] = False
        info["restore_error"] = str(e)[:200]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--url-part", default="127.0.0.1:5185")
    ap.add_argument("--areas", default="Left,Bottom,Right")
    a = ap.parse_args()
    rep = {"ui": "UI-20", "time": time.strftime("%Y-%m-%d %H:%M:%S"), "mode": "device-web(CDP)"}
    r = None
    prev = None  # 켜기 전 panelFeature — None 이면 아직 바꾸지 않았으므로 원복 불필요
    try:
        r = DeviceRemote(a.url_part)
        rep["metrics"] = r.b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio,ua:navigator.userAgent})")
        rep["ui_mode"] = r.b.call("ui.mode")
        prev = panel_feature_on(r, rep)
        rc, part = u.run_live(r, [x for x in a.areas.split(",") if x])
        rep.update(part)
    except u.Undecidable as e:
        rc = 2
        rep.update(verdict="UNDECIDABLE", reason=str(e))
    except AssertionError as e:
        rc = 1
        rep.update(verdict="FAIL", reason=str(e))
    finally:
        if r is not None:
            if prev is not None or "panel_feature" in rep:
                # set 직후 실패했을 가능성까지 포함해 prev 를 알면 항상 원복한다
                if prev is None:
                    prev = rep["panel_feature"].get("before")
                panel_feature_restore(r, rep, prev)
            r.b.close()
    rep.setdefault("verdict", {0: "PASS", 1: "FAIL", 2: "UNDECIDABLE"}[rc])
    rep["rc"] = rc
    txt = json.dumps(rep, ensure_ascii=False, indent=2)
    open(a.out, "w", encoding="utf-8").write(txt)
    print(txt[:3000])
    return rc


if __name__ == "__main__":
    sys.exit(main())

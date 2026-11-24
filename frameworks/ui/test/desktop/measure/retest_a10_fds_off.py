#!/usr/bin/env python3
# A10 판별 래퍼 — 동결 retest_real_input 을 import 해 시험용 임시 프로필 settings.json 에 FastDocumentSwitch=false 를 넣고 A10 만 잰다(KeepAlive 회귀 판별, 측정 로직 불변)
"""
사용: python3 -B retest_a10_fds_off.py --items A10 --run-dir <bin/run/<복사본>> --phase discovery --writer otest-N --monitor DISPLAY1
근거: odebug-a10 분석 — 이번 사이클 문서 KeepAlive(FastDocumentSwitch 켬 기본)가 관여한 회귀 추정. 끈 상태에서 2회 모두 죽지 않으면 회귀로 확정한다.
쓰는 곳: retest_real_input.Rig.start 가 새로 만드는 임시 profile 폴더(사용자 설정 아님)에만 settings.json 을 미리 둔다.
기동 직후 원격 settings.get 으로 FastDocumentSwitch 값을 읽어 notes 에 남긴다. 결과는 --phase discovery 로만 쓴다(판정기 대상 A10.json 을 덮지 않는다).
안전 규칙·종료 코드는 retest_real_input.py 와 같다.
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_real_input as ri  # noqa: E402

_orig_start = ri.Rig.start
_seen = {}


def _start(self, extra=(), reuse=False):
    if not reuse:
        real = os.makedirs

        def mk(path, *a, **kw):  # Rig.start 가 만드는 임시 profile 폴더에만 설정 파일을 둔다
            real(path, *a, **kw)
            if os.path.basename(path).startswith("profile_") and os.path.abspath(path).startswith(self.work):
                with open(os.path.join(path, "settings.json"), "w", encoding="utf-8") as f:
                    json.dump({"FastDocumentSwitch": False}, f)
                _seen["profile"] = path
        os.makedirs = mk
        try:
            out = _orig_start(self, extra, reuse)
        finally:
            os.makedirs = real
    else:
        out = _orig_start(self, extra, reuse)
    try:
        got = self.r.call("settings.get") or {}
        _seen["settings_get"] = got.get("FastDocumentSwitch", got.get("result", {}).get("FastDocumentSwitch") if isinstance(got.get("result"), dict) else None)
    except Exception as e:  # noqa: BLE001 — 읽기 실패는 기록만 한다
        _seen["settings_get_error"] = repr(e)
    print("FDS_OFF profile=%s settings.get FastDocumentSwitch=%s" % (_seen.get("profile"), _seen.get("settings_get")), flush=True)
    return out


ri.Rig.start = _start


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    args = p.parse_args()
    if args.phase != "discovery":
        print("거부: 판별 시험은 --phase discovery 로만 쓴다")
        return 3
    if [n.strip() for n in (args.items or "").split(",") if n.strip()] != ["A10"]:
        print("거부: 이 래퍼는 A10 전용이다")
        return 3
    return ri.drive(args, {"A10": ri.ITEMS["A10"]}, ri.REQ, "desktop_work", os.path.abspath(__file__), ri.HUMAN)


if __name__ == "__main__":
    sys.exit(main())

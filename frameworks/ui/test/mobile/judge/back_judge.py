#!/usr/bin/env python3
# Android BACK 판정 규칙(판정 계층) — 전면 활성이 시험 앱인지 함께 봐서, 앱 이탈(launcher 화면의 대상 노드 0개)을 '닫힘'으로 세지 않는다
"""
사용(자체 시험, 기기 불필요): python3 -B back_judge.py --self-test
가져다 쓰기: foreground_package(dumpsys_activities 출력), back_closed(대상 노드 수, 전면 패키지, 시험 앱 패키지)
음성 대조: 2026-11-05 Fold5 실측 launcher 줄(c4_debug_open.json)로 노드 0개 + 앱 이탈 = 닫힘 아님을 확인한다.
전면을 못 읽으면 앱 안이라고 볼 근거가 없으므로 닫힘이 아니다(fail-closed).
"""
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "tools"))
import host_identity  # noqa: E402  호스트 정체성 단일 출처(없으면 예외로 비0 종료)

TOP_RX = re.compile(r"(?:topResumedActivity|mResumedActivity|ResumedActivity)[=:]\s*ActivityRecord\{\S+ \S+ ([^/\s]+)/")


def foreground_package(dumpsys_text):
    """dumpsys activity activities 출력에서 전면 활성의 패키지. 못 읽으면 None."""
    m = TOP_RX.search(dumpsys_text or "")
    return m.group(1) if m else None


def back_closed(nodes_after, foreground, pkg):
    """BACK 뒤 대상 노드가 0개이고 전면이 시험 앱일 때만 닫힘이다."""
    return nodes_after == 0 and foreground == pkg


def self_test():
    pkg = host_identity.get("android.package")
    launcher = "topResumedActivity=ActivityRecord{135566092 u0 com.sec.android.app.launcher/.activities.LauncherActivity t7}\n  ResumedActivity: ActivityRecord{135566092 u0 com.sec.android.app.launcher/.activities.LauncherActivity t7}"
    app = "topResumedActivity=ActivityRecord{151762747 u0 " + pkg + "/crc64470901fbb571b1ad.MainActivity t42}"
    cases = [
        ("전면 패키지 launcher", foreground_package(launcher), "com.sec.android.app.launcher"),
        ("전면 패키지 앱", foreground_package(app), pkg),
        ("전면 패키지 못 읽음", foreground_package("no record"), None),
        ("음성 대조: 앱 이탈 + 노드 0 은 닫힘 아님", back_closed(0, foreground_package(launcher), pkg), False),
        ("전면 못 읽음 + 노드 0 은 닫힘 아님", back_closed(0, None, pkg), False),
        ("앱 안 + 노드 남음은 닫힘 아님", back_closed(3, pkg, pkg), False),
        ("양성: 앱 안 + 노드 0 은 닫힘", back_closed(0, foreground_package(app), pkg), True),
    ]
    bad = 0
    for name, got, want in cases:
        ok = got == want
        bad += 0 if ok else 1
        print("%s %s got=%s want=%s" % ("PASS" if ok else "FAIL", name, got, want))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(self_test() if "--self-test" in sys.argv else 2)

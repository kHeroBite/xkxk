#!/usr/bin/env python3
# Fold5 전면 화면 측정 중 화면 회전을 세로로 고정·원복하고 캡처가 세로 기준 크기인지 검사한다(기기를 가로로 둔 상태의 거짓 FAIL·UNDET 방지)
"""
motion_nav_fold5.py 가 쓰는 회전 보조.
  - lock_portrait: 시작 때 system 설정 accelerometer_rotation·user_rotation 을 읽어 두고 0·0(자동 회전 끔·세로)으로 고정한다.
  - restore: 읽어 둔 값으로 되돌린다. 원래 값이 없었으면(null) 설정을 지운다.
  - check_capture: 캡처 크기가 세로 전면 화면(904x2316)과 다르면 RotationError(RuntimeError)를 낸다.
    좌표·자르기 칸이 세로 기준이라 크기가 다르면 판정 자체가 무의미하다. UNDET·FAIL 대신 ERROR(rc 2)로 드러낸다.
시험 앱은 화면 방향을 직접 정하지 않는다(src 에 ScreenOrientation 지정 없음, 2026-11-24 확인). 그래서 사용자 회전 고정을 따른다.
자기 시험: python3 -B motion_nav_fold5_rotation.py (motion_nav_fold5.py --self-test 도 부른다)
"""
import os
import sys
import tempfile
import time

from PIL import Image

PORTRAIT = (904, 2316)  # 전면 화면 세로 캡처 크기(폭, 높이)
SPINNER_BOX = (136, 100, 180, 146)  # 세로 캡처의 문서 선택 칸 앞 아이콘(불러오는 중 스피너)
KEYS = ("accelerometer_rotation", "user_rotation")
RESTORE_ORDER = ("user_rotation", "accelerometer_rotation")  # 방향을 먼저 돌려놓고 자동 회전을 마지막에 켠다


class RotationError(RuntimeError):
    """캡처가 세로 기준이 아니다(회전 고정 실패)."""


def read(sh):
    return {k: sh("settings get system %s" % k).strip() for k in KEYS}


def restore_cmds(saved):
    cmds = []
    for k in RESTORE_ORDER:
        v = saved.get(k, "")
        cmds.append("settings delete system %s" % k if v in ("", "null") else "settings put system %s %s" % (k, v))
    return cmds


def restore(sh, saved):
    """읽어 둔 값으로 되돌리고 되돌린 값이 같은지 돌려준다. saved 가 비면 아무것도 하지 않는다."""
    if not saved:
        return False
    for cmd in restore_cmds(saved):
        sh(cmd)
    now = read(sh)
    return all(now[k] == (saved[k] or "null") for k in KEYS)


def lock_portrait(sh, wait=2.0):
    """세로 고정 전 값을 돌려준다. 고정 도중 실패하면 되돌리고 오류를 다시 낸다."""
    saved = read(sh)
    try:
        sh("settings put system accelerometer_rotation 0")
        sh("settings put system user_rotation 0")
    except Exception:
        restore(sh, saved)
        raise
    time.sleep(wait)  # 회전 애니메이션·앱 다시 배치
    return saved


def check_capture(path, expect=PORTRAIT):
    size = Image.open(path).size
    if tuple(size) != tuple(expect):
        raise RotationError("캡처 %dx%d 가 세로 기준 %dx%d 와 다르다 — 화면이 회전돼 좌표·칸 판정을 할 수 없다(세로 고정 실패): %s"
                            % (size[0], size[1], expect[0], expect[1], os.path.basename(path)))
    return path


def distinct_spinner_frames(shots, box=SPINNER_BOX):
    """스피너 칸의 서로 다른 그림 수. 캡처마다 세로 기준 크기를 먼저 확인한다."""
    return len({Image.open(check_capture(p)).convert("L").crop(box).tobytes() for p in shots})


def self_test():
    class FakeDevice:
        def __init__(self, values, fail_on=None):
            self.values, self.fail_on, self.log = dict(values), fail_on, []

        def __call__(self, cmd):
            self.log.append(cmd)
            if self.fail_on and self.fail_on in cmd:
                raise OSError("adb 실패 모의")
            p = cmd.split()
            if p[1] == "get":
                return self.values.get(p[3], "null")
            if p[1] == "put":
                self.values[p[3]] = p[4]
            elif p[1] == "delete":
                self.values.pop(p[3], None)
            return ""

    checks = {}
    dev = FakeDevice({"accelerometer_rotation": "1", "user_rotation": "3"})
    saved = lock_portrait(dev, wait=0)
    checks["고정 전 값 저장"] = saved == {"accelerometer_rotation": "1", "user_rotation": "3"}
    checks["세로 고정"] = dev.values == {"accelerometer_rotation": "0", "user_rotation": "0"}
    checks["원복 값 일치"] = restore(dev, saved) and dev.values == {"accelerometer_rotation": "1", "user_rotation": "3"}
    checks["원복 순서(방향 먼저)"] = dev.log[-4:-2] == ["settings put system user_rotation 3", "settings put system accelerometer_rotation 1"]
    dev = FakeDevice({"accelerometer_rotation": "1"})
    saved = lock_portrait(dev, wait=0)
    checks["없던 값은 지워서 원복"] = restore(dev, saved) and "user_rotation" not in dev.values
    checks["저장값 없으면 원복 안 함"] = restore(FakeDevice({}), {}) is False
    dev = FakeDevice({"accelerometer_rotation": "1", "user_rotation": "1"}, fail_on="put system user_rotation 0")
    try:
        lock_portrait(dev, wait=0)
        checks["고정 실패 시 되돌림·오류 전달"] = False
    except OSError:
        checks["고정 실패 시 되돌림·오류 전달"] = dev.values["accelerometer_rotation"] == "1"

    tmp = tempfile.mkdtemp(prefix="rot_selftest_")
    try:
        def img(name, size, shade):
            p = os.path.join(tmp, name + ".png")
            im = Image.new("L", size, 0)
            im.paste(shade, SPINNER_BOX)
            im.save(p)
            return p

        portrait = [img("p%d" % i, PORTRAIT, 40 * (i + 1)) for i in range(3)]
        same = [img("s%d" % i, PORTRAIT, 90) for i in range(3)]
        land = img("land", (PORTRAIT[1], PORTRAIT[0]), 50)
        checks["세로 캡처 통과"] = check_capture(portrait[0]) == portrait[0]
        try:
            check_capture(land)
            checks["가로 캡처는 명확한 오류"] = False
        except RotationError as e:
            checks["가로 캡처는 명확한 오류"] = "2316x904" in str(e) and isinstance(e, RuntimeError)
        checks["스피너 서로 다른 그림 3"] = distinct_spinner_frames(portrait) == 3
        checks["같은 그림이면 1"] = distinct_spinner_frames(same) == 1
        try:
            distinct_spinner_frames(portrait[:2] + [land])
            checks["가로 캡처 섞이면 오류(FAIL 아님)"] = False
        except RotationError:
            checks["가로 캡처 섞이면 오류(FAIL 아님)"] = True
    finally:
        for f in os.listdir(tmp):
            os.remove(os.path.join(tmp, f))
        os.rmdir(tmp)

    for name, ok in checks.items():
        print("%s %s" % ("PASS" if ok else "FAIL", name))
    print("rotation self-test %d/%d" % (sum(checks.values()), len(checks)))
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    sys.exit(self_test())

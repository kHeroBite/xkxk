#!/usr/bin/env python3
# Fold5 닫기 화소 판정 — 실터치 순간의 연속 screencap 을 받아 중간 프레임·첫 프레임 1:1·바탕 유지를 잰다
"""
motion_nav_fold5_close.py 가 부른다. 재생 기록(lastClose)만으로는 덮개가 확대되거나 비어도 PASS 가 나서(6차 거짓 PASS) 화면을 함께 본다.
  motion_nav_fold5_frames.py --judge <frames 폴더> | --self-test     기기 없이 저장된 프레임을 다시 판정한다
규칙(효과 fade)
  시작 그림 = 누르기 전에 끝난 마지막 장, 끝 그림 = 마지막 장. 둘이 같거나 끝이 안정되지 않으면 INVALID 다.
  첫 변화 장은 시작→끝 선형 혼합 위에 있어야 한다(off <= OFF_MAX). 확대·어긋남·바탕 없음은 모두 여기서 FAIL 이다.
  순서가 확실한 두 장 사이에 진행도(alpha)가 DROP_MAX 를 넘게 되돌아가면 FAIL(flicker)이다(문턱 근거는 motion_close_rules.py).
  혼합 선 위의 중간 장이 1장 이상이어야 한다. 표본 간격이 GAP_MAX_MS 를 넘어 중간 장을 놓쳤으면 INVALID 다.
규칙(효과 없음) 바뀐 첫 장이 곧 끝 그림이어야 한다.
규칙(전환·열기 — 나가는 그림 없이 들어오는 화면만 페이드. 사례 이름 낱말에 combo·open) 시작→끝 혼합이 아니라 바탕→끝이므로 선형 혼합 판정을 쓰지 않는다.
  진행도 p = 1 - d/D (d = 끝 그림과의 평균 회색 차, D = 첫 변화 뒤 d 의 최댓값). p 가 한 방향(되돌림 DROP_MAX 이하)이고
  첫 변화 장이 끝 그림과 달라야(중간 장 1장 이상) PASS 다. 측정 한계는 motion_close_rules.py DROP_MAX 주석과 같은 모형이다
  (화소마다 독립 반올림 오차 분산 1/6 → N 화소 평균 d 의 표준편차 1/√(6N), 두 장 차의 6σ = 2√3/√N 회색 → 진행도로 q = 2√3/(√N·D)).
  q > DROP_MAX(대비 부족)면 INVALID 다.
INVALID 와 표본 없음은 PASS 가 아니다(전체 UNDET).
비교 영역은 상태줄·제목줄·아래 탭 줄을 뺀다. 펼침 화면은 도구 패널·로그 패널도 뺀다(로그 줄은 닫는 순간 바뀌어 혼합 오차가 된다).
"""
import gzip
import io
import json
import os
import sys
import tarfile

from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "desktop", "judge"))
from motion_close_rules import DROP_MAX, PLAYED_MID_MIN, SAMPLE, diff_count, flicker, judge_trace, result, trace  # noqa: E402

TEMP = "/data/local/tmp/pd_fold5_frames"
BURST = ("rm -rf {d}; mkdir {d}; cd {d}; "
         "w() {{ i=0; while [ $i -lt 8 ]; do s=$EPOCHREALTIME; screencap -d {disp} t$1 2>/dev/null; "
         "mv t$1 ${{s}}_${{EPOCHREALTIME}}_$1.raw; i=$((i+1)); done; }}; "
         "w 1 & sleep 0.07; w 2 & sleep 0.07; w 3 & sleep 0.05; "
         "echo $EPOCHREALTIME > tap_start; input tap {x} {y}; echo $EPOCHREALTIME > tap_end; wait")
BOXES = {"cover": (0.05, 0.15, 0.95, 0.9), "inner": (0.22, 0.15, 0.78, 0.74)}  # 화면 비율(왼·위·오른·아래)
GAP_MAX_MS = 150  # 6차 실측 간격 중앙 52~60ms·최대 101ms
INCOMING = ("combo", "open")  # 들어오는 화면만 있는 사례(전환·새 탭 열기)의 사례 이름 낱말('_' 로 나눈 낱말과 정확히 같아야 한다)


def shoot(device, x, y, out):
    """누르는 순간을 연속으로 찍어 out 에 남긴다. 못 했으면 이유. 화면을 깨우지 않는다."""
    disp = device.display_id()
    if not disp:
        return "활성 화면 id 를 못 읽었다"
    os.makedirs(out, exist_ok=True)
    for name in os.listdir(out):  # 고정 경로에 덮어쓴다 — 앞 회차 장이 섞이면 안 된다
        if name.endswith(".png") or name == "tap.txt":
            os.remove(os.path.join(out, name))
    device.sh(BURST.format(d=TEMP, disp=disp, x=x, y=y), timeout=70)
    rc, data = device.adb("exec-out", "cd %s && tar -cf - . | gzip -1" % TEMP, binary=True, timeout=100)
    device.sh("rm -rf %s" % TEMP)
    with open(os.path.join(out, "tap.txt"), "w", encoding="utf-8") as f:
        f.write("xy %d %d\n" % (x, y))
        try:
            with tarfile.open(fileobj=io.BytesIO(gzip.decompress(data))) as tar:
                for item in tar.getmembers():
                    if not item.isfile():
                        continue
                    name, body = os.path.basename(item.name), tar.extractfile(item).read()
                    if name.startswith("tap_"):
                        f.write("%s %s\n" % (name, body.decode().strip()))
                    elif name.endswith(".raw") and len(body) > 16:
                        w, h = int.from_bytes(body[:4], "little"), int.from_bytes(body[4:8], "little")
                        if len(body) == 16 + w * h * 4:
                            shot = Image.frombytes("RGBA", (w, h), body[16:]).convert("RGB")
                            shot.resize((w // 2, h // 2)).save(os.path.join(out, name[:-4] + ".png"))
        except (OSError, EOFError, tarfile.TarError) as e:
            f.write("frames_error %r\n" % e)
    return None


def judge_images(shots, screen, fade):
    """shots = [(누른 뒤 촬영 끝 ms, 그림[, 촬영 시작 ms])] 시각순. fade = True(닫기 혼합)·False(효과 없음)·"incoming"(전환)."""
    before = [i for i, shot in enumerate(shots) if shot[0] <= 0]
    if not before or len(shots) - before[-1] < 3:
        return result("INVALID", "누르기 전·뒤 화면 표본이 모자란다")
    shots = shots[before[-1]:]
    times, ims, starts = [s[0] for s in shots], [s[1] for s in shots], [s[2] if len(s) > 2 else s[0] for s in shots]
    (w, h), ratio = ims[0].size, BOXES[screen]
    box = tuple(int(size * r) for size, r in zip((w, h, w, h), ratio))
    if diff_count(ims[0], ims[-1], box) < PLAYED_MID_MIN:
        return result("INVALID", "닫기 전·뒤 그림이 같아 덮개를 가릴 수 없다")
    if diff_count(ims[-2], ims[-1], box) >= PLAYED_MID_MIN:
        return result("INVALID", "관찰 시간 안에 끝 모습이 되지 않았다")
    points = [dict(p, t0=t0, t1=t1) for p, t0, t1 in zip(trace(ims[1:], ims[0], ims[-1], box), starts[1:], times[1:])]
    data = {"box": box, "seq": [dict(p, ms=ms) for p, ms in zip(points, times[1:])]}
    if not fade:
        first = next(p for p in points if p["s"] >= PLAYED_MID_MIN)
        if first["e"] >= PLAYED_MID_MIN:
            return result("FAIL", "효과 없음인데 중간 모습이 있다", **data)
        return result("PASS", "한 번에 끝 모습", **data)
    if fade == "incoming":
        return judge_incoming(ims[1:], points, box, data)
    judged = judge_trace(points, True)
    if judged["verdict"] == "FAIL" and judged.get("mid") == 0:
        changed = next(i for i, p in enumerate(points) if p["e"] < PLAYED_MID_MIN)
        if times[changed + 1] - times[changed] > GAP_MAX_MS:
            return result("INVALID", "표본 간격 %dms — 중간 프레임을 놓쳤을 수 있다" % (times[changed + 1] - times[changed]), **data)
    return dict(judged, **data)


def judge_incoming(frames, points, box, data):
    """전환: 바탕 위로 들어오는 화면의 진행도가 한 방향으로 0→1 인가(모듈 머리말 규칙)."""
    def small(im):
        return im.crop(box).convert("L").resize(SAMPLE).tobytes()

    end = small(frames[-1])
    for p, im in zip(points, frames):
        p["d"] = round(sum(abs(a - b) for a, b in zip(small(im), end)) / float(len(end)), 3)
    tc = next((i for i, p in enumerate(points) if p["s"] >= PLAYED_MID_MIN), None)
    if tc is None:
        return result("INVALID", "누른 뒤 바뀐 화면 표본이 없다", **data)
    seen = points[tc:]
    top = max(p["d"] for p in seen)
    if not top:
        return result("FAIL", "첫 변화 장이 곧 끝 그림이다(들어오는 효과 없음)", **data)
    q = 2.0 * 3 ** 0.5 / (len(end) ** 0.5 * top)  # 두 장 진행도 차의 측정 한계(모듈 머리말)
    if q > DROP_MAX:
        return result("INVALID", "대비 부족 — 진행도 분해능 q=%s > %s" % (round(q, 4), DROP_MAX), **data)
    marks = [dict(p, alpha=round(1 - p["d"] / top, 3)) for p in seen]
    data = dict(data, seq=data["seq"][:tc] + [dict(s, p=m["alpha"]) for s, m in zip(data["seq"][tc:], marks)])
    fall, i, j = flicker(marks)
    head = {"first": {"p": marks[0]["alpha"], "d": marks[0]["d"]}, "drop": fall}
    if fall > DROP_MAX:
        return result("FAIL", "되돌림(flicker) — 진행도 %s 감소(%s → %s)" % (fall, marks[i]["alpha"], marks[j]["alpha"]), **dict(data, **head))
    mid = sum(1 for p in seen if p["e"] >= PLAYED_MID_MIN)
    if not mid:
        gap = seen[1]["t1"] - seen[0]["t1"] if len(seen) > 1 else 0
        if gap > GAP_MAX_MS:
            return result("INVALID", "표본 간격 %dms — 중간 프레임을 놓쳤을 수 있다" % gap, **dict(data, **head))
        return result("FAIL", "첫 변화 장이 곧 끝 그림이다(들어오는 효과 없음)", **dict(data, **head))
    return result("PASS", "들어오는 화면 진행도 한 방향·중간 %d장" % mid, mid=mid, **dict(data, **head))


def kind_of(name, case=None):
    """사례 이름(행 메타의 case, 없으면 폴더 이름) → 판정 종류. none 은 효과 없음, INCOMING 낱말은 들어오는 화면, 그 밖은 닫기 혼합.
    낱말 단위로 보므로 'control_open' 같은 다른 낱말 속 부분 일치로 바뀌지 않는다(open 은 낱말 그대로일 때만)."""
    words = (case or name).split("_")
    if "none" in words:
        return False
    return "incoming" if any(k in words for k in INCOMING) else True


def case_names(root):
    """frames 폴더 옆 close_rows.json 의 행 메타 {폴더 이름: case}. 없거나 못 읽으면 빈 표(폴더 이름으로 판정)."""
    try:
        with open(os.path.join(os.path.dirname(os.path.abspath(root)), "close_rows.json"), encoding="utf-8") as f:
            rows = json.load(f)
    except (OSError, ValueError):
        return {}
    rows = rows.get("rows", []) if isinstance(rows, dict) else rows
    return {os.path.basename(r["folder"]): r["case"] for r in rows if isinstance(r, dict) and r.get("folder") and r.get("case")}


def judge_dir(folder, screen, fade):
    try:
        with open(os.path.join(folder, "tap.txt"), encoding="utf-8") as f:
            tap = dict(line.split(" ", 1) for line in f.read().splitlines())
        at = float(tap["tap_start"])
        names = sorted((n for n in os.listdir(folder) if n.endswith(".png")), key=lambda n: float(n.split("_")[1]))
        shots = [(round((float(n.split("_")[1]) - at) * 1000), Image.open(os.path.join(folder, n)).convert("RGB"),
                  round((float(n.split("_")[0]) - at) * 1000)) for n in names]
    except (OSError, KeyError, ValueError, IndexError) as e:
        return result("INVALID", "화면 표본이 없다: %r" % e)
    return judge_images(shots, screen, fade)


def judge_root(root):
    """저장된 폴더(<번호>_<cover|inner>_<사례>_<문서 수>) 전부. none 이 이름에 있으면 효과 없음이다."""
    found, cases = [], case_names(root)
    for name in sorted(os.listdir(root)):
        if os.path.isfile(os.path.join(root, name, "tap.txt")):
            got = judge_dir(os.path.join(root, name), "inner" if "_inner_" in name else "cover", kind_of(name, cases.get(name)))
            found.append(got["verdict"])
            print(name, got["verdict"], got["why"], got.get("first", ""), flush=True)
    print("RESULT", {v: found.count(v) for v in sorted(set(found))}, flush=True)
    if "FAIL" in found:
        return 1
    return 0 if found and set(found) == {"PASS"} else 4


def self_test():
    size = (400, 800)
    old, new = Image.new("RGB", size, "white"), Image.new("RGB", size, "white")
    for y in range(140, 700, 30):
        old.paste((0, 0, 0), (40, y, 190, y + 9))
        new.paste((30, 90, 220), (120, y + 12, 340, y + 24))
    zoomed = old.crop((40, 140, 120, 300)).resize(size)
    bare = Image.composite(old, new, old.convert("L").point(lambda v: 255 if v < 128 else 0))  # 바탕만 투명한 덮개

    def seen(*middle, start=old, end=new, fade=True, step=60):
        rest = list(middle) + [end, end, end]
        shots = [(-40, start)] + [(50 + step * i, im) for i, im in enumerate(rest)]
        return judge_images(shots, "cover", fade)["verdict"]

    half = [Image.blend(old, new, a) for a in (0.1, 0.4, 0.8)]
    blank = Image.new("RGB", size, "white")  # 전환 바탕(나가는 그림 없음)
    faint = old.copy()  # 끝이 시작과 거의 같은 그림 — 30x30 만 바뀌어 평균 회색 차 D 가 아주 작다
    faint.paste((128, 128, 128), (200, 400, 230, 430))
    checks = {
        "1:1 선형 혼합 PASS": seen(old, *half) == "PASS",
        "5배 확대 FAIL": seen(zoomed, Image.blend(zoomed, new, 0.5)) == "FAIL",
        "바탕 없는 덮개 FAIL": seen(bare, Image.blend(bare, new, 0.5)) == "FAIL",
        "중간 0 FAIL": seen() == "FAIL",
        "표본 간격 큼 INVALID": seen(old, step=300) == "INVALID",
        "같은 그림 INVALID": seen(old, end=old) == "INVALID",
        "끝 모습 미도달 INVALID": judge_images([(-40, old), (50, half[0]), (110, half[1]), (170, half[2])], "cover", True)["verdict"] == "INVALID",
        "누르기 전 표본 없음 INVALID": judge_images([(50, old), (110, new), (170, new)], "cover", True)["verdict"] == "INVALID",
        "되돌림 FAIL": seen(Image.blend(old, new, 0.3), new, Image.blend(old, new, 0.3), half[2]) == "FAIL",
        "폴더 없음 INVALID": judge_dir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "no_such_frames"), "cover", True)["verdict"] == "INVALID",
        "전환 바탕 위 페이드 PASS": seen(*[Image.blend(blank, new, a) for a in (0.2, 0.5, 0.8)], fade="incoming") == "PASS",
        "전환 되돌림 FAIL": seen(new, Image.blend(blank, new, 0.3), Image.blend(blank, new, 0.5), half[2], fade="incoming") == "FAIL",
        "전환 첫 장이 끝 FAIL": seen(fade="incoming") == "FAIL",
        "전환 대비 부족 INVALID": seen(*[Image.blend(old, faint, a) for a in (0.3, 0.6)], end=faint, fade="incoming") == "INVALID",
        "사례 종류 판별": [kind_of(n, c) for n, c in (("00_inner_open_00", None), ("02_cover_combo_00", None), ("x", "open_01"),
                                                    ("03_cover_nav_none_2", None), ("01_inner_dock_left_2", None), ("05_control_opener", None))]
        == ["incoming", "incoming", "incoming", False, True, True],
        "none 한 번에 PASS": seen(fade=False) == "PASS",
        "none 중간 FAIL": seen(half[1], fade=False) == "FAIL",
    }
    for name, ok in checks.items():
        print("%s %s" % ("PASS" if ok else "FAIL", name))
    print("frames self-test %d/%d" % (sum(checks.values()), len(checks)))
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-test"]:
        sys.exit(self_test())
    if len(sys.argv) == 3 and sys.argv[1] == "--judge":
        sys.exit(judge_root(sys.argv[2]))
    print("사용: motion_nav_fold5_frames.py --judge <frames 폴더> | --self-test")
    sys.exit(2)

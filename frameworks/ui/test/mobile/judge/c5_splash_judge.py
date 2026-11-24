#!/usr/bin/env python3
# C5 첫 프레임·스플래시 터치 판정(판정 계층) — 측정 JSON 을 계획 합격 기준과 대조해 rc 0(합격)·1(불합격)·2(입력 오류)를 돌려준다
"""
사용: python3 -B c5_splash_judge.py --open <펼침 JSON> --closed <접힘 JSON> [--baseline-open 2267] [--baseline-closed 2234]
자체 시험(기기 불필요): python3 -B c5_splash_judge.py --self-test
게이트 구분(참고): 시행 줄이 있으면 buffered·pre_gate_unbuffered·after_gate 로 나눈다. after_gate 탭만으로는 스플래시 터치 항목을 합격시키지 않는다(기준 강화 방향).
입력: retest_android_pcaway_splash.py 의 raw 저장본. 값이 raw 아래에 있든 최상위에 있든 읽고, 노드 수 키 settings_nodes·nodes 를 모두 받는다.
기준: 첫 프레임 중앙값 2000ms 이하 또는 기준값 대비 20% 이상 단축, 첫 프레임 뒤 터치 100%, 첫 프레임 전 터치 유실 0.
유실 분류: 측정기가 기기 InputDispatcher 근거로 붙인 loss.cls 를 쓴다(before_window=앱 창에 터치를 넘길 수 없던 상태, other=앱 창 전달 뒤 유실→after_window, 근거 없음→boundary). PC 시계 tap_ms 와 기기 시계 Displayed 는 섞어 비교하지 않는다.
before_window 탭은 판정에서 제외한다(사용자 결정 2026-11-08, excluded_before_window 로 보고). boundary·after_window 는 계속 판정한다(judged_rows). 제외 뒤 남은 표본이 0 이면(제외 ≥1) 합격이다.
"""
import argparse
import json
import re
import statistics
import sys

MIN_NODES = 3
LIMIT_MS = 2000
IMPROVE_RATIO = 0.8
GATE_MARGIN_MS = 200  # 기기 시계 Inject DOWN 이 Fully drawn 줄보다 이만큼 이상 뒤여야 게이트 뒤로 본다. 애매하면 게이트 전으로 남기도록 여유를 둔다


def nodes(row):
    return row.get("settings_nodes", row.get("nodes", 0))


def displayed_ms(text):
    """logcat 값 '1s446ms'·'998ms' 를 ms 로. 못 읽으면 None."""
    m = re.fullmatch(r"(?:(\d+)s)?(\d+)ms", text or "")
    return int(m.group(1) or 0) * 1000 + int(m.group(2)) if m else None


def row_displayed(row):
    """시행 logcat 줄(lines)에 Displayed 가 있으면 그 시행의 값(ms)."""
    for text in row.get("lines") or []:
        m = re.search(r"Displayed .*\+(\S+)\s*$", str(text))
        if m:
            return displayed_ms(m.group(1))
    return None


def window_part(row, shown=None):
    """측정기가 기기 InputDispatcher 근거로 붙인 loss.cls 를 그대로 쓴다. PC 시계(tap_ms)와 기기 시계(Displayed)는 섞지 않는다(2026-11-09).
    before_window = Android 가 앱 창에 터치를 넘길 수 없던 상태(입력 싱크·닿은 창 없음·다른 프로세스 창). Displayed 줄 뒤라도 입력 채널 전이면 포함한다.
    cls 가 없거나 그 밖이면(앱 창에 전달된 탭 포함) 판정 대상으로 남긴다: other 는 after_window, 근거 없음은 boundary."""
    cls = (row.get("loss") or {}).get("cls")
    if cls == "before_window":
        return "before_window"
    return "after_window" if cls == "other" else "boundary"


def log_ms(text):
    """logcat 줄 머리의 'MM-DD HH:MM:SS.mmm' 를 하루 안 ms 로. 기기 시계끼리만 비교하는 데 쓴다."""
    m = re.match(r"\s*\d\d-\d\d (\d\d):(\d\d):(\d\d)\.(\d{3})", str(text))
    return ((int(m.group(1)) * 60 + int(m.group(2))) * 60 + int(m.group(3))) * 1000 + int(m.group(4)) if m else None


def inject_down_ms(row):
    """기기 InputDispatcher 의 첫 'Inject motion ... action=0x0'(DOWN) 시각(기기 logcat 시계)."""
    for text in row.get("input_lines") or []:
        if "Inject motion" in str(text) and "action=0x0" in str(text):
            return log_ms(text)
    return None


def fully_drawn_ms(lines):
    """logcat 'Fully drawn' 값(ms). 게이트가 ready 로 열리는 순간 앱이 부르므로 시작 요청 기준 게이트 시각이기도 하다."""
    for text in lines or []:
        m = re.search(r"Fully drawn .*\+(\S+)\s*$", str(text))
        if m:
            return displayed_ms(m.group(1))
    return None


def gate_phase(row):
    """측정 도구는 탭이 끝난 뒤 첫 프레임을 재므로 before_ff 는 언제나 참에 가깝다. 탭이 실제로 게이트 전이었는지 로그로 가른다.
    게이트 시각(Fully drawn 줄)과 탭 시각(Inject DOWN 줄)은 둘 다 기기 logcat 시계로 비교한다(PC tap_ms 는 쓰지 않는다)."""
    lines = row.get("lines")
    if not lines:
        return "unknown"
    if any("touch buffered" in str(t) for t in lines):
        return "buffered"
    gate = next((log_ms(t) for t in lines if re.search(r"Fully drawn .*\+\S+\s*$", str(t))), None)
    down = inject_down_ms(row)
    if gate is None or down is None:
        return "unknown"
    return "after_gate" if down >= gate + GATE_MARGIN_MS else "pre_gate_unbuffered"


def ff_value(row):
    """첫 프레임 판정값(ms)과 대체 지표 사용 여부. ff_ms 가 없고 ff_miss 가 first_sample_late 이면 OS Displayed 보조값 ff_aux_ms 를 대체 판정값으로 쓴다(D2). 기준값은 그대로다."""
    if row.get("ff_ms"):
        return row["ff_ms"], False
    if row.get("ff_miss") == "first_sample_late" and row.get("ff_aux_ms"):
        return row["ff_aux_ms"], True
    return None, False


def judge_mode(data, baseline):
    raw = data.get("raw", data)
    ff, aux_used = [], 0
    for r in raw.get("ff_runs", []):
        v, used = ff_value(r)
        if v:
            ff.append(v)
            aux_used += 1 if used else 0
    after = raw.get("touch_after_ff", [])
    before_all = [r for r in raw.get("touch_during_splash", []) if r.get("before_ff")]
    shown = [v for v in (displayed_ms(r.get("displayed")) for r in raw.get("ff_runs", [])) if v]
    line = min(shown) if shown else None
    median = statistics.median(ff) if ff else None
    parts = {"before_window": [], "boundary": [], "after_window": []}
    for r in before_all:
        parts[window_part(r, row_displayed(r) or line)].append(r)
    # 화면이 뜨기 전(before_window) 탭은 앱에 전달되지 않는 플랫폼 동작이라 판정에서 제외한다(사용자 결정 2026-11-08). boundary 는 계속 판정한다
    excluded = parts["before_window"]
    before = [r for r in before_all if not any(r is x for x in excluded)]
    lost = [r for r in before if nodes(r) < MIN_NODES]
    checks = {
        "first_frame": median is not None and (median <= LIMIT_MS or (baseline is not None and median <= baseline * IMPROVE_RATIO)),
        "touch_after_first_frame": bool(after) and all(nodes(r) >= MIN_NODES for r in after),
        # 전부 게이트 뒤 탭이면 스플래시 중 터치를 잰 것이 아니므로 합격 근거가 없다(유실은 게이트 뒤 탭까지 전부 센다)
        # 남은 표본이 0 이어도 before_window 제외로 비었으면(제외 ≥1) 판정 대상이 없어 합격이다. 애초에 표본이 없으면 불합격이다
        "touch_during_splash_not_lost": (not before and bool(excluded)) or (any(gate_phase(r) != "after_gate" for r in before) and not lost),
    }
    runs = raw.get("ff_runs", [])
    drawn = [v for v in (fully_drawn_ms(r.get("startup_lines")) for r in runs) if v]
    info = {
        "median_ms": median,
        "baseline_ms": baseline,
        "after_ok": "%d/%d" % (sum(nodes(r) >= MIN_NODES for r in after), len(after)),
        "before_ff": len(before_all),
        "excluded_before_window": len(excluded),
        "judged_rows": len(before),
        "lost": len(lost),
        "displayed_min_ms": line,
        "대체 지표": "ff_aux_ms(OS Displayed) %d/%d회 사용 — 첫 프레임 실측이 아닌 대체 판정" % (aux_used, len(runs)) if aux_used else None,
    }
    info["displayed_median_ms"] = statistics.median(shown) if shown else None
    info["fully_drawn_median_ms"] = statistics.median(drawn) if drawn else None  # 참고값. 첫 프레임 판정은 캡처 값 그대로다
    for r in before_all:
        parts.setdefault("gate_" + gate_phase(r), []).append(r)
    for name, rows in parts.items():
        info[name] = len(rows)
        info["lost_" + name] = sum(1 for r in rows if nodes(r) < MIN_NODES)
    return checks, info


def self_test():
    good_ff = [{"ff_ms": v, "displayed": "1s400ms"} for v in (1700, 1800, 1900)]
    after = [{"nodes": 24}] * 3
    kept = [{"before_ff": True, "tap_end_ms": 1500, "nodes": 24}]
    lost_early = [{"before_ff": True, "tap_end_ms": 400, "settings_nodes": 0, "loss": {"cls": "before_window"}}]
    lost_late = [{"before_ff": True, "tap_end_ms": 1700, "settings_nodes": 0, "loss": {"cls": "other"}}]
    mk = lambda ff, a, d: {"raw": {"ff_runs": ff, "touch_after_ff": a, "touch_during_splash": d}}
    cases = [
        ("합격", judge_mode(mk(good_ff, after, kept), 2267)[0], (True, True, True)),
        ("2000 초과지만 20% 단축이면 합격", judge_mode(mk([{"ff_ms": 2100}], after, kept), 2700)[0]["first_frame"], True),
        ("2000 초과·단축 부족은 불합격", judge_mode(mk([{"ff_ms": 2100}], after, kept), 2267)[0]["first_frame"], False),
        ("기준값 없이 2000 초과는 불합격", judge_mode(mk([{"ff_ms": 2100}], after, kept), None)[0]["first_frame"], False),
        ("창 앞(before_window) 유실은 판정 제외라 합격", judge_mode(mk(good_ff, after, lost_early), 2267)[0]["touch_during_splash_not_lost"], True),
        ("창 앞 유실 분류는 보고에 남는다", judge_mode(mk(good_ff, after, lost_early), 2267)[1]["lost_before_window"], 1),
        ("제외 건수 보고", judge_mode(mk(good_ff, after, lost_early), 2267)[1]["excluded_before_window"], 1),
        ("제외 뒤 판정 행 0", judge_mode(mk(good_ff, after, lost_early), 2267)[1]["judged_rows"], 0),
        ("창 앞 제외 + boundary 유실은 불합격", judge_mode(mk(good_ff, after, [lost_early[0], {"before_ff": True, "tap_ms": 1300, "tap_end_ms": 1500, "settings_nodes": 0}]), 2267)[0]["touch_during_splash_not_lost"], False),
        ("창 앞 제외 + 판정 행 정상은 합격", judge_mode(mk(good_ff, after, [lost_early[0], {"before_ff": True, "tap_ms": 1500, "tap_end_ms": 1600, "nodes": 24}]), 2267)[1]["judged_rows"], 1),
        ("창 뒤 유실 분류", judge_mode(mk(good_ff, after, lost_late), 2267)[1]["lost_after_window"], 1),
        ("표본 없음은 불합격", judge_mode(mk(good_ff, after, []), 2267)[0]["touch_during_splash_not_lost"], False),
        ("첫 프레임 뒤 일부 실패는 불합격", judge_mode(mk(good_ff, after + [{"nodes": 0}], kept), 2267)[0]["touch_after_first_frame"], False),
        ("Displayed 변환", displayed_ms("1s446ms"), 1446),
        ("게이트 뒤 탭만 있으면 근거 없음(불합격)", judge_mode(mk(good_ff, after, [{"before_ff": True, "tap_ms": 1500, "tap_end_ms": 1700, "nodes": 24, "lines": ["11-09 10:00:01.000 I ActivityTaskManager: Fully drawn x: +950ms"], "input_lines": ["11-09 10:00:02.000 D InputDispatcher: Inject motion (1/2): action=0x0, time=1"]}]), 2267)[0]["touch_during_splash_not_lost"], False),
        ("받아 둔 탭 전달은 합격", judge_mode(mk(good_ff, after, [{"before_ff": True, "tap_ms": 800, "tap_end_ms": 950, "nodes": 24, "lines": ["[startup] touch buffered +900ms", "Fully drawn x: +950ms"]}]), 2267)[0]["touch_during_splash_not_lost"], True),
        ("게이트 전 미수신 분류", gate_phase({"lines": ["11-09 10:00:01.000 I ActivityTaskManager: Fully drawn x: +950ms"], "input_lines": ["11-09 10:00:01.050 D InputDispatcher: Inject motion (1/2): action=0x0, time=1"]}), "pre_gate_unbuffered"),
        ("줄 없는 옛 형식은 unknown", gate_phase({"tap_ms": 800}), "unknown"),
        ("근거(loss.cls) 없는 탭은 boundary(PC 시계로 나누지 않음)", window_part({"tap_ms": 1300, "tap_end_ms": 1500}, 1400), "boundary"),
        ("앱 창 전달 뒤 유실(other)은 after_window", window_part({"tap_ms": 1500, "tap_end_ms": 1600, "loss": {"cls": "other"}}, 1400), "after_window"),
        ("시계 어긋남 + Displayed 0~17ms 뒤 NO_INPUT_CHANNEL 은 제외라 합격", judge_mode(mk(good_ff, after, [{"before_ff": True, "tap_ms": 1439, "tap_end_ms": 2196, "settings_nodes": 0, "loss": {"cls": "before_window", "basis": "input_sink"}, "lines": ["11-09 15:31:28.127 I ActivityTaskManager: Displayed x: +1s37ms"], "input_lines": ["11-09 15:31:28.130 D InputDispatcher: Inject motion (1/2): action=0x0, time=1", "11-09 15:31:28.130 W InputDispatcher: Not sending touch gesture to ActivityRecordInputSink because it has config NO_INPUT_CHANNEL"]}]), 2267)[0]["touch_during_splash_not_lost"], True),
        ("앱 창에 전달됐다가 사라진 탭(other)은 불합격", judge_mode(mk(good_ff, after, [{"before_ff": True, "tap_ms": 1439, "tap_end_ms": 2196, "settings_nodes": 0, "loss": {"cls": "other"}}]), 2267)[0]["touch_during_splash_not_lost"], False),
        ("first_sample_late 는 ff_aux_ms 로 대체 판정(합격)", judge_mode(mk([{"ff_miss": "first_sample_late", "ff_aux_ms": 1141}] * 3, after, kept), 2267)[0]["first_frame"], True),
        ("대체 지표 표기", "대체" in str(judge_mode(mk([{"ff_miss": "first_sample_late", "ff_aux_ms": 1141}], after, kept), 2267)[1]["대체 지표"]), True),
        ("대체값도 기준 2000 초과·단축 부족이면 불합격", judge_mode(mk([{"ff_miss": "first_sample_late", "ff_aux_ms": 2100}], after, kept), 2267)[0]["first_frame"], False),
        ("다른 사유(all_blank)엔 대체 안 함", judge_mode(mk([{"ff_miss": "all_blank", "ff_aux_ms": 500}], after, kept), 2267)[0]["first_frame"], False),
        ("ff_ms 있으면 대체 미사용", judge_mode(mk(good_ff, after, kept), 2267)[1]["대체 지표"], None),
        ("시행 줄의 Displayed 우선", row_displayed({"lines": ["I ActivityTaskManager: Displayed com.pd.poc/x for user 0: +1s565ms"]}), 1565),
    ]
    bad = 0
    for name, got, want in cases:
        if isinstance(want, tuple):
            got = tuple(got.values())
        ok = got == want
        bad += 0 if ok else 1
        print("%s %s got=%s want=%s" % ("PASS" if ok else "FAIL", name, got, want))
    return 1 if bad else 0


def main():
    if "--self-test" in sys.argv:
        return self_test()
    p = argparse.ArgumentParser()
    p.add_argument("--open", required=True)
    p.add_argument("--closed", required=True)
    p.add_argument("--baseline-open", type=int, default=2267)
    p.add_argument("--baseline-closed", type=int, default=2234)
    a = p.parse_args()
    fail = 0
    for mode, path, base in (("open", a.open, a.baseline_open), ("closed", a.closed, a.baseline_closed)):
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, ValueError) as ex:
            print("입력 오류 %s: %s" % (path, ex))
            return 2
        checks, info = judge_mode(data, base)
        print(mode, json.dumps(info, ensure_ascii=False))
        for name, ok in checks.items():
            print("  %s %s" % ("PASS" if ok else "FAIL", name))
            fail += 0 if ok else 1
    print("판정:", "합격" if not fail else "불합격 %d건" % fail)
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())

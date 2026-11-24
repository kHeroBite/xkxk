#!/usr/bin/env python3
# Fold5 Release 첫 프레임·스플래시 터치·기동 회귀 측정(C5) — 콜드 기동 시각, logcat 기동 표식, 터치 전달·replay 사유, 스크롤·접기 회귀를 PC 유휴 값과 함께 남긴다
"""
사용: python3 -B retest_android_pcaway_splash.py --apk <Release APK> --out <폴더> --mode open|closed --stage touch|ff|all [--runs 5] [--min-idle-s 300] [--wait-idle-s 0] [--baseline-ms N] [--gate-delays 900,950] [--gate-runs 10] [--gate-step 25] [--gate-span 200] [--fresh] [--measure]
자체 시험(기기 불필요): python3 -B retest_android_pcaway_splash.py --self-test
단계: touch=터치 전달·replay·회귀(PC 사용 중에도 유효), ff=첫 프레임(PC 유휴일 때만 유효). 같은 폴더의 원자료에 합쳐 저장한다.
첫 프레임: 빈 화면(색 수 < 8) 표본을 한 번 본 뒤 터치 대상 영역 색 수 ≥ 8 이 2장 연속일 때 첫 장 시각. 첫 표본부터 그려져 있으면 기동 전 화면이라 인정하지 않는다(못 잡음 = None). 캡처 1장 소요만큼 늦게 잡히므로 표본을 남긴다.
유휴: 시행 직전 user_away_guard.read_probe 로 Windows 유휴 시간을 읽는다. --min-idle-s 미만 시행은 ff_reference 로 분리하고 ff_runs(판정 입력)에 넣지 않는다.
화면: ff 단계는 콜드 기동 직전 화면을 깨우고 mWakefulness=Awake·활성 화면을 확인한다. 아니면 그 시행은 ff_invalid(사유 display_inactive)로만 남긴다. 못 잡은 시행은 ff_miss 에 사유를 남긴다.
--wait-idle-s 를 주면 유휴가 될 때까지 그만큼 기다리고, 그래도 안 되면 참고값으로만 잰다. 유휴 대기는 입력을 만들지 않는다.
logcat: 콜드 기동마다 [startup]·Displayed·Fully drawn 줄을 startup_lines 에, touch buffered·replay·gate open 줄도 함께 저장한다.
터치 전달: 펼침은 시스템(Y) 상위 메뉴(shell.mnuSys, 도구모음 줄이 없어 설정 버튼은 없다)를 눌러 시스템 메뉴 항목(shell.mnuSys*) 노드 ≥3 이면 전달, 접힘은 설정 탭을 눌러 settings.* 노드 ≥3 이면 전달. 파일 메뉴가 열리면 오탭이다. 유실이면 다시 눌러 고착 여부를 본다.
덤프 재시도: 노드가 3 미만이면 간격을 두고 최대 3번 다시 덤프한다(uiautomator 미검출 오탐 대비).
유실 분류(C5·P4): 스플래시 중 탭 시행마다 유실을 loss={cls,basis} 로 남긴다. before_window=탭까지 앱 로그가 없던 때(앱 창이 붙기 전, 플랫폼 한계 후보), design_discard=앱이 연타·오래됨·끌기로 버린 때, other=그 밖(0 이어야 한다). 집계는 raw.loss_summary.
스플래시 중 탭의 첫 프레임: 탭과 병렬로 캡처해 탭과 독립적으로 잰다. 참값은 ff_lower_ms~ff_ms 사이다. 캡처가 기동에 주는 부하는 ff 단계와 같다.
before_ff: 탭 시작이 첫 프레임보다 앞이면 참(첫 프레임을 못 잡아도 참). 거짓인 시행은 touch_after_ff 에도 넣어 어느 시행도 판정에서 빠지지 않게 한다.
게이트 구간: --gate-delays(기본 펼침 900,950 / 접힘 1020,1070, 빈 문자열이면 생략)를 번갈아, 지연별 --gate-runs 회 이상 재고 buffered 가 --gate-buffered 건에 닿으면 멈춘다(상한 --gate-max-runs). 재전송했는데 안 열린 비율 재료는 raw.gate_summary 에 남는다. 구간이 좁아 buffered 가 없으면 --gate-step 씩 옮기되 기준 지연 ±--gate-span 안에 둔다.
시행 줄: lines(기동 표식), input_lines(logcat -b all 의 InputDispatcher·InputManager·dropped·Dropping), marks(buffered·direct·replay target·result·gate open).
판정은 test/mobile/judge/c5_splash_judge.py 가 한다.
"""
import argparse
import io
import json
import os
import re
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "desktop", "measure"))
import retest_android_pcaway_lib as L  # noqa: E402
import user_away_guard as guard  # noqa: E402

DELAYS_MS = (300, 800, 1500)
GATE_DELAYS_MS = {"open": "900,950", "closed": "1020,1070"}  # 전체 AOT 기준 추정, 시작 요청 기준
LINE_RX = re.compile(r"\[startup\]|Displayed [^ ]*com\.pd\.poc|Fully drawn [^ ]*com\.pd\.poc")
INPUT_RX = re.compile(r"InputDispatcher|InputManager|dropped|Dropping")
INPUT_LINES_MAX = 80
log_since = None  # 직전 콜드 기동 시작의 기기 시각. -b all 은 지우지 않고 이 시각 뒤만 읽는다
cold_dev_ms = None  # 직전 콜드 기동 시작의 기기 시각(그날 0시부터 ms). 앱 로그 시각과 같은 시계라 창 붙기 전 구간 판정에 쓴다
MODE = "closed"  # 측정 상태. main 이 정한다(호출하는 다른 도구도 S.MODE 로 정한다)
OPEN_TARGET = "shell.mnuSys"  # 펼침 대상: 시스템(Y) 상위 메뉴. 도구모음 줄이 없어져 설정 버튼은 없다
CLOSED_TARGET = "shell.navTab.Settings"  # 접힘 대상: 설정 탭
DUMP_TRIES = 3  # uiautomator 덤프 미검출 오탐 대비 재시도 횟수
LOSS_DESIGN_RX = re.compile(r"drop|discard|stale|old|multi|drag|coalesc|ignor|skip", re.I)  # 설계상 버림(연타·오래됨·끌기) 사유 낱말
LOSS_CLASSES = ("before_window", "design_discard", "other")


def target_id(mode):
    return CLOSED_TARGET if mode == "closed" else OPEN_TARGET


def target_nodes(ids, mode):
    """대상을 눌러 열린 것의 노드 수. 접힘은 설정 창 settings.* 이고, 펼침은 시스템 메뉴 항목(shell.mnuSys* 중 상위 메뉴 자신 제외)이다."""
    if mode == "closed":
        return len([i for i in ids if i.startswith("settings.")])
    return len([i for i in ids if i.startswith("shell.mnuSys") and i != OPEN_TARGET])


def activity():
    return L.sh("cmd package resolve-activity --brief %s" % L.PKG).splitlines()[-1].strip()


def cold(act, wait=False):
    global log_since, cold_dev_ms
    L.sh("am force-stop %s" % L.PKG)
    time.sleep(1.2)
    L.adb("logcat", "-c")
    stamp = L.sh("date '+%m-%d %H:%M:%S.%N'").strip()
    head, _dot, frac = stamp.partition(".")
    frac = (frac[:3] if frac[:3].isdigit() and len(frac) >= 3 else "000")
    log_since = "%s.%s" % (head, frac)
    cold_dev_ms = clock_ms(log_since)
    t0 = time.time()
    return t0, L.sh("am start -W -n %s" % act, timeout=90) if wait else L.adb("shell", "am start -n %s" % act)[1]


def colors(data, rect):
    try:
        return L.U.drawn_colors(data, rect)
    except Exception:  # noqa: BLE001 — 깨진 캡처는 안 그려진 것으로 센다
        return 0


def ff_from_samples(samples, min_colors):
    """표본 [(ms, 색 수)] 에서 첫 프레임 시각. 빈 화면(색 수 < min_colors) 표본을 한 번 본 뒤 2장 연속 그려진 첫 장 시각이다.
    첫 표본부터 그려져 있으면 앱 그림이 아니라 기동 전 화면(앞 앱·전환 잔상)이므로 인정하지 않는다(P4 2026-11-10: 그때 앱 창은 탭 뒤에 붙었다)."""
    blank, streak = False, 0
    for i, (_t, n) in enumerate(samples):
        if n < min_colors:
            blank, streak = True, 0
        elif blank:
            streak += 1
            if streak >= 2:
                return samples[i - 1][0]
    return None


def first_frame(rect, t0, limit=30, disp=None):
    samples, disp = [], disp or L.display_id()
    while time.time() - t0 < limit:
        n = colors(L.png(disp), rect)
        samples.append((int((time.time() - t0) * 1000), n))
        ms = ff_from_samples(samples, L.U.FIRST_FRAME_MIN_COLORS)
        if ms is not None:
            return ms, samples
    return None, samples


def ff_miss_reason(ms, samples, disp, min_colors):
    """첫 프레임을 못 잡은 사유. 잡았으면 None.
    display_inactive=캡처할 활성 화면이 없음, first_sample_late=첫 표본부터 그려져 있어 기동 전 화면과 구분할 수 없음(첫 캡처가 늦음),
    all_blank=끝까지 빈 화면, no_streak=빈 화면 뒤 2장 연속 그려짐이 없음."""
    if ms is not None:
        return None
    if not disp:
        return "display_inactive"
    if not samples or all(n < min_colors for _t, n in samples):
        return "all_blank"
    if all(n >= min_colors for _t, n in samples):
        return "first_sample_late"
    return "no_streak"


def displayed_ms(text):
    """OS Displayed 값('+523ms'·'+1s234ms'·'+1s')을 ms 로. 못 읽으면 None."""
    m = re.fullmatch(r"\+?(?:(\d+)s)?(?:(\d+)ms)?", text or "")
    if not m or not (m.group(1) or m.group(2)):
        return None
    return int(m.group(1) or 0) * 1000 + int(m.group(2) or 0)


def ff_aux_ms(miss, samples, disp_text):
    """first_sample_late 일 때만 쓰는 보조 판정. 첫 표본이 이미 그려졌어도 OS Displayed 가 첫 표본 시각 이내이면
    앱이 그 시점까지 그려진 것이므로 Displayed ms 를 보조 첫 프레임으로 돌려준다. 주 판정(ff_ms)·기존 사유는 바꾸지 않는다."""
    if miss != "first_sample_late" or not samples:
        return None
    d = displayed_ms(disp_text)
    return d if d is not None and d <= samples[0][0] else None


def power_state(wake_text, disp):
    """기동 직전 전원·화면 상태. ok 는 mWakefulness=Awake 이고 활성 화면이 있을 때만 참이다."""
    m = re.search(r"mWakefulness=(\w+)", wake_text or "")
    w = m.group(1) if m else None
    return {"wakefulness": w, "display_id": disp, "ok": w == "Awake" and bool(disp)}


def screen_ready():
    """화면을 깨운 뒤 전원 상태를 다시 읽는다. 화면 꺼짐(Dozing)에서 콜드 기동하면 캡처가 내내 검은 화면이라 첫 프레임을 잴 수 없다."""
    L.wake()
    return power_state(L.sh("dumpsys power | grep mWakefulness="), L.display_id())


def startup_lines():
    return [ln.strip()[:200] for ln in L.adb("logcat", "-d", "-v", "threadtime")[1].splitlines() if LINE_RX.search(ln)]


def input_lines():
    """시스템이 버린 터치를 직접 보기 위한 입력 계열 줄. 뒤쪽 INPUT_LINES_MAX 줄만 남긴다."""
    out = L.adb("logcat", "-b", "all", "-d", "-v", "threadtime", "-T", log_since)[1]
    return [ln.strip()[:240] for ln in out.splitlines() if INPUT_RX.search(ln)][-INPUT_LINES_MAX:]


def trial_marks(lines):
    """기동 표식에서 터치 경로 값만 뽑는다. 없으면 None."""
    def first(rx):
        for ln in lines:
            m = re.search(rx, ln)
            if m:
                return m.group(1)
        return None
    return {
        "buffered": first(r"touch buffered \+(\d+)ms"),
        "direct": first(r"touch direct \+(\d+ms target=.*)$"),
        "replay_target": first(r"replay target=(.*)$"),
        "replay_result": first(r"replay result=(.*)$"),
        "gate_open": first(r"gate open (reason=.*)$"),
    }


def is_before_ff(tap_ms, ff_ms):
    return ff_ms is None or tap_ms < ff_ms


def clock_ms(stamp):
    """logcat/date 의 'MM-DD HH:MM:SS.mmm' 에서 그날 0시부터의 ms. 읽지 못하면 None."""
    m = re.search(r"(\d\d):(\d\d):(\d\d)\.(\d{3})", stamp or "")
    return None if not m else ((int(m.group(1)) * 60 + int(m.group(2))) * 60 + int(m.group(3))) * 1000 + int(m.group(4))


def first_app_line_ms(lines, cold_ms):
    """앱의 첫 [startup] 줄이 기동 시작 뒤 몇 ms 에 찍혔는지. 앱 로그가 없으면 None."""
    for ln in lines:
        if "[startup]" in ln:
            t = clock_ms(ln)
            if t is not None and cold_ms is not None:
                return (t - cold_ms) % 86400000
    return None


def app_pids(lines):
    """앱 프로세스 pid 집합(logcat threadtime 의 DOTNET 줄 pid 칸)."""
    out = set()
    for ln in lines:
        m = re.match(r"\d\d-\d\d \S+\s+(\d+)\s+\d+\s+\w\s+DOTNET", ln)
        if m:
            out.add(m.group(1))
    return out


def dispatch_outside_app(input_lines_, pids):
    """마지막 주입 DOWN 을 시스템이 앱 창 밖에서 처리했으면 그 근거 줄, 아니면 None.
    근거: InputDispatcher 가 ActivityRecordInputSink 에 막혔다(NO_INPUT_CHANNEL)·닿은 창이 없다·앱이 아닌 pid 창에 DOWN 을 보냈다. 앱 창이 화면에 뜨기 전이라는 뜻이다."""
    idx = max([i for i, ln in enumerate(input_lines_) if "Inject motion" in ln and "action=0x0," in ln] or [-1])
    if idx < 0:
        return None
    for ln in input_lines_[idx + 1:]:
        if "Inject motion" in ln and "action=0x1," in ln:
            return None
        if "Not sending touch gesture" in ln and "ActivityRecordInputSink" in ln:
            return "input_sink"
        if "No new touched window" in ln:
            return "no_window"
        m = re.search(r"Delivering touch to \((\d+)\): action: 0x0,", ln)
        if m:
            return None if not pids or m.group(1) in pids else "other_pid:%s" % m.group(1)
    return None


def classify_loss(row, cold_ms=None):
    """스플래시 중 터치 유실을 ①창 붙기 전 ②설계상 버림(연타·오래됨·끌기) ③그 밖으로 나눈다. 유실이 아니면 None.
    유실: 첫 덤프에서 대상이 안 열림(nodes<3). ①은 앱 표식이 없고, 시스템 입력 줄(input_lines)이 탭을 앱 창 밖에서 처리했다고 남겼거나(입력 싱크·닿은 창 없음·다른 pid),
    탭 시각까지 앱 로그([startup])가 하나도 없던 때다(앱이 터치를 받을 수 없다). 앱 프로세스 로그는 창이 뜨기 약 0.9초 전부터 찍히므로 그것만으로 ③ 이라 하지 않는다.
    ②는 앱이 터치를 받아 사유를 남기며 버린 때(replay result 에 버림 사유). 앱 로그가 있고 사유도 없으면 ③(앱이 받았어야 할 유실)이다."""
    if row.get("nodes", 0) >= 3:
        return None
    marks = row.get("marks") or {}
    result = marks.get("replay_result") or ""
    tap_ms = row.get("tap_ms")
    app_ms = first_app_line_ms(row.get("lines") or [], cold_ms)
    has_mark = any(marks.get(k) for k in ("buffered", "direct", "replay_target", "replay_result"))
    outside = None if has_mark else dispatch_outside_app(row.get("input_lines") or [], app_pids(row.get("lines") or []))
    if outside:
        return {"cls": "before_window", "basis": "시스템 입력 줄 %s(앱 창 표시 전) tap_ms=%s" % (outside, tap_ms)}
    if not has_mark and (app_ms is None or (tap_ms is not None and tap_ms < app_ms)):
        return {"cls": "before_window", "basis": "앱 로그 없음 app_first_ms=%s tap_ms=%s" % (app_ms, tap_ms)}
    if result and not result.startswith("sent") and LOSS_DESIGN_RX.search(result):
        return {"cls": "design_discard", "basis": "replay result=%s" % result}
    return {"cls": "other", "basis": "marks=%s app_first_ms=%s tap_ms=%s" % ({k: v for k, v in marks.items() if v}, app_ms, tap_ms)}


def loss_summary(rows):
    """유실 분류 집계. classified_all 은 유실 행 전부가 분류됐는지(미분류 0)다."""
    lost = [r for r in rows if (r.get("loss") or {}).get("cls")]
    counts = {c: len([r for r in lost if r["loss"]["cls"] == c]) for c in LOSS_CLASSES}
    return {"trials": len(rows), "lost": len(lost), **counts, "classified_all": all(r["loss"]["cls"] in LOSS_CLASSES for r in lost)}


def next_delay(cur, base, step, span, marks, nodes):
    """buffered 가 나오면 유지, 게이트 뒤로 간 탭이면 앞으로, 아무 흔적이 없으면 뒤로 옮긴다."""
    if marks["buffered"]:
        return cur
    move = -step if marks["direct"] or nodes >= 3 else step
    return max(base - span, min(base + span, cur + move))


def gate_summary(rows):
    """게이트 구간 시행의 buffered 수와, 재전송을 보냈는데 안 열린 비율의 재료."""
    zone = [r for r in rows if r.get("gate_zone")]
    buffered = [r for r in zone if r["marks"]["buffered"]]
    sent = [r for r in zone if (r["marks"]["replay_result"] or "").startswith("sent")]
    missed = [r for r in sent if r["nodes"] < 3]
    return {"trials": len(zone), "buffered": len(buffered), "sent": len(sent), "sent_not_opened": len(missed), "sent_not_opened_targets": [r["marks"]["replay_target"] for r in missed]}


def displayed(lines):
    for ln in lines:
        m = re.search(r"Displayed [^ ]*com\.pd\.poc[^ ]* (?:for user \d+: )?\+([0-9a-z]+)", ln)
        if m:
            return m.group(1)
    return None


def idle():
    """Windows 유휴 시간(초)과 부하. 읽기 실패는 None(유휴로 세지 않는다)."""
    load = open("/proc/loadavg").read().split()[0]
    try:
        p = guard.read_probe()
        return {"idle_s": round(p["idle_ms"] / 1000, 1), "load1": load}
    except Exception as e:  # noqa: BLE001 — 유휴를 못 읽어도 터치 측정은 계속한다
        return {"idle_s": None, "load1": load, "error": repr(e)[:120]}


def wait_idle(min_s, limit_s):
    t0, cur = time.time(), idle()
    while (cur["idle_s"] or 0) < min_s and time.time() - t0 < limit_s:
        time.sleep(30)
        cur = idle()
    return cur


def settings_nodes(c, tries=DUMP_TRIES):
    """대상이 열려 생긴 노드 수. uiautomator 덤프가 가끔 못 잡는(오탐) 일이 있어 3 미만이면 간격을 두고 최대 tries 번 다시 덤프한다."""
    n = 0
    for k in range(tries):
        n = target_nodes(c.ids(c.dump()), MODE)
        if n >= 3:
            break
        if k < tries - 1:
            time.sleep(0.8)
    return n


def tap_ok(c, target):
    ok = c.tap_id(target)
    time.sleep(2.5)
    n = settings_nodes(c)
    c.tap_id(target) if target.startswith("shell.navTab") else L.sh("input tap %d %d" % (L.wm_size()[0] // 2, L.wm_size()[1] - 400))
    time.sleep(1.2)
    return {"tap": ok, "nodes": n}


def crop_changed(before, after):
    from PIL import Image, ImageChops
    a, b = (Image.open(io.BytesIO(x)).convert("L") for x in (before, after))
    box = (a.width // 4, a.height // 3, a.width * 3 // 4, a.height * 2 // 3)
    d = ImageChops.difference(a.crop(box), b.crop(box)).point(lambda v: 255 if v > 24 else 0)
    return round(sum(1 for v in d.getdata() if v) / (d.width * d.height), 4)


def regress_scroll(c, mode):
    """기동 뒤 실터치로 그리드를 열고 스와이프해 내용 영역이 바뀌는지 본다(원격 없음)."""
    if mode == "closed":
        c.tap_id("shell.btnDrawer")
        time.sleep(1.5)
        opened = c.tap_id("shell.navDrawer.OpenGrid")
    else:
        c.tap_id("shell.mnuFile")
        time.sleep(1.5)
        opened = c.tap_id("shell.mnuOpenGrid")
    time.sleep(3)
    w, h = L.wm_size()
    p0 = L.png()
    # 펼침의 그리드 창은 세로 약 500~1200px 이다. 창 밖에서 시작하면 스크롤이 일어나지 않으므로 창 안에서 민다
    y0, y1 = (1150, 700) if mode == "open" else (h * 2 // 3, h // 3)
    L.sh("input swipe %d %d %d %d 300" % (w // 2, y0, w // 2, y1))
    time.sleep(1.5)
    p1 = L.png()
    r = {"opened": opened, "changed_ratio": crop_changed(p0, p1), "on_top": L.app_on_top(), "pid": bool(L.sh("pidof %s" % L.PKG))}
    r["pass"] = opened and r["changed_ratio"] > 0.01 and r["on_top"] and r["pid"]
    return r


def regress_fold(c, act, mode):
    """실행 중 접기·펴기와 기동 도중 접기 뒤 앱 전면·터치 반응."""
    rows = []
    other = "open" if mode == "closed" else "closed"
    for step, m in ((other, other), ("back", mode)):
        (L.set_closed if m == "closed" else L.set_open)()
        time.sleep(2)
        target = target_id(m)
        r = {"step": step, "state": L.dev_state(), "on_top": L.app_on_top(), **tap_ok(c, target)}
        rows.append(r)
    # 기동 도중 전환
    t0, _ = cold(act)
    time.sleep(0.3)
    (L.set_closed if other == "closed" else L.set_open)()
    time.sleep(5)
    target = target_id(other)
    r = {"step": "during_boot", "state": L.dev_state(), "on_top": L.app_on_top(), **tap_ok(c, target), "lines": startup_lines()}
    rows.append(r)
    (L.set_closed if mode == "closed" else L.set_open)()
    return rows


def stage_ff(c, a, act, rect, state):
    ff, ref, invalid = [], [], []
    for i in range(a.runs):
        L.ensure(state)
        cur = wait_idle(a.min_idle_s, a.wait_idle_s) if a.wait_idle_s else idle()
        power = screen_ready()  # 유휴 대기 뒤 화면이 꺼져 있을 수 있어 기동 직전에 깨우고 확인한다
        if not power["ok"]:  # 화면이 안 켜지면 잴 수 없는 시행이라 판정 입력(ff_runs)·참고값 어디에도 넣지 않는다
            invalid.append({"i": i, "reason": "display_inactive", "power": power, "idle": cur})
            print("first_frame", i, "invalid display_inactive", power, flush=True)
            continue
        t0, _ = cold(act)
        ms, samples = first_frame(rect, t0, disp=power["display_id"])
        time.sleep(1.5)
        lines = startup_lines()
        rec = {"i": i, "ff_ms": ms, "ff_miss": ff_miss_reason(ms, samples, power["display_id"], L.U.FIRST_FRAME_MIN_COLORS), "power": power,
               "displayed": displayed(lines), "idle": cur, "startup_lines": lines, "samples": samples[:30]}
        rec["ff_aux_ms"] = ff_aux_ms(rec["ff_miss"], samples, rec["displayed"])  # first_sample_late 보조 판정(없으면 None)
        (ff if (cur["idle_s"] or 0) >= a.min_idle_s else ref).append(rec)
        print("first_frame", i, ms, rec["ff_miss"], rec["displayed"], cur, flush=True)
        time.sleep(1)
    c.raw["ff_runs"], c.raw["ff_reference"], c.raw["ff_invalid"] = ff, ref, invalid
    am = []
    for _i in range(a.runs):
        L.ensure(state)
        _t, o = cold(act, wait=True)
        tt = re.search(r"TotalTime: (\d+)", o)
        am.append(int(tt.group(1)) if tt else None)
        time.sleep(1)
    c.raw["am_total_ms"] = am


def stage_touch(c, a, act, rect, tap_xy, state):
    tx, ty = tap_xy
    after = []
    for i in range(a.runs):
        L.ensure(state)
        t0, _ = cold(act)
        ms, _s = first_frame(rect, t0)
        t_tap = time.time()
        L.sh("input tap %d %d" % (tx, ty))
        time.sleep(3.5)
        after.append({"i": i, "ff_ms": ms, "tap_ms": int((t_tap - t0) * 1000), "nodes": settings_nodes(c), "lines": startup_lines()})
        print("after", {k: after[-1][k] for k in ("i", "ff_ms", "tap_ms", "nodes")}, flush=True)
    c.raw["touch_after_ff"] = after
    during = []
    disp = L.display_id()

    def trial(delay, used, i, zone):
        L.ensure(state)
        t0, _ = cold(act)
        box = {}
        th = threading.Thread(target=lambda: box.update(zip(("ms", "samples"), first_frame(rect, t0, 15, disp))), daemon=True)
        th.start()  # 탭과 무관하게 첫 프레임을 잰다
        wait = used / 1000 - (time.time() - t0)
        if wait > 0:
            time.sleep(wait)
        t_tap = time.time()
        L.sh("input tap %d %d" % (tx, ty))
        t_end = time.time()
        th.join()
        time.sleep(max(0, 4 - (time.time() - t_end)))
        ms, samples = box.get("ms"), box.get("samples", [])
        r = {"delay": delay, "delay_used": used, "gate_zone": zone, "i": i, "tap_ms": int((t_tap - t0) * 1000), "tap_end_ms": int((t_end - t0) * 1000), "ff_ms": ms, "ff_lower_ms": samples[-3][0] if ms and len(samples) >= 3 else None, "nodes": settings_nodes(c)}
        r["before_ff"] = is_before_ff(r["tap_ms"], ms)
        r["lines"] = startup_lines()
        r["input_lines"] = input_lines()
        r["marks"] = trial_marks(r["lines"])
        r["loss"] = classify_loss(r, cold_dev_ms)  # 유실이면 {cls, basis}, 아니면 None
        if r["nodes"] < 3:
            L.sh("input tap %d %d" % (tx, ty))
            time.sleep(3)
            r["retap_nodes"] = settings_nodes(c)
        during.append(r)
        if not r["before_ff"]:
            after.append(r)
        print("during", {k: r.get(k) for k in ("delay", "delay_used", "i", "tap_ms", "tap_end_ms", "ff_ms", "nodes", "before_ff", "retap_nodes", "marks")}, "input_lines", len(r["input_lines"]), flush=True)
        return r

    for delay in ([int(x) for x in a.delays.split(",")] if a.delays else DELAYS_MS):
        for i in range(a.runs):
            trial(delay, delay, i, False)
    gate = GATE_DELAYS_MS[a.mode] if a.gate_delays is None else a.gate_delays
    used = {d: d for d in [int(x) for x in gate.split(",") if x]}
    done = 0
    # 지연을 번갈아 돌리며 buffered 시행이 목표에 닿거나 상한에 이를 때까지 잰다
    while used and done < a.gate_max_runs:
        for delay in used:
            r = trial(delay, used[delay], done, True)
            used[delay] = next_delay(used[delay], delay, a.gate_step, a.gate_span, r["marks"], r["nodes"])
            done += 1
        if done >= a.gate_runs * len(used) and gate_summary(during)["buffered"] >= a.gate_buffered:
            break
    c.raw["touch_during_splash"] = during
    c.raw["gate_summary"] = gate_summary(during)
    c.raw["loss_summary"] = loss_summary([r for r in during if r["before_ff"]])
    print("gate_summary", c.raw["gate_summary"], flush=True)
    print("loss_summary", c.raw["loss_summary"], flush=True)


def stage_regress(c, a, act, rect):
    t0, _ = cold(act)
    first_frame(rect, t0)
    time.sleep(2)
    c.raw["regress_scroll"] = regress_scroll(c, a.mode)
    print("regress_scroll", c.raw["regress_scroll"], flush=True)
    c.shot("regress_scroll")
    c.raw["regress_fold"] = regress_fold(c, act, a.mode)
    print("regress_fold", json.dumps(c.raw["regress_fold"], ensure_ascii=False)[:600], flush=True)


def self_test():
    global MODE
    MODE = "open"
    none = trial_marks([])
    buf = trial_marks(["D DOTNET: [startup] touch buffered +930ms", "D DOTNET: [startup] gate open reason=ready +950ms", "D DOTNET: [startup] replay target=shell.mnuSys", "D DOTNET: [startup] replay result=sent age=20ms"])
    direct = trial_marks(["D DOTNET: [startup] touch direct +960ms target=shell.mnuSys"])
    cm = clock_ms("11-07 10:00:00.000")
    app_line = "11-07 10:00:00.700 D DOTNET: [startup] boot"
    lost = {"nodes": 0, "tap_ms": 500, "lines": [app_line], "marks": none}
    pid_lines = ["11-07 10:00:00.700 12715 12715 I DOTNET  : [startup] app_builder_begin +76ms"]

    def inj(line):
        """주입 DOWN·UP 사이에 판정 줄 하나를 넣은 시스템 입력 줄."""
        return ["11-07 10:00:00.900  3024  3057 D InputDispatcher: Inject motion (1/2000): action=0x0, time=1, f=0x0", "11-07 10:00:00.901  3024  3904 " + line,
                "11-07 10:00:00.902  3024  3057 D InputDispatcher: Inject motion (1/2000): action=0x1, time=1, f=0x0"]
    sys_ids = ["shell.mnuSys", "shell.mnuSysSettings", "shell.mnuSysMonitor", "shell.mnuSysLog", "shell.mnuFile"]

    class FakeCtx:  # 덤프 재시도 시험용: 앞 두 번은 못 잡고 세 번째에 잡는다
        def __init__(self, seq):
            self.seq, self.n = seq, 0

        def dump(self):
            self.n += 1
            return self.seq[min(self.n - 1, len(self.seq) - 1)]

        def ids(self, xml):
            return xml
    cases = [
        ("표식 없음", none["buffered"], None),
        ("buffered 값", buf["buffered"], "930"),
        ("replay target 값", buf["replay_target"], "shell.mnuSys"),
        ("replay result 값", buf["replay_result"], "sent age=20ms"),
        ("gate open 값", buf["gate_open"], "reason=ready +950ms"),
        ("direct 값", direct["direct"], "960ms target=shell.mnuSys"),
        ("첫 프레임 전 탭", is_before_ff(900, 1000), True),
        ("첫 프레임 뒤 탭", is_before_ff(1000, 1000), False),
        ("첫 프레임 못 잡음", is_before_ff(900, None), True),
        ("첫 프레임: 빈 화면 뒤 2장 연속의 첫 장", ff_from_samples([(600, 0), (1100, 0), (1600, 9), (2100, 9)], 8), 1600),
        ("첫 프레임: 첫 표본부터 그려짐은 불인정", ff_from_samples([(620, 9), (1150, 9), (1700, 9)], 8), None),
        ("첫 프레임: 잔상 뒤 빈 화면 거쳐 그려짐", ff_from_samples([(620, 9), (1150, 0), (1700, 9), (2200, 9)], 8), 1700),
        ("첫 프레임: 1장만 그려지면 아직 아님", ff_from_samples([(600, 0), (1100, 9), (1600, 0)], 8), None),
        ("buffered 면 유지", next_delay(900, 900, 25, 200, buf, 24), 900),
        ("direct 면 앞으로", next_delay(900, 900, 25, 200, direct, 0), 875),
        ("흔적 없고 전달이면 앞으로", next_delay(900, 900, 25, 200, none, 24), 875),
        ("흔적 없고 유실이면 뒤로", next_delay(900, 900, 25, 200, none, 0), 925),
        ("범위 위 끝", next_delay(1100, 900, 25, 200, none, 0), 1100),
        ("범위 아래 끝", next_delay(700, 900, 25, 200, direct, 0), 700),
        ("게이트 요약", gate_summary([
            {"gate_zone": True, "nodes": 0, "marks": buf},
            {"gate_zone": True, "nodes": 24, "marks": buf},
            {"gate_zone": True, "nodes": 0, "marks": none},
            {"gate_zone": False, "nodes": 0, "marks": buf},
        ]), {"trials": 3, "buffered": 2, "sent": 2, "sent_not_opened": 1, "sent_not_opened_targets": ["shell.mnuSys"]}),
        ("펼침 대상 노드: 시스템 메뉴 항목만 센다", target_nodes(sys_ids, "open"), 3),
        ("접힘 대상 노드: settings.* 만 센다", target_nodes(["settings.a", "settings.b", "shell.mnuSysLog"], "closed"), 2),
        ("펼침: 파일 메뉴만 열리면 0", target_nodes(["shell.mnuSys", "shell.mnuFile", "shell.mnuOpenGrid"], "open"), 0),
        ("덤프 재시도: 3번째에 잡으면 열림", (lambda c: (settings_nodes(c, 3), c.n))(FakeCtx([[], [], sys_ids])), (3, 3)),
        ("덤프 재시도: 끝내 못 잡으면 0, 3번까지만", (lambda c: (settings_nodes(c, 3), c.n))(FakeCtx([[]])), (0, 3)),
        ("유실 아님(열림)", classify_loss({"nodes": 24, "marks": none}, cm), None),
        ("유실 ① 앱 로그 없음", (classify_loss({"nodes": 0, "tap_ms": 500, "lines": [], "marks": none}, cm) or {}).get("cls"), "before_window"),
        ("유실 ① 앱 첫 로그 앞 탭", (classify_loss(lost, cm) or {}).get("cls"), "before_window"),
        ("유실 ③ 앱 로그 뒤 탭·표식 없음", (classify_loss({**lost, "tap_ms": 900}, cm) or {}).get("cls"), "other"),
        ("유실 ② 설계상 버림", (classify_loss({**lost, "marks": {**none, "replay_result": "dropped reason=stale"}}, cm) or {}).get("cls"), "design_discard"),
        ("유실 ③ sent 인데 안 열림", (classify_loss({**lost, "marks": buf}, cm) or {}).get("cls"), "other"),
        ("유실 ① 입력 싱크(앱 로그 뒤 탭)", (classify_loss({**lost, "tap_ms": 900, "input_lines": inj("W InputDispatcher: Not sending touch gesture to 51bf5a5 ActivityRecordInputSink com.pd.poc/x.MainActivity because it has config NO_INPUT_CHANNEL")}, cm) or {}).get("cls"), "before_window"),
        ("유실 ① 닿은 창 없음", (classify_loss({**lost, "tap_ms": 900, "input_lines": inj("I InputDispatcher: No new touched window at (646.0, 116.0) in display 0")}, cm) or {}).get("cls"), "before_window"),
        ("유실 ① 다른 pid 창", (classify_loss({**lost, "tap_ms": 900, "lines": pid_lines, "input_lines": inj("I InputDispatcher: Delivering touch to (32528): action: 0x0, f=0x0, d=0, 'Embedde', t=1")}, cm) or {}).get("cls"), "before_window"),
        ("유실 ③ 앱 pid 창에 전달됐는데 안 열림", (classify_loss({**lost, "tap_ms": 900, "lines": pid_lines, "input_lines": inj("I InputDispatcher: Delivering touch to (12715): action: 0x0, f=0x0, d=0, 'a5b9e03', t=1")}, cm) or {}).get("cls"), "other"),
        ("유실 ③ 앱 표식 있으면 입력 줄 무시", (classify_loss({**lost, "tap_ms": 900, "marks": buf, "input_lines": inj("I InputDispatcher: No new touched window at (1.0, 1.0) in display 0")}, cm) or {}).get("cls"), "other"),
        ("유실 ③ 입력 줄 없음은 기존 규칙", (classify_loss({**lost, "tap_ms": 900, "input_lines": []}, cm) or {}).get("cls"), "other"),
        ("앱 pid 추출", app_pids(pid_lines), {"12715"}),
        ("못 잡은 사유: 잡으면 None", ff_miss_reason(1600, [(600, 0), (1600, 9)], "1", 8), None),
        ("못 잡은 사유: 화면 없음", ff_miss_reason(None, [(600, 0)], None, 8), "display_inactive"),
        ("못 잡은 사유: 첫 표본부터 그려짐", ff_miss_reason(None, [(620, 9), (1150, 9)], "1", 8), "first_sample_late"),
        ("못 잡은 사유: 끝까지 빈 화면", ff_miss_reason(None, [(600, 0), (1100, 0)], "1", 8), "all_blank"),
        ("못 잡은 사유: 연속 2장 없음", ff_miss_reason(None, [(600, 0), (1100, 9), (1600, 0)], "1", 8), "no_streak"),
        ("Displayed 파싱 ms", displayed_ms("+523ms"), 523),
        ("Displayed 파싱 s+ms", displayed_ms("+1s234ms"), 1234),
        ("Displayed 파싱 불가", displayed_ms("abc"), None),
        ("보조: 첫 표본 이내 Displayed 인정", ff_aux_ms("first_sample_late", [(620, 9), (1150, 9)], "+523ms"), 523),
        ("보조: 첫 표본보다 늦은 Displayed 불인정", ff_aux_ms("first_sample_late", [(620, 9)], "+900ms"), None),
        ("보조: 다른 사유엔 미적용", ff_aux_ms("all_blank", [(600, 0)], "+100ms"), None),
        ("보조: Displayed 없음", ff_aux_ms("first_sample_late", [(620, 9)], None), None),
        ("전원: 깨어 있고 화면 있음", power_state("  mWakefulness=Awake", "4630946"), {"wakefulness": "Awake", "display_id": "4630946", "ok": True}),
        ("전원: Dozing 이면 무효", power_state("mWakefulness=Dozing", "4630946")["ok"], False),
        ("전원: 화면 없으면 무효", power_state("mWakefulness=Awake", None)["ok"], False),
        ("전원: 읽기 실패면 무효", power_state("", "1")["ok"], False),
        ("유실 요약", (lambda s: (s["lost"], s["before_window"], s["other"], s["classified_all"]))(loss_summary([{"loss": classify_loss(lost, cm)}, {"loss": classify_loss({**lost, "tap_ms": 900}, cm)}, {"loss": None}])), (2, 1, 1, True)),
        ("입력 줄 필터", [bool(INPUT_RX.search(s)) for s in ("W InputDispatcher: x", "chan dropped", "I DOTNET: y")], [True, True, False]),
    ]
    bad = 0
    for name, got, want in cases:
        ok = got == want
        bad += 0 if ok else 1
        print("%s %s got=%s want=%s" % ("PASS" if ok else "FAIL", name, got, want))
    return 1 if bad else 0


def main():
    if "--self-test" in sys.argv:
        return self_test()
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--mode", required=True, choices=("open", "closed"))
    p.add_argument("--stage", default="all", choices=("touch", "ff", "regress", "all"))
    p.add_argument("--runs", type=int, default=5)
    p.add_argument("--min-idle-s", type=int, default=300)
    p.add_argument("--wait-idle-s", type=int, default=0)
    p.add_argument("--baseline-ms", type=int)
    p.add_argument("--delays", help="기동 뒤 탭 지연 ms 쉼표 목록(기본 300,800,1500)")
    p.add_argument("--gate-delays", help="게이트 구간 탭 지연 ms 쉼표 목록(기본 펼침 900,950 / 접힘 1020,1070, 빈 문자열이면 생략)")
    p.add_argument("--gate-runs", type=int, default=10, help="지연별 최소 횟수")
    p.add_argument("--gate-buffered", type=int, default=20, help="모을 buffered 시행 수")
    p.add_argument("--gate-max-runs", type=int, default=80, help="게이트 구간 전체 시행 상한")
    p.add_argument("--gate-step", type=int, default=25)
    p.add_argument("--gate-span", type=int, default=200)
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--measure", action="store_true")
    a = p.parse_args()
    c = L.Ctx(a.mode, "Release", a.out)
    raw_path = os.path.join(a.out, "c5_%s.json" % a.mode)
    if os.path.exists(raw_path):  # 단계를 나눠 돌리면 앞 단계 값을 이어 쓴다
        with open(raw_path, encoding="utf-8") as f:
            c.raw.update(json.load(f).get("raw", {}))
    c.raw["apk"] = a.apk
    (L.set_closed if a.mode == "closed" else L.set_open)()
    if not L.install(a.apk, fresh=a.fresh):
        return 1
    global MODE
    MODE = a.mode
    act = activity()
    target = target_id(a.mode)
    state = "CLOSED" if a.mode == "closed" else "OPENED"
    cold(act)
    rect = None
    for _i in range(60):
        rect = L.U.control_rect(c, c.dump(), target)
        if rect:
            break
        time.sleep(1)
    if not rect:
        print("터치 대상 없음")
        return 1
    xml = c.dump("base")
    b = c.bounds(xml, target)
    tap_xy = (b[0], b[1] + c.bounds_top(xml, "android:id/content"))
    c.raw["target"] = {"id": target, "rect": rect, "tap": tap_xy, "state": L.dev_state(), "wm": L.wm_size()}
    shot0 = c.shot("base")
    if a.stage in ("touch", "all"):
        stage_touch(c, a, act, rect, tap_xy, state)
    if a.stage in ("touch", "all", "regress"):
        stage_regress(c, a, act, rect)
    if a.stage in ("ff", "all"):
        stage_ff(c, a, act, rect, state)
    n, ex = c.fatal_count()
    c.check("치명 0", n == 0, (n, ex), 0)
    raw = L.save_raw(c, "c5_%s" % a.mode)
    if a.measure:
        ff = c.raw.get("ff_runs", [])
        after = c.raw.get("touch_after_ff", [])
        during = c.raw.get("touch_during_splash", [])
        vals = sorted(r["ff_ms"] for r in ff if r["ff_ms"])
        median = vals[len(vals) // 2] if vals else None
        before = [r for r in during if r["before_ff"]]
        lost = [r for r in before if r["nodes"] < 3]
        ok_ff = median is not None and (median <= 2000 or bool(a.baseline_ms and median <= a.baseline_ms * 0.8))
        rows = {
            "first_frame_ms_fold5": ({"median": median, "values": vals, "mode": a.mode, "idle": [r["idle"] for r in ff]}, median is not None),
            "first_frame_improved_or_le_2000ms": ({"median": median, "baseline_ms": a.baseline_ms}, ok_ff),
            "touch_after_first_frame_ok": ([r["nodes"] for r in after], bool(after) and all(r["nodes"] >= 3 for r in after)),
            "touch_during_splash_not_lost": ({"before_ff": len(before), "lost": len(lost)}, bool(before) and not lost),
        }
        ls = c.raw.get("loss_summary") or loss_summary(before)
        rows["splash_loss_classified_all"] = (ls, bool(before) and ls["classified_all"])
        rows["loss_app_unexpected"] = ({"other": ls["other"], "rows": [{"i": r["i"], "delay": r["delay"], "loss": r["loss"]} for r in before if (r.get("loss") or {}).get("cls") == "other"]}, ls["other"] == 0)
        rows["loss_before_window_reported"] = ({"before_window": ls["before_window"], "design_discard": ls["design_discard"]}, True)  # 건수 보고용. 플랫폼 한계(§6-1)는 사용자 판단
        rows["touch_after_first_frame"] = ([r["nodes"] for r in after], bool(after) and all(r["nodes"] >= 3 for r in after))
        L.write_measure(a.out, "C5", rows, "retest_android_pcaway_splash.py " + raw, [shot0])
    return 0


if __name__ == "__main__":
    sys.exit(main())

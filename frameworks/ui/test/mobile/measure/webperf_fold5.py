#!/usr/bin/env python3
# Fold5 앱·웹(Chrome) 조작 속도 비교 측정 — 터치 표시 점과 화면 녹화로 입력→화면 반응 시간을 같은 방법으로 재 원자료 JSON 에 남긴다
"""
사용(가상환경 python, imageio-ffmpeg·numpy·pillow·websocket-client 필요):
  python webperf_fold5.py --target app|web --mode open|closed --runs 10 --out <폴더> --apk <제품 APK>
         [--url http://127.0.0.1:5180/] [--actions cold,warm,menu,doc,tab,scroll,drag] [--web-dy N] [--trace] [--trace-tab N]
  --trace-tab N: 웹 펼침에서 시행 뒤 문서 탭 전환을 타임라인+CPU 프로파일 trace 로 N회 모아 trace_tab_open_<k>.json 과 긴 작업 요약(하위 이벤트·상위 함수)을 남긴다.
방법:
  - 조작마다 screenrecord(화면 절반 크기)로 녹화하고 개발자 옵션 '포인터 위치'(pointer_location)로 입력 순간을 화면에 남긴다.
    ('터치 표시'는 adb input 주입 입력을 그리지 않고, sendevent 는 SELinux 로 막힌다 — 실측.)
    탭은 80ms 눌렀다 뗀다(input tap 은 Chrome 위에서 누름이 녹화되지 않았다). 입력 = 탭 지점의 십자선(세로·가로, 탭 주변 ±150px 제외)
    또는 누르는 동안 바뀌는 상단 정보줄이 처음 보인 프레임(누름 시작), 반응(resp) = 반응 영역(ROI, 십자선 줄 제외)이 처음 바뀐 프레임,
    안정(settle) = 반응 영역이 마지막 프레임과 같아진 뒤 다시 바뀌지 않는 첫 프레임. 앱·웹을 같은 계측으로 잰다. 시간 해상도는 녹화 프레임 간격(≈8ms)이다.
  - cold·warm 은 입력 점이 없으므로 시작 = 화면 어디든 처음 바뀐 프레임, 끝 = 상단 띠 ROI 가 최종 화면과 같아진 프레임이다.
    웹 cold·warm 은 Navigation/Resource Timing(nav)도 함께 남긴다.
  - 좌표는 앱(제품판) uiautomator 덤프에서 얻는다. 웹은 같은 배치라 보고 세로 이동량(dy = 화면 높이 − 내비게이션 막대 − innerHeight×dpr − 상태 표시줄)을
    더한다(--web-dy 로 고정 가능). 하단 탭은 화면 바닥에 붙어 dy 를 더하지 않는다.
  - 메뉴는 같은 버튼을 다시 눌러 닫는다(Back 키는 앱을 빠져나간다).
  - 시행 전 호스트 1분 평균 부하가 50 을 넘으면 40 미만이 2분 유지될 때까지 기다리고 run 에 load1·load_wait_s 를 남긴다.
  - 웹 cold 는 Chrome 원격 디버깅(CDP)으로 이 시험 탭만 캐시를 끄고(Network.setCacheDisabled) 시험 출처(127.0.0.1:5180) 저장소만 지운 뒤 연다.
    사용자 Chrome 캐시·데이터는 지우지 않는다. --trace 면 cold 로딩 trace 의 긴 작업(50ms 초과)·WASM 컴파일 이벤트를 요약한다.
  - 조작마다 그 시각 이후 logcat 의 [startup]·입력 관련 줄을 -d -T 로 읽어 행에 남긴다(logcat -c 를 쓰지 않는다).
  - 시행마다 <out>/webperf_<target>_<mode>.partial.json 에 누적 저장한다. 끝나면 pointer_location 원값·접힘 상태(reset)를 되돌리고 시험 탭을 닫는다.
  - logcat -c 를 쓰지 않는다. 앱은 시험 패키지(host_identity 의 android.package)만 pm clear 한다.
결과: <out>/webperf_<target>_<mode>.json(시행별 행·요약 중앙값/p90), 첫 시행 대표 프레임 PNG.
"""
import argparse
import json
import os
import re
import statistics
import subprocess
import sys
import time
import http.client
import urllib.request

import imageio_ffmpeg
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
import webperf_startup as ws  # 시작 구간·캐시 경유·탭 처음/재방문 확장(새 옵션 인자로만 켜진다)
import webperf_inject as wi  # --settings-json 주입·doc/open_* 조작별 trace(--trace)
import webperf_insets as wn  # dy 계산(보이는 navigationBars 만, --self-test 로 기기 없이 검사)

ADB = os.environ.get("ADB", "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe")
SER = os.environ.get("SERIAL", "R3CW70202FP")
PKG = _paths.get("android.package")
CHROME = "com.android.chrome"
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
CDP_PORT = 9229
SCALE = 2  # 녹화 축소 배율
DIFF_T = 6.0  # ROI 평균 절대차 문턱(회색 0~255)
FRAC_T = 0.02  # 바뀐 화소 비율 문턱(차 24 초과)
POST = {"cold": (6.0, 25.0), "warm": (4.0, 15.0)}  # (앱, 웹) 녹화 뒤 대기 초


def adb(*a, timeout=60):
    p = subprocess.run([ADB, "-s", SER] + list(a), capture_output=True, timeout=timeout)
    return p.stdout.decode("utf-8", "replace")


def sh(cmd, timeout=60):
    return adb("shell", cmd, timeout=timeout).strip()


def win(path):
    return subprocess.run(["wslpath", "-w", path], capture_output=True, text=True).stdout.strip()


def wake():
    if "Awake" not in sh("dumpsys power | grep mWakefulness"):
        sh("input keyevent 224")
        time.sleep(1)


def set_mode(mode):
    sh("cmd device_state state 0" if mode == "closed" else "cmd device_state state reset")
    time.sleep(3)
    wake()


ROT_KEYS = ("accelerometer_rotation", "user_rotation")


ORIENT_LOG = []  # 설정 단계 방향 확인 기록(main 이 doc["orientation_checks"] 로 남긴다)


def display_ids():
    """dumpsys window displays 에 나오는 논리 디스플레이 id 목록(Fold5 는 0=안쪽 화면, 1=바깥 화면)."""
    return [int(x) for x in re.findall(r"Display: mDisplayId=(\d+)", sh("dumpsys window displays"))] or [0]


def lock_portrait():
    """자동 회전을 끄고 세로(0)로 고정한다. 가로로 돌아가 있으면 녹화 크기·터치 좌표가 어긋나 측정이 무효가 된다.
    settings 값만으로는 Fold5 안쪽 화면이 가로(cur=2176x1812)로 남는 것이 실측됐다(a16_fold5.json) — 디스플레이마다
    공식 명령 `cmd window user-rotation -d N lock 0` 으로도 고정한다(원래 값은 main 이 읽어 finally 에서 되돌린다)."""
    sh("settings put system accelerometer_rotation 0")
    sh("settings put system user_rotation 0")
    for did in display_ids():
        sh("cmd window user-rotation -d %d lock 0" % did)
    time.sleep(1.5)


def active_display_id():
    """지금 켜진 물리 화면이 붙은 논리 디스플레이 id(dumpsys display 의 isActive=true). 못 읽으면 0."""
    m = re.search(r"isActive=true, displayId=(\d+), uniqueId='local:", sh("dumpsys display"))
    return int(m.group(1)) if m else 0


def parse_window_display(out, did):
    """dumpsys window displays 출력에서 디스플레이 did 절의 (init 크기, cur 크기, mRotation). 없으면 None.
    옛 판독은 첫 디스플레이(바깥 화면 1) 절의 'mRotation=ROTATION_90' 처럼 숫자가 아닌 줄만 grep -m3 로 잡아 실패하고,
    dumpsys display 의 'orientation=' (패널 설치 방향 등 고정값)으로 넘어가 실제 방향을 읽지 못했다."""
    parts = re.split(r"\n\s*Display: mDisplayId=", "\n" + out)
    for p in parts[1:]:
        m = re.match(r"(\d+)", p)
        if not m or int(m.group(1)) != did:
            continue
        init = re.search(r"\binit=(\d+)x(\d+)", p)
        cur = re.search(r"\bcur=(\d+)x(\d+)", p)
        rot = re.search(r"^\s*mRotation=(\d+)", p, re.M)  # DisplayRotation 절의 실제 회전(0·1·2·3)
        return (tuple(map(int, init.groups())) if init else None, tuple(map(int, cur.groups())) if cur else None,
                int(rot.group(1)) if rot else None)
    return None, None, None


def orientation():
    """실제 화면 방향. 판정은 켜진 디스플레이의 cur 크기(지금 그려지는 크기)가 init(자연 방향, 세로)과 같은 방향인지로 한다.
    portrait: True=세로, False=가로, None=판독 불가."""
    did = active_display_id()
    init, cur, rot = parse_window_display(sh("dumpsys window displays"), did)
    portrait = None if not (init and cur) else ((cur[0] > cur[1]) == (init[0] > init[1]))
    return {"display": did, "init": init, "cur": cur, "rotation": rot, "portrait": portrait}


def current_rotation():
    """실제 화면 회전(0=세로, 1·3=가로, 2=거꾸로 세로). 읽지 못하면 None."""
    return orientation()["rotation"]


def ensure_portrait(timeout=6.0):
    """방향 확인. 가로면 다시 고정한 뒤 0.5초 간격으로 timeout 초까지 재확인하고, 세로가 0.5초 뒤 한 번 더 확인돼야 일치로 본다.
    (일치 여부, 판독 정보) — 판독 불가(portrait=None)는 일치로 보지 않는다."""
    info = orientation()
    if info["portrait"]:
        return True, info
    lock_portrait()
    t0 = time.time()
    while time.time() - t0 < timeout:
        time.sleep(0.5)
        info = orientation()
        if info["portrait"]:
            time.sleep(0.5)
            info = orientation()
            if info["portrait"]:
                return True, info
    return False, info


def require_portrait(stage):
    """설정 단계(좌표 발견·layout 캡처·insets·web_dy 계산) 직전 방향 확인. 세로가 아니면 이후 좌표가 전부 무효라 측정을 멈춘다."""
    ok, info = ensure_portrait()
    ORIENT_LOG.append({"stage": stage, "ok": ok, **info})
    if not ok:
        raise RuntimeError("방향 불일치(%s): %s" % (stage, info))
    return info


def display_size():
    return tuple(map(int, re.findall(r"(\d+)x(\d+)", sh("wm size"))[-1]))


def screencap(path):
    disp = re.search(r"isActive=true, displayId=\d+, uniqueId='local:(\d+)", sh("dumpsys display"))
    data = subprocess.run([ADB, "-s", SER, "exec-out", "screencap", "-p"] + (["-d", disp.group(1)] if disp else []), capture_output=True, timeout=30).stdout
    with open(path, "wb") as f:
        f.write(data)
    return Image.open(path).convert("RGB")


def dump_nodes():
    sh("uiautomator dump /sdcard/wp.xml", timeout=60)
    xml = sh("cat /sdcard/wp.xml")
    sh("rm -f /sdcard/wp.xml")
    nodes = []
    for m in re.finditer(r"<node [^>]*>", xml):
        t = m.group(0)
        g = lambda k: (re.search(k + r'="([^"]*)"', t) or [None, ""])[1]  # noqa: E731
        b = re.search(r'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', t)
        if b:
            nodes.append({"id": g("resource-id"), "text": g("text"), "cls": g("class"), "b": tuple(int(x) for x in b.groups())})
    return nodes


def find(nodes, id_=None, text=None, cls=None):
    for n in nodes:
        if (id_ is None or n["id"] == id_) and (text is None or n["text"] == text) and (cls is None or n["cls"] == cls):
            return n["b"]
    return None


def center(b):
    return ((b[0] + b[2]) // 2, (b[1] + b[3]) // 2)


def tap(x, y):
    sh("input tap %d %d" % (x, y))


HOLD_MS = 80


def tap_hold(x, y):
    """측정용 탭: 80ms 눌렀다 뗀다. input tap 은 누름이 너무 짧아 Chrome 위에서 십자선이 한 프레임도 녹화되지 않았다(23:48 실측).
    반응 시간은 누름 시작 기준이며 앱·웹 같은 방식이다. 뗀 시각은 row 의 t_release·hold_ms 로 따로 남긴다."""
    sh("input swipe %d %d %d %d %d" % (x, y, x, y, HOLD_MS))


def swipe(x0, y0, x1, y1, ms):
    sh("input swipe %d %d %d %d %d" % (x0, y0, x1, y1, ms))


# ---------- 녹화·해석 ----------
class Rec:
    def __init__(self, size):
        self.w, self.h = size[0] // SCALE // 2 * 2, size[1] // SCALE // 2 * 2
        self.p = None

    def start(self):
        sh("rm -f /sdcard/wp.mp4")
        self.p = subprocess.Popen([ADB, "-s", SER, "shell", "screenrecord", "--size", "%dx%d" % (self.w, self.h), "--bit-rate", "12000000", "--time-limit", "40", "/sdcard/wp.mp4"],
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        t0 = time.time()
        while time.time() - t0 < 4 and sh("stat -c %s /sdcard/wp.mp4 2>/dev/null").strip() in ("", "0"):
            time.sleep(0.1)
        time.sleep(1.2)  # 파일 머리가 생긴 뒤에도 인코더가 첫 프레임을 내기까지 여유를 둔다

    def stop(self, local):
        sh("pkill -INT screenrecord")
        try:
            self.p.wait(timeout=15)
        except subprocess.TimeoutExpired:
            self.p.kill()
        time.sleep(0.8)
        if os.path.exists(local):
            os.remove(local)
        adb("pull", "/sdcard/wp.mp4", win(local), timeout=120)
        sh("rm -f /sdcard/wp.mp4")
        return os.path.exists(local) and os.path.getsize(local) > 1000


def decode(mp4, w, h):
    p = subprocess.run([FFMPEG, "-v", "info", "-i", mp4, "-vf", "format=gray,showinfo", "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "gray", "-"], capture_output=True, timeout=300)
    pts = [float(x) for x in re.findall(r"pts_time:([0-9.]+)", p.stderr.decode("utf-8", "replace"))]
    raw = np.frombuffer(p.stdout, dtype=np.uint8)
    n = len(raw) // (w * h)
    return pts[:n], raw[: n * w * h].reshape(n, h, w)


INFO_SB = 80  # 상태 표시줄 높이(px) — main 이 dumpsys window 의 statusBars 로 다시 정한다
CROSS_MASK = None  # (x, y) 축소 좌표 — pointer_location 십자선이 지나는 줄·열을 반응 판정에서 뺀다


def roi_diff(frames, ref, box):
    x0, y0, x1, y1 = box
    d = np.abs(frames[:, y0:y1, x0:x1].astype(np.int16) - ref[y0:y1, x0:x1].astype(np.int16))
    if CROSS_MASK is not None:
        cx, cy = CROSS_MASK
        keep = np.ones(d.shape[1:], dtype=bool)
        keep[max(0, cy - 3 - y0):max(0, cy + 4 - y0), :] = False  # 십자선은 1~2px 이다 — 넓게 빼면 탭 머리 글자(선택 표시)가 다 가려진다
        keep[:, max(0, cx - 3 - x0):max(0, cx + 4 - x0)] = False
        if keep.any():
            d = d[:, keep]
            return d.mean(axis=1), (d > 24).mean(axis=1)
    return d.mean(axis=(1, 2)), (d > 24).mean(axis=(1, 2))


def first_change(frames, ref, box, start=0):
    mean, frac = roi_diff(frames, ref, box)
    for i in range(start, len(frames)):
        if mean[i] > DIFF_T or frac[i] > FRAC_T:
            return i
    return None


def settle(frames, box, start):
    mean, frac = roi_diff(frames, frames[-1], box)
    ok = (mean <= DIFF_T) & (frac <= FRAC_T)
    for i in range(start, len(frames)):
        if ok[i:].all():
            return i
    return None


def crosshair(frames, ref, xy, w, h):
    """pointer_location 이 누르는 동안 그리는 세로선: 탭 x 의 좁은 띠에서 탭 주변을 뺀 위·아래 구간이 처음 바뀐 프레임."""
    x, y = xy
    boxes = [sbox((x - 6, 60, x + 6, y - 150), w, h), sbox((x - 6, y + 150, x + 6, h * SCALE), w, h),
             sbox((0, y - 6, x - 150, y + 6), w, h), sbox((x + 150, y - 6, w * SCALE, y + 6), w, h)]  # 세로선 + 가로선(상단 탭 대비)
    boxes = [b for b in boxes if max(b[3] - b[1], b[2] - b[0]) > 20 and b[3] > b[1] and b[2] > b[0]]
    # 상단 정보줄은 탭 뒤 값이 0 으로 돌아가 남지 않았다(23:48 실측) — 그래서 탭은 tap_hold 로 눌러 십자선을 확실히 남긴다
    # 누르는 동안에는 정보줄(펼침 y≈70~76) 값이 바뀌고 떼면 되돌아간다 — 절반 축소로 흐려진 십자선보다 확실하다(23:52 실측: 정보줄 700px 변화, 십자선 열은 112/1088 행만)
    # 정보줄 위치(Fold5 실측): 펼침 y 58~86(상태 표시줄 80 과 겹침), 접힘 y 85~113(상태 표시줄 85 바로 아래, 00:00 실측)
    bar = (58, 86) if INFO_SB <= 80 else (INFO_SB, INFO_SB + 28)
    boxes = [(b, 0.08) for b in boxes] + [(sbox((0, bar[0], w * SCALE, bar[1]), w, h), 0.02)]

    def on(i):
        for b, t in boxes:
            d = np.abs(frames[i, b[1]:b[3], b[0]:b[2]].astype(np.int16) - ref[b[1]:b[3], b[0]:b[2]].astype(np.int16))
            if (d > 24).mean() > t:
                return True
        return False
    for i in range(len(frames)):
        if on(i):
            rel = next((j for j in range(i + 1, len(frames)) if not on(j)), None)
            return i, rel
    return None, None


def sbox(box, w, h):
    x0, y0, x1, y1 = (int(v / SCALE) for v in box)
    return (max(0, x0), max(0, y0), min(w, x1), min(h, y1))


LOG_RX = re.compile(r"\[startup\]|InputDispatcher.*(drop|pd\.poc|chrome)|Choreographer.*Skipped", re.I)


def log_lines(since):
    out = adb("logcat", "-d", "-v", "threadtime", "-T", since, timeout=60)
    return [ln.strip()[:220] for ln in out.splitlines() if LOG_RX.search(ln)][-40:]


KEEP_DIFF = False  # --keep-diff: 첫 반응~settle 사이 늦게 바뀐 행 구간을 시행마다 남긴다(판정·표본에는 영향 없음)


def late_rows(frames, pts, resp, ir, ist):
    """첫 반응(ir) 프레임 이후에도 최종(settle 이후) 화면과 달랐던 행을 구간으로 묶는다.
    행 = 반응 영역(ROI) 안의 가로줄, '다르다' = 그 줄의 평균 절대차 > DIFF_T. 구간마다 마지막으로 달랐던 프레임 시각·면적(축소 화소)을 기록한다."""
    x0, y0, x1, y1 = resp
    final = frames[min(ist + 1, len(frames) - 1)] if ist + 1 < len(frames) else frames[-1]
    n = y1 - y0
    last = np.full(n, -1)  # 행별 마지막으로 달랐던 프레임
    per_frame = []
    for i in range(ir, ist + 1):
        d = np.abs(frames[i, y0:y1, x0:x1].astype(np.int16) - final[y0:y1, x0:x1].astype(np.int16)).mean(axis=1) > DIFF_T
        last[d] = i
        per_frame.append({"i": i, "t_ms": round((pts[i] - pts[ir]) * 1000), "rows": int(d.sum())})
    spans, s = [], None
    for y in range(n + 1):
        late = y < n and last[y] > ir
        if late and s is None:
            s = y
        if not late and s is not None:
            seg = last[s:y]
            spans.append({"y0": y0 + s, "y1": y0 + y, "rows": y - s, "area_px": (y - s) * (x1 - x0),
                          "last_i": int(seg.max()), "last_t_ms": round((pts[int(seg.max())] - pts[ir]) * 1000)})
            s = None
    return {"roi_rows": n, "late_rows": int((last > ir).sum()), "spans": spans[:20], "frames": per_frame[:60]}


def phase_trace(frames, pts, ref, resp, it, ir, ist):
    """기록 전용(판정 무관): 터치~settle+2 프레임마다 반응 영역이 처음 화면(ref)·최종 화면과 얼마나 다른지(변화 화소 비율)와,
    처음과도 최종과도 다른 '중간 화면'(빈 칸·스피너·덜 그린 표)이 처음 보인 시각을 남긴다. 구간 원인(첫 반응·표 실현) 분리용(K15)."""
    end = min(len(frames) - 1, (ist if ist is not None else ir) + 2)
    final = frames[end]
    _, fr = roi_diff(frames, ref, resp)
    _, ff = roi_diff(frames, final, resp)
    rows, t_mid = [], None
    for i in range(it, end + 1):
        t = round((pts[i] - pts[it]) * 1000)
        rows.append([t, round(float(fr[i]), 3), round(float(ff[i]), 3)])
        if t_mid is None and i >= ir and fr[i] > FRAC_T and ff[i] > FRAC_T:
            t_mid = t
    return {"mid_ms": t_mid, "frames": rows[:80], "cols": ["t_ms", "diff_from_start", "diff_from_final"]}


# 기록 전용(판정 무관, 웹): 탭 직전부터 메인 스레드 긴 작업(longtask)과 50ms 넘는 rAF 간격을 모은다. 시각은 주입 시점 기준 ms(K15)
MAIN_BEGIN_JS = ("(()=>{const s={t0:performance.now(),lt:[],gaps:[]};window.__pdMain=s;try{s.obs=new PerformanceObserver(l=>{for(const e of l.getEntries())"
                 "s.lt.push([Math.round(e.startTime-s.t0),Math.round(e.duration)])});s.obs.observe({type:'longtask'})}catch(e){s.lt=null}"
                 "let p=performance.now();const f=()=>{const t=performance.now();if(t-p>50)s.gaps.push([Math.round(p-s.t0),Math.round(t-p)]);p=t;"
                 "if(t-s.t0<6000)requestAnimationFrame(f)};requestAnimationFrame(f);return 1})()")
MAIN_END_JS = ("(()=>{const s=window.__pdMain;if(!s)return null;try{s.obs&&s.obs.disconnect()}catch(e){}"
               "return JSON.stringify({longtask:s.lt,raf_gaps:s.gaps,since_ms:Math.round(performance.now()-s.t0)})})()")


def swipe_like(name):
    return name in ("scroll", "drag")  # 손가락이 움직이는 조작은 반응 영역을 손가락 경로에서 떨어뜨려 잡았다


def measure(rec, tmp, name, act, roi, touch_xy, post, keep=None):
    mp4 = os.path.join(tmp, "wp.mp4")
    since = sh("date '+%m-%d %H:%M:%S.000'")
    rec.start()
    t_host = time.time()
    act()
    time.sleep(post)
    if not rec.stop(mp4):
        return {"name": name, "error": "녹화 실패"}
    lines = log_lines(since)
    w, h = rec.w, rec.h
    pts, frames = decode(mp4, w, h)
    if len(frames) < 3:
        return {"name": name, "error": "프레임 부족 %d" % len(frames)}
    ref = frames[0]
    resp = sbox(roi, w, h)
    row = {"name": name, "frames": len(frames), "roi": list(resp), "host_t": round(t_host, 3), "log": lines}
    if touch_xy:
        it, irel = crosshair(frames, ref, touch_xy, w, h)
        if irel is not None:
            row.update(t_release=pts[irel], hold_ms=round((pts[irel] - pts[it]) * 1000) if it is not None else None)
        if it is None:
            row["error"] = "터치 표시 없음"
            return row
    else:
        it = first_change(frames, ref, (0, 0, w, h))
        if it is None:
            row["error"] = "화면 변화 없음"
            return row
    global CROSS_MASK  # noqa: PLW0603
    CROSS_MASK = (int(touch_xy[0] / SCALE), int(touch_xy[1] / SCALE)) if touch_xy and not swipe_like(name) else None
    ir = first_change(frames, ref, resp, it)
    row.update(t_touch=pts[it], i_touch=it)
    if ir is None:
        row["error"] = "반응 없음"
        return row
    ist = settle(frames, resp, ir)
    row.update(t_resp=pts[ir], resp_ms=round((pts[ir] - pts[it]) * 1000),
               t_settle=pts[ist] if ist is not None else None, settle_ms=round((pts[ist] - pts[it]) * 1000) if ist is not None else None)
    if KEEP_DIFF and ist is not None:
        row["keep_diff"] = late_rows(frames, pts, resp, ir, ist)
    try:
        row["phase"] = phase_trace(frames, pts, ref, resp, it, ir, ist)  # 기록 전용 — 판정·표본 계산은 위 값 그대로
    except Exception as e:  # noqa: BLE001
        row["phase"] = {"error": str(e)[:120]}
    x0, y0, x1, y1 = resp
    moving = [i for i in range(max(ir, 1), len(frames)) if np.abs(frames[i, y0:y1, x0:x1].astype(np.int16) - frames[i - 1, y0:y1, x0:x1].astype(np.int16)).mean() > 1.0]
    if len(moving) >= 2:
        span = pts[moving[-1]] - pts[moving[0]]
        row.update(moving_frames=len(moving), moving_span_ms=round(span * 1000), moving_fps=round((len(moving) - 1) / span, 1) if span > 0 else None)
    if keep:
        Image.fromarray(frames[ist if ist is not None else -1]).save(keep)
    os.remove(mp4)
    return row


# ---------- CDP(웹) ----------
class CDP:
    def __init__(self):
        import websocket  # noqa: PLC0415
        adb("forward", "tcp:%d" % CDP_PORT, "localabstract:chrome_devtools_remote")
        self.websocket = websocket
        self.ws = None
        self.mid = 0
        self.tab = None

    def http(self, path, method="GET"):
        # WSL(NAT) 중계(cdp_tcp_relay.py)는 Windows 쪽이 미리 열어 둔 대기 연결을 쓴다 — 오래 놀던 대기 연결이 끊겨 있으면 첫 요청이 응답 없이 닫힌다(RemoteDisconnected).
        # 끊긴 연결은 요청 한 번에 소진되고 중계가 새 대기 연결을 만들므로 몇 번 다시 시도한다.
        for attempt in range(6):
            req = urllib.request.Request("http://127.0.0.1:%d%s" % (CDP_PORT, path), method=method)
            try:
                with urllib.request.urlopen(req, timeout=15) as r:
                    body = r.read().decode()
                    return json.loads(body) if body.strip().startswith(("{", "[")) else body
            except (http.client.RemoteDisconnected, ConnectionResetError, urllib.error.URLError) as e:
                if isinstance(e, urllib.error.URLError) and not isinstance(e.reason, (ConnectionResetError, ConnectionRefusedError)):
                    raise
                if attempt == 5:
                    raise
                time.sleep(1)

    def new_tab(self, url):
        try:
            self.tab = self.http("/json/new?" + url, method="PUT")
        except (urllib.error.HTTPError, http.client.RemoteDisconnected, ConnectionError):  # 새 Chrome 은 /json/new 에 응답 없이 연결을 닫기도 한다(RemoteDisconnected, 2026-11-13 A16 실측)
            # Android Chrome 156 은 /json/new 를 거부한다("Could not create new page", 23:4x 실측).
            # am start VIEW 로 연 시험 탭(시험 URL 로 시작하는 page, 목록 맨 앞 = 최근)에 붙는다. 다른 탭은 건드리지 않는다.
            base = url.split("?")[0]
            pages = [t for t in self.http("/json/list") if t.get("type") == "page" and t.get("url", "").startswith(base)]
            if not pages:
                raise
            self.tab = pages[0]
            # 같은 출처 탭이 둘 이상이면 앱의 단일 인스턴스 가드("PD 가 다른 탭에서 열려 있습니다")에 막힌다(23:37 실측) — 시험 URL 탭만 정리한다
            for t in pages[1:]:
                self.http("/json/close/" + t["id"])
            time.sleep(1)
            self.ws = self.websocket.create_connection(self.tab["webSocketDebuggerUrl"], timeout=120, suppress_origin=True)
            self.call("Page.navigate", url=url)
            return self.tab
        self.ws = self.websocket.create_connection(self.tab["webSocketDebuggerUrl"], timeout=120, suppress_origin=True)
        return self.tab

    def call(self, method, **params):
        self.mid += 1
        self.ws.send(json.dumps({"id": self.mid, "method": method, "params": params}))
        while True:
            m = json.loads(self.ws.recv())
            if m.get("id") == self.mid:
                return m.get("result", m.get("error"))

    def eval(self, expr):
        r = self.call("Runtime.evaluate", expression=expr, returnByValue=True, awaitPromise=True)
        return (r or {}).get("result", {}).get("value")

    def trace(self, fn, cats):
        self.call("Tracing.start", categories=cats, transferMode="ReportEvents")
        fn()
        self.mid += 1
        self.ws.send(json.dumps({"id": self.mid, "method": "Tracing.end", "params": {}}))
        events = []
        while True:
            m = json.loads(self.ws.recv())
            if m.get("method") == "Tracing.dataCollected":
                events += m["params"]["value"]
            if m.get("method") == "Tracing.tracingComplete":
                return events

    def close(self):
        try:
            if self.ws:
                self.ws.close()
            if self.tab:
                self.http("/json/close/" + self.tab["id"])
        except Exception:  # noqa: BLE001 — 닫기 실패는 기록만
            pass


NAV_JS = """(() => { const n = performance.getEntriesByType('navigation')[0] || {};
 const r = performance.getEntriesByType('resource').map(e => ({name: e.name.split('/').pop().split('?')[0], dur: Math.round(e.duration), size: e.transferSize, enc: e.encodedBodySize}));
 const big = r.filter(e => /\\.(wasm|dll|dat|js)$/.test(e.name)).sort((a,b)=>b.dur-a.dur).slice(0,12);
 return {domContentLoaded: Math.round(n.domContentLoadedEventEnd||0), load: Math.round(n.loadEventEnd||0), transfer: r.reduce((s,e)=>s+(e.size||0),0), resources: r.length, top: big}; })()"""


def trace_summary(events):
    # 긴 작업은 시험 페이지 렌더러의 메인 스레드(CrRendererMain)만 센다 — 다른 탭·GPU·브라우저 스레드를 섞지 않는다.
    # 렌더러가 여럿이면 wasm/v8 이벤트가 가장 많은 프로세스를 시험 페이지로 본다.
    mains = {(e["pid"], e["tid"]) for e in events if e.get("ph") == "M" and e.get("name") == "thread_name" and (e.get("args") or {}).get("name") == "CrRendererMain"}
    score = {}
    for e in events:
        if (e.get("pid"), e.get("tid")) in mains and ("wasm" in e.get("name", "").lower() or e.get("cat", "").startswith("v8")):
            score[(e["pid"], e["tid"])] = score.get((e["pid"], e["tid"]), 0) + 1
    page = max(score, key=score.get) if score else None
    main = [e for e in events if e.get("ph") == "X" and e.get("name") in ("RunTask", "ThreadControllerImpl::RunTask")
            and (page is None or (e.get("pid"), e.get("tid")) == page)]
    long_tasks = sorted([e["dur"] / 1000 for e in main if e.get("dur", 0) > 50000], reverse=True)
    wasm = {}
    for e in events:
        n = e.get("name", "")
        if e.get("ph") == "X" and ("wasm" in n.lower() or "Compile" in n):
            s = wasm.setdefault(n, [0, 0.0, 0.0])
            s[0] += 1
            s[1] += e.get("dur", 0) / 1000
            s[2] = max(s[2], e.get("dur", 0) / 1000)
    top = sorted(wasm.items(), key=lambda kv: -kv[1][1])[:15]
    return {"long_tasks_n": len(long_tasks), "long_tasks_total_ms": round(sum(long_tasks)), "long_tasks_top_ms": [round(x) for x in long_tasks[:15]],
            "compile_events": {k: {"n": v[0], "total_ms": round(v[1]), "max_ms": round(v[2])} for k, v in top}}


def tab_trace_summary(events, top_n=12):
    """탭 전환 구간 trace(타임라인 + CPU 프로파일) 요약 — 시험 페이지 메인 스레드의 긴 작업마다 하위 이벤트 이름별 합과
    그 작업 시간 안의 CPU 프로파일 표본(자기 시간) 상위 함수를 남긴다."""
    mains = {(e["pid"], e["tid"]) for e in events if e.get("ph") == "M" and e.get("name") == "thread_name" and (e.get("args") or {}).get("name") == "CrRendererMain"}
    score = {}
    for e in events:
        k = (e.get("pid"), e.get("tid"))
        if k in mains and e.get("ph") == "X":
            score[k] = score.get(k, 0) + e.get("dur", 0)
    page = max(score, key=score.get) if score else None
    xs = [e for e in events if e.get("ph") == "X" and (e.get("pid"), e.get("tid")) == page]
    tasks = sorted([e for e in xs if e.get("name") in ("RunTask", "ThreadControllerImpl::RunTask") and e.get("dur", 0) > 50000], key=lambda e: -e["dur"])
    # CPU 프로파일(ProfileChunk) — 같은 pid 의 표본을 (시각, 노드) 로 편다
    nodes, samples, t = {}, [], None
    for e in sorted([e for e in events if e.get("name") in ("Profile", "ProfileChunk") and e.get("pid") == (page or (None,))[0]], key=lambda e: e.get("ts", 0)):
        d = (e.get("args") or {}).get("data") or {}
        if e["name"] == "Profile":
            t = d.get("startTime", t)
            continue
        cp = d.get("cpuProfile") or {}
        for n in cp.get("nodes") or []:
            cf = n.get("callFrame") or {}
            nodes[n["id"]] = "%s %s" % (cf.get("functionName") or "(anonymous)", (cf.get("url") or "").split("/")[-1])
        for sid, dt in zip(cp.get("samples") or [], d.get("timeDeltas") or []):
            t = (t or 0) + dt
            samples.append((t, sid))
    out = []
    for task in tasks[:5]:
        a0, a1 = task["ts"], task["ts"] + task["dur"]
        kids = {}
        for e in xs:
            if e is not task and a0 <= e.get("ts", 0) < a1 and e.get("name") not in ("RunTask", "ThreadControllerImpl::RunTask"):
                kids[e["name"]] = kids.get(e["name"], 0) + e.get("dur", 0) / 1000
        prof = {}
        for st, sid in samples:
            if a0 <= st < a1:
                nm = nodes.get(sid, str(sid))
                prof[nm] = prof.get(nm, 0) + 1
        ns = sum(prof.values())
        out.append({"dur_ms": round(task["dur"] / 1000), "ts": task["ts"],
                    "children_ms": {k: round(v) for k, v in sorted(kids.items(), key=lambda kv: -kv[1])[:top_n]},
                    "profile_samples": ns,
                    "profile_top": [{"fn": k, "samples": v, "pct": round(100 * v / ns, 1)} for k, v in sorted(prof.items(), key=lambda kv: -kv[1])[:top_n]] if ns else []})
    return {"page_thread": list(page) if page else None, "long_tasks_n": len(tasks), "long_tasks_ms": [round(e["dur"] / 1000) for e in tasks[:15]],
            "profile_samples_total": len(samples), "tasks": out}


# ---------- 좌표 ----------
def band_dy(app_img, web_img, y0, y1):
    """앱 띠(y0~y1)를 웹 화면에서 세로로 옮겨 가며 가장 잘 맞는 이동량."""
    a = np.asarray(app_img.convert("L"), dtype=np.int16)
    b = np.asarray(web_img.convert("L"), dtype=np.int16)
    band = a[y0:y1:2, ::4]
    best = None
    for s in range(0, 500, 2):
        if y1 + s > b.shape[0]:
            break
        d = np.abs(b[y0 + s:y1 + s:2, ::4] - band).mean()
        if best is None or d < best[1]:
            best = (s, d)
    return best


def discover(a, w, h):
    """제품 앱에서 조작 좌표를 얻는다. 펼침=메뉴·문서 탭·독립 창, 접힘=서랍·하단 탭."""
    ids = {}
    sh("am force-stop %s" % PKG)
    sh("pm clear %s" % PKG)
    sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % PKG)
    time.sleep(7)
    require_portrait("discover_launch")  # 앱 기동이 방향을 바꿀 수 있다 — 노드 좌표를 읽기 전에 세로를 확인한다
    n = dump_nodes()
    opener = "shell.btnDrawer" if a.mode == "closed" else "shell.mnuFile"
    item = "shell.navDrawer.OpenGrid" if a.mode == "closed" else "shell.mnuOpenGrid"
    ids["opener"] = find(n, opener)
    if a.mode == "closed":
        tabs = [x for x in n if x["id"].startswith("shell.navTab.")]
        ids["navtabs"] = {x["id"]: x["b"] for x in tabs}
    else:
        ids["sdi_grid"] = find(n, text="그리드 독립 창")
    for _ in range(3):  # 기동 직후 탭이 게이트에 먹히면 메뉴가 안 열린다(23:4x 실측) — 열릴 때까지 다시 연다
        tap(*center(ids["opener"]))
        time.sleep(1.8)
        nd = dump_nodes()
        ids["item"] = find(nd, item)
        for kd in ("form", "gantt"):  # R2: 웹 open_<kind> 는 Chrome 덤프에 id 가 없어 앱 덤프 좌표를 쓴다
            ids["item_" + kd] = find(nd, item.replace("OpenGrid", "Open" + kd.capitalize()))
        ids["item_grid"] = ids["item"]
        tap(*center(ids["opener"]))  # 닫기: Back 키는 앱을 빠져나간다
        time.sleep(1.5)
        if ids["item"]:
            break
    if not ids["item"]:
        raise RuntimeError("메뉴 항목 좌표 없음: %s" % item)
    for _ in range(2):
        tap(*center(ids["opener"]))
        time.sleep(1.8)
        tap(*center(ids["item"]))
        time.sleep(3)
    n = dump_nodes()
    ids["tab1"] = find(n, "Grid-1", cls="DocumentTabStripItem")
    ids["tab2"] = find(n, "Grid-2", cls="DocumentTabStripItem")
    ids["documents"] = find(n, "Documents")
    if a.mode == "open" and ids.get("sdi_grid"):
        tap(*center(ids["sdi_grid"]))
        time.sleep(3)
        n = dump_nodes()
        win_b = find(n, "IDockWindow")
        ids["sdi_window"] = win_b
        inside = [x["b"] for x in n if x["id"] in ("TitleArea", "PART_Title") and win_b and x["b"][0] >= win_b[0] and x["b"][1] >= win_b[1] and x["b"][3] <= win_b[1] + 200]
        ids["sdi_title"] = max(inside, key=lambda b: b[2] - b[0]) if inside else ((win_b[0], win_b[1], win_b[2], win_b[1] + 60) if win_b else None)
    require_portrait("layout_app")
    img = screencap(os.path.join(a.out, "layout_app_%s.png" % a.mode))
    return ids, img


# ---------- 조작 ----------
class Ctx:
    def __init__(self, a, w, h, ids, dy, rec, tmp, cdp):
        self.a, self.w, self.h, self.ids, self.dy, self.rec, self.tmp, self.cdp = a, w, h, ids, dy, rec, tmp, cdp
        self.web = a.target == "web"
        self.nav_h = wn.nav_bar_h(sh("dumpsys window | grep -m2 'type=navigationBars frame'"), 60)

    def pt(self, b):
        x, y = center(b)
        return x, y + self.dy

    def box(self, b, pad=0):
        return (b[0] - pad, b[1] + self.dy - pad, b[2] + pad, b[3] + self.dy + pad)

    def top_band(self):
        return (0, 80 + self.dy, self.w, 190 + self.dy)

    def doc_area(self):
        d = self.ids.get("documents")
        if d:
            # 펼침은 메뉴 팝업(x≈122~657, 23:17 실측)이 닫히는 변화를 빼려고 팝업 오른쪽만 본다
            x0 = max(d[0] + 40, 720) if self.a.mode == "open" else d[0] + 40
            x1 = d[2] - 40 if self.a.mode == "open" else (d[0] + d[2]) // 2
            return (x0, d[1] + 120 + self.dy, x1, min(d[3], d[1] + 700) + self.dy)
        return (int(self.w * 0.1), int(self.h * 0.3) + self.dy, int(self.w * 0.5), int(self.h * 0.6) + self.dy)

    def post(self, act):
        return POST[act][1 if self.web else 0]

    def fresh(self):
        """시행 시작 상태(문서 0, 새 배치)를 만든다."""
        if self.web:
            self.cdp.call("Storage.clearDataForOrigin", origin=self.a.url.rstrip("/"), storageTypes="local_storage,indexeddb,cache_storage,service_workers")
            wi.apply(self.cdp, self.a)  # --settings-json 이 있을 때만
            self.cdp.call("Page.navigate", url=self.a.url + "?fresh")
            time.sleep(20)
            wi.verify(self)  # --settings-json 이 있을 때만 settings.get 응답을 기록
        else:
            sh("am force-stop %s" % PKG)
            sh("pm clear %s" % PKG)
            sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % PKG)
            time.sleep(7)

    def open_doc(self):
        fg_guard(self)
        tap(*self.pt(self.ids["opener"]))
        time.sleep(2.5 if self.web else 1.8)  # 웹은 메뉴 열림이 앱보다 느리다(257ms 대 41ms, 23:42 실측)
        fg_guard(self)
        tap(*self.pt(self.ids["item"]))
        time.sleep(5 if self.web else 3)


def fg_guard(c):
    """터치 전에 앞 화면이 시험 대상(앱=host_identity 의 android.package, 웹=Chrome)인지 확인한다. 아니면 사용자 앱을 건드리지 않게 즉시 멈춘다.
    (00:46 실측: 하단 제스처 영역의 스와이프로 홈이 열린 뒤 다음 스와이프가 사용자 런처에 들어갔다.)"""
    top = sh("dumpsys activity activities | grep -m1 topResumedActivity")
    want = "com.android.chrome" if c.web else PKG
    if want not in top:
        raise SystemExit("FG_GUARD: 앞 화면이 시험 대상이 아니다 — 측정 중단: %s" % top.strip()[:160])


def safe_y(c, y):
    """스와이프 좌표를 하단 제스처 영역(내비게이션 막대) 위로 제한한다(여유 150px)."""
    return min(y, c.h - c.nav_h - 150)


def do_action(c, act, keep):
    a = c.a
    if act not in ("cold", "warm"):
        fg_guard(c)
    if act == "cold":
        if c.web:
            c.cdp.call("Storage.clearDataForOrigin", origin=a.url.rstrip("/"), storageTypes="local_storage,indexeddb,cache_storage,service_workers")
            c.cdp.call("Network.setCacheDisabled", cacheDisabled=True)
            ws.prepare(c.cdp, a)  # 새 옵션이 있을 때만 시작 옵션·TabHeaderFirst 를 쓴다
            wi.apply(c.cdp, a)  # --settings-json 이 있을 때만
            row = measure(c.rec, c.tmp, act, lambda: c.cdp.call("Page.navigate", url=a.url + "?fresh&c=%d" % time.time()), c.top_band(), None, c.post(act), keep)
            row["nav"] = c.cdp.eval(NAV_JS)
            if a.collect_startup:
                row["startup"] = ws.collect(c.cdp)
            c.cdp.call("Network.setCacheDisabled", cacheDisabled=False)
        else:
            sh("am force-stop %s" % PKG)
            sh("pm clear %s" % PKG)
            time.sleep(1)
            row = measure(c.rec, c.tmp, act, lambda: sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % PKG), c.top_band(), None, c.post(act), keep)
        return row
    if act == "warm":
        if c.web:
            row = measure(c.rec, c.tmp, act, lambda: c.cdp.call("Page.reload"), c.top_band(), None, c.post(act), keep)
            row["nav"] = c.cdp.eval(NAV_JS)
            if a.collect_startup:
                row["startup"] = ws.collect(c.cdp)
        else:
            sh("input keyevent 3")
            time.sleep(2)
            row = measure(c.rec, c.tmp, act, lambda: sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % PKG), c.top_band(), None, c.post(act), keep)
        return row
    if act == "menu":
        x, y = c.pt(c.ids["opener"])
        b = c.box(c.ids["opener"])
        roi = (0, b[3] + 10, c.w // 2, min(c.h, b[3] + 700)) if a.mode == "open" else (0, b[3] + 10, int(c.w * 0.7), int(c.h * 0.6))
        for attempt in range(2):
            row = measure(c.rec, c.tmp, act, lambda: tap_hold(x, y), roi, (x, y), 2.5, keep)
            tap(x, y)  # 같은 버튼을 다시 눌러 닫는다(Back 키는 앱을 빠져나가 다음 항목을 망친다 — 23:17 실측)
            time.sleep(1.5)
            # 녹화가 늦게 시작돼 탭·메뉴가 녹화 전에 끝난 경우(프레임 3장, 23:3x 실측)만 1회 다시 잰다
            if not (row.get("error") == "터치 표시 없음" and row.get("frames", 99) < 6):
                break
            row["retried"] = attempt + 1
        return row
    if act == "doc":
        tap(*c.pt(c.ids["opener"]))
        time.sleep(1.8)
        fg_guard(c)
        wi.pre_open(c, act)  # --prewarm-probe 가 있을 때만
        x, y = c.pt(c.ids["item"])
        tr = wi.trace_begin(c, act)  # 웹 + --trace 일 때만
        if c.web:
            c.cdp.eval(MAIN_BEGIN_JS)  # 기록 전용: 메인 스레드 긴 작업·rAF 간격
        row = wi.trace_end(c, act, measure(c.rec, c.tmp, act, lambda: tap_hold(x, y), c.doc_area(), (x, y), 3.0, keep), tr)
        if c.web:
            try:
                row["main_thread"] = json.loads(c.cdp.eval(MAIN_END_JS) or "null")
            except Exception as e:  # noqa: BLE001
                row["main_thread"] = {"error": str(e)[:120]}
        time.sleep(1)
        c.open_doc()  # 탭 전환용 두 번째 문서(측정 안 함)
        return row
    if act == "tab":
        if a.mode == "open":
            if not (c.ids.get("tab1") and c.ids.get("tab2")):
                raise RuntimeError("문서 탭 좌표 없음")
            x, y = c.pt(c.ids["tab1"])
            t1, t2 = c.box(c.ids["tab1"]), c.box(c.ids["tab2"])
            strip = (min(t1[0], t2[0]), min(t1[1], t2[1]) - 6, max(t1[2], t2[2]), max(t1[3], t2[3]) + 6)  # 두 탭 머리(선택 표시가 옮겨 간다)
            return measure(c.rec, c.tmp, act, lambda: tap_hold(x, y), strip, (x, y), 2.5, keep)
        tabs = c.ids.get("navtabs") or {}
        key = next((k for k in tabs if "Explorer" in k), None) or next(iter(tabs), None)
        if not key:
            raise RuntimeError("하단 탭 좌표 없음")
        b = tabs[key]
        x, y = center(b)  # 하단 탭은 화면 바닥에 붙어 웹에서도 같은 자리다(Chrome 주소창은 위만 민다, 23:53 실측) — dy 를 더하지 않는다
        row = measure(c.rec, c.tmp, act, lambda: tap_hold(x, y), (int(c.w * 0.1), int(c.h * 0.3) + c.dy, int(c.w * 0.9), int(c.h * 0.6) + c.dy), (x, y), 2.5, keep)
        row["tab"] = key
        doc_key = next((k for k in tabs if "Doc" in k), None)
        if doc_key:
            tap(*center(tabs[doc_key]))
            time.sleep(1.5)
        return row
    if act == "scroll":
        d = c.ids.get("documents") or (0, int(c.h * 0.2), c.w, int(c.h * 0.85))
        top, bot = d[1] + 200 + c.dy, safe_y(c, d[3] - 80 + c.dy)
        x = int(d[0] + (d[2] - d[0]) * 0.75)
        roi = (d[0] + 20, top, d[0] + int((d[2] - d[0]) * 0.45), bot)
        return measure(c.rec, c.tmp, act, lambda: swipe(x, bot - 40, x, top + 40, 400), roi, (x, bot - 40), 3.0, keep)
    if act == "drag":
        if a.mode == "closed":
            return {"name": act, "skipped": "모바일형에는 끌 창이 없다"}
        if not c.ids.get("sdi_grid"):
            raise RuntimeError("독립 창 버튼 좌표 없음")
        tap(*c.pt(c.ids["sdi_grid"]))
        time.sleep(3)
        fg_guard(c)  # 버튼 탭 뒤 끌기 전에 다시 확인한다
        win_b, title = c.ids.get("sdi_window"), c.ids.get("sdi_title")
        if not (win_b and title):
            raise RuntimeError("독립 창 좌표 없음")
        x, y = c.pt(title)
        x = title[0] + 60
        wb = c.box(win_b)
        roi = (wb[2] - 140, wb[1] + 120, wb[2] + 10, wb[3] - 10)  # 창 오른쪽 띠: 손가락(제목 왼쪽)과 떨어진 곳
        row = measure(c.rec, c.tmp, act, lambda: swipe(x, y, x - 250, y + 350, 700), roi, (x, y), 3.0, keep)
        return row
    if act in ws.TAB_ACTIONS:
        return ws.do_tab(c, act, keep, measure, tap_hold, fg_guard)
    if act in ws.EXT_ACTIONS:
        return ws.do_ext(c, act, keep, measure, tap, tap_hold, swipe, fg_guard, find, dump_nodes, center)
    raise ValueError(act)


def summarize(doc):
    out = {}
    for run in doc["runs"]:
        for r in run["rows"]:
            s = out.setdefault(r["name"], {"resp": [], "settle": [], "fps": [], "errors": 0, "skipped": 0})
            if r.get("skipped"):
                s["skipped"] += 1
                continue
            if r.get("error"):
                s["errors"] += 1
                continue
            for k, key in (("resp", "resp_ms"), ("settle", "settle_ms"), ("fps", "moving_fps")):
                if r.get(key) is not None:
                    s[k].append(r[key])

    def stat(v):
        if not v:
            return None
        v = sorted(v)
        return {"n": len(v), "median": statistics.median(v), "p90": v[min(len(v) - 1, int(round(0.9 * (len(v) - 1))))], "min": v[0], "max": v[-1]}
    return {k: {"resp_ms": stat(v["resp"]), "settle_ms": stat(v["settle"]), "fps": stat(v["fps"]), "errors": v["errors"], "skipped": v["skipped"]} for k, v in out.items()}


def save(path, doc):
    with open(path + ".tmp", "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
    os.replace(path + ".tmp", path)


def user_present(path):
    """pc_away.json 의 state 가 present 면 True(사용자 복귀 — 측정 중단). 경로가 비었으면 False, 읽기 실패는 안전하게 present 로 본다."""
    if not path:
        return False
    try:
        with open(path, encoding="utf-8") as f:
            raw = f.read()
    except OSError:
        return True
    try:
        return json.loads(raw).get("state") == "present"
    except (ValueError, AttributeError):
        return '"present"' in raw


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--target", required=True, choices=("app", "web"))
    p.add_argument("--mode", required=True, choices=("open", "closed"))
    p.add_argument("--runs", type=int, default=10)
    p.add_argument("--out", required=True)
    p.add_argument("--apk", required=True, help="좌표를 얻는 제품 APK(앱 측정 대상이기도 하다)")
    p.add_argument("--url", default="http://127.0.0.1:5180/")
    p.add_argument("--actions", default="cold,warm,menu,doc,tab,scroll,drag")
    p.add_argument("--web-dy", type=int)
    p.add_argument("--trace", action="store_true")
    p.add_argument("--no-install", action="store_true")
    p.add_argument("--trace-tab", type=int, default=0, help="웹 펼침: 시행 뒤 탭 전환을 trace+CPU 프로파일로 N회 수집")
    p.add_argument("--keep-diff", action="store_true", help="시행마다 첫 반응~settle 사이 늦게 바뀐 행 구간·면적·프레임 시각을 row.keep_diff 로 남긴다")
    p.add_argument("--away-state", default="", help="pc_away.json 경로 — 시작 전·시행 전·조작 전마다 present 이면 중단한다(a16_trace.py 와 같은 방식, 중단한 시행은 표본에서 뺀다)")
    ws.add_args(p)
    wi.add_args(p)
    a = p.parse_args()
    global KEEP_DIFF  # noqa: PLW0603
    KEEP_DIFF = a.keep_diff
    wi.parse(a)  # 잘못된 --settings-json 은 측정 전에 실패시킨다
    if user_present(a.away_state):
        print("사용자 복귀(present) — 시작하지 않음", flush=True)
        return 3
    os.makedirs(a.out, exist_ok=True)
    tmp = os.path.join(a.out, "tmp")
    os.makedirs(tmp, exist_ok=True)
    part = os.path.join(a.out, "webperf_%s_%s.partial.json" % (a.target, a.mode))
    doc = {"target": a.target, "mode": a.mode, "url": a.url if a.target == "web" else None, "apk": a.apk, "started_at": time.strftime("%Y-%m-%dT%H:%M:%S+09:00"),
           "method": __doc__.split("방법:")[1].split("결과:")[0].strip(), "runs": [], "notes": []}
    orig_touch = sh("settings get system pointer_location")
    doc["pointer_location_orig"] = orig_touch
    orig_rot = {k: sh("settings get system %s" % k) for k in ROT_KEYS}
    doc["rotation_orig"] = orig_rot
    orig_wrot = {did: sh("cmd window user-rotation -d %d" % did) for did in display_ids()}  # "free" 또는 "lock N"
    doc["user_rotation_orig"] = orig_wrot
    doc["orientation_skipped"] = []
    doc["orientation_checks"] = ORIENT_LOG
    cdp = None
    try:
        lock_portrait()
        if not a.no_install:
            doc["install"] = adb("install", "-r", win(a.apk), timeout=400).strip()[-80:]
        set_mode(a.mode)
        info = require_portrait("after_set_mode")  # 접힘/펼침 전환이 회전을 바꿀 수 있어 전환 뒤에 다시 확인한다
        w, h = display_size()
        doc["display"] = [w, h]
        if info["cur"] and tuple(info["cur"]) != (w, h):  # 기록만(측정 크기 계산은 바꾸지 않는다)
            doc["notes"].append("wm size %dx%d 와 켜진 화면 cur %s 다름" % (w, h, info["cur"]))
        ids, app_img = discover(a, w, h)
        doc["ids"] = ids
        dy = 0
        if a.target == "web":
            sh("am force-stop %s" % PKG)
            adb("reverse", "tcp:%s" % a.url.split("//")[1].split("/")[0].split(":")[-1], "tcp:%s" % a.url.split("//")[1].split("/")[0].split(":")[-1])
            sh("am start -a android.intent.action.VIEW -d %s %s" % (a.url, CHROME))
            time.sleep(5)
            cdp = CDP()
            cdp.new_tab(a.url + "?fresh")
            cdp.call("Network.enable")
            cdp.call("Page.enable")
            doc["chrome"] = cdp.http("/json/version")
            time.sleep(25)
            require_portrait("layout_web")  # Chrome 기동 뒤 — layout 캡처·insets·web_dy 계산보다 먼저
            web_img = screencap(os.path.join(a.out, "layout_web_%s.png" % a.mode))
            best = band_dy(app_img, web_img, 80, 400)  # 참고값(접힘은 앱 본문이 달라 오판한다 — 23:53 실측 dy=0)
            # 정본: 웹 본문 위끝 = 화면 높이 − 내비게이션 막대 − innerHeight×dpr, 앱 본문 위끝 = 상태 표시줄 아래(펼침 실측 216 과 일치)
            vp = json.loads(cdp.eval("JSON.stringify([innerHeight, devicePixelRatio])"))
            ins = sh("dumpsys window | grep -m4 -E 'type=(statusBars|navigationBars) frame'")
            calc = wn.calc_web_dy(h, ins, vp)  # 보이는(visible=true·높이>0) navigationBars 만 쓴다(D-44)
            dy = a.web_dy if a.web_dy is not None else calc
            doc["web_dy"] = {"used": dy, "calc": calc, "viewport": vp, "insets": ins.strip()[:400], "band_auto": best}
            if a.mode == "open" and best and abs(best[0] - calc) > 20:  # 경고만(판정·시행 무변경). 접힘 band_auto 는 오판하므로 펼침만
                doc["web_dy"]["warn"] = "calc %d 와 band_auto %d 차이 %d px" % (calc, best[0], abs(best[0] - calc))
        sh("settings put system pointer_location 1")
        time.sleep(1.5)
        global INFO_SB  # noqa: PLW0603
        m_sb = re.search(r"type=statusBars frame=\[0,0\]\[\d+,(\d+)\]", sh("dumpsys window | grep -m2 'type=statusBars frame'"))
        INFO_SB = int(m_sb.group(1)) if m_sb else 80
        doc["status_bar_px"] = INFO_SB
        rec = Rec((w, h))
        c = Ctx(a, w, h, ids, dy, rec, tmp, cdp)
        c.trace_summarizer = tab_trace_summary
        for i in range(a.runs):
            if user_present(a.away_state):
                doc["stopped_present"] = {"before_run": i, "at": time.strftime("%H:%M:%S")}
                print("사용자 복귀(present) — 중단(시행 %d 전)" % i, flush=True)
                save(part, doc)
                break
            # 부하 게이트(호스트 1분 평균): 50 초과면 새 시행을 시작하지 않고, 40 미만이 2분 유지되면 재개한다
            waited, below_since = 0, None
            while True:
                la = os.getloadavg()[0]
                if la <= 50 and (waited == 0 or (below_since and time.time() - below_since >= 120)):
                    break
                below_since = (below_since or time.time()) if la < 40 else None
                waited += 10
                time.sleep(10)
            ok_port, oinfo = ensure_portrait()
            if not ok_port:  # 다시 고정해도 가로(또는 판독 불가)면 그 시행을 표본에서 뺀다 — 시행 수를 늘려 메우지 않는다
                doc["orientation_skipped"].append({"i": i, "stage": "시행 전", "reason": "방향 불일치", **oinfo})
                print(i, "방향 불일치 — 시행 제외 %s" % oinfo, flush=True)
                save(part, doc)
                continue
            run = {"i": i, "rows": [], "started": time.strftime("%H:%M:%S"), "load1": round(os.getloadavg()[0], 1), "load_wait_s": waited}
            ws.run_begin(c, a, run, i)
            for act in a.actions.split(","):
                if user_present(a.away_state):
                    run["stopped_present"] = {"before_action": act, "at": time.strftime("%H:%M:%S")}
                    break
                keep = os.path.join(a.out, "%s_%s_%s.png" % (a.target, a.mode, act)) if i == 0 else None
                try:
                    if act != "cold" and i == 0 and act == a.actions.split(",")[0]:
                        c.fresh()
                    run["rows"].append(do_action(c, act, keep))
                except Exception as e:  # noqa: BLE001 — 한 조작 실패가 나머지를 막지 않게 값으로 남긴다
                    run["rows"].append({"name": act, "error": repr(e)[:200]})
                r = run["rows"][-1]
                print(i, act, {k: r.get(k) for k in ("resp_ms", "settle_ms", "moving_fps", "error", "skipped")}, flush=True)
            ws.run_end(run, a)
            if "stopped_present" in run:  # 조작 도중 복귀 — 덜 끝난 시행은 표본에서 빼고(값은 기록) 측정을 끝낸다
                doc["stopped_present"] = {"in_run": i, "run": run, **run["stopped_present"]}
                print("사용자 복귀(present) — 중단(시행 %d 중, %s 전)" % (i, run["stopped_present"]["before_action"]), flush=True)
                save(part, doc)
                break
            oinfo = orientation()  # 시행 중 앱·Chrome 재기동으로 가로가 됐으면 그 시행 값은 좌표가 어긋난 것이라 표본에서 뺀다
            if not oinfo["portrait"]:
                doc["orientation_skipped"].append({"i": i, "stage": "시행 뒤", "reason": "방향 불일치", "run": run, **oinfo})
                print(i, "방향 불일치(시행 뒤) — 시행 제외 %s" % oinfo, flush=True)
                save(part, doc)
                continue
            doc["runs"].append(run)
            save(part, doc)
        if a.target == "web" and a.trace and cdp and not user_present(a.away_state):
            cats = "devtools.timeline,disabled-by-default-devtools.timeline,v8,v8.wasm,disabled-by-default-v8.compile,blink.user_timing,loading"
            cdp.call("Storage.clearDataForOrigin", origin=a.url.rstrip("/"), storageTypes="local_storage,indexeddb,cache_storage,service_workers")
            cdp.call("Network.setCacheDisabled", cacheDisabled=True)
            ev = cdp.trace(lambda: (cdp.call("Page.navigate", url=a.url + "?fresh&trace=1"), time.sleep(25)), cats)
            cdp.call("Network.setCacheDisabled", cacheDisabled=False)
            tpath = os.path.join(a.out, "trace_cold_%s.json" % a.mode)
            with open(tpath, "w", encoding="utf-8") as f:
                json.dump(ev, f)
            doc["trace"] = {"file": tpath, "events": len(ev), **trace_summary(ev)}
        if a.target == "web" and a.trace_tab and cdp and a.mode == "open":
            # 탭 전환 trace + CPU 프로파일 N회 — 시행 뒤 tab1 이 선택돼 있으므로 tab2·tab1 을 번갈아 누른다
            cats = ("devtools.timeline,disabled-by-default-devtools.timeline,disabled-by-default-devtools.timeline.frame,"
                    "v8,disabled-by-default-v8.cpu_profiler,blink.user_timing,toplevel")
            doc["trace_tab"] = []
            for k in range(a.trace_tab):
                if user_present(a.away_state):
                    doc["notes"].append("trace_tab %d 전 사용자 복귀(present) — 중단" % k)
                    break
                key = "tab2" if k % 2 == 0 else "tab1"
                x, y = c.pt(c.ids[key])
                fg_guard(c)
                ev = cdp.trace(lambda: (time.sleep(0.5), tap_hold(x, y), time.sleep(2.5)), cats)
                tpath = os.path.join(a.out, "trace_tab_%s_%d.json" % (a.mode, k))
                with open(tpath, "w", encoding="utf-8") as f:
                    json.dump(ev, f)
                s = {"k": k, "tab": key, "tap": [x, y], "file": tpath, "events": len(ev), **tab_trace_summary(ev)}
                doc["trace_tab"].append(s)
                print("trace_tab", k, key, s["long_tasks_ms"], (s["tasks"][0]["profile_top"][:3] if s["tasks"] else None), flush=True)
                time.sleep(2)
    finally:
        if cdp:
            cdp.close()
        sh("settings put system pointer_location %s" % (orig_touch if orig_touch not in ("null", "") else "0"))
        sh("cmd device_state state reset")
        for did, ov in orig_wrot.items():  # 디스플레이별 user-rotation 원래 값("free" 또는 "lock N") 복구 — settings 복구보다 먼저
            mo = re.match(r"\s*(free|lock)(?:\s+(\d+))?", ov or "")
            if mo:
                sh("cmd window user-rotation -d %d %s" % (did, mo.group(1) if mo.group(1) == "free" else "lock %s" % (mo.group(2) or "0")))
        for k in ("user_rotation", "accelerometer_rotation"):  # 원래 값 복구(정상·예외·중단 공통)
            ov = orig_rot.get(k, "")
            sh("settings delete system %s" % k if ov in ("null", "") else "settings put system %s %s" % (k, ov))
    doc["orientation_skipped_count"] = len(doc["orientation_skipped"])
    doc["summary"] = summarize(doc)
    doc["finished_at"] = time.strftime("%Y-%m-%dT%H:%M:%S+09:00")
    path = os.path.join(a.out, "webperf_%s_%s.json" % (a.target, a.mode))
    save(path, doc)
    if os.path.exists(part):
        os.remove(part)
    print("원자료", path, json.dumps(doc["summary"], ensure_ascii=False), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

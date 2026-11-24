# Fold5 PC 비움 측정 공용 함수 — 접힘·펼침 상태 전환, 표시 장치별 캡처, 재기동·원격 연결, pcaway_report 측정 파일 작성(판정 없이 값만 모은다)
"""
ui28_android.Ctx 를 재사용하고 다음만 다르다.
- 캡처는 활성 물리 디스플레이 id 를 -d 로 준다. -d 없이 찍으면 접힘에서 첫 물리 디스플레이가 찍혀 틀린 화면이 된다.
- 앱 제거·stay_on 원복은 호출자(세션 스크립트, phone_session.sh)가 맡는다. 측정마다 앱을 지우지 않는다.
- BACK 뒤 판정은 반드시 app_on_top() 을 함께 본다. launcher 화면의 노드 0개를 '닫힘'으로 세면 거짓 통과다(C4 발견).
제스처 BACK 은 오른쪽 가장자리 주입을 쓴다. Fold5 대조군에서 왼쪽(x=0~2) 주입은 시스템 설정 앱에서도 뒤로 가기가 되지 않았다.
"""
import json
import os
import re
import sys
import tempfile
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ui28_android as U  # noqa: E402

ra, rr = U.ra, U.rr
SER = "R3CW70202FP"
PKG = "com.pd.poc"
PLAY_PROTECT = []  # 설치 중 만난 Play 프로텍트 전송 질문 기록


def adb(*a, **k):
    return ra.adb(SER, *a, **k)


def sh(cmd, timeout=60):
    return adb("shell", cmd, timeout=timeout)[1].strip()


def dev_state():
    return sh("cmd device_state state").split("name='")[-1].split("'")[0]


def wm_size():
    m = re.findall(r"(\d+)x(\d+)", sh("wm size"))
    return tuple(map(int, m[-1]))


def display_id():
    m = re.search(r"isActive=true, displayId=\d+, uniqueId='local:(\d+)", sh("dumpsys display"))
    return m.group(1) if m else None


def png(disp=None):
    disp = disp or display_id()
    rc, data = adb(*(["exec-out", "screencap", "-p"] + (["-d", disp] if disp else [])), binary=True, timeout=60)
    i = data.find(b"\x89PNG")
    return data[i:] if i >= 0 else b""


def wake():
    if "mWakefulness=Awake" not in sh("dumpsys power | grep mWakefulness="):
        sh("input keyevent KEYCODE_WAKEUP")
        time.sleep(1)


def top_activity():
    return sh("dumpsys activity activities | grep -E 'topResumedActivity|ResumedActivity:' | head -3")


def app_on_top():
    return PKG in top_activity()


def relaunch():
    sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % PKG)
    time.sleep(2.5)
    wake()


def key_back():
    sh("input keyevent KEYCODE_BACK")


def gesture_back():
    w, h = wm_size()
    sh("input swipe %d %d %d %d 300" % (w - 1, h // 2, max(w // 2, w - 500), h // 2))


class Ctx(U.Ctx):
    def __init__(self, role, build, out):
        os.makedirs(out, exist_ok=True)
        super().__init__(SER, role, build, out)

    def shot(self, name):
        p = os.path.join(self.out, "%s.png" % name)
        data = png()
        with open(p, "wb") as f:
            f.write(data)
        self.raw.setdefault("shots", []).append({"file": os.path.basename(p), "bytes": len(data), "t": time.strftime("%H:%M:%S")})
        return p


def install(apk, fresh=False):
    """fresh=True 면 시험 앱을 지우고 설치한다(서명이 다른 판본 전환, 저장된 레이아웃 제거)."""
    wake()
    if fresh:
        adb("uninstall", PKG)
    box = {}

    def run():
        box["res"] = adb("install", "-r", ra.win_path(apk), timeout=300)

    t = threading.Thread(target=run)
    t.start()
    while t.is_alive():
        t.join(3)
        if t.is_alive():
            decline_play_protect()
    rc, out = box.get("res", (1, ""))
    print("install", rc, out.strip().splitlines()[-1] if out.strip() else "", flush=True)
    return "Success" in out


def decline_play_protect():
    """설치를 막는 Play 프로텍트 전송 질문에 '전송하지 않음' 만 누른다. 전송·설정 변경 항목은 누르지 않는다. 문구를 못 찾으면 누르지 않고 기록만 한다."""
    if "protectdialogs" not in sh("dumpsys activity activities | grep -m1 topResumedActivity"):
        return
    adb("shell", "uiautomator", "dump", "/sdcard/pp.xml")
    xml = adb("exec-out", "cat", "/sdcard/pp.xml", binary=True)[1].decode("utf-8", "replace")
    adb("shell", "rm", "-f", "/sdcard/pp.xml")
    m = re.search(r'text="전송하지 않음"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', xml)
    rec = {"t": time.strftime("%H:%M:%S"), "declined": bool(m), "texts": re.findall(r'text="([^"]+)"', xml)[:8]}
    if m:
        x1, y1, x2, y2 = map(int, m.groups())
        sh("input tap %d %d" % ((x1 + x2) // 2, (y1 + y2) // 2))
    PLAY_PROTECT.append(rec)
    print("play_protect", rec, flush=True)
    time.sleep(2)


def attach(c, restart=True, token_from_logcat=False, wait=80):
    """앱을 띄우고 원격 연결을 잡는다. 진단 Release 는 run-as 가 안 돼 logcat 토큰을 쓴다."""
    if restart:
        sh("am force-stop %s" % PKG)
        if not token_from_logcat:
            sh("run-as %s rm -f files/PdPoc/remote-control.json" % PKG)
        adb("logcat", "-c")
        sh("monkey -p %s -c android.intent.category.LAUNCHER 1" % PKG)
    adb("forward", "tcp:%d" % ra.HOST_PORT, "tcp:5960")
    info, t0 = None, time.time()
    while time.time() - t0 < wait and not info:
        if token_from_logcat:
            ms = re.findall(r"원격 제어 서버 시작 (\S+) bind=(\S*) pid=(\d+) token=(\S+)", adb("logcat", "-d")[1])
            if ms:
                info = {"url": ms[-1][0], "port": 5960, "token": ms[-1][3], "pid": int(ms[-1][2]), "bind": ms[-1][1].split(",")}
        else:
            rc, out = adb("exec-out", "run-as", PKG, "cat", "files/PdPoc/remote-control.json")
            if rc == 0 and out.strip().startswith("{"):
                info = json.loads(out)
        if not info:
            time.sleep(1)
    if not info:
        return None
    info["port"] = ra.HOST_PORT
    if "url" in info:
        info["url"] = "http://localhost:%d/" % ra.HOST_PORT
    fd, path = tempfile.mkstemp(prefix="pd_f5_", suffix=".json")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(info, f)
    os.environ["PD_REMOTE_INFO"] = path
    c.r = rr.Remote()
    return path


def set_closed():
    sh("cmd device_state state 0")
    time.sleep(3)
    wake()
    return dev_state()


def set_open():
    sh("cmd device_state state reset")
    time.sleep(3)
    wake()
    return dev_state()


def ensure(state):
    """측정마다 상태 재확인. override 가 풀렸으면 다시 건다."""
    now = dev_state()
    if state == "CLOSED" and now != "CLOSED":
        print("device_state 풀림 감지", now, flush=True)
        set_closed()
    if state == "OPENED" and now != "OPENED":
        set_open()
    return dev_state()


def walk(n, out):
    if isinstance(n, dict):
        out.append(n)
        for ch in n.get("children") or n.get("tree") or []:
            walk(ch, out)
    elif isinstance(n, list):
        for ch in n:
            walk(ch, out)
    return out


def write_measure(out, item, rows, source, captures=()):
    """pcaway_report.py emit --measure-file 형식. rows={이름: (값, 통과)}. 캡처 판독 기록은 otest 가 따로 넣는다."""
    doc = {"measurements": [{"name": k, "value": v[0], "pass": bool(v[1]), "source": source} for k, v in rows.items()], "captures": [{"path": p} for p in captures]}
    p = os.path.join(out, "%s_measure.json" % item)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
    for k, v in rows.items():
        print("%s %s %s" % ("PASS" if v[1] else "FAIL", k, str(v[0])[:160]), flush=True)
    print("측정 파일", p, flush=True)
    return p


def save_raw(c, name):
    p = os.path.join(c.out, name + ".json")
    c.raw.setdefault("play_protect", []).extend(PLAY_PROTECT)
    with open(p, "w", encoding="utf-8") as f:
        json.dump({"finished_at": time.strftime("%Y-%m-%dT%H:%M:%S+09:00"), "checks": c.checks, "raw": c.raw}, f, ensure_ascii=False, indent=1, default=str)
    print("원자료", p, "PASS", sum(1 for k in c.checks if k["pass"]), "FAIL", sum(1 for k in c.checks if not k["pass"]), flush=True)
    return p

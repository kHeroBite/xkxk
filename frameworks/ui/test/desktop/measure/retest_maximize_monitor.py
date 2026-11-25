#!/usr/bin/env python3
# 최대화 소속 모니터 회귀 측정 — 메인·SDI 창을 각 모니터·걸침 위치에 놓고 최대화·복원한 뒤 창 경계·소속 모니터·DPI 를 Windows 기준(MonitorFromRect)과 대조해 JSON·표로 남긴다
"""
사용: python3 -B retest_maximize_monitor.py --conf run/<복사본> --out-dir logs/<에이전트>/<회차> --onscreen-approved '<누가·언제>' [--select <장치 id>]
- 승인 인자 없이는 실행을 거부한다(rc=3). 창이 사용자 모니터를 실제로 차지하며, 투명 처리가 보이지 않음을 보장한다는 검증은 아직 없다.
- 앱은 이 스크립트가 진단 빌드 복사본을 임시 --data-dir·--profile-dir·--multi-instance on 으로 띄우고, 끝나면 그 PID 만 app.shutdown 으로 끈다.
- 모니터가 3대면 왼쪽·가운데·오른쪽, 2대면 있는 짝만 잰다. 기대 모니터는 대조용으로 OS 에 직접 묻는다(MonitorFromRect, MONITOR_DEFAULTTONEAREST).
- 실제 모니터 최대화라 시험 창은 사용자 화면 영역에 놓인다. 알파 0·클릭 통과·활성화 안 됨으로 만들고(win_place.ps1 cloak) 맨 아래 Z 로 내린다.
- 입력 API·전경 획득 없음(L0 원격 명령 + SetWindowPos NOACTIVATE). 원격 명령은 제목줄 버튼과 같은 상태 변경을 부르므로 버튼 클릭 자체는 재지 않는다.
- 조작마다 전경을 읽어 자기 앱이 전경이 되면 즉시 중단한다(rc=4). 사용자 layout.json·settings.json 은 전후 md5 를 대조한다.
종료 코드: 0=전 항목 통과, 1=실패 있음, 2=기동·연결 실패, 3=자기 PID 확인 실패, 4=전경 게이트 위반.
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
PS = os.path.join(HERE, "win_place.ps1")
SETTLE_S = 0.8  # 최대화·복원 뒤 OS·앱 처리가 끝나기를 기다린다
EDGE_TOL = 1  # 보이는 프레임과 작업 영역의 허용 차(px)
RESTORE_TOL = 1
W, H = 1400, 800
SHADOW = "PdSdiShadow"


class ForegroundViolation(Exception):
    pass


def decode_ps(raw):
    """win_place.ps1 출력은 콘솔 코드 페이지(한국어 Windows 는 CP949)다. 한글 창 제목(SDI '입력폼(한글)' 등)이 있으면 UTF-8 해석이 깨지므로 CP949 로 다시 읽는다(otest-9 11-07)."""
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("cp949", errors="replace")


def wp(pid, ops):
    out = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", to_win(PS), "-OwnPid", str(pid), "-Ops", ";".join(ops)],
                         capture_output=True, timeout=90)
    out.stdout, out.stderr = decode_ps(out.stdout), decode_ps(out.stderr)
    res = [json.loads(line) for line in out.stdout.splitlines() if line.strip().startswith("{")]
    if len(res) != len(ops):
        raise RuntimeError("win_place 결과 %d/%d: %s" % (len(res), len(ops), out.stderr[:400] + out.stdout[:200]))
    return res


def to_win(path):
    return subprocess.run(["wslpath", "-w", path], capture_output=True, text=True, check=True).stdout.strip()


def rect_tuple(r):
    return (r["l"], r["t"], r["r"], r["b"])


def overlap_share(rect, mons):
    """창과 겹치는 면적 비율(모니터별, 표에 남겨 걸침 비율을 눈으로 확인한다)."""
    area = max(1, (rect["r"] - rect["l"]) * (rect["b"] - rect["t"]))
    out = {}
    for m in mons:
        b = m["bounds"]
        w = max(0, min(rect["r"], b["r"]) - max(rect["l"], b["l"]))
        h = max(0, min(rect["b"], b["b"]) - max(rect["t"], b["t"]))
        out[m["device"].strip("\\.")] = round(w * h / area, 3)
    return out


class Session:
    def __init__(self, pid, remote, mons):
        self.pid, self.r, self.mons, self.rows = pid, remote, mons, []
        self.main = None
        self.allow_foreground, self.foreground_hits = False, []  # 사용자가 PC 를 비운 측정에서만 켠다

    def windows(self):
        res = wp(self.pid, ["list"])[0]
        if res["fg"]["isOwn"]:
            if not self.allow_foreground:
                raise ForegroundViolation(json.dumps(res["fg"]))
            self.foreground_hits.append(time.strftime("%H:%M:%S"))
        return res["windows"]

    def win(self, hwnd):
        return next((w for w in self.windows() if w["hwnd"] == hwnd), None)

    def hide_all(self):
        """자기 창 전부 투명·클릭 통과·맨 아래. 그림자 창은 레이어드라 알파 설정이 실패할 수 있어 결과를 남긴다."""
        hs = [w["hwnd"] for w in self.windows()]
        return wp(self.pid, ["cloak:%d" % h for h in hs] + ["bottom:%d" % h for h in hs])

    def app_windows(self):
        return [w for w in self.windows() if w["cls"] != SHADOW and w["rect"]["r"] - w["rect"]["l"] > 100]

    def place(self, hwnd, x, y, w=W, h=H):
        return wp(self.pid, ["move:%d:%d:%d:%d:%d" % (hwnd, x, y, w, h)])[0]["win"]

    def expected(self, rect):
        return wp(self.pid, ["monrect:%d:%d:%d:%d" % (rect["l"], rect["t"], rect["r"] - rect["l"], rect["b"] - rect["t"])])[0]["monitor"]

    def set_state(self, kind, doc, state):
        if kind == "main":
            self.r.call("monitor.maximize", via="button" if state == "maximized" else "restore")
        else:
            self.r.call("docs.state", doc=doc, state=state)
        time.sleep(SETTLE_S)

    def mon(self, device):
        return next(m for m in self.mons if m["device"] == device)

    def judge_max(self, name, kind, doc, hwnd, x, y, w=W, h=H, others=()):
        """놓기 → 기대 모니터 조회 → 최대화·측정 → 복원·측정. others = 움직이면 안 되는 창 hwnd."""
        placed = self.place(hwnd, x, y, w, h)
        before = placed["rect"]
        expect = self.expected(before)
        watch = {o: self.win(o)["rect"] for o in others}
        self.set_state(kind, doc, "maximized")
        wp(self.pid, ["bottom:%d" % hwnd])
        after = self.win(hwnd)
        work = self.mon(expect)["work"]
        frame = after["frame"] or after["rect"]
        delta = [abs(a - b) for a, b in zip(rect_tuple(frame), rect_tuple(work))]
        moved_others = [o for o, r in watch.items() if self.win(o)["rect"] != r]
        self.set_state(kind, doc, "normal")
        restored = self.win(hwnd)
        back = [abs(a - b) for a, b in zip(rect_tuple(restored["rect"]), rect_tuple(before))]
        row = {
            "case": name, "kind": kind, "before": before, "share": overlap_share(before, self.mons), "expect": expect,
            "maxRect": after["rect"], "maxFrame": frame, "maxClient": after["client"], "zoomed": after["zoomed"], "maxMonitor": after["monitor"],
            "work": work, "dpiMonitor": self.mon(expect)["dpi"], "dpiWindow": after["dpi"], "frameDelta": delta, "owner": after["owner"],
            "othersMoved": moved_others, "restored": restored["rect"], "restoreDelta": back, "restoreZoomed": restored["zoomed"],
        }
        row["pass"] = bool(after["zoomed"] and after["monitor"] == expect and max(delta) <= EDGE_TOL and not moved_others
                           and not restored["zoomed"] and max(back) <= RESTORE_TOL)
        self.rows.append(row)
        print("%-4s %-22s %-22s 기대 %-12s 실제 %-12s frame %s work %s 복원차 %s %s" % (
            kind, name, row["share"], expect.strip("\\."), after["monitor"].strip("\\."), rect_tuple(frame), rect_tuple(work), max(back), "PASS" if row["pass"] else "FAIL"), flush=True)
        return row


def positions(mons):
    """왼쪽→오른쪽 정렬 모니터에서 소속·걸침(60:40·40:60·50:50) 좌표. 세로는 이웃 두 모니터 작업 영역의 공통 구간."""
    order = sorted(mons, key=lambda m: m["bounds"]["l"])
    cases = []
    for i, m in enumerate(order):
        work = m["work"]
        cases.append(("in_%d_%s" % (i, m["device"].strip("\\.")), work["l"] + 200, work["t"] + 150, W, H))
    for i in range(len(order) - 1):
        a, b = order[i]["work"], order[i + 1]["work"]
        edge = order[i + 1]["bounds"]["l"]
        top = max(a["t"], b["t"]) + 60
        for label, left_share in (("60_40", 0.6), ("40_60", 0.4), ("50_50", 0.5)):
            cases.append(("span%d_%s" % (i, label), edge - int(1800 * left_share), top, 1800, H))
    return order, cases


def user_files_md5():
    base = subprocess.run(["cmd.exe", "/c", "echo %LOCALAPPDATA%"], capture_output=True, text=True).stdout.strip()
    root = os.path.join(subprocess.run(["wslpath", base], capture_output=True, text=True).stdout.strip(), "xkxk", "frameworks", "ui")
    out = {}
    for name in ("layout.json", "settings.json"):
        path = os.path.join(root, name)
        out[name] = hashlib.md5(open(path, "rb").read()).hexdigest() if os.path.exists(path) else None
    return out


def launch(conf, out_dir, mons, approved, pos=None):
    data, profile = os.path.join(out_dir, "data"), os.path.join(out_dir, "profile")
    os.makedirs(data, exist_ok=True)
    os.makedirs(profile, exist_ok=True)
    left = min(mons, key=lambda m: m["bounds"]["l"])["bounds"]
    # 고정 좌표가 모니터 안이어야 진단판이 화면 밖으로 되돌리지 않는다(IsOnscreenPlacement). 왼쪽 모니터 맨 아래 줄이라 첫 표시 노출은 1px 줄이다.
    pos = pos or "%d,%d" % (left["l"] + 1, left["b"] - 1)
    env = dict(os.environ, PD_ALLOW_ONSCREEN="1", PD_ONSCREEN_REASON="최대화 소속 모니터 측정, 승인: " + approved)
    cmd = ["bash", os.path.join(UI, "tools", "run_desktop_bg.sh"), "--conf", conf, "--no-kill", "--", "--multi-instance", "on", "--remote-control",
           "--data-dir", data, "--profile-dir", profile, "--bg-pos", pos]
    res = subprocess.run(cmd, capture_output=True, text=True, env=env, timeout=120)
    print(res.stdout[-500:], flush=True)
    os.environ["PD_REMOTE_INFO"] = os.path.join(data, "remote-control.json")
    return res.returncode


def run_cases(s, order, cases, out, select):
    main = max(s.app_windows(), key=lambda w: (w["rect"]["r"] - w["rect"]["l"]) * (w["rect"]["b"] - w["rect"]["t"]))["hwnd"]
    s.main = main
    s.hide_all()
    out["cloak"] = "applied"
    if select:
        out["select"] = s.r.call("monitor.select", id=select)
    for name, x, y, w, h in cases:
        s.judge_max("sel_" + name if select else name, "main", None, main, x, y, w, h)
    known = {w["hwnd"] for w in s.windows()}
    docs = []
    for i in range(len(order)):
        res = s.r.call("docs.open", kind="form", title="MAXMON%d" % i, sdi=True, allowDuplicate=True)
        time.sleep(1.5)
        doc = (res[0] if isinstance(res, list) else res).get("id")
        added = [w for w in s.app_windows() if w["hwnd"] not in known]
        known |= {w["hwnd"] for w in s.windows()}
        s.hide_all()
        docs.append((doc, added[0]["hwnd"]))
    out["sdi"] = docs
    first_doc, first = docs[0]
    # 메인을 왼쪽 첫 모니터에 둔다. SDI 는 다른 모니터에서도 메인 모니터로 끌려가지 않아야 한다(메인 사각형 불변도 함께 본다).
    s.place(main, order[0]["work"]["l"] + 100, order[0]["work"]["t"] + 100)
    for name, x, y, w, h in cases:
        s.judge_max(name, "sdi", first_doc, first, x, y, w, h, others=(main,))
    # SDI 여러 개가 각자 모니터에서 최대화·복원(서로·메인에 끌려가지 않음)
    for (doc, hwnd), mon in zip(docs, order):
        s.place(hwnd, mon["work"]["l"] + 300, mon["work"]["t"] + 200)
    befores = {h: s.win(h)["rect"] for _, h in docs}
    for doc, _ in docs:
        s.set_state("sdi", doc, "maximized")
    multi = []
    for (doc, hwnd), mon in zip(docs, order):
        w = s.win(hwnd)
        frame = w["frame"] or w["rect"]
        multi.append({"doc": doc, "expect": mon["device"], "monitor": w["monitor"], "frame": frame, "work": mon["work"], "owner": w["owner"], "zoomed": w["zoomed"],
                      "pass": w["zoomed"] and w["monitor"] == mon["device"] and w["owner"] == 0 and max(abs(a - b) for a, b in zip(rect_tuple(frame), rect_tuple(mon["work"]))) <= EDGE_TOL})
    for doc, _ in docs:
        s.set_state("sdi", doc, "normal")
    for item, (_, hwnd) in zip(multi, docs):
        item["restored"] = s.win(hwnd)["rect"]
        item["pass"] = item["pass"] and item["restored"] == befores[hwnd]
    out["multiSdi"] = multi
    print("multiSdi", [(m["monitor"].strip("\\."), m["owner"], m["pass"]) for m in multi], flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--conf", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--select", help="메뉴 '멀티 모니터' 에서 고른 상태로 메인 최대화 버튼 경로를 잰다(고른 값에 끌려가지 않아야 한다)")
    ap.add_argument("--onscreen-approved", help="실제 모니터 사용 승인 근거(누가·언제). 없으면 어떤 창도 띄우지 않고 rc=3 으로 끝난다")
    ap.add_argument("--spawn-conf", help="항목 5 를 이어서 잴 복사본(일반 기동, retest_maximize_spawn.py)")
    ap.add_argument("--spawn-before-conf", help="항목 5 재현용 수정 전 복사본(run/odev-fix-maximize_before)")
    ap.add_argument("--user-away", help="항목 5 로 넘긴다: 사용자가 PC 를 비운다는 근거(누가·언제). 없으면 항목 5 는 거부된다")
    a = ap.parse_args()
    if not a.onscreen_approved or len(a.onscreen_approved) < 10:
        print("거부: 이 측정은 시험 창이 사용자 모니터를 차지한다. 리드 승인을 받아 --onscreen-approved '<누가·언제>' 를 준다(10자 이상).")
        return 3
    out_dir = os.path.abspath(a.out_dir)
    if os.sep + "ui28" + os.sep in out_dir + os.sep:
        print("logs/ui28 쓰기 금지")
        return 3
    os.makedirs(out_dir, exist_ok=True)
    mons = wp(0, ["monitors"])[0]["monitors"]
    if isinstance(mons, dict):
        mons = [mons]
    out = {"monitors": mons, "control": [], "userFilesBefore": user_files_md5()}
    order, cases = positions(mons)
    for name, x, y, w, h in cases:  # 대조군: 같은 사각형의 표준 Win32 창이 OS 최대화로 가는 곳
        c = wp(0, ["control:%d:%d:%d:%d" % (x, y, w, h)])[0]
        out["control"].append({"case": name, **c})
    rc = launch(a.conf, out_dir, mons, a.onscreen_approved)
    if rc != 0:
        print("기동 실패 rc=%d" % rc)
        return 2
    from retest_remote import Remote  # 연결 정보 환경변수를 지정한 뒤에 만든다
    remote = Remote()
    status = remote.call("status") or {}
    pid = status.get("pid")
    if not pid:
        print("status 실패", remote.calls[-1:])
        return 2
    exe = subprocess.run(["powershell.exe", "-NoProfile", "-Command", "(Get-Process -Id %d).Path" % pid], capture_output=True, text=True).stdout.strip()
    out["pid"], out["exe"] = pid, exe
    if a.conf.replace("/", "\\") not in exe:
        print("자기 복사본이 아님 — 조작 거부:", pid, exe)
        return 3
    s = Session(pid, remote, mons)
    code = 0
    try:
        run_cases(s, order, cases, out, a.select)
    except ForegroundViolation as ex:
        print("전경 게이트 위반 — 중단:", ex)
        out["foregroundViolation"] = str(ex)
        code = 4
    finally:
        remote.call("app.shutdown")
        out["rows"], out["userFilesAfter"] = s.rows, None
        time.sleep(2)
        out["userFilesAfter"] = user_files_md5()
        with open(os.path.join(out_dir, "maximize.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    failed = [r["case"] + "/" + r["kind"] for r in s.rows if not r["pass"]] + [m["doc"] for m in out.get("multiSdi", []) if not m["pass"]]
    ctl = {c["case"]: c["monitor"].strip("\\.") for c in out["control"]}
    print("대조군(표준 창 OS 최대화):", ctl)
    print("사용자 파일 불변:", out["userFilesBefore"] == out["userFilesAfter"], " 실패:", failed or "없음")
    if code == 0 and a.spawn_conf:  # 항목 5(분리 창 생성 위치)까지 한 번에. 수정 전 판본을 따로 주면 재현도 함께 잰다
        for label, conf in (("spawn_before", a.spawn_before_conf), ("spawn", a.spawn_conf)):
            if conf:
                extra = ["--user-away", a.user_away] if a.user_away else []
                spawn = subprocess.run([sys.executable, "-B", os.path.join(HERE, "retest_maximize_spawn.py"), "--conf", conf, "--out-dir", os.path.join(out_dir, label), "--onscreen-approved", a.onscreen_approved] + extra)
                print(label, "rc=%d" % spawn.returncode, flush=True)
                if label == "spawn" and spawn.returncode != 0:
                    return spawn.returncode
    return code or (1 if failed else 0)


if __name__ == "__main__":
    sys.exit(main())

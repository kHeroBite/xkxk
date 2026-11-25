#!/usr/bin/env python3
# 데스크톱 Snap Assist(11 문서 §3.9) 원격 시험 — 진단판을 화면 밖 격리로 띄우거나(--run-dir) 이미 띄운 격리 인스턴스에 붙어(--info·--port) snapassist.state·pick·zone·skip·cancel 로 MDI·SDI 시나리오를 동작 기준으로 판정한다
"""
사용: python3 -B snap_assist_remote.py --run-dir <bin/run/<이름>/net10.0> --work <결과 폴더> [--label <회차>] [--keep]
      python3 -B snap_assist_remote.py --info <격리 data 폴더의 remote-control.json> [--port 5961] --work <결과 폴더>
      python3 -B snap_assist_remote.py --self-test
기동·종료는 desktop/measure/mdi_drag_app.launch/stop(--multi-instance on, 임시 data·profile, 화면 밖 --bg-pos, 자기 PID 만 종료)을 그대로 쓴다.
--info 모드는 사용자 폴더(%LOCALAPPDATA%\\xkxk\\frameworks\\ui)의 연결 정보와 사용자 포트(5180·5220)를 거부한다. --port 는 연결 정보 파일의 포트를 덮어쓴다.
진입은 실입력 없이 최대화 단추 팝업 원격(snapflyout.hover → snapflyout.pick, SnapFlyoutHover.Pick 과 같은 ApplyCell(…, Flyout) 경로)으로 한다.
판정은 상태(snapassist.state)와 배치 결과(placement.state 의 MDI bounds)만 본다. 시간(ms) 판정은 하지 않는다(성능은 별도 측정).
썸네일은 snapassist.thumbdump(진단: 원래 크기·축소 비트맵을 --work 아래 thumbdump_* 에 저장)와 카드 칸 좌표(ui.find snap.assist.card.N.thumb·.label)로,
대체 카드는 snapassist.thumbfail(진단 강제 실패)로 본다. 위치 고정은 chrome.lock(제목줄 단추와 같은 경로)으로 건다.
배치 확인: 고른 창에 같은 칸을 snapcell.apply 로 한 번 더 적용해 bounds 가 ±0.5 DIP 안에서 그대로면 그 칸에 놓인 것으로 본다(칸 사각형 재계산 없이 ApplyCell 결과로 대조).
종료 코드: 0=전 항목 통과, 1=검사 실패, 2=연결·기동 실패, 3=실행 거부·창 노출 감지. 결과는 --work/<label>/snap_assist_remote.json(커밋 폴더·logs/ui28 금지).
"""
import argparse
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()

TOL = 0.5
USER_PORTS = (5180, 5220)
FOREGROUND_NEEDED = [
    {"item": "MDI 창을 끌기 막대 칸·가장자리·모서리에 실제로 끌어 놓기 진입", "why": "데스크톱 MDI 끌기 원격 명령이 없다 — 여기서는 팝업 원격 진입만 본다(같은 세션 경로). 웹 축 w24_snap_assist.py 가 실마우스 끌기를 본다"},
    {"item": "후보 카드 실클릭·흐린 칸 실클릭·바깥 실클릭·Esc 실키", "why": "비전경 실입력 금지 — snapassist.pick·zone·cancel(같은 Session API)로 대신 본다"},
    {"item": "SDI 앱 모드 호스트 창 표시·고르기", "why": "화면 밖 비전경(--bg-pos)에서는 호스트를 띄우지 않는다(skip=bg-mode, 계획 §5.5). 사용자 PC 비움 때 화면 안 시험으로 보류(a10)"},
]


# 순수 판정(자가시험 대상) -----------------------------------------------------
def same_rect(a, b, tol=TOL):
    return bool(a and b) and all(abs(a[k] - b[k]) <= tol for k in ("x", "y", "w", "h"))


def parse_state(text):
    """SnapAssistSession.State 1줄('active=True scope=Mdi layout=quad cells=4 filled=[0,1] …')을 사전으로 바꾼다."""
    out = {}
    for k, v in re.findall(r"(\w+)=(\[[^\]]*\]|\S*)", text or ""):
        if v.startswith("["):
            out[k] = [int(x) for x in v[1:-1].split(",") if x.strip().lstrip("-").isdigit()]
        elif v in ("True", "False"):
            out[k] = v == "True"
        elif v.lstrip("-").isdigit():
            out[k] = int(v)
        else:
            out[k] = v
    return out


def moved(before, after, ignore=()):
    """두 bounds 사전({doc: rect})에서 위치·크기가 바뀐 문서 id 목록."""
    return [d for d in after if d not in ignore and not same_rect(before.get(d), after.get(d))]


def thumb_statuses(text):
    """상태 1줄의 thumb 필드('thumb=[0:bitmap,1:fallback:diag]' 또는 'thumb=off')를 후보 순서 결과 목록으로. off 면 'off', 없으면 None."""
    m = re.search(r"\bthumb=(\[[^\]]*\]|\S+)", text or "")
    if not m:
        return None
    v = m.group(1)
    if not v.startswith("["):
        return v
    return [item.split(":", 1)[1] if ":" in item else item for item in v[1:-1].split(",") if item]


def parse_dump(line):
    """snapassist.thumbdump 1줄('0 title=… src=… bounds=x,y,WxH … full=WxH fullContent=x,y,WxH thumb=WxH …')에서 수치를 뽑는다."""
    out = {}
    m = re.search(r"bounds=-?[\d.]+,-?[\d.]+,(\d+)x(\d+)", line or "")
    if m:
        out["size"] = (int(m.group(1)), int(m.group(2)))
    for key in ("legacy", "full", "thumb"):
        m = re.search(r"\b%s=(\d+)x(\d+)" % key, line or "")
        if m:
            out[key] = (int(m.group(1)), int(m.group(2)))
    for key in ("legacyContent", "fullContent"):
        m = re.search(r"\b%s=(\d+),(\d+),(\d+)x(\d+)" % key, line or "")
        if m:
            out[key] = tuple(int(g) for g in m.groups())
    return out


def dump_ok(d, cover=0.9, ratio_tol=0.03):
    """창 전체가 담겼는가(원래 크기 비트맵의 불투명 범위가 가로·세로 cover 이상) + 썸네일 비율이 창 비율과 같은가(±ratio_tol)."""
    if not all(k in d for k in ("size", "full", "fullContent", "thumb")):
        return False
    fw, fh = d["full"]
    _, _, cw, ch = d["fullContent"]
    (sw, sh), (tw, th) = d["size"], d["thumb"]
    return cw >= fw * cover and ch >= fh * cover and sh > 0 and th > 0 and abs((tw / th) / (sw / sh) - 1) <= ratio_tol


# 실행 ------------------------------------------------------------------------
class Run:
    def __init__(self, remote, pid):
        self.r, self.pid, self.checks, self.calls = remote, pid, [], []
        self.guard = None

    def check(self, name, ok, detail=""):
        self.checks.append({"name": name, "pass": bool(ok), "detail": detail})
        print("  %s %s %s" % ("PASS" if ok else "FAIL", name, json.dumps(detail, ensure_ascii=False, default=str)[:400]), flush=True)

    def call(self, cmd, **kw):
        res = self.r.call(cmd, **kw)
        self.calls.append({"cmd": cmd, "args": kw, "result": res})
        if self.guard:
            self.guard.check()
        return res

    def assist(self):
        s = self.call("snapassist.state") or {}
        s["parsed"] = parse_state(s.get("state"))
        return s

    def wait_assist(self, active, tries=15):
        """배치·세션 시작은 다음 UI 틱(Post)이다 — 원하는 isActive 가 될 때까지 짧게 다시 묻는다(시간 판정 아님)."""
        s = {}
        for _ in range(tries):
            s = self.assist()
            if bool(s.get("isActive")) == active:
                break
            time.sleep(0.2)
        return s

    def bounds(self):
        return {d.get("id"): d.get("bounds") for d in (self.call("placement.state") or {}).get("docs") or []}

    def open_mdi(self, n):
        self.call("docs.close", all=True)
        time.sleep(0.5)
        for _ in range(n):
            self.call("docs.open", kind="grid", allowDuplicate=True)
        time.sleep(1.0)
        ids = [d.get("id") for d in self.call("docs.list") or []]
        for i, d in enumerate(ids):  # 겹치지 않게 펼쳐 둔다(후보 판정에 영향 없음, 배치 전후 비교를 쉽게)
            self.call("placement.markPlaced", doc=d, x=30 + i * 60, y=30 + i * 50, w=320, h=240)
        time.sleep(0.6)
        return ids

    def flyout_pick(self, doc, layout, cell, kind="mdi"):
        """최대화 단추 호버(지연 시작) → 팝업 열림 대기 → 칸 고르기. 열리지 않으면 None."""
        st = (self.call("snapflyout.hover", kind=kind, doc=doc) or {}).get("state") or {}
        for _ in range(25):
            if st.get("open") or st.get("skip"):
                break
            time.sleep(0.2)
            st = self.call("snapflyout.state") or {}
        if not st.get("open"):
            self.call("snapflyout.leave")
            return {"open": False, "state": st}
        res = self.call("snapflyout.pick", layout=layout, cell=cell) or {}
        time.sleep(0.6)
        return {"open": True, "done": res.get("done"), "state": res.get("state")}

    def same_cell(self, doc, layout, cell):
        """고른 창에 같은 칸을 다시 적용해도 bounds 가 그대로인가(세션 밖에서만 부른다 — ApplyCell 은 assist 없이 세션을 띄우지 않는다)."""
        before = self.bounds().get(doc)
        self.call("snapcell.apply", kind="mdi", doc=doc, layout=layout, cell=cell)
        time.sleep(0.4)
        after = self.bounds().get(doc)
        return same_rect(before, after), {"doc": doc, "layout": layout, "cell": cell, "before": before, "after": after}

    def set_assist_option(self, on):
        """설정 ▸ 창 페이지의 Snap Assist 확인란(settings.chkSnapAssist)을 바꾼다 — 옵션은 settings.set 에 없어 설정 화면 경로로 바꾼다."""
        self.call("sys.open", view="Settings")
        time.sleep(0.6)
        self.call("settings.window", category="Window")
        time.sleep(0.6)
        self.call("ui.toggle", id="settings.chkSnapAssist", on=on)
        time.sleep(0.3)
        self.call("ui.click", id="settings.btnClose")
        time.sleep(0.4)
        return ((self.assist().get("options") or {}).get("enabled"))


def case_disabled(run):
    ids = run.open_mdi(3)
    eff = run.set_assist_option(False)
    try:
        f = run.flyout_pick(ids[0], "half", 0)
        s = run.wait_assist(False, tries=5)
        run.check("D1 SnapAssistEnabled=false → 배치는 되고 세션 미표시(skip=disabled)",
                  eff is False and f.get("done") and not s.get("isActive") and s.get("lastSkipReason") == "disabled",
                  {"enabled": eff, "flyout": f, "state": s.get("state"), "skip": s.get("lastSkipReason")})
    finally:
        back = run.set_assist_option(True)
        run.check("D1 옵션 원복(켬)", back is True, back)


def case_no_candidates(run):
    ids = run.open_mdi(1)
    f = run.flyout_pick(ids[0], "half", 0)
    s = run.wait_assist(False, tries=5)
    run.check("D2 후보 0개(문서 1개) → 세션 미표시(skip=no-candidates)",
              f.get("done") and not s.get("isActive") and s.get("lastSkipReason") == "no-candidates", {"flyout": f, "state": s.get("state")})


def case_max(run):
    ids = run.open_mdi(3)
    f = run.flyout_pick(ids[0], "max", 0)
    s = run.wait_assist(False, tries=5)
    run.check("D3 max 칸 → 세션 미표시(skip=max)", not s.get("isActive") and s.get("lastSkipReason") == "max", {"flyout": f, "state": s.get("state")})


def pick_and_find(run, ignore):
    """활성 칸에 후보 0번을 고르고 (결과, 움직인 문서 id, 놓인 칸)을 돌려준다."""
    before_s = run.assist()["parsed"]
    cell = before_s.get("activeCell")
    b0 = run.bounds()
    res = run.call("snapassist.pick", index=0) or {}
    time.sleep(0.5)
    mv = moved(b0, run.bounds(), ignore)
    return res, (mv[0] if len(mv) == 1 else None), cell, mv


def case_quad_flow(run):
    ids = run.open_mdi(4)
    placed = ids[0]
    f = run.flyout_pick(placed, "quad", 0)
    s = run.wait_assist(True)
    p = s["parsed"]
    run.check("D4 quad 0칸 배치 → 세션 표시(Mdi·quad·칸 4·filled [0]·활성 1·후보 ≥1)",
              s.get("isActive") and p.get("scope") == "Mdi" and p.get("layout") == "quad" and p.get("cells") == 4
              and p.get("filled") == [0] and p.get("activeCell") == 1 and (p.get("candidates") or 0) >= 1,
              {"flyout": f, "state": s.get("state")})
    if not s.get("isActive"):
        return
    placed_ids = [placed]
    res, doc1, cell1, mv = pick_and_find(run, placed_ids)
    p = parse_state(res.get("state"))
    run.check("D4 후보 고르기(snapassist.pick 0) → 그 창 1개만 움직이고 칸 1 채움·활성 다음 칸",
              res.get("done") and doc1 and 1 in (p.get("filled") or []) and p.get("activeCell") not in (0, 1), {"moved": mv, "state": res.get("state")})
    if doc1:
        placed_ids.append(doc1)
    zone = run.call("snapassist.zone", cell=3) or {}
    zp = parse_state(zone.get("state"))
    run.check("D4 칸 바꾸기(snapassist.zone 3) → 활성 칸 3", zone.get("done") and zp.get("activeCell") == 3, zone.get("state"))
    res, doc3, cell3, mv = pick_and_find(run, placed_ids)
    run.check("D4 칸 3 에 고르기 → 칸 3 채움", res.get("done") and doc3 and 3 in (parse_state(res.get("state")).get("filled") or []), {"moved": mv, "state": res.get("state")})
    if doc3:
        placed_ids.append(doc3)
    sk = run.call("snapassist.skip") or {}
    end = run.wait_assist(False, tries=10)
    run.check("D4 남은 칸 건너뛰기(snapassist.skip) → 세션 끝(남은 칸 없음)", not end.get("isActive") and end.get("lastEnd") in ("filled", "no-candidates", "cancel"),
              {"skip": sk.get("state"), "end": end.get("state"), "lastEnd": end.get("lastEnd")})
    for d, c in ((doc1, cell1), (doc3, 3)):
        if d is not None and c is not None:
            ok, det = run.same_cell(d, "quad", c)
            run.check("D4 고른 창이 그 칸 사각형에 놓임(같은 칸 재적용 bounds 불변 ±%.1f)" % TOL, ok, det)


def case_cancel_keeps(run):
    ids = run.open_mdi(4)
    placed = ids[0]
    run.flyout_pick(placed, "quad", 0)
    s = run.wait_assist(True)
    if not s.get("isActive"):
        run.check("D5 세션 표시", False, s.get("state"))
        return
    res, doc1, cell1, _ = pick_and_find(run, [placed])
    b_mid = run.bounds()
    end = run.call("snapassist.cancel") or {}
    time.sleep(0.4)
    b_end = run.bounds()
    run.check("D5 취소(Esc 와 같은 효과) → 세션 끝·lastEnd=cancel", not end.get("isActive") and run.assist().get("lastEnd") == "cancel", end.get("state"))
    run.check("D5 취소 뒤 되돌림 없음(고른 창·나머지 창 bounds 그대로)", not moved(b_mid, b_end), {"moved": moved(b_mid, b_end)})
    if doc1 is not None:
        ok, det = run.same_cell(doc1, "quad", cell1)
        run.check("D5 취소 전에 고른 창은 그 칸에 남음", ok, det)


def case_half(run):
    ids = run.open_mdi(2)
    run.flyout_pick(ids[0], "half", 0)
    s = run.wait_assist(True)
    p = s["parsed"]
    run.check("D6 half 0칸 → 세션 표시(활성 1·후보 1)", s.get("isActive") and p.get("activeCell") == 1 and p.get("candidates") == 1, s.get("state"))
    if not s.get("isActive"):
        return
    res, doc1, _, mv = pick_and_find(run, [ids[0]])
    end = run.wait_assist(False, tries=10)
    run.check("D6 마지막 칸 고르기 → 세션 끝(lastEnd=filled)", res.get("done") and doc1 == ids[1] and not end.get("isActive") and end.get("lastEnd") == "filled",
              {"moved": mv, "end": end.get("state")})


def case_sdi_bg(run):
    """SDI 실 창: 화면 밖 비전경에서는 호스트를 띄우지 않는다(skip=bg-mode). 팝업이 SDI 를 거부하면(sdi-off·os-flyout) 그 사유를 기록한다."""
    run.call("docs.close", all=True)
    time.sleep(0.5)
    for _ in range(3):
        run.call("docs.open", kind="grid", allowDuplicate=True, sdi=True)
        time.sleep(0.8)
    ids = [d.get("id") for d in run.call("docs.list") or []]
    f = run.flyout_pick(ids[0], "half", 0, kind="sdi") if ids else {"open": False}
    s = run.wait_assist(False, tries=5)
    run.check("D7 SDI(화면 밖 비전경) → 세션 미표시", not s.get("isActive"),
              {"flyout": f, "state": s.get("state"), "skip": s.get("lastSkipReason")})
    run.call("docs.redock", all=True)
    time.sleep(0.6)


def case_locked(run):
    """위치 고정 창은 후보에서 빠진다 — 잠금은 실제 단추와 같은 경로(chrome.lock)로 건다(ui.toggle 은 잠금이 걸리지 않는다)."""
    ids = run.open_mdi(4)
    lk = run.call("chrome.lock", doc=ids[1]) or {}
    try:
        run.flyout_pick(ids[0], "quad", 0)
        s = run.wait_assist(True)
        run.check("L1 위치 고정 창 1개(chrome.lock) → 후보 2개(4문서−배치1−고정1)",
                  lk.get("locked") is True and s.get("isActive") and s["parsed"].get("candidates") == 2,
                  {"lock": lk.get("locked"), "state": s.get("state")})
        run.call("snapassist.cancel")
        time.sleep(0.3)
    finally:
        un = run.call("chrome.lock", doc=ids[1]) or {}
        run.check("L1 위치 고정 해제(chrome.lock 다시)", un.get("locked") is False, un.get("locked"))


def case_thumbs(run):
    """썸네일: 후보마다 thumb=bitmap(새 표기 [i:bitmap]), 창 전체가 비율 그대로 담김, 후보끼리 다름, 이름표는 썸네일 칸 아래."""
    import hashlib
    import subprocess
    ids = run.open_mdi(3)
    run.call("docs.open", kind="grid", allowDuplicate=True)  # 다른 크기 창 하나 더(비율 확인)
    time.sleep(0.8)
    ids = [d.get("id") for d in run.call("docs.list") or []]
    run.call("placement.markPlaced", doc=ids[-1], x=420, y=60, w=480, h=200)
    time.sleep(0.5)
    run.flyout_pick(ids[0], "half", 0)
    s = run.wait_assist(True)
    sts = thumb_statuses(s.get("state"))
    run.check("T1 썸네일 기본 켬 → 후보마다 thumb=bitmap(표기 [i:bitmap])",
              s.get("isActive") and isinstance(sts, list) and len(sts) >= 2 and all(x == "bitmap" for x in sts) and (s.get("options") or {}).get("thumbnails") is True,
              {"state": s.get("state"), "thumbs": s.get("thumbs")})
    if not s.get("isActive"):
        return
    dump_dir = os.path.join(run.work, "thumbdump_%s" % time.strftime("%H%M%S"))
    os.makedirs(dump_dir, exist_ok=True)
    win_dir = subprocess.check_output(["wslpath", "-w", dump_dir], text=True).strip()
    dump = run.call("snapassist.thumbdump", dir=win_dir) or {}
    lines = dump.get("lines") or []
    parsed = [parse_dump(x) for x in lines]
    run.check("T2 후보마다 창 전체(불투명 범위 ≥90%)를 창 비율 그대로(±3%) 줄임",
              len(parsed) >= 2 and all(dump_ok(d) for d in parsed), {"lines": lines})
    hashes = []
    for i in range(len(lines)):
        p = os.path.join(dump_dir, "thumb_%d_thumb.png" % i)
        hashes.append(hashlib.md5(open(p, "rb").read()).hexdigest() if os.path.exists(p) else None)
    # 같은 크기·같은 내용 시험 창은 좁아서 제목이 '그…' 로 잘려 비트맵이 같을 수 있다(2026-11-12 실측) — 카드는 비트맵 또는 이름표로 구분되면 된다.
    # 크기가 다른 창(마지막에 연 480x200)은 비트맵 자체가 달라야 한다.
    labels = [(t.get("title"), (run.call("ui.find", id="snap.assist.card.%d.label" % i) or {}).get("bounds") is not None) for i, t in enumerate(s.get("thumbs") or [])]
    keys = [(hashes[i] if i < len(hashes) else None, labels[i][0] if i < len(labels) else None) for i in range(max(len(hashes), len(labels)))]
    run.check("T3 후보 카드끼리 구분됨(비트맵 md5 또는 이름표 제목이 다름, 크기가 다른 창은 비트맵 자체가 다름)",
              len(hashes) >= 2 and None not in hashes and len(set(keys)) == len(keys) and len(set(hashes)) >= 2 and all(ok for _, ok in labels),
              {"dir": dump_dir, "md5": hashes, "labels": labels})
    th = (run.call("ui.find", id="snap.assist.card.0.thumb") or {}).get("bounds")
    lb = (run.call("ui.find", id="snap.assist.card.0.label") or {}).get("bounds")
    shot = (run.call("screenshot", window=0, path=subprocess.check_output(["wslpath", "-w", os.path.join(dump_dir, "cards.png")], text=True).strip()) or {}).get("path")
    run.check("T4 이름표가 썸네일 칸 아래(이름표 위 ≥ 썸네일 칸 아래)",
              bool(th and lb) and lb["y"] >= th["y"] + th["h"] - TOL, {"thumb": th, "label": lb, "shot": shot})
    run.call("snapassist.cancel")
    time.sleep(0.3)


def case_fallback(run):
    """대체 카드: 썸네일을 못 찍으면(진단 강제 실패) thumb=fallback:<사유>, 카드는 그대로 보이고 고를 수 있다."""
    import subprocess
    run.call("snapassist.thumbfail", reason="diag")
    try:
        ids = run.open_mdi(3)
        run.flyout_pick(ids[0], "half", 0)
        s = run.wait_assist(True)
        sts = thumb_statuses(s.get("state"))
        run.check("F1 썸네일 실패 → 후보마다 thumb=fallback:diag",
                  s.get("isActive") and isinstance(sts, list) and sts and all(x == "fallback:diag" for x in sts), s.get("state"))
        th = (run.call("ui.find", id="snap.assist.card.0.thumb") or {}).get("bounds")
        lb = (run.call("ui.find", id="snap.assist.card.0.label") or {}).get("bounds")
        shot_path = os.path.join(run.work, "fallback_cards_%s.png" % time.strftime("%H%M%S"))
        shot = (run.call("screenshot", window=0, path=subprocess.check_output(["wslpath", "-w", shot_path], text=True).strip()) or {}).get("path")
        run.check("F2 대체 카드 표시(그림 칸 + 아래 이름표)", bool(th and lb) and lb["y"] >= th["y"] + th["h"] - TOL, {"thumb": th, "label": lb, "shot": shot})
        note = run.call("ui.find", id="snap.assist.card.0.note", props="Text") or {}
        text = ((note.get("props") or {}).get("Text") or "").strip()
        run.check("F2 대체 카드 보조 표기(미리보기 없음 등) 글자 있음", bool(text) and note.get("isVisible") is not False, {"note": text, "isVisible": note.get("isVisible")})
        if s.get("isActive"):
            res = run.call("snapassist.pick", index=0) or {}
            run.check("F3 대체 카드도 고르기 동작", res.get("done") is True, res.get("state"))
        run.call("snapassist.cancel")
    finally:
        off = run.call("snapassist.thumbfail", reason="") or {}
        run.check("F4 진단 강제 실패 해제", off.get("thumbFail") is None, off)


# 글자 비침 판정(11 문서 §3.9 '글자 비침 판정') — 순수 계산(자가시험 대상) ---------------
BLEED_DIST = 8          # 기대 토큰 색과 RGB(유클리드) 거리가 이보다 크면 비침 화소
BLEED_LIMIT = 0.001     # 합격 비율
SHADOW_BAND = 12        # 카드 BoxShadow(0 2 8 0 #30000000) 대역(DIP) — 카드 바깥 이 거리 안은 그림자 허용 범위로 본다
SHADOW_ALPHA = 0.40     # 그림자 최대 어둡게(#30=0.19, 이웃 카드 그림자 겹침 2배 여유)
PICKER_MARGIN = 12      # SnapAssistLayer.PickerMargin — 카드 목록 사각형 = 활성 칸 - 12
ZONE_EDGE = 2           # 칸 외곽선(1 DIP)·반 화소 경계를 뺀다
ZONE_CORNER = 10        # 칸 모서리(반지름 8 + 2) 사각형은 판이 덮지 않아 뺀다(C1 은 따로 센다)
CARD_CORNER = 8         # 카드 모서리(반지름 6 + 테두리 2) 사각형 — 초점 테두리 곡선 대역


def parse_color(text):
    """'#AARRGGBB'·'#RRGGBB'(ui.find props 문자열) → (r, g, b). 해석 못 하면 None."""
    m = re.search(r"#([0-9A-Fa-f]{6,8})\b", text or "")
    if not m:
        try:  # Avalonia 는 알려진 색을 이름(White 등)으로 돌려준다
            from PIL import ImageColor
            return tuple(ImageColor.getrgb((text or "").strip())[:3])
        except (ValueError, ImportError):
            return None
    h = m.group(1)[-6:]
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def _dist(a, b):
    return ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5


def _shadow_dist(px, surface, alpha=SHADOW_ALPHA):
    """px 가 surface 를 검정으로 0~alpha 만큼 어둡게 한 선분에서 얼마나 떨어졌는가(그림자 허용 범위)."""
    dark = tuple(c * (1 - alpha) for c in surface)
    seg = tuple(b - a for a, b in zip(surface, dark))
    den = sum(v * v for v in seg)
    t = 0.0 if den == 0 else max(0.0, min(1.0, sum((p - a) * v for p, a, v in zip(px, surface, seg)) / den))
    return _dist(px, tuple(a + v * t for a, v in zip(surface, seg)))  # 선분까지 연속 거리


def _inside(x, y, r, pad=0.0):
    return r["x"] - pad <= x < r["x"] + r["w"] + pad and r["y"] - pad <= y < r["y"] + r["h"] + pad


def bleed_classify(x, y, picker, cards):
    """DIP 좌표 한 점의 측정 분류: None(측정 제외)·'zone'(카드 밖 판)·'shadow'(카드 그림자 대역)·'pad'(카드 패딩)·'corner'(칸 모서리, 비율 밖 진단)."""
    zone = {"x": picker["x"] - PICKER_MARGIN, "y": picker["y"] - PICKER_MARGIN, "w": picker["w"] + PICKER_MARGIN * 2, "h": picker["h"] + PICKER_MARGIN * 2}
    if not _inside(x, y, zone, -ZONE_EDGE):
        return None
    cx = min(x - zone["x"], zone["x"] + zone["w"] - x)
    cy = min(y - zone["y"], zone["y"] + zone["h"] - y)
    if cx < ZONE_CORNER and cy < ZONE_CORNER:
        return "corner"
    for c in cards:
        card, thumb, label = c.get("card"), c.get("thumb"), c.get("label")
        if thumb and _inside(x, y, thumb, 1) or label and _inside(x, y, label, 2):
            return None  # 썸네일·이름표 글자 영역은 뺀다
        if card and _inside(x, y, card):
            kx = min(x - card["x"], card["x"] + card["w"] - x)
            ky = min(y - card["y"], card["y"] + card["h"] - y)
            if kx < CARD_CORNER and ky < CARD_CORNER:
                return None  # 카드 모서리(반지름 6) 안쪽은 초점 테두리 곡선이 번져 들어온다
            return "pad" if _inside(x, y, card, -2.5) else None  # 카드 테두리 2(초점 강조색) 대역은 뺀다
    for c in cards:
        card = c.get("card")
        if card and _inside(x, y, {"x": card["x"], "y": card["y"] + 2, "w": card["w"], "h": card["h"]}, SHADOW_BAND):
            return "shadow"
    return "zone"


def bleed_measure(img, scale, picker, cards, surface, dist=BLEED_DIST):
    """캡처(PIL RGB)에서 측정 영역 화소 중 기대 색(surface)과 거리 > dist 인 비율. 그림자 대역은 그림자 허용 선분까지 기대 색으로 본다.
    반환 {ratio, bad, total, by_area:{zone,shadow,pad:[bad,total]}, corner:[bad,total], bbox, bad_points(최대 20)}."""
    px = img.load()
    x0, y0 = int((picker["x"] - PICKER_MARGIN) * scale), int((picker["y"] - PICKER_MARGIN) * scale)
    x1 = min(img.width, int((picker["x"] + picker["w"] + PICKER_MARGIN) * scale) + 1)
    y1 = min(img.height, int((picker["y"] + picker["h"] + PICKER_MARGIN) * scale) + 1)
    areas = {"zone": [0, 0], "shadow": [0, 0], "pad": [0, 0], "corner": [0, 0]}
    pts, bbox = [], None
    for yy in range(max(0, y0), y1):
        for xx in range(max(0, x0), x1):
            kind = bleed_classify((xx + 0.5) / scale, (yy + 0.5) / scale, picker, cards)
            if kind is None:
                continue
            p = px[xx, yy][:3]
            d = _shadow_dist(p, surface) if kind == "shadow" else _dist(p, surface)
            areas[kind][1] += 1
            if d > dist:
                areas[kind][0] += 1
                if kind != "corner":
                    bbox = (min(bbox[0], xx), min(bbox[1], yy), max(bbox[2], xx), max(bbox[3], yy)) if bbox else (xx, yy, xx, yy)
                    if sum(1 for q in pts if q["area"] == kind) < 8:  # 영역마다 8개까지(원인 위치 판정용)
                        pts.append({"px": [xx, yy], "area": kind, "rgb": "%02X%02X%02X" % p, "dist": round(d, 1)})
    bad = sum(v[0] for k, v in areas.items() if k != "corner")
    total = sum(v[1] for k, v in areas.items() if k != "corner")
    return {"ratio": round(bad / total, 6) if total else None, "bad": bad, "total": total,
            "by_area": {k: v for k, v in areas.items() if k != "corner"}, "corner": areas["corner"], "bbox": bbox, "bad_points": pts}


def bleed_cards(run):
    """보이는 카드마다 카드·썸네일·이름표 사각형(DIP, 창 기준)."""
    out = []
    for i in range(16):
        card = (run.call("ui.find", id="snap.assist.card.%d" % i) or {}).get("bounds")
        if not card:
            break
        out.append({"card": card, "thumb": (run.call("ui.find", id="snap.assist.card.%d.thumb" % i) or {}).get("bounds"),
                    "label": (run.call("ui.find", id="snap.assist.card.%d.label" % i) or {}).get("bounds")})
    return out


BLEED_THEMES = ("pd-light", "pd-dark", "pd-contrast")


def case_backdrop(run):
    """남은 영역 뒤판: 실효 alpha ≥ 0.99 + 글자 비침 비율 ≤ 0.001(뒤 창 = 글자 많은 그리드 2개를 영역 전체에 깐다, 11 문서 §3.9)."""
    import subprocess
    from PIL import Image
    ids = run.open_mdi(3)
    for d in ids[1:]:  # 뒤 창: 그리드 문서를 MDI 영역 전체(max 칸)에 깔아 남은 칸 판 뒤가 글자로 차게 한다
        run.call("snapcell.apply", kind="mdi", doc=d, layout="max", cell=0)
        time.sleep(0.4)
    run.bleed = {}
    for theme in getattr(run, "bleed_themes", BLEED_THEMES):
        run.call("theme.select", id=theme)
        time.sleep(0.8)
        run.flyout_pick(ids[0], "half", 0)
        s = run.wait_assist(True)
        time.sleep(0.6)  # 썸네일·카드 배치가 그려질 때까지(판정은 화소, 시간 판정 아님)
        m = re.search(r"\bbackdrop=([\d.]+)/(\w+)", s.get("state") or "")
        alpha = float(m.group(1)) if m else None
        shot_path = os.path.join(run.work, "backdrop_%s_%s.png" % (theme, time.strftime("%H%M%S")))
        shot = (run.call("screenshot", window=0, path=subprocess.check_output(["wslpath", "-w", shot_path], text=True).strip()) or {}).get("path")
        run.check("K1 남은 영역 뒤판 불투명(%s) 실효 alpha ≥ 0.99" % theme, s.get("isActive") and alpha is not None and alpha >= 0.99,
                  {"backdrop": m.group(0) if m else None, "shot": shot})
        res = {"shot": shot_path, "backdrop": m.group(0) if m else None}
        try:
            picker = (run.call("ui.find", id="snap.assist.zone.%s" % s["parsed"].get("activeCell")) or {}).get("bounds")
            cards = bleed_cards(run)
            surface = parse_color(((run.call("ui.find", id="snap.assist.card.0", props="Background") or {}).get("props") or {}).get("Background"))
            title = (run.call("ui.find", id="shell.titleBar") or {}).get("bounds") or {}
            for _ in range(20):
                if os.path.exists(shot_path) and os.path.getsize(shot_path) > 0:
                    break
                time.sleep(0.2)
            img = Image.open(shot_path).convert("RGB")
            scale = img.width / (title.get("w") or img.width)
            res.update({"picker": picker, "cards": len(cards), "surface": "%02X%02X%02X" % surface if surface else None, "scale": round(scale, 3)})
            if picker and cards and surface:
                res.update(bleed_measure(img, scale, picker, cards, surface))
        except Exception as e:  # noqa: BLE001 — 측정 실패는 FAIL 로 남긴다
            res["error"] = repr(e)[:300]
        run.bleed[theme] = res
        run.check("K2 글자 비침 비율 ≤ %.3f(%s, 거리 > %d)" % (BLEED_LIMIT, theme, BLEED_DIST), res.get("ratio") is not None and res["ratio"] <= BLEED_LIMIT,
                  {k: res.get(k) for k in ("ratio", "bad", "total", "by_area", "corner", "bbox", "surface", "scale", "shot", "error")})
        run.call("snapassist.cancel")
        time.sleep(0.4)
    run.call("theme.select", id="pd-light")


CASES = (case_disabled, case_no_candidates, case_max, case_quad_flow, case_cancel_keeps, case_half, case_locked, case_thumbs, case_fallback, case_backdrop, case_sdi_bg)


def attach(args):
    """이미 떠 있는 격리 인스턴스에 붙는다. 사용자 연결 정보·사용자 포트는 거부한다."""
    info = os.path.abspath(args.info)
    low = info.replace("\\", "/").lower()
    if "appdata/local/xkxk/frameworks/ui" in low or not os.path.exists(info):
        sys.exit("거부: 사용자 폴더 연결 정보이거나 파일이 없다 — 격리 data 폴더의 remote-control.json 만 쓴다: %s" % info)
    os.environ["PD_REMOTE_INFO"] = info
    from retest_remote import Remote
    remote = Remote()
    if args.port:
        remote.info["port"] = args.port
    if int(remote.info.get("port") or 0) in USER_PORTS:
        sys.exit("거부: 사용자 포트 %s" % remote.info.get("port"))
    st = remote.call("status") or {}
    if not st.get("pid"):
        sys.exit("연결 실패: status 응답 없음")
    return remote, st.get("pid"), info, None


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--run-dir")
    p.add_argument("--info", help="이미 띄운 격리 인스턴스의 remote-control.json(사용자 폴더 불가)")
    p.add_argument("--port", type=int, help="연결 정보의 포트를 덮어쓴다(예 5961, 5180·5220 거부)")
    p.add_argument("--work", help="결과 폴더(세션 work 폴더 등 — 커밋 대상 폴더 금지)")
    p.add_argument("--label", default="snap_assist")
    p.add_argument("--keep", action="store_true", help="--run-dir 모드에서 끝난 뒤 앱을 남긴다")
    p.add_argument("--self-test", action="store_true")
    p.add_argument("--cases", help="쉼표로 고른 시나리오만(예 backdrop — 글자 비침 대조군·현행 비교). 생략하면 전부")
    p.add_argument("--themes", help="case_backdrop 테마(쉼표, 기본 pd-light,pd-dark,pd-contrast)")
    args = p.parse_args()
    if args.self_test:
        return self_test()
    if not args.work or not (args.run_dir or args.info):
        print("거부: --work 와 (--run-dir 또는 --info) 가 필요하다")
        return 3
    work = os.path.abspath(os.path.join(args.work, args.label))
    norm = work.replace("\\", "/")
    if "logs/ui28" in norm or "/frameworks/ui/test/" in norm + "/":
        print("거부: 결과는 커밋 폴더(frameworks/ui/test)·logs/ui28 밖에 쓴다: %s" % work)
        return 3
    os.makedirs(work, exist_ok=True)
    raw = {"label": args.label, "mode": "launch" if args.run_dir else "attach", "foreground_needed": FOREGROUND_NEEDED, "checks": []}
    from offscreen_guard import GuardBreach, OffscreenGuard
    run, breach, pid, info, log_path = None, None, None, None, None
    try:
        if args.run_dir:
            import mdi_drag_app as app
            pid, info, log_path = app.launch(args.run_dir, work)
            from retest_remote import Remote
            remote = Remote()
        else:
            remote, pid, info, log_path = attach(args)
        print("연결 pid=%s info=%s" % (pid, info), flush=True)
        run = Run(remote, pid)
        run.work = work
        if args.themes:
            run.bleed_themes = tuple(t for t in args.themes.split(",") if t)
        picked = set(x.strip() for x in (args.cases or "").split(",") if x.strip())
        run.guard = OffscreenGuard(pid)
        run.guard.start()
        try:
            for case in [c for c in CASES if not picked or c.__name__[len("case_"):] in picked]:
                print("[%s]" % case.__name__, flush=True)
                try:
                    case(run)
                except GuardBreach:
                    raise
                except Exception as e:  # noqa: BLE001 — 한 시나리오가 죽어도 나머지는 본다
                    run.check("예외 " + case.__name__, False, repr(e)[:300])
                    run.call("snapassist.cancel")
        finally:
            run.guard.stop()
            raw["guard"] = run.guard.summary()
            run.check("화면 밖 유지(창 노출·전경 없음)", not raw["guard"]["breaches"] and raw["guard"]["stoppedCleanly"], raw["guard"]["breaches"])
            if args.run_dir:
                import mdi_drag_app as app
                bad = app.log_violation(log_path)
                run.check("단일 인스턴스 흔적 없음", bad is None, bad)
                if not args.keep:
                    raw["stop"] = app.stop(pid, app.win(os.path.dirname(info)))
    except GuardBreach as e:
        breach = str(e)
        print("창 노출·전경 감지 — 중단: %s" % breach, flush=True)
    except SystemExit as e:
        print("기동·연결 거부/실패: %s" % e, flush=True)
        return 2
    finally:
        raw["breach"] = breach
        if run:
            raw["checks"], raw["calls"] = run.checks, run.calls
            raw["bleed"] = getattr(run, "bleed", None)
        out = os.path.join(work, "snap_assist_remote.json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump(raw, f, ensure_ascii=False, indent=1, default=str)
        print(out, flush=True)
    failed = [c["name"] for c in raw["checks"] if not c["pass"]]
    print("검사 %d건, 실패 %d건 %s" % (len(raw["checks"]), len(failed), failed))
    print("포그라운드 필요(원격 미판정) %d건: %s" % (len(FOREGROUND_NEEDED), [x["item"] for x in FOREGROUND_NEEDED]))
    if breach:
        return 3
    return 1 if failed else 0


def bleed_self_test():
    """합성 장면: 표면색 판 + 카드 1장. ① 깨끗하면 0 ② 판에 0.96 합성 글자(4% 비침) 줄을 그리면 > 0.001 ③ 그림자 대역의 어둡게는 비침 아님 ④ 썸네일 안 글자는 제외."""
    from PIL import Image, ImageDraw
    fails = []
    surf = (250, 250, 250)
    picker = {"x": 12, "y": 12, "w": 276, "h": 176}
    cards = [{"card": {"x": 100, "y": 60, "w": 100, "h": 80}, "thumb": {"x": 108, "y": 68, "w": 84, "h": 50}, "label": {"x": 108, "y": 122, "w": 84, "h": 12}}]
    base = Image.new("RGB", (300, 200), surf)
    d = ImageDraw.Draw(base)
    d.rectangle([108, 68, 191, 117], fill=(20, 20, 20))  # 썸네일 안 어두운 내용(제외 대상)
    r0 = bleed_measure(base, 1.0, picker, cards, surf)
    if r0["ratio"] != 0:
        fails.append("깨끗한 판 비율 %s" % r0["ratio"])
    sh = base.copy()
    ImageDraw.Draw(sh).rectangle([90, 52, 99, 150], fill=tuple(int(c * 0.85) for c in surf))  # 카드 왼쪽 그림자
    if bleed_measure(sh, 1.0, picker, cards, surf)["ratio"] != 0:
        fails.append("그림자 대역 오탐")
    bl = base.copy()
    text = tuple(int(c * 0.96 + 20 * 0.04) for c in surf)  # 0.96 판 뒤 검은 글자 → 4% 비침
    dd = ImageDraw.Draw(bl)
    for y in range(20, 50, 6):
        dd.line([20, y, 260, y], fill=text)
    r2 = bleed_measure(bl, 1.0, picker, cards, surf)
    if not (r2["ratio"] and r2["ratio"] > BLEED_LIMIT and r2["by_area"]["zone"][0] > 0):
        fails.append("4%% 비침 미검출 %s" % r2.get("ratio"))
    if parse_color("#FF1E1E1E") != (30, 30, 30) or parse_color("#123456") != (0x12, 0x34, 0x56) or parse_color(None) is not None \
            or parse_color("White") != (255, 255, 255):
        fails.append("색 문자열 해석")
    return fails


def self_test():
    fails = []
    s = parse_state("active=True scope=Mdi layout=quad cells=4 filled=[0,1] skipped=[] activeCell=2 candidates=2 excludedSmall=0 focus=0 thumb=on lastEnd=")
    if s != {"active": True, "scope": "Mdi", "layout": "quad", "cells": 4, "filled": [0, 1], "skipped": [], "activeCell": 2,
             "candidates": 2, "excludedSmall": 0, "focus": 0, "thumb": "on", "lastEnd": ""}:
        fails.append("상태 1줄 해석 %s" % s)
    if parse_state("active=False lastEnd=esc lastSkip=no-candidates") != {"active": False, "lastEnd": "esc", "lastSkip": "no-candidates"}:
        fails.append("비활성 상태 해석")
    if parse_state(None) != {}:
        fails.append("빈 상태")
    r = {"x": 1, "y": 2, "w": 3, "h": 4}
    if not same_rect(r, {"x": 1.4, "y": 2, "w": 3, "h": 4}) or same_rect(r, {"x": 2, "y": 2, "w": 3, "h": 4}) or same_rect(None, r):
        fails.append("사각형 비교")
    if moved({"a": r, "b": r}, {"a": r, "b": {"x": 9, "y": 2, "w": 3, "h": 4}}) != ["b"] or moved({"a": r}, {"a": {"x": 9, "y": 2, "w": 3, "h": 4}}, ignore=("a",)):
        fails.append("움직인 창 찾기")
    if thumb_statuses("active=True thumb=[0:bitmap,1:fallback:diag] lastEnd=") != ["bitmap", "fallback:diag"] \
            or thumb_statuses("active=True thumb=off lastEnd=") != "off" or thumb_statuses("active=False") is not None:
        fails.append("thumb 필드 해석")
    good = parse_dump("0 title=그리드 1 src=MdiDocumentWindow bounds=30,30,320x240 render=1 scale=0.5 legacy=160x120 legacyContent=0,0,80x60 "
                      "full=320x240 fullContent=0,0,320x240 thumb=160x120 saved=x")
    bad = parse_dump("0 title=g src=X bounds=0,0,320x240 full=320x240 fullContent=0,0,80x60 thumb=160x120")
    if good.get("legacyContent") != (0, 0, 80, 60) or not dump_ok(good) or dump_ok(bad) or dump_ok({}):
        fails.append("thumbdump 줄 해석·판정 %s" % good)
    fails += bleed_self_test()
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# 분리 창 생성 위치 측정(항목 5) — 메인을 왼쪽 모니터에 두고 MDI 최대화 문서·새 문서를 SDI 로 띄웠을 때 SDI 가 메인 모니터에 뜨고 그 자리에서 최대화되는지 잰다
"""
사용: python3 -B retest_maximize_spawn.py --conf run/<복사본> --out-dir logs/<에이전트>/<회차> --onscreen-approved '<누가·언제>'
- --onscreen-approved 와 --user-away 가 모두 없으면 거부한다(rc=3). 수정 전 판본(run/odev-fix-maximize_before)으로 돌리면 SDI 가 주 모니터에 떠야 재현이다.
- 일반 기동(--bg-pos off)이다. 비전경 기동은 SDI 표시 전 좌표를 화면 밖 원점으로 바꾸고 배치 경로를 끄므로(BackgroundMode) 이 경로를 잴 수 없다.
- 일반 기동은 창이 활성화될 수 있어 사용자가 PC 를 비운다고 명시한 때에만 돌린다. 자기 앱이 전경이 되면 기본은 중단(rc=4)이고, --on-foreground record 면 시각을 기록하고 계속한다. 입력 API 는 쓰지 않는다.
종료 코드: 0=통과, 1=실패, 2=기동·연결 실패, 3=거부, 4=전경 게이트 위반.
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_maximize_monitor as base  # noqa: E402


def one(s, order, title, make_doc):
    """make_doc 이 SDI 를 만든다. 메인과 같은 모니터에 떠야 하고, 그 자리에서 최대화·복원돼야 한다."""
    main_win = s.win(s.main)
    known = {w["hwnd"] for w in s.windows()}
    doc = make_doc(title)
    time.sleep(1.5)
    added = [w for w in s.app_windows() if w["hwnd"] not in known]
    if not added:
        return {"case": title, "pass": False, "error": "새 SDI 창을 찾지 못함"}
    s.hide_all()
    spawned = s.win(added[0]["hwnd"])
    s.set_state("sdi", doc, "maximized")
    maxed = s.win(spawned["hwnd"])
    layout_while_max = s.r.call("layout.get")  # D: 최대화 중 저장값이 최대화 사각형이면 재기동 뒤 복원 크기를 잃는다
    s.set_state("sdi", doc, "normal")
    back = s.win(spawned["hwnd"])
    s.r.call("docs.close", doc=doc)
    time.sleep(0.5)
    row = {"case": title, "doc": doc, "mainMonitor": main_win["monitor"], "spawnRect": spawned["rect"], "spawnMonitor": spawned["monitor"],
           "maxMonitor": maxed["monitor"], "maxFrame": maxed["frame"], "zoomed": maxed["zoomed"], "restored": back["rect"], "layoutWhileMax": layout_while_max}
    row["pass"] = spawned["monitor"] == main_win["monitor"] and maxed["monitor"] == main_win["monitor"] and maxed["zoomed"] and back["rect"] == spawned["rect"]
    print(title, {k: row[k] for k in ("mainMonitor", "spawnMonitor", "maxMonitor", "pass")}, flush=True)
    return row


def run(s, order, out):
    s.main = max(s.app_windows(), key=lambda w: (w["rect"]["r"] - w["rect"]["l"]) * (w["rect"]["b"] - w["rect"]["t"]))["hwnd"]
    s.hide_all()
    s.place(s.main, order[0]["work"]["l"] + 100, order[0]["work"]["t"] + 100)

    def doc_id(res):
        return (res[0] if isinstance(res, list) else res).get("id")

    def from_maximized_mdi(title):
        doc = doc_id(s.r.call("docs.open", kind="form", title=title, allowDuplicate=True))
        s.r.call("docs.state", doc=doc, state="maximized")
        time.sleep(0.5)
        s.r.call("docs.sdi", doc=doc, on=True)
        return doc

    def new_sdi(title):
        return doc_id(s.r.call("docs.open", kind="form", title=title, sdi=True, allowDuplicate=True))

    out["rows"] = [one(s, order, "MAXIMIZED_MDI_TO_SDI", from_maximized_mdi), one(s, order, "NEW_DOC_SDI", new_sdi)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--conf", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--onscreen-approved")
    ap.add_argument("--user-away", help="사용자가 PC 를 비운다는 근거(누가·언제)")
    ap.add_argument("--on-foreground", choices=("stop", "record"), default="stop")
    a = ap.parse_args()
    if not a.onscreen_approved or len(a.onscreen_approved) < 10 or not a.user_away or len(a.user_away) < 10:
        print("거부: 일반 기동 측정이라 창이 사용자 모니터를 차지하고 전경을 가져갈 수 있다. --onscreen-approved·--user-away '<누가·언제>' 를 모두 준다(각 10자 이상).")
        return 3
    out_dir = os.path.abspath(a.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    mons = base.wp(0, ["monitors"])[0]["monitors"]
    order = sorted(mons, key=lambda m: m["bounds"]["l"])
    out = {"monitors": mons, "conf": a.conf, "userFilesBefore": base.user_files_md5()}
    if base.launch(a.conf, out_dir, mons, a.onscreen_approved, pos="off") != 0:
        return 2
    from retest_remote import Remote
    remote = Remote()
    pid = (remote.call("status") or {}).get("pid")
    if not pid:
        return 2
    exe = subprocess.run(["powershell.exe", "-NoProfile", "-Command", "(Get-Process -Id %d).Path" % pid], capture_output=True, text=True).stdout.strip()
    if a.conf.replace("/", "\\") not in exe:
        print("자기 복사본이 아님 — 조작 거부:", pid, exe)
        return 3
    s = base.Session(pid, remote, mons)
    s.allow_foreground = a.on_foreground == "record"
    code = 0
    try:
        run(s, order, out)
    except base.ForegroundViolation as ex:
        print("전경 게이트 위반 — 중단:", ex)
        out["foregroundViolation"] = str(ex)
        code = 4
    finally:
        remote.call("app.shutdown")
        time.sleep(2)
        out["pid"], out["userFilesAfter"], out["foregroundHits"] = pid, base.user_files_md5(), s.foreground_hits
        with open(os.path.join(out_dir, "spawn.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    return code or (1 if not all(r["pass"] for r in out.get("rows", [])) else 0)


if __name__ == "__main__":
    sys.exit(main())

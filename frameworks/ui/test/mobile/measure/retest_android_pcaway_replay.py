#!/usr/bin/env python3
# Fold5 스플래시 터치 재전송 대상 측정(K7) — 게이트 구간에 설정 버튼을 눌러 재전송(sent) 시행마다 설정이 열렸는지, 다른 컨트롤이 열렸는지 값만 모은다. 원격 없이 실터치·uiautomator·logcat 만 쓴다
"""
사용: python3 -B retest_android_pcaway_replay.py --apk <Release APK> --out <폴더> --mode open|closed [--sent 20] [--max-runs 80] [--docs 5] [--fresh]
      [--delays 875,900,950,975 --delays-basis '<gate open 실측 5회와 중앙값>'] [--args-only]
--delays 는 측정 조건이다(판정 기준 아님). 판본·상태마다 측정 전에 gate open 을 5회 재 중앙값 기준으로 정하고, 그 근거를 --delays-basis 로 함께 준다(없으면 거부). 측정 도중에는 바꾸지 않는다.
--delays 를 주지 않으면 아래 DELAYS 상수를 쓴다. --args-only 는 기기 없이 인자 해석 결과만 출력하고 끝낸다.
결과: <out>/replay_<mode>.json. 시행마다 <out>/replay_<mode>.partial.json 에 그때까지의 행을 덮어써 중단돼도 시행 줄(logcat [startup] 포함)이 남는다. 중간 파일은 판정에 쓰지 않고 정상 종료 때 지운다. 판정은 test/mobile/judge/replay_target_judge.py 가 한다.
시행 전 pm clear 로 저장 레이아웃을 지우고 --docs 개수만큼 문서를 실터치로 열어 저장 상태를 만든다(기본 5, 불발 3/15 재현 조건이 문서 복원 상태였다). 시행은 콜드 기동 → 지연 뒤 대상 탭 → 4초 뒤 덤프다.
대상: 접힘은 설정 탭(shell.navTab.Settings), 펼침은 시스템(Y) 상위 메뉴(shell.mnuSys — 도구모음 줄이 없어 설정 버튼은 없다). 펼침 성공은 시스템 메뉴 항목(shell.mnuSys*)이 보이는 것이고, 파일 메뉴 같은 다른 메뉴가 열리면 오탭(mnu_extra>0)이다.
값: nodes=대상이 열려 생긴 노드 수(접힘 settings.*, 펼침 shell.mnuSys* 항목), mnu_extra=시스템 메뉴 항목을 뺀 셸 메뉴 식별자 수의 기준 대비 증가분, drawer=서랍 노드 수, popups=OverlayPopupHost 수.
덤프 재시도: nodes 가 3 미만이면 간격을 두고 최대 3번 다시 덤프한다(dump_tries). 유실 분류: 행마다 loss={cls,basis}(before_window·design_discard·other)와 문서 loss_summary 를 남긴다.
지연은 고정이다. 자동으로 옮기면 1000ms 부근으로 밀려 불발이 나오는 이른 탭(수정 전 buffered 846~885ms 등)을 놓친다(sent 20 중 0 실측). 기기 설정 원복은 호출자 몫이다.
"""
import argparse
import hashlib
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "desktop", "measure"))
import retest_android_pcaway_lib as L  # noqa: E402
import retest_android_pcaway_splash as S  # noqa: E402

DELAYS = {"open": (875, 900, 950, 975), "closed": (995, 1020, 1070, 1095)}


def sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def on_top_stable():
    """전면 읽기는 부하 때 빈 값이 나온 실측이 있다(시트가 열린 캡처인데 거짓). 세 번 모두 아닐 때만 이탈로 본다."""
    for _i in range(3):
        if L.app_on_top():
            return True
        time.sleep(0.7)
    return False


def count_mnu(xml):
    """셸 메뉴 식별자 수. 시스템(Y) 메뉴 항목(shell.mnuSys*)은 펼침 대상이 열린 결과라 세지 않는다. 파일 메뉴 같은 다른 메뉴가 열리면 늘어난다."""
    return len([m for m in re.findall(r'resource-id="(shell\.mnu[^"]*)"', xml) if not m.startswith("shell.mnuSys")])


def dump_state(c, name, mode, base_mnu):
    """덤프 후 상태를 읽는다. 대상 노드가 3 미만이면 uiautomator 미검출 오탐 대비로 간격을 두고 최대 S.DUMP_TRIES 번 다시 덤프한다."""
    st = None
    for k in range(S.DUMP_TRIES):
        xml = c.dump(name if k == 0 else "%s_r%d" % (name, k))
        st = read_state(c, xml, base_mnu, mode)
        if st["nodes"] >= 3:
            break
        if k < S.DUMP_TRIES - 1:
            time.sleep(0.8)
    st["dump_tries"] = k + 1
    return st


def read_state(c, xml, base_mnu, mode="closed"):
    ids = c.ids(xml)
    return {
        "nodes": S.target_nodes(ids, mode),
        "mnu_extra": max(0, count_mnu(xml) - base_mnu),
        "drawer": len([i for i in ids if i.startswith("shell.navDrawer.")]),
        "popups": len(re.findall(r'class="[^"]*OverlayPopupHost"', xml)),
    }


def partial_path(a):
    return os.path.join(a.out, "replay_%s.partial.json" % a.mode)


def save_partial(a, doc):
    """시행마다 지금까지의 행을 중간 파일에 덮어쓴다(중단돼도 시행 줄이 남게). 판정 입력인 replay_<mode>.json 과 이름을 나눠 미완성 파일이 판정에 쓰이지 않게 한다."""
    doc = dict(doc, complete=False, note="중간 저장 — 판정에 쓰지 않는다. 정상 종료 시 replay_%s.json 을 쓰고 이 파일은 지운다" % a.mode)
    path = partial_path(a)
    with open(path + ".tmp", "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
    os.replace(path + ".tmp", path)


def resolve_delays(p, a):
    """(지연 4개, 결과에 남길 출처 정보). --delays 를 주면 근거가 있어야 받는다."""
    if not a.delays:
        return DELAYS[a.mode], {"delays_source": "default", "delays_basis": None}
    try:
        delays = tuple(int(x) for x in a.delays.split(","))
    except ValueError:
        p.error("--delays 는 쉼표로 구분한 정수 ms 다")
    if len(delays) != 4 or any(d <= 0 for d in delays):
        p.error("--delays 는 양의 정수 4개다")
    if not (a.delays_basis or "").strip():
        p.error("--delays 를 주면 --delays-basis(gate open 실측 5회와 중앙값)가 필요하다")
    return delays, {"delays_source": "arg", "delays_basis": a.delays_basis.strip()}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--mode", required=True, choices=("open", "closed"))
    p.add_argument("--sent", type=int, default=20)
    p.add_argument("--max-runs", type=int, default=80)
    p.add_argument("--docs", type=int, default=5, help="시행 전 실터치로 여는 그리드 수")
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--delays", help="쉼표로 구분한 지연 ms 4개. 생략하면 DELAYS 상수")
    p.add_argument("--delays-basis", help="--delays 근거(gate open 실측 5회 값과 중앙값). --delays 를 주면 필수")
    p.add_argument("--args-only", action="store_true", help="기기 없이 인자 해석 결과만 출력")
    p.add_argument("--tap", help="경계 시행용 손가락 화면 좌표 X,Y. 생략하면 대상 컨트롤 bounds 기준 기본 좌표")
    a = p.parse_args()
    delays, delays_info = resolve_delays(p, a)
    if a.args_only:
        print(json.dumps({"mode": a.mode, "sent": a.sent, "max_runs": a.max_runs, "docs": a.docs, "delays": list(delays), **delays_info}, ensure_ascii=False))
        return 0
    started_at = time.strftime("%Y-%m-%dT%H:%M:%S+09:00")
    c = L.Ctx(a.mode, "Release", a.out)
    (L.set_closed if a.mode == "closed" else L.set_open)()
    if not L.install(a.apk, fresh=a.fresh):
        print("설치 실패")
        return 1
    L.sh("pm clear %s" % L.PKG)
    time.sleep(2)
    act = S.activity()
    S.MODE = a.mode
    target = S.target_id(a.mode)  # 접힘 shell.navTab.Settings, 펼침 시스템(Y) 상위 메뉴 shell.mnuSys
    state = "CLOSED" if a.mode == "closed" else "OPENED"
    S.cold(act)
    rect = None
    for _i in range(60):
        rect = L.U.control_rect(c, c.dump(), target)
        if rect:
            break
        time.sleep(1)
    if not rect:
        print("터치 대상 없음")
        return 1
    time.sleep(2)
    for _d in range(a.docs):
        if a.mode == "closed":
            c.tap_id("shell.btnDrawer")
            time.sleep(1.5)
            c.tap_id("shell.navDrawer.OpenGrid")
        else:
            c.tap_id("shell.mnuFile")
            time.sleep(1.5)
            c.tap_id("shell.mnuOpenGrid")
        time.sleep(2.5)
    time.sleep(3)  # 자동 저장 대기
    S.cold(act)
    for _i in range(60):
        if L.U.control_rect(c, c.dump(), target):
            break
        time.sleep(1)
    time.sleep(2)
    xml = c.dump("base")
    base_mnu = count_mnu(xml)
    b = c.bounds(xml, target)
    tx, ty = b[0], b[1] + c.bounds_top(xml, "android:id/content")
    if a.tap:
        tx, ty = (int(v) for v in a.tap.split(","))
        delays_info["tap_override"] = [tx, ty]
    rows, sent, n = [], 0, 0
    while sent < a.sent and n < a.max_runs:
        for d in delays:
            if sent >= a.sent or n >= a.max_runs:
                break
            L.ensure(state)
            t0, _ = S.cold(act)
            wait = d / 1000 - (time.time() - t0)
            if wait > 0:
                time.sleep(wait)
            t_tap = time.time()
            L.sh("input tap %d %d" % (tx, ty))
            tap_ms = int((t_tap - t0) * 1000)
            time.sleep(4)
            name = "t%03d" % n
            r = {"i": n, "delay": d, "tap_ms": tap_ms, **dump_state(c, name, a.mode, base_mnu), "on_top": on_top_stable()}
            r["lines"] = S.startup_lines()
            r["marks"] = S.trial_marks(r["lines"])
            r["input_lines"] = S.input_lines()  # 시스템이 탭을 앱 창 밖에서 처리했는지(유실 분류 근거)
            r["loss"] = S.classify_loss(r, S.cold_dev_ms)  # 유실이면 ①창 붙기 전 ②설계상 버림 ③그 밖, 아니면 None
            is_sent = (r["marks"]["replay_result"] or "").startswith("sent")
            r["sent"] = is_sent
            if is_sent:
                sent += 1
            if is_sent or r["marks"]["direct"]:
                r["cap"] = os.path.basename(c.shot(name))
            rows.append(r)
            save_partial(a, {"mode": a.mode, "apk": a.apk, "target": target, "tap": [tx, ty], "docs_opened": a.docs, "delays": list(delays), **delays_info,
                             "trials": len(rows), "sent": sent, "rows": rows, "started_at": started_at, "saved_at": time.strftime("%Y-%m-%dT%H:%M:%S+09:00")})
            print({k: r.get(k) for k in ("i", "delay", "nodes", "mnu_extra", "drawer", "popups", "sent")}, r["marks"]["replay_target"], r["marks"]["replay_result"], flush=True)
            n += 1
    fatal, ex = c.fatal_count()
    doc = {"mode": a.mode, "apk": a.apk, "apk_sha256": sha(a.apk), "target": target, "tap": [tx, ty], "base_mnu": base_mnu, "docs_opened": a.docs, "delays": list(delays), **delays_info, "wm": L.wm_size(), "state": L.dev_state(), "trials": n, "sent": sent, "fatal": [fatal, ex], "loss_summary": S.loss_summary(rows), "rows": rows, "started_at": started_at, "finished_at": time.strftime("%Y-%m-%dT%H:%M:%S+09:00")}
    path = os.path.join(a.out, "replay_%s.json" % a.mode)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
    partial = partial_path(a)
    if os.path.exists(partial):
        os.remove(partial)
    print("원자료", path, "시행", n, "sent", sent, flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

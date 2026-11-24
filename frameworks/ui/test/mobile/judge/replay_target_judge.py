#!/usr/bin/env python3
# 스플래시 터치 재전송 대상 판정(K7, 판정 계층) — 재전송(sent) 시행마다 대상 설정이 열리고 다른 컨트롤이 열리지 않았는지 보고 rc 0(합격)·1(불합격)·2(표본 부족·입력 오류)를 돌려준다
"""
사용: python3 -B replay_target_judge.py --open <replay_open.json> [--closed <replay_closed.json>] [--min-sent 20]
자체 시험(기기 불필요): python3 -B replay_target_judge.py --self-test
대상: 접힘은 설정 탭 → 설정 창(settings.*), 펼침은 시스템(Y) 상위 메뉴 → 시스템 메뉴 항목(shell.mnuSys*)이다(도구모음 줄이 없어 설정 버튼은 없다). 파일 메뉴 같은 다른 메뉴가 열리면 mnu_extra>0 이라 불합격이다.
유실 분류(loss: before_window·design_discard·other)는 측정기가 행마다 남긴 값을 세어 보고만 한다(rc 불변).
기준: 터치가 앱에 닿은 시행(sent·direct) 전부 nodes≥3, mnu_extra=0, drawer=0, 앱 전면, 치명 0. 하나라도 어기면 표본 수와 무관하게 rc=1 이다.
합격(rc=0)은 펼침·접힘 원자료가 모두 있고 상태별 sent 가 --min-sent 이상일 때만 낸다. 그 밖에는 rc=2 다.
표본 수는 sent 만 센다. direct 는 수정 전 같은 오작동(메뉴 열림) 실측이 있어 위반만 본다. 표식 없는 유실은 이 판정의 대상이 아니다. 그 값은 c5_splash_judge.py 가 본다.
"""
import argparse
import json
import sys

MIN_NODES = 3


def bad_reasons(row):
    out = []
    if row.get("nodes", 0) < MIN_NODES:
        out.append("target_not_opened")
    if row.get("mnu_extra", 0) > 0:
        out.append("menu_opened")
    if row.get("drawer", 0) > 0:
        out.append("drawer_opened")
    if not row.get("on_top", False):
        out.append("app_left")
    return out


def judge_doc(doc):
    rows = doc.get("rows", [])
    sent = [r for r in rows if r.get("sent")]
    candidates = [r for r in rows if r.get("sent") or (r.get("marks") or {}).get("direct")]
    # 앱 창 표시 전(before_window)에 주입된 탭은 판정에서 제외한다(사용자 결정 2026-11-08). cls=other 와 직접 지연 도달은 계속 판정한다
    excluded = [r for r in candidates if (r.get("loss") or {}).get("cls") == "before_window"]
    checked = [r for r in candidates if not any(r is x for x in excluded)]
    bad = [{"i": r.get("i"), "path": "sent" if r.get("sent") else "direct", "reasons": bad_reasons(r), "target": (r.get("marks") or {}).get("replay_target") or (r.get("marks") or {}).get("direct"), "cap": r.get("cap")} for r in checked if bad_reasons(r)]
    fatal = (doc.get("fatal") or [0])[0]
    return {"trials": len(doc.get("rows", [])), "sent": len(sent), "bad": len(bad), "bad_rows": bad, "excluded_before_window": len(excluded), "fatal": fatal, "apk_sha256": doc.get("apk_sha256"), "loss": loss_counts(rows)}


def loss_counts(rows):
    """측정기가 행마다 남긴 유실 분류(before_window·design_discard·other) 집계. 보고용이며 rc 에는 영향 없다(유실 판정은 c5_splash_judge.py)."""
    out = {"before_window": 0, "design_discard": 0, "other": 0, "unclassified": 0}
    for r in rows:
        if r.get("nodes", 0) >= MIN_NODES:
            continue
        cls = (r.get("loss") or {}).get("cls")
        out[cls if cls in out else "unclassified"] += 1
    return out


def verdict(results, min_sent):
    """results={모드: judge_doc 값}. 위반이 먼저, 그다음 표본·모드 부족."""
    if any(v["bad"] or v["fatal"] for v in results.values()):
        return 1
    if set(results) != {"open", "closed"} or any(v["sent"] < min_sent for v in results.values()):
        return 2
    return 0


def self_test():
    good = {"sent": True, "nodes": 24, "mnu_extra": 0, "drawer": 0, "on_top": True}
    menu = {"sent": True, "nodes": 0, "mnu_extra": 12, "drawer": 0, "on_top": True}
    other = {"sent": False, "nodes": 0, "mnu_extra": 0, "drawer": 0, "on_top": True}
    mk = lambda *rows: judge_doc({"rows": list(rows), "fatal": [0, []]})
    cases = [
        ("합격", verdict({"open": mk(*[good] * 20), "closed": mk(*[good] * 20)}, 20), 0),
        ("메뉴 열림은 불합격(음성 대조)", verdict({"open": mk(good, menu)}, 20), 1),
        ("대상 안 열림", bad_reasons({"nodes": 0, "on_top": True}), ["target_not_opened"]),
        ("시트와 서랍 동시", bad_reasons({"nodes": 24, "drawer": 3, "on_top": True}), ["drawer_opened"]),
        ("앱 이탈", bad_reasons({"nodes": 24}), ["app_left"]),
        ("sent 아닌 유실은 대상 밖", mk(good, other)["bad"], 0),
        ("표본 부족은 판정 불가", verdict({"open": mk(good), "closed": mk(good)}, 20), 2),
        ("한 상태만이면 판정 불가", verdict({"open": mk(*[good] * 20)}, 20), 2),
        ("치명은 불합격", verdict({"open": judge_doc({"rows": [good] * 20, "fatal": [1, []]}), "closed": mk(*[good] * 20)}, 20), 1),
        ("direct 메뉴 열림도 불합격", verdict({"open": mk(good, {**menu, "sent": False, "marks": {"direct": "974ms target=Border/PART_LayoutRoot"}})}, 20), 1),
        ("sent 0건은 판정 불가", verdict({"open": mk(other), "closed": mk(other)}, 20), 2),
        ("펼침: 시스템 메뉴 대신 파일 메뉴가 열리면 오탭 불합격", verdict({"open": mk(good, {**menu, "nodes": 0, "mnu_extra": 5}), "closed": mk(*[good] * 20)}, 20), 1),
        ("before_window 표식 행은 판정 제외", (lambda d: (d["bad"], d["excluded_before_window"]))(mk(good, {**menu, "loss": {"cls": "before_window"}})), (0, 1)),
        ("cls=other 행은 계속 판정", mk(good, {**menu, "loss": {"cls": "other"}})["bad"], 1),
        ("유실 분류 집계는 rc 에 영향 없음", judge_doc({"rows": [good, {**other, "loss": {"cls": "before_window"}}, {**other, "loss": {"cls": "other"}}, other], "fatal": [0, []]})["loss"], {"before_window": 1, "design_discard": 0, "other": 1, "unclassified": 1}),
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
    p.add_argument("--open")
    p.add_argument("--closed")
    p.add_argument("--min-sent", type=int, default=20)
    a = p.parse_args()
    results = {}
    for mode, path in (("open", a.open), ("closed", a.closed)):
        if not path:
            continue
        try:
            with open(path, encoding="utf-8") as f:
                results[mode] = judge_doc(json.load(f))
        except (OSError, ValueError) as ex:
            print("입력 오류 %s: %s" % (path, ex))
            return 2
    if not results:
        print("입력 오류: --open·--closed 가 없다")
        return 2
    rc = verdict(results, a.min_sent)
    print(json.dumps({"rc": rc, "min_sent": a.min_sent, "results": results}, ensure_ascii=False, indent=1))
    return rc


if __name__ == "__main__":
    sys.exit(main())

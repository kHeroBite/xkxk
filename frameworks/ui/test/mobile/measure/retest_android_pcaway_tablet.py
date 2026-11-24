#!/usr/bin/env python3
# Fold5 태블릿 첫 기동 문서 배치(K6)·독립 창 아이콘 Android 동등성(E6)·서랍과 설정 시트 동시 열림 측정 — 값만 모으고 판정은 tablet_first_layout_judge.py·pcaway_gate.py 가 한다
"""
사용:
  retest_android_pcaway_tablet.py k6 --apk <Debug APK> --out <폴더> [--measure]        pm clear 펼침 첫 기동(문서 0개·1개), 접기·펴기, 접힘 기동 뒤 펼치기
  retest_android_pcaway_tablet.py e6 --apk <Debug APK> --out <폴더> [--fresh] [--measure]  펼침 독립 창 아이콘 수·실터치 활성·최소화 복원, 접힘 제목 줄 없음
  retest_android_pcaway_tablet.py flydrawer --apk <Debug APK> --out <폴더> [--fresh]   접힘에서 서랍과 설정 시트를 겹쳐 열고 BACK 2회의 전면·노드 수를 남긴다
k6 는 docs.arrange·ui.style set 을 부르지 않는다. 문서 0개 덤프는 첫 기동 기본 문서가 있으면 docs.close 만 쓴다(값 first_boot_docs 로 남긴다).
빈 문서 영역 안내는 shell.ko.json 의 doc.empty·panel.documents.empty 문구가 덤프에 있는지 보고, 판정 근거 캡처는 otest 가 읽는다.
기기 설정 원복은 호출자가 phone_session.sh 와 cmd device_state state reset 으로 한다.
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_android_pcaway_lib as L  # noqa: E402

JUDGE = os.path.join(HERE, "..", "judge", "tablet_first_layout_judge.py")
EMPTY_TEXTS = ("열린 문서가 없습니다", "No open documents", "doc.empty", "panel.documents.empty")


def judge(path):
    p = subprocess.run([sys.executable, "-B", JUDGE, "--dump", path], capture_output=True, text=True)
    return p.returncode, p.stdout.strip()[:400]


def dump_judge(c, name):
    """덤프를 저장하고 판정기 rc 를 돌려준다."""
    xml = c.dump(name)
    path = os.path.join(c.out, "%s.xml" % name)
    if not os.path.isfile(path):
        with open(path, "w", encoding="utf-8") as f:
            f.write(xml)
    rc, out = judge(path)
    return xml, rc, out


def docs(c):
    return c.call("docs.list") or []


def fresh_boot(c):
    """pm clear 뒤 첫 기동. 설치 직후 monkey 가 앱을 띄우지 못한 실측(2026-11-05 k6 1회)이 있어 앱이 안 뜨면 pm clear 부터 다시 한다. 시도 수를 남긴다."""
    time.sleep(3)
    for n in range(1, 4):
        L.sh("am force-stop %s" % L.PKG)
        L.sh("pm clear %s" % L.PKG)
        time.sleep(2)
        info = L.attach(c, wait=30)
        c.raw.setdefault("boot_attempts", []).append({"n": n, "ok": bool(info), "t": time.strftime("%H:%M:%S")})
        if info:
            return info
    return None


def k6(a):
    c = L.Ctx("tablet", "Debug", a.out)
    c.raw["apk"] = a.apk
    L.set_open()
    if not L.install(a.apk, fresh=True) or not fresh_boot(c):
        print("설치·연결 실패")
        return 1
    st = L.U.style_wait(c, "Tablet", 60)
    time.sleep(3)
    first = docs(c)
    c.raw["first_boot"] = {"style": st, "docs": [d.get("kind") or d.get("id") for d in first], "state": L.dev_state(), "tour": c.call("tour.state")}
    cap_first = c.shot("k6_first_boot")
    c.dump("k6_first_boot")
    if first:
        c.call("docs.close", all=True)
        time.sleep(2)
    xml0, rc0, out0 = dump_judge(c, "k6_doc0")
    hint = [t for t in EMPTY_TEXTS if t in xml0]
    cap0 = c.shot("k6_doc0")
    c.raw["doc0"] = {"rc": rc0, "judge": out0, "docs": len(docs(c)), "hint_texts": hint}
    c.call("docs.open", kind="grid")
    time.sleep(2.5)
    xml1, rc1, out1 = dump_judge(c, "k6_doc1")
    cap1 = c.shot("k6_doc1")
    c.raw["doc1"] = {"rc": rc1, "judge": out1, "docs": len(docs(c)), "doc_tab_strip": len(re.findall(r'class="DocumentTabStrip"', xml1))}
    pid0 = c.pid()
    L.set_closed()
    closed_style = L.U.style_wait(c, "Mobile", 30)
    L.set_open()
    open_style = L.U.style_wait(c, "Tablet", 30)
    time.sleep(2)
    xml2, rc2, out2 = dump_judge(c, "k6_fold_cycle")
    cap2 = c.shot("k6_fold_cycle")
    c.raw["fold_cycle"] = {"rc": rc2, "judge": out2, "closed_style": closed_style, "open_style": open_style, "docs": len(docs(c)), "pid_same": c.pid() == pid0, "doc_tab_strip": len(re.findall(r'class="DocumentTabStrip"', xml2))}
    n1, ex1 = c.fatal_count()
    # 접힘 기동 → 펼치기
    L.set_closed()
    if not fresh_boot(c):
        print("접힘 기동 연결 실패")
        return 1
    start_style = L.U.style_wait(c, "Mobile", 60)
    time.sleep(2)
    L.set_open()
    reopen_style = L.U.style_wait(c, "Tablet", 30)
    time.sleep(2)
    if not docs(c):
        c.call("docs.open", kind="grid")
        time.sleep(2.5)
    xml3, rc3, out3 = dump_judge(c, "k6_closed_start_open")
    cap3 = c.shot("k6_closed_start_open")
    c.raw["closed_start"] = {"rc": rc3, "judge": out3, "start_style": start_style, "reopen_style": reopen_style, "docs": len(docs(c)), "doc_tab_strip": len(re.findall(r'class="DocumentTabStrip"', xml3))}
    n2, ex2 = c.fatal_count()
    fatal = n1 + n2
    c.raw["fatal"] = {"before_clear": (n1, ex1), "after_clear": (n2, ex2)}
    c.check("치명 0", fatal == 0, (n1, n2), 0)
    raw = L.save_raw(c, "k6")
    if a.measure:
        L.write_measure(a.out, "K6", {
            "tablet_first_doc0": (rc0, rc0 == 0),
            "tablet_first_doc1": (rc1, rc1 == 0),
            "tablet_first_empty_hint": (bool(hint), bool(hint)),
            "tablet_fold_cycle_keeps": (rc2 == 0 and c.raw["fold_cycle"]["docs"] == c.raw["doc1"]["docs"], rc2 == 0 and c.raw["fold_cycle"]["docs"] == c.raw["doc1"]["docs"]),
            "tablet_closed_start_open": (rc3 == 0, rc3 == 0),
            "tablet_no_fatal": (fatal, fatal == 0),
        }, "retest_android_pcaway_tablet.py k6 " + raw, (cap0, cap1, cap2, cap3, cap_first))
    return 0


def icon_ids(c, xml):
    return sorted(i for i in c.ids(xml) if i.startswith("shell.sdiIcon."))


def e6(a):
    c = L.Ctx("tablet", "Debug", a.out)
    c.raw["apk"] = a.apk
    L.set_open()
    if not L.install(a.apk, fresh=a.fresh) or not fresh_boot(c):
        print("설치·연결 실패")
        return 1
    L.U.style_wait(c, "Tablet", 60)
    time.sleep(2)
    c.call("docs.close", all=True)
    time.sleep(1)
    xml0 = c.dump("e6_none")
    base = {"icons": icon_ids(c, xml0), "strip": "shell.titleStrip" in c.ids(xml0), "tabs": "shell.tabStrip" in c.ids(xml0)}
    ids = []
    for k in ("grid", "gantt", "form"):
        c.call("docs.open", kind=k)
        time.sleep(1.5)
    ids = [d["id"] for d in docs(c)]
    c.call("docs.sdi", doc=ids[0], on=True)
    c.call("docs.sdi", doc=ids[1], on=True)
    time.sleep(2.5)
    xml1 = c.dump("e6_two")
    icons = icon_ids(c, xml1)
    cap1 = c.shot("e6_two")
    sdi_ids = [d["id"] for d in docs(c) if d.get("floating") or d.get("sdi")]
    target = next((i for i in icons if i.endswith(ids[0])), icons[0] if icons else None)
    tap = c.tap_id(target) if target else False
    time.sleep(2)
    active = [d["id"] for d in docs(c) if d.get("active")]
    cap2 = c.shot("e6_icon_tap")
    c.raw["icons"] = {"base": base, "docs": docs(c), "sdi_ids": sdi_ids, "icons": icons, "tap": tap, "target": target, "active_after": active, "tabs_after_sdi": "shell.tabStrip" in c.ids(xml1), "strip": "shell.titleStrip" in c.ids(xml1)}
    print("icons", json.dumps(c.raw["icons"], ensure_ascii=False, default=str)[:600], flush=True)
    # 접힘: 독립 창이 있어도 제목 줄·탭 줄·아이콘이 없어야 한다
    L.set_closed()
    L.U.style_wait(c, "Mobile", 30)
    time.sleep(2)
    xml2 = c.dump("e6_phone")
    ph = c.ids(xml2)
    cap3 = c.shot("e6_phone")
    c.raw["phone"] = {"strip": "shell.titleStrip" in ph, "tabs": "shell.tabStrip" in ph, "icons": icon_ids(c, xml2), "nav": "shell.navTabBar" in ph or "shell.btnDrawer" in ph, "style": (c.call("ui.style") or {}).get("effective"), "wm": L.wm_size()}
    print("phone", c.raw["phone"], flush=True)
    L.set_open()
    n, ex = c.fatal_count()
    c.check("치명 0", n == 0, (n, ex), 0)
    raw = L.save_raw(c, "e6")
    if a.measure:
        target_active = bool(tap) and target is not None and any(target.endswith(x) for x in active)
        p = c.raw["phone"]
        L.write_measure(a.out, "E6", {
            "android_tablet_icon": (c.raw["icons"], len(icons) == 2 and target_active),
            "phone_nav_no_strip": (p, not p["strip"] and not p["tabs"] and not p["icons"] and p["nav"] and p["style"] == "Mobile"),
        }, "retest_android_pcaway_tablet.py e6 " + raw, (cap1, cap2, cap3))
    return 0


def flydrawer(a):
    """서랍과 설정 시트를 겹쳐 열어 본다. 겹쳐 열리지 않으면 그 값을 그대로 남긴다."""
    c = L.Ctx("phone", "Debug", a.out)
    c.raw["apk"] = a.apk
    L.set_closed()
    if not L.install(a.apk, fresh=a.fresh) or not L.attach(c):
        print("설치·연결 실패")
        return 1
    L.U.style_wait(c, "Mobile", 60)
    time.sleep(2)
    c.call("tour.end")

    def probe():
        xml = c.dump()
        ids = c.ids(xml)
        return {"drawer": len([i for i in ids if i.startswith("shell.navDrawer.")]), "settings": len([i for i in ids if i.startswith("settings.")]), "on_top": L.app_on_top()}

    def back_seq(tag):
        rows = []
        for i in range(2):
            L.adb("logcat", "-c")
            L.key_back()
            time.sleep(1.8)
            lines = [ln.split("[host] ")[-1].strip()[:120] for ln in L.adb("logcat", "-d")[1].splitlines() if "[host] back" in ln]
            on_top = L.app_on_top()
            row = {"i": i, "on_top": on_top, "host": lines, **(probe() if on_top else {})}
            c.shot("%s_back%d" % (tag, i))
            rows.append(row)
            print(tag, row, flush=True)
            if not on_top:
                L.relaunch()
                time.sleep(2)
                break
            if not row["drawer"] and not row["settings"]:
                break
        return rows

    res = {}
    # 순서 1: 서랍을 연 채 설정 탭을 누른다
    c.tap_id("shell.btnDrawer")
    time.sleep(1.5)
    before = probe()
    tap = c.tap_id("shell.navTab.Settings")
    time.sleep(2.5)
    mid = probe()
    c.shot("drawer_then_settings")
    res["drawer_then_settings"] = {"drawer_only": before, "tap": tap, "after_tap": mid, "backs": back_seq("drawer_then_settings")}
    if L.app_on_top():
        clear = probe()
        if clear.get("settings"):
            c.tap_id("shell.navTab.Settings")
            time.sleep(1.5)
    # 순서 2: 설정 시트를 연 채 서랍 버튼을 누른다
    c.tap_id("shell.navTab.Settings")
    time.sleep(2.5)
    before = probe()
    tap = c.tap_id("shell.btnDrawer")
    time.sleep(2)
    mid = probe()
    c.shot("settings_then_drawer")
    res["settings_then_drawer"] = {"settings_only": before, "tap": tap, "after_tap": mid, "backs": back_seq("settings_then_drawer")}
    c.raw["flydrawer"] = res
    print(json.dumps(res, ensure_ascii=False, default=str)[:1500], flush=True)
    n, ex = c.fatal_count()
    c.check("치명 0", n == 0, (n, ex), 0)
    L.save_raw(c, "flydrawer")
    L.set_open()
    return 0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("cmd", choices=("k6", "e6", "flydrawer"))
    p.add_argument("--apk", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--measure", action="store_true")
    a = p.parse_args()
    return {"k6": k6, "e6": e6, "flydrawer": flydrawer}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())

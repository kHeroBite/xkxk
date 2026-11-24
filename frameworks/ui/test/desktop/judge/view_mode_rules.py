#!/usr/bin/env python3
# 화면 방식 전환 측정(retest_view_mode.py) 판정 규칙 — 클릭 오류 없음, 저장값·적용 형이 고른 값과 일치, 기기 규칙 유지


def lower(v):
    return str(v).lower() if v is not None else None


def judge(out):
    """항목별 PASS/FAIL 표. 기준: 클릭 오류 없음, 저장값·적용 형이 고른 값과 일치, 칸 활성."""
    rows = []

    def add(name, ok, detail):
        rows.append({"item": name, "pass": bool(ok), "detail": detail})

    for key, row in out.get("autoToManual", {}).items():
        a = row["after"]
        add("자동→%s" % key, not row["clickError"] and a["setting"] == key and a["effective"] == key,
            "칸활성(전)=%s 오류=%s setting=%s effective=%s" % (row["before"]["cells"][key]["enabled"], row["clickError"], a["setting"], a["effective"]))
    for pair, row in out.get("manualToManual", {}).items():
        dst = pair.split(">")[1]
        a = row["after"]
        add("수동 %s" % pair, not row["clickError"] and a["setting"] == dst and a["effective"] == dst,
            "오류=%s setting=%s effective=%s" % (row["clickError"], a["setting"], a["effective"]))
    for src, row in out.get("manualToAuto", {}).items():
        a = row["after"]
        add("%s→자동" % src, not row["clickError"] and a["setting"] == "auto", "setting=%s effective=%s" % (a["setting"], a["effective"]))
    rules = out.get("rules", {})
    if rules:
        expect = {"auto_wide": "pc", "auto_narrow": "tablet", "auto_phoneWidth": "mobile", "auto_wideBack": "pc", "tablet_phoneWidth": "tablet", "tablet_wideBack": "tablet"}
        for name, want in expect.items():
            got = rules.get(name, {}).get("effective")
            add("규칙 %s" % name, got == want, "기대=%s 실제=%s 폭=%s" % (want, got, rules.get(name, {}).get("width")))
    settings = out.get("settings", {})
    for key in ("pc", "tablet", "mobile"):
        row = settings.get("combo_auto>" + key)
        if row:
            a = row["after"]
            add("설정 콤보 자동→%s" % key, a["setting"] == key and a["effective"] == key and row["combo"] == key,
                "setting=%s effective=%s combo=%s 오류=%s" % (a["setting"], a["effective"], row["combo"], row["selectError"]))
    sync = settings.get("switch>tablet")
    if sync:
        add("스위치→설정 콤보 동기화", sync["combo"] == "tablet", "combo=%s 오류=%s" % (sync["combo"], sync["clickError"]))
    if "saved" in out:
        add("저장값", lower(out["saved"]) in ("tablet", "2"), "settings.json UiStyle=%s" % (out["saved"],))
    return rows


def judge_l2(out):
    rows = []
    for key, row in out["l2"].items():
        a = row["after"]
        rows.append({"item": "L2 자동→%s" % key, "pass": a["setting"] == key and a["effective"] == key,
                     "detail": "칸활성(전)=%s setting=%s effective=%s %s" % (row["enabledBefore"], a["setting"], a["effective"], row["sent"][:60])})
    return rows


def judge_touch(out):
    t = out["touch"]
    click = t["tablet_click_pc"]
    phone = t["phone"]
    return [
        {"item": "태블릿 자동=태블릿형", "pass": t["tablet_auto"]["device"] == "Tablet" and t["tablet_auto"]["effective"] == "tablet",
         "detail": "device=%s effective=%s" % (t["tablet_auto"]["device"], t["tablet_auto"]["effective"])},
        {"item": "태블릿 자동→PC형 클릭", "pass": not click["clickError"] and click["after"]["setting"] == "pc" and click["after"]["effective"] == "pc",
         "detail": "오류=%s setting=%s effective=%s" % (click["clickError"], click["after"]["setting"], click["after"]["effective"])},
        {"item": "폰=모바일형·스위치 숨김", "pass": phone["device"] == "Phone" and phone["effective"] == "mobile" and phone["cells"]["pc"]["visible"] is False,
         "detail": "device=%s effective=%s PC칸 보임=%s 활성=%s 모바일칸 활성=%s" % (phone["device"], phone["effective"], phone["cells"]["pc"]["visible"], phone["cells"]["pc"]["enabled"], phone["cells"]["mobile"]["enabled"])},
        {"item": "폰 PC칸 비활성·클릭 거부", "pass": phone["cells"]["pc"]["enabled"] is False and bool(t["phone_click_pc"]["clickError"]) and t["phone_click_pc"]["after"]["effective"] == "mobile",
         "detail": "오류=%s effective=%s" % (t["phone_click_pc"]["clickError"], t["phone_click_pc"]["after"]["effective"])},
        {"item": "폰 ui.style set=pc 거부", "pass": isinstance(t["phone_set_pc"], dict) and bool(t["phone_set_pc"].get("error")) and t["phone_after_set"]["effective"] == "mobile",
         "detail": "응답=%s effective=%s" % (str(t["phone_set_pc"])[:70], t["phone_after_set"]["effective"])},
    ]

# B1·B2 판정기 — 상단 메뉴 4종 전 항목 아이콘과, 메뉴로 연 창(MDI·SDI 문서·시스템 창)의 창 아이콘이 메뉴 아이콘과 같은지 판정한다
"""
사용
  menu_icons_judge.py --info <격리>/remote-control.json --approved '<누가·언제>' --out <c27>/b1b2.json [--part b1|b2|all] [--results <c27>/RESULTS.json]
  menu_icons_judge.py --self-test
기본 remote_only(원격 명령·앱 렌더만 → PC 비움 불요). 깨끗한 격리 인스턴스(문서 0개)에서 돈다. --out·--results 가 evidence/ 아래면 거부.
B1  menu.render 로 메뉴(와 하위 메뉴 부모) 팝업 PNG 를 남기고 menu.icons 로 보이는 항목마다 아이콘 종류·키·리소스를 본다(visible=false 는
    제외). menu.list 항목이 menu.icons 에서 빠지면 FAIL. 문서 1개를 연 채 열린 창 칸(mnuWinList1~9)을 따로 본다(b1.winlist).
B2  메뉴 경로를 ui.click(Invoke)으로 눌러 창을 열고 문서 iconKey(window.load.state)·창 iconKey/explicit/big/small(window.icon)이
    메뉴 항목 키·계약 키(계획서 §4.1 대응표)와 같아야 PASS. --results 면 RESULTS.json items.B1·B2 에 {verdict, evidence[], method}.
"""
import sys
import time

import judge_session as session
from c27_b10 import B10_METHOD, active_doc, b10_cases, b10_open, icon_bands, slots_of
from c27_judge_common import add_c27_args, run_c27, stage_render, verdict

TOP_MENUS = ("shell.mnuFile", "shell.mnuWindow", "shell.mnuView", "shell.mnuSys")
DOC_CASES = (  # (메뉴 id, 누를 부모 경로, 계약 키, SDI 여부)
    ("shell.mnuOpenGrid", ("shell.mnuFile",), "PdMenuIcon1009", False),
    ("shell.mnuOpenGantt", ("shell.mnuFile",), "PdMenuIcon1015", False),
    ("shell.mnuOpenForm", ("shell.mnuFile",), "PdMenuIcon1010", False),
    ("shell.mnuOpenGridSdi", ("shell.mnuFile", "shell.mnuOpenSdiGroup"), "PdMenuIcon1009", True),
    ("shell.mnuOpenGanttSdi", ("shell.mnuFile", "shell.mnuOpenSdiGroup"), "PdMenuIcon1015", True),
    ("shell.mnuOpenFormSdi", ("shell.mnuFile", "shell.mnuOpenSdiGroup"), "PdMenuIcon1010", True),
)
SYS_CASES = (  # (메뉴 id, 부모 경로, 계약 키, 닫기 이름(settings=Esc), 메뉴가 숨었을 때 sys.open view)
    ("shell.mnuSysSettings", ("shell.mnuSys",), "PdShellIconSettings", "settings", None),
    ("shell.mnuSettings", ("shell.mnuView",), "PdShellIconSettings", "settings", None),
    ("shell.mnuSysMonitorApp", ("shell.mnuSys", "shell.mnuSysMonitor"), "PdCmdIconAppMonitor", "monitor", "AppMonitor"),
    ("shell.mnuSysLogView", ("shell.mnuSys", "shell.mnuSysLog"), "PdShellIconLog", "log", "LogView"),
    ("shell.mnuSysInquiry", ("shell.mnuSys",), "PdCmdIconInquiry", "inquiry", "Inquiry"),
    ("shell.mnuSysInquiryList", ("shell.mnuSys",), "PdCmdIconInquiryList", "inquiryList", "InquiryList"),
    ("shell.mnuSysUpdate", ("shell.mnuSys",), "PdCmdIconUpdate", "update", "Update"),
    ("shell.mnuSysChangelog", ("shell.mnuSys",), "PdCmdIconChangelog", "changelog", "Changelog"),
    ("shell.mnuSysAbout", ("shell.mnuSys",), "PdCmdIconAbout", "about", "About"),
    ("shell.mnuSysGpuRestart", ("shell.mnuSys",), "PdCmdIconGpuRestart", "restartRequired", "RestartRequired"),
)
METHOD = {"B1": "원격 menu.render(팝업 PNG)·menu.icons(항목별 iconKind·iconKey·geometryFound)·menu.list 대조, 격리 진단 Release·remote_only",
          "B2": "원격 ui.click 메뉴 경로로 창 열기 → window.load.state iconKey·window.icon iconKey/explicit/big/small 을 메뉴 키·계약 키와 대조"}


# ---------- B1 ----------
def judge_item(it):
    """항목 1개의 문제. 문제 없음·셈 제외면 None."""
    kind, key = it.get("iconKind"), it.get("iconKey")
    if it.get("visible") is False:
        return None  # 사용자가 볼 수 없는 칸(빈 열린 창 칸 등) — 보이는 열린 창 칸은 judge_win_slots 가 따로 본다
    if kind not in ("window", "snap", "doc"):
        return "아이콘 없음"
    if not it.get("iconPresent", True):
        return "표시 컨트롤 없음(첨부 키 %s 만 있음)" % key
    if kind != "snap" and (not key or it.get("geometryFound") is not True):
        return "아이콘 리소스 없음(%s)" % key
    return None


def judge_menu(menu, listed_ids):
    items = menu.get("items") or []
    if not items:
        return {"verdict": "INVALID", "why": "menu.icons 항목 0개"}
    bad = {}
    for it in items:
        p = judge_item(it)
        if p:
            bad[it.get("id") or it.get("header")] = p
    lost = sorted(set(listed_ids) - {it.get("id") for it in items})
    if lost:
        bad["menu.icons 에 빠진 항목"] = lost
    return verdict(bad, "보이는 전 항목 아이콘 + 리소스 존재", total=len(items), with_icon=menu.get("withIcon"))


def judge_win_slots(items, docs):
    """창 1개 이상 연 상태의 열린 창 칸(shell.mnuWinList1~9): 보이는 칸 수 = 열린 문서 수(최대 9), 칸마다 문서 아이콘(doc)·리소스·그 문서 키.
    활성 ✓ 는 진단 명령이 내지 않아 menu.render PNG 판독 몫이다(결과 why 에 적는다)."""
    if not docs:
        return {"verdict": "INVALID", "why": "열린 문서가 없어 열린 창 칸을 볼 수 없다"}
    slots = [it for it in items if str(it.get("id", "")).startswith("shell.mnuWinList") and it["id"][-1:].isdigit()]
    shown = [it for it in slots if it.get("visible")]
    keys = {d.get("iconKey") for d in docs}
    bad = {}
    if len(shown) != min(len(docs), 9):
        bad["보이는 칸 수"] = [len(shown), min(len(docs), 9)]
    for it in shown:
        if it.get("iconKind") != "doc" or not it.get("iconPresent") or it.get("geometryFound") is not True or it.get("iconKey") not in keys:
            bad[it["id"]] = {k: it.get(k) for k in ("iconKind", "iconKey", "geometryFound", "iconPresent")}
    return verdict(bad, "보이는 열린 창 칸 = 문서 아이콘(활성 ✓ 는 menu.render PNG 판독)", shown=len(shown), docs=len(docs))


def winlist_case(s):
    """열린 문서가 있는 지금 창 메뉴의 열린 창 칸을 판정한다(데스크톱·웹 공용)."""
    menu = (s.call("menu.icons", id="shell.mnuWindow").get("menus") or [{}])[0]
    return judge_win_slots(menu.get("items") or [], s.call("window.load.state").get("docs") or [])


def b1(s, cases):
    cases["b1.winlist"] = winlist_case(s)  # 부른 쪽이 문서 1개를 열어 둔다
    for top in TOP_MENUS:
        listed = s.call("menu.list", id=top).get("items") or []
        parents = {it.get("parent") for it in listed if it.get("parent") and it.get("parent") != top}
        for mid in (top,) + tuple(sorted(p for p in parents if p)):
            try:
                path, r = stage_render(s, "b1_" + mid.replace("shell.", ""), id=mid)
                cases["b1.render." + mid] = verdict({} if r.get("w") and r.get("h") else {"크기": r}, "팝업 PNG", path=path)
            except session.Abort as e:  # 숨은 하위 메뉴 부모 등 — 판정은 menu.icons 로
                s.meta.setdefault("render_skipped", {})[mid] = str(e)  # 사례에 넣으면 RECORDED 가 전체 판정을 덮는다
        menu = (s.call("menu.icons", id=top).get("menus") or [{}])[0]
        ids = [it["id"] for it in listed if it.get("id") and not it.get("separator")]
        cases["b1.menu." + top] = judge_menu(menu, ids)


# ---------- B2 ----------
def judge_window(row, want, menu_key):
    if row is None:
        return {"verdict": "FAIL", "why": "열린 창을 찾지 못했다(창 아이콘 키 %s)" % want}
    bad = {}
    if menu_key != want:
        bad["메뉴 키"] = [menu_key, want]
    if row.get("iconKey") != want:
        bad["창 아이콘 키"] = [row.get("iconKey"), want]
    bad.update({k: row.get(k) for k in ("explicit", "big", "small") if row.get(k) is not True})
    return verdict(bad, "창 아이콘 = 메뉴 아이콘", title=row.get("title"), kind=row.get("kind"))


def judge_doc(doc, want, menu_key, sdi_row=None, sdi=False):
    if doc is None:
        return {"verdict": "FAIL", "why": "새 문서가 열리지 않았다"}
    bad = {}
    if menu_key != want or doc.get("iconKey") != want:
        bad["키"] = {"menu": menu_key, "doc": doc.get("iconKey"), "want": want}
    if sdi:
        w = judge_window(sdi_row, want, menu_key)
        if w["verdict"] != "PASS":
            bad["SDI OS 창"] = w.get("problems") or w.get("why")
        elif sdi_row.get("docIconKey") not in (None, want):
            bad["SDI 활성 문서 키"] = sdi_row.get("docIconKey")
    return verdict(bad, "문서·창 아이콘 = 메뉴 아이콘", doc=doc.get("id"))


def menu_keys(s):
    """메뉴 항목 id → (아이콘 키, 보임). 보임은 menu.icons 의 visible(팝업을 열지 않고 읽음)."""
    keys = {}
    for menu in s.call("menu.icons").get("menus") or []:
        keys.update({it.get("id"): (it.get("iconKey"), it.get("visible") is not False) for it in menu.get("items") or []})
    return keys


def press(s, path, item):
    for mid in path + (item,):
        s.call("ui.click", id=mid)
        time.sleep(0.6)


icon_rows = lambda s: s.call("window.icon").get("windows") or []  # noqa: E731


def b2(s, cases):  # 문서 6종(MDI·SDI) → 시스템 창 10종
    keys = menu_keys(s)
    for item, path, want, sdi in DOC_CASES:
        s.call("docs.close", all=True)
        time.sleep(1)
        press(s, path, item)
        time.sleep(2.5)
        docs = s.call("window.load.state").get("docs") or []
        doc = docs[-1] if docs else None
        row = next((r for r in icon_rows(s) if r.get("kind") == "sdi"), None) if sdi else None
        cases["b2.%s" % item] = judge_doc(doc, want, keys.get(item, (None,))[0], row, sdi)
        if sdi:
            s.capture("b2_" + item.replace("shell.", ""))
    s.call("docs.close", all=True)
    for item, path, want, close, view in SYS_CASES:
        before = {(r.get("title"), r.get("iconKey")) for r in icon_rows(s)}
        menu_key, shown = keys.get(item, (None, True))
        how = "menu"
        if view and not shown:
            s.call("sys.open", view=view)  # 조건부로 숨는 항목(재시작 대기 없음 등) — 같은 창 열기 경로
            how = "sys.open(메뉴 숨김)"
        else:
            press(s, path, item)
        row = None
        for _ in range(10):
            time.sleep(0.5)
            row = next((r for r in icon_rows(s) if r.get("kind") != "main" and (r.get("title"), r.get("iconKey")) not in before), None)
            if row:
                break
        cases["b2.%s" % item] = dict(judge_window(row, want, menu_key), opened_by=how)
        if close == "settings":
            s.capture("b2_" + item.replace("shell.", ""))
            s.call("settings.esc")
        else:
            s.call("sys.close", name=close)
        time.sleep(1)


# ---------- B10(D-5) ----------
def b10(s, cases):
    """창 메뉴 팝업을 menu.render 로 그려 왼쪽 띠(체크 칸+아이콘 칸)의 마지막 N 행(= 보이는 열린 창 칸, 메뉴 맨 끝)을 잘라 나란히 두고,
    표 문서를 활성화해 다시 조회·렌더한다(✓ 이동). 판정 규칙은 c27_b10.judge_b10_field."""
    from PIL import Image

    slots, docs, active = b10_open(s)
    path, _ = stage_render(s, "b10_window_menu", id="shell.mnuWindow")  # 메뉴 전체(체크 칸 들여쓰기 포함) 증거
    img = Image.open(path).convert("RGB")
    n = sum(1 for it in slots if it.get("visible"))
    bands = icon_bands(img, 0, max(8, img.width * 15 // 100))
    crops = [img.crop((0, max(0, y0 - (y1 - y0) // 2), img.width * 45 // 100, min(img.height, y1 + (y1 - y0) // 2))) for y0, y1 in bands[-n:]] \
        if n >= 2 and len(bands) >= n else []
    if not crops:
        s.meta["b10_crop_problem"] = "보이는 열린 창 칸 %d개·아이콘 행 %d개 — 크롭 생략(필드 판정은 그대로)" % (n, len(bands))
    s.call("docs.activate", doc=docs[0]["id"])  # 표 문서 활성화 → ✓ 가 칸 1 로 옮겨 가야 한다
    time.sleep(1)
    stage_render(s, "b10_window_menu_moved", id="shell.mnuWindow")
    cases.update(b10_cases(s, slots, docs, active, crops, slots_of(s), active_doc(s)))
    if not crops and "b10.read" in cases:
        cases["b10.read"] = {"verdict": "INVALID", "why": s.meta["b10_crop_problem"]}


def judge(s, part):
    if s.call("window.load.state").get("docs"):
        raise session.Abort("INVALID", "문서가 열려 있다(깨끗한 격리 폴더로 다시)")
    cases = {}
    try:
        if part in ("b1", "all"):
            s.call("docs.open", kind="grid")  # 열린 창 칸 1번이 문서 아이콘으로 보이게
            time.sleep(2.5)
            b1(s, cases)
        if part == "b10":  # 판독용(B10) — all 에 넣지 않는다(B1·B2 판정과 따로 돈다)
            b10(s, cases)
        if part in ("b2", "all"):
            b2(s, cases)  # 첫 사례가 docs.close 로 시작한다
    finally:
        try:
            s.call("settings.esc")  # 중간에 멈췄으면 설정(앱 모달)이 열린 채 남지 않게 — 열려 있지 않으면 실패해도 무시
        except session.Abort:
            pass
        s.call("docs.close", all=True)
    return cases


def self_test():
    win = {"kind": "other", "title": "설정", "iconKey": "K", "explicit": True, "big": True, "small": True}
    ok_item = {"id": "a", "iconKind": "window", "iconKey": "K", "geometryFound": True, "iconPresent": True, "visible": True}
    slot1 = dict(ok_item, id="shell.mnuWinList1", iconKind="doc")
    return session.run_cases([
        ("항목 PASS", judge_item(ok_item) is None),
        ("아이콘 없음", judge_item(dict(ok_item, iconKind="none")) == "아이콘 없음"),
        ("리소스 없음", "리소스" in judge_item(dict(ok_item, geometryFound=False))),
        ("첨부 키만", "표시" in judge_item(dict(ok_item, iconPresent=False))),
        ("스냅 칸 키 없이 PASS", judge_item({"iconKind": "snap", "visible": True}) is None),
        ("숨은 열린 창 칸 제외(doc)", judge_item({"iconKind": "doc", "visible": False}) is None),
        ("숨은 칸 제외(window·키 없음, 웹 실측 사례)", judge_item({"iconKind": "window", "iconKey": None, "visible": False}) is None),
        ("보이는 칸 키 없음은 FAIL", judge_item({"iconKind": "window", "iconKey": None, "visible": True}) is not None),
        ("열린 창 칸 PASS", judge_win_slots([slot1, dict(slot1, id="shell.mnuWinList2", visible=False, iconKind="window", iconKey=None)],
                                           [{"iconKey": "K"}])["verdict"] == "PASS"),
        ("열린 창 칸 수 모자람 FAIL", judge_win_slots([slot1], [{"iconKey": "K"}, {"iconKey": "K"}])["verdict"] == "FAIL"),
        ("열린 창 칸 아이콘 없음 FAIL", judge_win_slots([dict(slot1, iconKind="window", iconKey=None)], [{"iconKey": "K"}])["verdict"] == "FAIL"),
        ("열린 창 칸 다른 문서 키 FAIL", judge_win_slots([dict(slot1, iconKey="X")], [{"iconKey": "K"}])["verdict"] == "FAIL"),
        ("문서 0개 INVALID", judge_win_slots([slot1], [])["verdict"] == "INVALID"),
        ("메뉴 PASS", judge_menu({"items": [ok_item]}, ["a"])["verdict"] == "PASS"),
        ("menu.list 항목 누락 FAIL", judge_menu({"items": [ok_item]}, ["a", "b"])["verdict"] == "FAIL"),
        ("빈 메뉴 INVALID", judge_menu({"items": []}, [])["verdict"] == "INVALID"),
        ("창 PASS", judge_window(win, "K", "K")["verdict"] == "PASS"),
        ("창 키 다름 FAIL", judge_window(dict(win, iconKey=None), "K", "K")["verdict"] == "FAIL"),
        ("메뉴 키 다름 FAIL", judge_window(win, "K", "X")["verdict"] == "FAIL"),
        ("WM_GETICON small 없음 FAIL", judge_window(dict(win, small=False), "K", "K")["verdict"] == "FAIL"),
        ("창 못 찾음 FAIL", judge_window(None, "K", "K")["verdict"] == "FAIL"),
        ("문서 PASS", judge_doc({"id": "d", "iconKey": "K"}, "K", "K")["verdict"] == "PASS"),
        ("SDI OS 창 키 다름 FAIL", judge_doc({"id": "d", "iconKey": "K"}, "K", "K", dict(win, iconKey="A"), True)["verdict"] == "FAIL"),
        ("SDI PASS", judge_doc({"id": "d", "iconKey": "K"}, "K", "K", dict(win, kind="sdi", docIconKey="K"), True)["verdict"] == "PASS"),
        ("문서 안 열림 FAIL", judge_doc(None, "K", "K")["verdict"] == "FAIL"),
        ("계약 표 키 형식", all(c[2].startswith(("PdMenuIcon", "PdShellIcon", "PdCmdIcon")) for c in DOC_CASES + SYS_CASES)),
    ])


def main():
    ap = add_c27_args(session.parser(__doc__))
    ap.add_argument("--part", choices=("b1", "b2", "all", "b10"), default="all")
    ap.set_defaults(remote_only=True)
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    items = {k: ("b1." if k == "B1" else "b2.", METHOD[k]) for k in ("B1", "B2") if args.part in (k.lower(), "all")}
    if args.part == "b10":
        items = {"B10": ("b10.", B10_METHOD)}
    return run_c27(args, "menu_icons_judge", lambda s: judge(s, args.part), items)


if __name__ == "__main__":
    sys.exit(main())

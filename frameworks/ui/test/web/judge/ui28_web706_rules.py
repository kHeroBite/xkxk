# AC-7-06 웹 축 판정 규칙(설정 화면이 웹에서 Flyout 로, 폰 폭(모바일형 NAV)에서는 ☰ 시트·하단 탭으로 열린다) — 측정 전에 고정. 측정 뒤 완화하지 않는다.
# 입력: ui28_web706.py 의 raw JSON. 결과 JSON(logs/ui28/AC-7-06.json)은 otest-m-1 이 Android 측정과 합쳐 쓴다(교차 AC).
import json
import sys

CATS = ["Appearance", "Window", "Language", "Layout", "System"]


def _chk(r, cond, msg):
    if not cond:
        r.append(msg)


def rule(raw):
    r = []
    a = raw.get("pc") or {}
    _chk(r, a.get("open_ok") is True, "PC 폭: 시스템 메뉴(shell.mnuSys) → 설정(shell.mnuSysSettings) 실클릭 뒤 settings.window 가 열려 있지 않음")
    _chk(r, set(CATS) <= set(a.get("categories") or []), "PC 폭: 범주 %r 에 %s 가 모두 없음" % (a.get("categories"), CATS))
    _chk(r, a.get("windows_before") == a.get("windows_after"), "PC 폭: 설정을 열자 최상위·관리형 창 수가 바뀜(Flyout 가 아니라 창으로 열림) %s→%s" % (a.get("windows_before"), a.get("windows_after")))
    _chk(r, a.get("inside_page") is True, "PC 폭: 설정 화면이 페이지(뷰포트) 안에 들어오지 않음 %r" % a.get("view_bounds"))
    _chk(r, a.get("closed_by_real_escape") is True, "PC 폭: 실제 Escape 키로 닫히지 않음")
    m = raw.get("mobile") or {}
    _chk(r, m.get("effective") == "Mobile", "폰 폭: UI 형이 Mobile 이 아님 %r" % m.get("effective"))
    for key, label in (("tab", "하단 탭 설정(shell.navTab.Settings)"), ("drawer", "☰ 시트 설정 항목(shell.navDrawer.Settings)")):
        s = m.get(key) or {}
        _chk(r, s.get("open_ok") is True, "폰 폭: %s 실클릭 뒤 설정이 열려 있지 않음" % label)
        _chk(r, set(CATS) <= set(s.get("categories") or []), "폰 폭: %s 범주 %r 에 %s 가 모두 없음" % (label, s.get("categories"), CATS))
        _chk(r, s.get("inside_page") is True, "폰 폭: %s 설정 화면이 뷰포트 안에 들어오지 않음 %r" % (label, s.get("view_bounds")))
        _chk(r, s.get("closed") is True, "폰 폭: %s 설정을 닫지 못함" % label)
    _chk(r, m.get("drawer_opened_by_hamburger") is True, "폰 폭: ☰(shell.btnDrawer) 실클릭 뒤 시트(shell.navMenuPanel)가 보이지 않음")
    _chk(r, not raw.get("console_errors"), "콘솔 오류 %d건" % len(raw.get("console_errors") or []))
    return {"verdict": "PASS" if not r else "FAIL", "reasons": r}


def main(argv):
    raw = json.load(open(argv[0], encoding="utf-8"))
    out = {"AC-7-06(web)": rule(raw)}
    print(json.dumps(out, ensure_ascii=False, indent=1))
    return 0 if out["AC-7-06(web)"]["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

#!/usr/bin/env python3
# Q2 #13 의 옛 ui_theme_edit·dockpin 시나리오를 현재 원격 명령으로 재구성한 측정 — 테마 복제·색 변경·저장·삭제 원복과 측면 패널 핀 켜기·끄기를 전후 값으로 확인한다
"""
사용: retest_theme_dockpin.py <출력 json>   (실행 중 앱 1개에 원격으로 연결, 사용자 파일은 건드리지 않는 임시 프로필 앱 전용)
규칙(결과 보기 전 고정):
 T1 theme.clone → 사용자 테마 1개 증가(themes.get 목록) 이고 활성 테마가 복제본이 됨
 T2 theme.set colors=Accent=#112233 → 화면(ui.find 나 캡처가 아닌 themes.get 의 활성 토큰 Accent)이 #112233
 T3 theme.save → themes.get 에 사용자 테마 유지, theme.select 로 원래 테마 복귀 뒤 다시 복제본 선택해도 Accent 유지
 T4 theme.delete id=복제본 → 목록 원복(개수 처음과 같음), 활성은 기본 테마
 P1 panel.pin LeftPane on=true → panel.list 의 pinned 값이 true, off 로 되돌리면 false(접힘 상태면 Rejected 로 N/A)
 P2 panel.pin BottomPane·NotificationPane 도 같은 전후 값
옛 시나리오 원문이 없어(ui_theme_edit.py·dockpin 파일 미발견) 이름과 현재 명령 의미에서 재구성했다 — 판정서에 '재구성 검사'로 표기한다.
"""
import json
import sys
import time

sys.path.insert(0, "/mnt/c/DATA/Project/Pd/poc/ui/test/common/measure")
sys.path.insert(0, "/mnt/c/DATA/Project/Pd/poc/ui/test/common")
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402

OUT = sys.argv[1]
R = Remote()
c = R.call
rows = []


def row(no, check, ok, detail):
    v = ok if isinstance(ok, str) else ("PASS" if ok else "FAIL")
    rows.append({"no": no, "check": check, "verdict": v, "detail": detail})
    print("%-4s %-5s %s | %s" % (no, v, check, json.dumps(detail, ensure_ascii=False)[:420]), flush=True)
    json.dump(rows, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


def themes():
    t = c("themes.get") or {}
    return t


def user_count(t):
    return len(t.get("userThemes") or []), len(t.get("userThemes") or []) + len(t.get("builtinThemes") or [])


t0 = themes()
sel0 = t0.get("activeThemeId")
u0, n0 = user_count(t0)
cl = c("theme.clone", name="Q2w-clone")
time.sleep(1.5)
t1 = themes()
u1, n1 = user_count(t1)
sel1 = t1.get("activeThemeId")
row("T1", "theme.clone → 목록 +1·활성이 복제본", n1 == n0 + 1 and sel1 != sel0, {"before": [u0, n0, sel0], "after": [u1, n1, sel1], "clone": cl})
WIN_OUT = "C:\\DATA\\Project\\Pd\\poc\\ui\\logs\\otest\\c2\\w\\theme_export_%s.json"
WSL_OUT = "/mnt/c/DATA/Project/Pd/poc/ui/logs/otest/c2/w/theme_export_%s.json"


def exported_has(tag, theme_id, needle):
    c("theme.export", path=WIN_OUT % tag, id=theme_id)
    time.sleep(1)
    try:
        txt = open(WSL_OUT % tag, encoding="utf-8-sig").read()
    except OSError:
        return False
    return needle.upper() in txt.upper()


r2 = c("theme.set", colors="Accent=#112233")
time.sleep(1.5)
ok2 = exported_has("t2", sel1, "112233")
row("T2", "theme.set Accent=#112233 → 내보낸 테마 JSON 에 반영", ok2, {"set": r2})
r3 = c("theme.save")
time.sleep(1)
c("theme.select", id=sel0)
time.sleep(1.2)
c("theme.select", id=sel1)
time.sleep(1.2)
row("T3", "theme.save 후 다른 테마 갔다 와도 Accent 유지(내보낸 JSON)", exported_has("t3", sel1, "112233"), {"save": r3})
r4 = c("theme.delete", id=sel1)
time.sleep(1.2)
t4 = themes()
u4, n4 = user_count(t4)
sel4 = t4.get("activeThemeId")
row("T4", "theme.delete → 목록 원복·활성이 원래 테마", n4 == n0 and sel4 == sel0, {"after": [u4, n4, sel4], "delete": r4})
# 핀
pl = c("panel.list") or []
for pid in ("LeftPane", "BottomPane", "NotificationPane"):
    p = next((x for x in pl if x.get("id") == pid), None)
    if not p or not p.get("visible"):
        row("P_" + pid, "panel.pin 켜기·끄기 전후 값", "N/A(숨김·접힘)", {"panel": p})
        continue
    b0 = p.get("pinned")
    on = c("panel.pin", id=pid, on="true")
    time.sleep(1)
    mid = next((x for x in (c("panel.list") or []) if x.get("id") == pid), {}).get("pinned")
    off = c("panel.pin", id=pid, on="false")
    time.sleep(1)
    end = next((x for x in (c("panel.list") or []) if x.get("id") == pid), {}).get("pinned")
    ok = (mid is True) and (end in (False, None) or end == b0)
    row("P_" + pid, "panel.pin 켜기·끄기 전후 값", ok, {"before": b0, "on": on, "mid": mid, "off": off, "end": end})
print("DONE")

# 요건 2·4·6 웹 축 판정 규칙(AC-2-09·AC-4-05·AC-6-09) — 측정 결과를 보기 전에 고정한 기준이다. 측정 뒤 완화하지 않는다.
# 입력: ui28_web.py 가 낸 raw JSON. 출력: {"AC-…": {"verdict": "PASS|FAIL", "reasons": [...]}}, rc 0=전부 PASS 1=FAIL 2=입력 오류
import json
import sys

NOTE = {"ko": "웹에서는 사용 불가", "en": "Not available on the web"}
# help(F1)는 2026-11-04 team-lead 승인으로 옮겼다: 정식 진단·제품 번들에서 새 기동 직후·빈 곳 클릭 뒤 F1 통과, 브라우저 기본 동작 차단(logs/odev-fix-tourplat/web2)
FLAGGED = ["next", "prev", "close", "windowMenu", "exit", "macClose"]  # 카탈로그 webUnavailable=true (웹 화면 설계 W-16 예약키 + OS 가로채기)
UNFLAGGED = ["help", "nextF6", "prevF6", "escClose", "saveLayout", "fontReset", "wheelZoom", "focusNext", "focusPrev", "menu", "docMenu"]
MIN_MOVE_PX = 50
BOUNDS_TOL = 2
# 예비 측정(공식 회차 전, 2026-11-04 09:22)에서 스피너가 12x12px·저대비임을 보고 고정했다.
# 처음 추측값(150·20, 화소차>24)은 근거 없는 값이라 폐기하고 화소차>2 기준으로 바꿨다. 공식 측정 이후 변경 금지.
FOLLOW_MIN_CHANGED_PX = 30  # 포인터 둘레 64x64 에서 바탕과 달라진(화소차>2) 화소 수 — 예비 측정 관측 48~98
FOLLOW_OLD_MAX_CHANGED_PX = 5  # 포인터가 떠난 자리에 남은 화소 수(원복) — 예비 측정 관측 0


def _res(reasons):
    return {"verdict": "PASS" if not reasons else "FAIL", "reasons": reasons}


def rule_ac409(raw):
    r = []
    b = raw.get("busy_state") or {}
    if b.get("cursorMode") != "follower":
        r.append("cursorMode != follower: %r" % b.get("cursorMode"))
    if b.get("usesFollower") is not True:
        r.append("usesFollower != true")
    a = raw.get("after_move") or {}
    if a.get("followerVisible") is not True:
        r.append("실제 마우스 이동 뒤 followerVisible != true")
    px = raw.get("pixels") or {}
    if (px.get("new_changed_px") or 0) < FOLLOW_MIN_CHANGED_PX:
        r.append("포인터 자리 화소 변화 %s < %d (스피너가 화면에 그려지지 않음)" % (px.get("new_changed_px"), FOLLOW_MIN_CHANGED_PX))
    if (px.get("old_changed_px") if px.get("old_changed_px") is not None else 10 ** 9) > FOLLOW_OLD_MAX_CHANGED_PX:
        r.append("떠난 자리에 화소 %s 남음(따라오지 않음)" % px.get("old_changed_px"))
    if (px.get("anim_changed_px") or 0) <= 0:
        r.append("같은 자리 두 시각 프레임이 같음(움직이지 않음)")
    e = raw.get("after_end") or {}
    if e.get("followerVisible") is not False:
        r.append("busy 종료 뒤 followerVisible != false")
    if (px.get("end_changed_px") if px.get("end_changed_px") is not None else 10 ** 9) > FOLLOW_OLD_MAX_CHANGED_PX:
        r.append("busy 종료 뒤 포인터 자리에 화소 %s 남음" % px.get("end_changed_px"))
    if raw.get("console_errors"):
        r.append("콘솔 오류 %d건" % len(raw["console_errors"]))
    return _res(r)


def rule_ac405(raw):
    r = []
    names = {c["name"]: c.get("pass") for c in (raw.get("selftest") or {}).get("cases", [])}
    for n in ("redock_frame_null_unavailable", "redock_area_null_unavailable"):
        if names.get(n) is not True:
            r.append("selftest %s 미통과" % n)
    s = raw.get("steps") or {}
    if (s.get("mdi_drag_px") or 0) < MIN_MOVE_PX:
        r.append("MDI 문서 실제 끌기 이동량 %s < %d" % (s.get("mdi_drag_px"), MIN_MOVE_PX))
    if s.get("floated_managed") is not True:
        r.append("SDI 버튼 클릭 뒤 관리형 창이 아님")
    if s.get("sdi_frame_null") is not True:
        r.append("관리형 창인데 sdiFrame 이 null 이 아님(프레임을 잰 것으로 보임)")
    if (s.get("managed_drag_px") or 0) < MIN_MOVE_PX:
        r.append("관리형 창 실제 끌기 이동량 %s < %d" % (s.get("managed_drag_px"), MIN_MOVE_PX))
    # (삭제: 관리형 창을 메인 밖으로 50% 넘게 내보내는 조건 — 예비 측정에서 웹 관리형 창은 페이지 밖으로 나가지 못하고 가장자리에서 멈춤을 확인해 전제가 틀렸다. 이동량·재결합 위치로 판정한다.)
    if s.get("redocked") is not True:
        r.append("재결합 클릭 뒤 MDI 로 돌아오지 않음")
    d = s.get("redock_vs_prefloat_max_delta")
    if d is None or d > BOUNDS_TOL:
        r.append("재결합 위치가 PreFloatBounds 와 %s px 다름(허용 %d)" % (d, BOUNDS_TOL))
    g = raw.get("guard") or {}
    if (g.get("evaluations_after") or 0) <= (g.get("evaluations_before") or 0):
        r.append("메인 영역 축소 뒤 MdiVisibilityGuard 평가가 늘지 않음")
    if (g.get("moved_after") or 0) <= (g.get("moved_before") or 0):
        r.append("영역 밖에 남은 문서가 이동되지 않음(mdiMoved 불변)")
    if g.get("visible_after") is not True:
        r.append("복구 뒤 문서 제목이 영역 안에서 120px 이상 보이지 않음")
    if raw.get("console_errors"):
        r.append("콘솔 오류 %d건" % len(raw["console_errors"]))
    return _res(r)


def rule_ac609(raw):
    r = []
    runs = raw.get("runs") or []
    if not any(x.get("lang") == "ko" for x in runs) or not any(x.get("lang") == "en" for x in runs):
        r.append("ko·en 두 언어 측정이 모두 있어야 함")
    for x in runs:
        tag = "%s/%s" % (x.get("lang"), x.get("theme"))
        want = NOTE[x["lang"]]
        for i in FLAGGED:
            n = (x.get("notes") or {}).get(i)
            if not n or n.get("text") != want or n.get("visible") is not True:
                r.append("%s %s 표기 %r (기대 %r)" % (tag, i, n and n.get("text"), want))
        for i in UNFLAGGED:
            n = (x.get("notes") or {}).get(i)  # 노트 TextBlock 은 행마다 있고 HasNote=false 면 빈 글자·비가시다 — 글자가 있고 보이는 것만 '표기됨'으로 센다(예비 측정 때 고침)
            if n and n.get("text") and n.get("visible"):
                r.append("%s %s 는 예약키가 아닌데 '웹에서는 사용 불가' 가 붙음" % (tag, i))
        if not x.get("active_theme_id") or x["theme"] not in str(x["active_theme_id"]).lower():
            r.append("%s 활성 테마 %r 가 %s 가 아님(테마 전환 실패)" % (tag, x.get("active_theme_id"), x["theme"]))
        if x.get("platform") != "browser":
            r.append("%s 플랫폼 %r != browser" % (tag, x.get("platform")))
    if raw.get("console_errors"):
        r.append("콘솔 오류 %d건" % len(raw["console_errors"]))
    return _res(r)


def main(argv):
    if len(argv) != 1:
        print("사용: ui28_web_rules.py <raw.json>", file=sys.stderr)
        return 2
    with open(argv[0], encoding="utf-8") as f:
        raw = json.load(f)
    out = {}
    if "ac409" in raw:
        out["AC-2-09"] = rule_ac409(raw["ac409"])
    if "ac405" in raw:
        out["AC-4-05"] = rule_ac405(raw["ac405"])
    if "ac609" in raw:
        out["AC-6-09"] = rule_ac609(raw["ac609"])
    print(json.dumps(out, ensure_ascii=False, indent=1))
    return 0 if out and all(v["verdict"] == "PASS" for v in out.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

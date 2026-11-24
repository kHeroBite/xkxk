# otest-12 포그라운드 시험 구동기 — Q6 윈도우 메뉴 항목(원본 함수 그대로, HOLD 분기만 우회)·재기동 유지 대조·Q12 SDI zIndex 를 원격 명령으로 실행하고 결과를 표준출력 JSON 한 줄로 낸다(파일 쓰기 없음)
"""
사용: python3 -u fg_drive.py q6            → 항목 실행, 마지막 줄 RESULT_JSON {...}(rows·persist·restore)
      python3 -u fg_drive.py q6persist '<persist·restore JSON>' → 재기동 뒤 유지 대조 + 되돌림
      python3 -u fg_drive.py q12open       → MDI 3 + SDI 2 열고 zIndex·MRU 판독
      python3 -u fg_drive.py q12after      → 재기동 뒤 SDI 존재·zIndex null 판독
      python3 -u fg_drive.py call <명령> '<인자 JSON>' → 원격 명령 1회(판독용)
판정 규칙: rules_otest12.md(결과 보기 전 고정).
"""
import json
import sys
import time
import types

sys.path.insert(0, "/mnt/c/DATA/Project/xkxk/frameworks/ui/tools")
sys.path.insert(0, "/mnt/c/DATA/Project/xkxk/frameworks/ui/test/common")  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_window_menu as rwm  # noqa: E402
import retest_window_menu_items as items  # noqa: E402
from retest_remote import Remote  # noqa: E402

Q6_ITEMS = [5, 6, 7, 8, 10, 11, 12, 121, 13]


def emit(obj):
    print("RESULT_JSON " + json.dumps(obj, ensure_ascii=False, default=str), flush=True)


def ctx_new():
    args = types.SimpleNamespace(primary_override=None, known_fail="", conf="i2410_diag", no_restart=True)
    return rwm.Ctx(Remote(), "desktop", args)


def run_q6(only=None):
    items.desktop_hold = lambda ctx: None  # 데스크톱 ④⑤⑥ HOLD 우회(사용자 14:59 승인) — 이 줄과 #7 target 처리만 원본과 다르다
    ctx = ctx_new()
    ctx.r.call("perf.reset")
    for no, name, fn in items.ITEMS:
        if no not in (only or Q6_ITEMS):
            continue
        ctx.target = "desktop-fg" if no == 7 else "desktop"  # #7 HOLD 분기(target=='desktop')만 피한다
        try:
            ok, detail = fn(ctx)
            verdict = ok if isinstance(ok, str) else ("PASS" if ok else "FAIL" if ok is not None else "N/A")
        except items.Pending as e:
            verdict, detail = "PENDING", "원격 명령 미구현: %s" % e
        except items.NotApplicable as e:
            verdict, detail = "N/A", str(e)
        except Exception as e:  # noqa: BLE001 — 한 항목 예외가 나머지를 막지 않게(러너와 같음)
            verdict, detail = "FAIL", repr(e)
        ctx.target = "desktop"
        ctx.row(no, name, verdict, detail)
        time.sleep(0.5)
    emit({"rows": ctx.rows, "persist": ctx.persist, "restore": ctx.restore})


def run_q65():
    """Q6 #5 만(기동 직후 첫 항목) — 원본 main_fit(ctx,'single') 그대로, HOLD 우회만."""
    items.desktop_hold = lambda ctx: None
    ctx = ctx_new()
    try:
        ok, detail = items.main_fit(ctx, "single")
        verdict = ok if isinstance(ok, str) else ("PASS" if ok else "FAIL")
    except Exception as e:  # noqa: BLE001
        verdict, detail = "FAIL", repr(e)
    ctx.row(5, "싱글 모니터 최대화(주 모니터 맞춤)", verdict, detail)
    emit({"rows": ctx.rows})


def untuple(v):
    """JSON 왕복으로 리스트가 된 기대값을 러너 check_persist 가 보는 튜플 형태로 되돌린다."""
    if isinstance(v, list) and v and v[0] in ("contains", "field", "titles"):
        return tuple(v)
    return v


def run_q6persist(blob):
    data = json.loads(blob)
    ctx = ctx_new()
    ctx.persist = [(c, k, untuple(e)) for c, k, e in data.get("persist", [])]
    rwm.check_persist(ctx)
    for cmd, kw in reversed(data.get("restore", [])):
        ctx.r.call(cmd, **{k: v for k, v in kw.items() if v is not None})
    emit({"rows": ctx.rows})


def zdocs(r):
    return [{k: d.get(k) for k in ("id", "title", "floating", "zIndex", "zScale", "zSource", "active")} for d in (r.call("docs.list") or [])]


def run_q12open():
    r = Remote()
    r.call("docs.close", all=True)
    for t in ("Q12-M1", "Q12-M2", "Q12-M3"):
        r.call("docs.open", kind="grid", title=t, allowDuplicate=True)
    for t in ("Q12-S1", "Q12-S2"):
        r.call("docs.open", kind="form", title=t, sdi=True, allowDuplicate=True)
    time.sleep(1.0)
    res = {"opened": zdocs(r)}
    ids = {d["title"]: d["id"] for d in res["opened"]}
    seq = []
    for t in ("Q12-S1", "Q12-S2", "Q12-M1", "Q12-S1"):
        r.call("docs.activate", doc=ids.get(t))
        time.sleep(0.6)
        seq.append({"activated": t, "docs": zdocs(r)})
    res["mru"] = seq
    emit(res)


def run_q12after():
    emit({"after": zdocs(Remote())})


def run_call(cmd, blob):
    emit({"cmd": cmd, "result": Remote().call(cmd, **(json.loads(blob) if blob else {}))})


if __name__ == "__main__":
    m = sys.argv[1]
    if m == "q6":  # 선택 인자: 항목 번호 쉼표 목록(없으면 Q6_ITEMS)
        run_q6([int(x) for x in sys.argv[2].split(",")] if len(sys.argv) > 2 else None)
    elif m == "q65":
        run_q65()
    elif m == "q6persist":
        run_q6persist(sys.argv[2])
    elif m == "q12open":
        run_q12open()
    elif m == "q12after":
        run_q12after()
    elif m == "call":
        run_call(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "")
    else:
        sys.exit("모드: q6 | q6persist | q12open | q12after | call")

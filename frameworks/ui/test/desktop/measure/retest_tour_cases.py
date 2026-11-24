# 요건 6 도움말 투어 측정 공용부 — 원격 호출 도우미와 창 숨김·설정 창·UI 방식별 측정(retest_tour.py 가 부른다)
import time

SETTLE_S = 0.35  # 200ms 전환이 끝난 뒤 값을 읽는다
MAX_STEPS = 60  # 진행 무한 반복 방지


def safe(r, cmd, **kw):
    """명령 실패도 측정값이다 — 예외 대신 {"error": 문구} 로 남긴다."""
    try:
        return r.call(cmd, **kw)
    except Exception as ex:  # noqa: BLE001 — 측정 도구는 멈추지 않고 실패를 기록한다
        return {"error": str(ex)}


def settle(r, cmd, **kw):
    result = safe(r, cmd, **kw)
    time.sleep(SETTLE_S)
    return result


def visible(r, control_id, **kw):
    node = safe(r, "ui.find", id=control_id, **kw)
    return bool(isinstance(node, dict) and node.get("effectivelyVisible"))


def text(r, control_id):
    node = safe(r, "ui.find", id=control_id)
    return node.get("text") if isinstance(node, dict) else None


def sessions(r):
    state = safe(r, "tour.state")
    return state if isinstance(state, list) else []


def documents(r):
    listing = safe(r, "docs.list")
    items = listing.get("documents", listing) if isinstance(listing, dict) else listing
    return items if isinstance(items, list) else []


def doc_ids(r):
    return [d.get("id") for d in documents(r)]


def end_all(r):
    for session in sessions(r):
        safe(r, "tour.end", window=session.get("window"))
    time.sleep(SETTLE_S)


def measure_hidden_main(r):
    """최소화·트레이 숨김 상태의 메인 창에서 트레이 메뉴 도움말: 창을 되살린 뒤 투어가 시작돼야 한다."""
    out = {}
    for name, cmd, kw in (("minimized", "tray.minimizeMain", {}), ("hidden", "tray.invoke", {"item": "hide"})):
        settle(r, cmd, **kw)
        case = {"before": safe(r, "window.snapshot"), "probeBefore": safe(r, "tour.snapshot", window=0)}
        settle(r, "tray.invoke", item="help")
        time.sleep(SETTLE_S)
        case["after"] = safe(r, "window.snapshot")
        case["probeAfter"] = safe(r, "tour.snapshot", window=0)
        case["sessions"] = sessions(r)
        case["overlay"] = visible(r, "tour.overlay")
        end_all(r)
        safe(r, "tray.invoke", item="show")
        time.sleep(SETTLE_S)
        out[name] = case
    return out


def measure_settings(r):
    """통합 설정 창 투어: F1(라우팅 키)로 그 창의 단계를 시작하고 끝까지 간다."""
    settle(r, "sys.open", view="Settings")
    out = {"sysOpen": (safe(r, "sys.get") or {}).get("open")}
    tops = safe(r, "tree.get", window=1, depth=1)
    out["hasSecondWindow"] = isinstance(tops, dict) and "error" not in tops
    out["f1"] = settle(r, "tour.key", key="F1", window=1)
    out["start"] = sessions(r)
    steps = []
    for _ in range(MAX_STEPS):
        now = sessions(r)
        if not now or now[0].get("onShortcutLayer"):
            break
        steps.append((now[0].get("stepId"), now[0].get("index"), now[0].get("count")))
        settle(r, "tour.next", window=now[0].get("window"))
    out["steps"] = steps
    end_all(r)
    out["close"] = settle(r, "ui.click", id="settings.btnClose")
    return out


def measure_modes(r):
    """UI 방식별 단계 집합: 태블릿형(DOCK)·모바일형(NAV). 끝나면 자동으로 되돌린다."""
    out = {}
    for name in ("tablet", "mobile"):
        case = {"style": settle(r, "ui.style", set=name)}
        time.sleep(1.0)  # 방식 전환 애니메이션
        settle(r, "tour.start")
        steps = []
        for _ in range(MAX_STEPS):
            now = sessions(r)
            if not now or now[0].get("onShortcutLayer"):
                case["last"] = now
                break
            steps.append((now[0].get("scope"), now[0].get("stepId"), now[0].get("index"), now[0].get("count")))
            settle(r, "tour.next")
        case["steps"] = steps
        end_all(r)
        out[name] = case
    out["restore"] = settle(r, "ui.style", set="auto")
    time.sleep(1.0)
    return out

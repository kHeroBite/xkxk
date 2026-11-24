# RETEST MDI-Z 단계의 문서 목록 해석 보조 함수 — 화면 쌓임·저장 순서·핀/잠금·패널 탭 기록·zIndex 재조회
import json
import time

TITLES = ["Z1", "Z2", "Z3", "Z4", "Z5"]
DIAG_KEYS = ("id", "window", "visible", "floating", "managedWindow", "state", "zIndex", "childIndex", "zReason", "modelZIndex")


def by_title(docs):
    return {d.get("title"): d for d in docs or []}


def is_mdi(d):
    """MDI 창 문서인지 본다.

    w7 부터 패널 탭 문서도 zIndex 와 zScale="panelTab" 을 가진다.
    zScale 이 없는 옛 판은 모든 문서를 MDI 로 본다(예전처럼 zIndex 유무로만 거른다).
    """
    if "zScale" in d:
        return d.get("zScale") == "mdi"
    return True


def screen_order(docs):
    """docs.list → 화면 쌓임(아래→위) 제목 목록. MDI 창만 본다.

    null 판정은 docs_with_z 가 모든 문서로 한다.
    """
    mdi = []
    for d in docs or []:
        if not is_mdi(d):
            continue
        if d.get("zIndex") is None:
            continue
        if d.get("title") not in TITLES:
            continue
        mdi.append(d)
    ordered = sorted(mdi, key=lambda d: (d["zIndex"], d["childIndex"]))
    return [d["title"] for d in ordered]


def panel_tabs(docs):
    """패널 탭 문서의 (제목, zIndex) 목록. 기록 전용이며 판정에는 쓰지 않는다."""
    return [(d.get("title"), d.get("zIndex")) for d in docs or [] if d.get("zScale") == "panelTab"]


def saved_order(layout):
    """layout.get → (ZOrder 아래→위 제목 목록, 활성 제목, {제목: (IsPinned, IsLocked)})."""
    docs = [d for d in (layout or {}).get("Documents", []) if d.get("Title") in TITLES]
    order = [d["Title"] for d in sorted(docs, key=lambda d: (d.get("ZOrder") is None, d.get("ZOrder") or 0))]
    active = next((d["Title"] for d in docs if d.get("Id") == (layout or {}).get("ActiveId")), None)
    flags = {d["Title"]: (d.get("IsPinned"), d.get("IsLocked")) for d in docs}
    return order, active, flags


def chrome_flags(r, docs):
    out = {}
    for t, d in by_title(docs).items():
        if t in TITLES:
            s = r.call("chrome.state", doc=d["id"]) or {}
            out[t] = (s.get("pinned"), s.get("locked"))
    return out


def docs_with_z(r, label, tries=5):
    """
    docs.list 를 받아 Z1~Z5 의 zIndex 가 모두 있는지 본다. null 이 있으면 0.5초 간격으로 최대 tries 회 다시 조회.
    끝까지 null 이면 문서별 진단(창 번호·보임·분리·상태·zReason·modelZIndex — 앞의 둘은 빌드에 있을 때만)을 검사 기록에 남긴다.
    """
    docs = []
    for i in range(tries + 1):
        docs = r.call("docs.list") or []
        mine = [d for d in docs if d.get("title") in TITLES]
        if mine and all(d.get("zIndex") is not None for d in mine):
            if i:
                r.check("%s: zIndex 재조회 %d회 뒤 확보" % (label, i), True)
            return docs
        time.sleep(0.5)
    diag = {d.get("title"): {k: d.get(k) for k in DIAG_KEYS if k in d} for d in docs if d.get("title") in TITLES}
    r.check("%s: zIndex 확보(재조회 %d회)" % (label, tries), False, json.dumps(diag, ensure_ascii=False))
    return docs

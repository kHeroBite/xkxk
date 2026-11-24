# 레이아웃 템플릿 적용 단계마다 활성 문서 본문이 보이는지 확인(D-2 집중→계단식 빈 화면 결함 — lead 06:3x) + 템플릿 순서 조합 항목
"""
판정: 템플릿을 적용할 때마다 활성 문서(docs.list active=true)의 본문 대표 요소를 ui.find doc=<문서> 로 찾아 effectivelyVisible=True 인지 본다.
  대표 요소(문서 종류 kind 별, Documents/*View.axaml AutomationId): grid=grid.lblCount, form=form.txtTitle, gantt=gantt.lblZoom.
  - 요소를 못 찾거나(트리에 없음) effectivelyVisible 이 True 가 아니면 False(FAIL).
  - 활성 문서가 없거나 대표 요소를 모르는 종류면 UNDET(판정 불능 — PASS 로 세지 않음).
with_body(fn): 기존 항목(#122·#124~#127)을 감싸, 그 항목 안의 layout.template.apply 호출마다 위 확인을 끼워 넣고 결과를 판정에 합친다.
it_template_order: 순서 조합(집중→계단식 등)을 단계마다 본문 확인하는 독립 항목.
"""
from retest_window_menu_focus import UNDET, verdict

BODY_IDS = {"grid": "grid.lblCount", "form": "form.txtTitle", "gantt": "gantt.lblZoom"}
ORDERS = [  # 각 줄 = 한 회차(앞 단계부터 차례로 적용). 첫 회차가 D-2 재현 순서
    ["tpl.focus", "tpl.mdi-cascade"],
    ["tpl.focus", "tpl.mdi-tile-v"],
    ["tpl.focus", "tpl.mdi-tile-h"],
    ["tpl.focus", "tpl.tabbed"],
    ["tpl.tabbed", "tpl.focus", "tpl.mdi-cascade"],
]


def body_state(ctx, step):
    """활성 문서 본문 대표 요소 확인 → {step, doc, kind, id, ok(True|False|UNDET), why?}."""
    act = next((d for d in ctx.call("docs.list") or [] if d.get("active")), None)
    if not act:
        return {"step": step, "ok": UNDET, "why": "활성 문서 없음"}
    kind, doc = act.get("kind"), act.get("id")
    bid = BODY_IDS.get(kind)
    if not bid:
        return {"step": step, "doc": doc, "kind": kind, "ok": UNDET, "why": "대표 요소 미정 종류"}
    f = ctx.call("ui.find", id=bid, doc=doc)
    if not f:
        err = ((ctx.r.calls[-1] if getattr(ctx.r, "calls", None) else {}) or {}).get("error")
        return {"step": step, "doc": doc, "kind": kind, "id": bid, "ok": False, "why": "본문 요소 없음(트리에 없음 또는 원격 오류)", "error": err}
    return {"step": step, "doc": doc, "kind": kind, "id": bid, "ok": f.get("effectivelyVisible") is True,
            "effectivelyVisible": f.get("effectivelyVisible"), "bounds": f.get("bounds")}


def _merge(res, body):
    """항목 결과(ok, detail)에 본문 확인을 합친다: False 하나면 FAIL, UNDET 있으면 PASS 를 UNDET 로 낮춘다."""
    ok, detail = res
    vals = [b["ok"] for b in body]
    detail = dict(detail, bodyChecks=body) if isinstance(detail, dict) else {"detail": detail, "bodyChecks": body}
    if not body or (isinstance(ok, str) and ok != UNDET):
        return ok, detail  # 본문 확인이 없었거나 N/A·HOLD 등 — 그대로
    if any(v is False for v in vals):
        return False, detail
    if ok is True and any(v == UNDET for v in vals):
        return UNDET, detail
    return ok, detail


def with_body(fn):
    """항목 fn 실행 중 layout.template.apply 가 불릴 때마다 본문 확인을 덧붙인다(ctx.call 을 잠시 감싼다)."""
    def run(ctx):
        body, orig = [], ctx.call

        def call(cmd, **kw):
            r = orig(cmd, **kw)
            if cmd == "layout.template.apply":
                body.append(body_state(ctx, kw.get("id")))
            return r
        ctx.call = call
        try:
            res = fn(ctx)
        finally:
            ctx.call = orig
        return _merge(res, body)
    return run


def it_template_order(ctx):
    """#128 템플릿 순서 조합: 회차마다 tpl.mdi-cascade 에서 시작해 차례로 적용하고, 단계마다 활성 문서 본문이 보여야 한다."""
    rounds = []
    for seq in ORDERS:
        ctx.call("layout.template.apply", id="tpl.mdi-cascade")
        steps = []
        for t in seq:
            applied = ctx.call("layout.template.apply", id=t) is True
            b = body_state(ctx, t)
            b["applied"] = applied
            steps.append(b)
        rounds.append({"order": "→".join(seq), "steps": steps})
    ctx.call("layout.template.apply", id="tpl.mdi-cascade")  # 정리: 집중 해제 상태로
    checks = {}
    for r in rounds:
        for i, s in enumerate(r["steps"]):
            checks["%s #%d %s" % (r["order"], i + 1, s["step"])] = s["ok"] if s["applied"] else False
    return verdict(True, checks), {"basis": "lead 06:3x — D-2(집중→계단식 뒤 본문 빈 화면) 재발 감시", "rounds": rounds, "checks": checks}

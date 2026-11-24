#!/usr/bin/env python3
# F08 공식 판정 새 판 — 원본 retest_pcaway_extra2.py 를 재사용해 끝점을 MDI 영역 왼쪽 밖(시험 앱 창 안)으로 두고 끌기 중 grab·커서를 기록해 잔존 421 의 원인을 가린다
"""
사용:
  python3 -B retest_pcaway_extra2_f08v2.py --run-dir <bin/run/<복사본>/net10.0> --phase final --writer otest-d [--monitor DISPLAY3]
  python3 -B retest_pcaway_extra2_f08v2.py --run-dir <...> --phase final --writer otest-d --via app-pointer   (앱 내부 포인터 — 진단 빌드 필요)
  python3 -B retest_pcaway_extra2_f08v2.py --self-test --session <세션 폴더>
--via app-pointer(11-09): OS 커서 경로는 창 안 제약으로 한계 도달이 수학적으로 불가하다(reach_analysis). 진단 ui.pointerDrag 로 제목 칸을 눌러
  Dock 의 실제 끌기 경로(누름→캡처→DragMove)를 앱 안에서 구동하고, 같은 판정식으로 잰다(OS 입력·전경 변화 없음).
출력: 원본과 같은 logs/pcaway[/discovery]/extra/F08.measure.json(MeasureOut 재사용). 이어서 pcaway_report.py emit --item F08 --measure-file <그 파일>.

원본과의 차이(11-09 새 판, 원본 파일·sha 불변):
- 원본은 끝점을 MDI 영역 왼쪽 +8 에 둔다. 잡는 점이 창 왼쪽에서 약 227 DIP 면 창 x 최소가 8−227=−219 라 잔존 421 에서 멈춘다(계산, 원인 미확정).
- 새 판은 제품 한계(잔존 158 DIP 근처)를 넘어서도록 끝점을 '한계 x − 여유(--overshoot-dip) + 잡는 점 거리' 로 계산해 MDI 영역 왼쪽 밖, 같은 시험 앱 창 안(탐색기 패널 위)에 둔다.
- 안전 게이트는 우회하지 않는다. 끝점은 (1) 시험 앱 메인 창 클라이언트 안, (2) 모니터 경계 100px 밖, (3) WindowFromPoint 소유자가 시험 앱 PID 인 점만 쓴다.
  셋 중 하나라도 어긋나면 끌지 않고 그 사실을 측정값으로 남긴다(safe_click 의 종료점 소유권 검사와 같은 조건을 미리 본다. safe_click 자체도 그대로 거친다).
  한계까지 못 가는 위치면 가장 먼 허용 점으로 줄이고 endpoint.reachable=false 로 남긴다(값을 만들지 않는다).
- 끌기 동안 placement.state(문서 bounds·screen·grab)를 --poll-ms 간격으로, 커서 화면 좌표를 PowerShell 표본기로 기록한다.
  끝난 뒤 cause 를 tool_endpoint_limit(커서가 한계에 닿을 만큼 못 감)·product_not_following(커서는 충분히 갔는데 창이 한계 전에 멈춤)·
  reached_limit·not_moved·undetermined 로 분류해 left_remain_measured 값에 넣는다. 분류는 기록이며 판정식에 쓰지 않는다.
- 판정식은 원본과 같다: left_remain_measured = 옮겨짐 and 0<share<1, grab_rule_kept = 보이는 폭−108 ≥ 50 and 보이는 폭 ≤ 잔존 기대+2. 완화 없음.
  잔존 기대(11-10) = min(창 폭, max(ceil(창 폭 × minVisiblePercent), 158)) — placement.state 의 비율을 읽는다(키 없음·0 이면 158 = 원본과 같음).
- 직전 '창 안 끝점 변형'에서 창이 움직이지 않았던 기록이 있어, 끌기 전·후 bounds 와 함께 표본 첫 이동 시각(first_move_ms)을 남겨 미이동을 구별한다.
안전 규칙은 retest_real_input.py 와 같다(자기 임시 data·profile·--multi-instance, 자기 PID 만 종료, 조작 앞 user_away_guard, 입력 직전 전경 확인).
종료 코드는 retest_real_input.drive 와 같다: 0=통과, 1=실패, 2=기동 실패, 3=거부, 5=사용자 복귀.
"""
import argparse
import base64
import json
import math
import os
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
import retest_pcaway_extra2 as ex2  # noqa: E402

REQ = {"F08": list(ex2.REQ["F08"])}
DOC_W, DOC_H = ex2.DOC_W, ex2.DOC_H
GRAB_RULE_DIP, GRAB_TOL = ex2.GRAB_RULE_DIP, ex2.GRAB_TOL
CAPTION_DIP = 108  # 원본 판정식의 108(캡션). 원본과 같은 값
LIMIT_X_DIP = -(DOC_W - GRAB_RULE_DIP)  # 제품 한계 추정: 잔존 158 DIP 일 때 창 x(−482). 원인 분류에만 쓰고 판정에는 쓰지 않는다


def expected_keep(win_w, pct, rule=GRAB_RULE_DIP):
    """왼쪽 이탈 잔존 기대(DIP) = min(창 폭, max(ceil(창 폭 × 비율 − 1e-9), 잡을 곳 규칙 158)). MdiDragLimit.KeepWhenLeftOut 과 같은 식, pct 는 0~90 으로 자른다. 비율 0 이면 158 그대로."""
    p = min(max(pct or 0, 0), 90) / 100.0
    return min(win_w, max(math.ceil(win_w * p - 1e-9) if p > 0 else 0, rule))


CLIENT_IN_PX = 12  # 끝점이 메인 창 클라이언트 왼쪽 끝에서 안쪽으로 떨어질 최소 거리(px)
CURSOR_PS = r"""
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PdC {
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v);
}
'@
try { [void][PdC]::SetProcessDpiAwareness(2) } catch { }
[Console]::Out.WriteLine('READY'); [Console]::Out.Flush()
$sw = [Diagnostics.Stopwatch]::StartNew(); $rows = New-Object System.Collections.Generic.List[string]
while ($sw.ElapsedMilliseconds -lt %(limit)d) {
  $p = New-Object PdC+PT; [void][PdC]::GetCursorPos([ref]$p)
  $rows.Add(('[' + $sw.ElapsedMilliseconds + ',' + $p.X + ',' + $p.Y + ']'))
  Start-Sleep -Milliseconds %(step)d
}
[Console]::Out.WriteLine('[' + ($rows -join ',') + ']')
"""


def judge(visible, share, moved, keep=GRAB_RULE_DIP):
    """원본 f08 과 같은 판정식(완화 금지). keep = 잔존 기대(expected_keep, 비율 0 이면 158). (left_remain_measured, grab_rule_kept)."""
    left_ok = bool(moved and 0 < share < 1)
    grab_ok = bool(moved and visible - CAPTION_DIP >= 50 and visible <= keep + GRAB_TOL)
    return left_ok, grab_ok


def choose_grab_offset(g, win_w, margin_dip):
    """제목줄 잡는 지점의 창 왼쪽 기준 오프셋(DIP). 잡을 곳 [left, w−rightInset] 의 오른쪽 끝에서 margin 안쪽(캡션 버튼 영역 밖).
    창 x = 커서 x − 오프셋 이므로 오프셋이 클수록 커서가 덜 왼쪽으로 가도 창이 한계(x = −(창 폭 − 158))에 닿는다. 잡을 곳이 margin*2 보다 좁으면 가운데로 되돌린다."""
    left, right = g["left"], win_w - g["rightInset"]
    if right - left >= 2 * margin_dip:
        off, kind = right - margin_dip, "right_end"
    else:
        off, kind = (left + right) / 2, "middle"
    return {"off_dip": round(off, 1), "kind": kind, "grab_left": left, "grab_right": right, "margin_dip": margin_dip,
            "formula": "off = (w − rightInset) − margin; 창 x = 커서 x − off; 한계 도달 커서 상대 = %d + off" % LIMIT_X_DIP}


def plan_endpoint(area_x, scale, grab_off_dip, client_l, mon_l, edge_margin, overshoot_dip, limit_x=LIMIT_X_DIP):
    """끝점 x(화면 물리 px). 목표 = 창 x 가 한계−여유에 닿는 커서 위치. 허용 하한(클라이언트 안·모니터 경계 밖)보다 왼쪽이면 하한으로 줄인다.
    돌려주는 값: {to_x, want_x, lower, reachable, cursor_rel_dip(끝점의 영역 원점 기준 DIP), window_x_at_end(제품이 커서를 따라갈 때 예상 창 x, 한계 미적용)}."""
    want_rel_dip = limit_x - overshoot_dip + grab_off_dip
    want_x = int(area_x + want_rel_dip * scale)
    lower = max(int(client_l + CLIENT_IN_PX), int(mon_l + edge_margin))
    to_x = max(want_x, lower)
    rel = (to_x - area_x) / scale
    return {"to_x": to_x, "want_x": want_x, "lower": lower, "reachable": to_x == want_x,
            "cursor_rel_dip": round(rel, 1), "window_x_at_end": round(rel - grab_off_dip, 1)}


MIN_MARGIN_DIP = 2.0  # 잡는 점을 캡션 버튼 영역에서 떼는 최소 거리(DIP) — 이보다 가까우면 버튼을 누를 수 있어 쓰지 않는다


def reach_analysis(area_x, scale, win_w, grab, lower_px, margin_dip, tol=GRAB_TOL, rule=GRAB_RULE_DIP):
    """끌기 한계 도달 가능성의 닫힌 식(기록용, 판정식 아님). 창 x(영역 기준 DIP) = 커서 rel − off, 한계 x = GRAB_RULE_DIP − w, off ≤ w − rightInset − margin.
    그러므로 커서가 갈 수 있는 가장 왼쪽 lower_rel 에서 남는 폭의 최소 = w + (lower_rel − off) = lower_rel + rightInset + margin 이고 w 가 약분된다.
    ⇒ 창 폭을 키우거나 끌기 전에 창을 오른쪽으로 옮겨도(시작 위치 무관) 도달 가능성은 변하지 않는다. 바뀌는 것은 lower_rel(커서 하한)과 rightInset·margin 뿐이다."""
    lower_rel = (lower_px - area_x) / scale
    remain_min = lower_rel + grab["rightInset"] + margin_dip
    return {"lower_rel_dip": round(lower_rel, 1), "rightInset": grab["rightInset"], "margin_dip": margin_dip, "win_w": win_w,
            "min_remain_dip": round(remain_min, 1), "rule_dip": rule, "can_reach_limit": remain_min <= rule + tol,
            "note": "min_remain = lower_rel + rightInset + margin (w 약분: 창 폭·시작 위치를 바꿔도 불변)"}


def plan_with_fallback(area_x, scale, g, win_w, client_l, mon_l, edge_margin, overshoot_dip, margin_dip, limit_x=LIMIT_X_DIP):
    """(마진, 여유) 순서 (m,o)→(m,0)→(MIN_MARGIN,0) 로 처음 reachable 인 계획. 모두 불가면 마지막 시도와 시도 목록을 돌려준다. 기준(한계 158 + 허용 오차)은 그대로."""
    tries, plan, choice = [], None, None
    for m, o in ((margin_dip, overshoot_dip), (margin_dip, 0.0), (MIN_MARGIN_DIP, 0.0)):
        choice = choose_grab_offset(g, win_w, m)
        plan = plan_endpoint(area_x, scale, choice["off_dip"], client_l, mon_l, edge_margin, o, limit_x)
        tries.append({"margin_dip": m, "overshoot_dip": o, "off_dip": choice["off_dip"], "want_x": plan["want_x"], "lower": plan["lower"], "reachable": plan["reachable"]})
        if plan["reachable"]:
            break
    return plan, choice, tries


def classify(before_x, final_x, cursor_min_rel_dip, grab_off_dip, tol=4.0, limit_x=LIMIT_X_DIP):
    """끌기 결과의 원인 분류(기록용). cursor_min_rel_dip = 끌기 중 커서가 간 가장 왼쪽(영역 원점 기준 DIP), None 이면 표본 없음."""
    if final_x is None or before_x is None:
        return "undetermined"
    if final_x >= before_x - 0.5:
        return "not_moved"
    if final_x <= limit_x + tol:
        return "reached_limit"
    if cursor_min_rel_dip is None:
        return "undetermined"
    if cursor_min_rel_dip - grab_off_dip > limit_x + tol:
        return "tool_endpoint_limit"
    return "product_not_following"


def first_move_ms(samples, before_x):
    """placement 표본에서 창 x 가 처음 바뀐 시각(끌기 시작 기준 ms). 없으면 None."""
    for s in samples:
        if s.get("x") is not None and abs(s["x"] - before_x) > 0.5:
            return s["t_ms"]
    return None


class CursorSampler:
    """끌기 동안 커서 화면 좌표를 PowerShell 안에서 step ms 간격으로 읽는다(입력 없음, 읽기 전용)."""

    def __init__(self, limit_ms=6000, step_ms=20):
        script = CURSOR_PS % {"limit": limit_ms, "step": step_ms}
        encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
        self.proc = subprocess.Popen([ri.app.PS, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
                                     stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, encoding="utf-8", errors="replace")
        self.ready = self.proc.stdout.readline().strip() == "READY"

    def finish(self):
        try:
            out, _ = self.proc.communicate(timeout=30)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            return []
        try:
            return json.loads(out.strip().splitlines()[-1])
        except (ValueError, IndexError):
            return []


class PlacementPoller(threading.Thread):
    """끌기 동안 placement.state 의 대상 문서 행(bounds·screen·grab)을 기록한다. 읽기 전용 원격 명령만 쓴다."""

    def __init__(self, rig, doc, step_ms):
        super().__init__(daemon=True)
        self.rig, self.doc, self.step = rig, doc, step_ms / 1000.0
        self.rows, self.stop, self.t0 = [], threading.Event(), time.time()

    def run(self):
        while not self.stop.is_set():
            try:
                st = self.rig.r.call("placement.state") or {}
                row = next((d for d in st.get("docs") or [] if d.get("id") == self.doc), None)
            except Exception as e:  # noqa: BLE001 — 기록만 하고 끌기를 막지 않는다
                row, st = None, {"error": repr(e)[:120]}
            b = (row or {}).get("bounds") or {}
            self.rows.append({"t_ms": round((time.time() - self.t0) * 1000), "x": b.get("x"), "bounds": b or None,
                              "screen": (row or {}).get("screen"), "grab": (row or {}).get("grab"), "error": st.get("error")})
            self.stop.wait(self.step)


SIDE_PANELS = ("LeftPane", "BottomPane", "NotificationPane")


def point_in_panels(x, y, panels, client_l, client_t, scale):
    """화면 물리 px 점이 보이는 패널 bounds(메인 창 DIP) 안인가. 안이면 그 패널 id, 아니면 None. 패널 위에서 250ms 이상 멈추면 합류가 일어나므로(PanelJoinDrag.DwellMs) 끝점이 여기 있으면 끌지 않는다."""
    for p in panels or []:
        b = p.get("bounds")
        if not p.get("visible") or not b:
            continue
        l, t = client_l + b["x"] * scale, client_t + b["y"] * scale
        if l <= x < l + b["w"] * scale and t <= y < t + b["h"] * scale:
            return p.get("id")
    return None


def close_side_panels(rig):
    """측면 패널을 닫아 끝점이 합류 대상(PanelJoinTargets.FindAt: 보이는 측면 패널) 위가 되지 않게 한다. 닫은 결과를 기록한다(panel.close 는 머리줄 ✕ 와 같은 경로, 입력 주입 아님)."""
    out = {}
    for pid in SIDE_PANELS:
        try:
            out[pid] = (rig.r.call("panel.close", id=pid) or {}).get("status")
        except Exception as e:  # noqa: BLE001 — 이미 닫힘·없음은 기록만
            out[pid] = "err:" + repr(e)[:80]
    time.sleep(ri.SETTLE_S)
    try:
        left = [{"id": p.get("id"), "visible": p.get("visible")} for p in rig.r.call("panel.list") or [] if p.get("visible")]
    except Exception as e:  # noqa: BLE001
        left = "err:" + repr(e)[:80]
    return {"closed": out, "visible_after": left}


def f08v2(rig, res):
    args = rig.args
    panels_info = close_side_panels(rig)
    doc = ri.open_docs(rig)[0]
    rig.r.call("placement.markPlaced", doc=doc, x=300, y=120, w=DOC_W, h=DOC_H)
    time.sleep(ri.SETTLE_S)
    state = lambda: rig.r.call("placement.state")  # noqa: E731
    row_of = lambda st: next(d for d in st["docs"] if d["id"] == doc)  # noqa: E731
    st_before = state()
    before = row_of(st_before)
    g = before.get("grab") or {}
    if not g:
        res.add("left_remain_measured", {"grab": None, "tool": "f08v2"}, False)
        res.add("grab_rule_kept", {"na": "grab 없음 — 잡는 점을 정할 수 없다"}, False)
        return
    area = st_before["area"]
    pct = st_before.get("minVisiblePercent", 0)  # 잔존 비율 %(11-10). 키 없는 옛 판본은 0 = 원본 기대 158
    keep = expected_keep(DOC_W, pct)
    limit_x = keep - DOC_W  # 잔존 기대일 때 창 x(비율 0 이면 LIMIT_X_DIP)
    win = rig.win(rig.main)
    s = win["dpi"] / 96.0
    # 계획: (마진, 여유) 를 (기본, 40)→(기본, 0)→(최소 2, 0) 순으로 줄여 처음 도달 가능한 것을 쓴다(기준 158 은 그대로). 도달 분석 닫힌 식을 결과에 남긴다
    plan, choice, tries = plan_with_fallback(area["x"], s, g, before["bounds"]["w"], win["client"]["l"], rig.monitor["bounds"]["l"], ri.EDGE_MARGIN, args.overshoot_dip, args.grab_margin_dip, limit_x)
    reach = reach_analysis(area["x"], s, before["bounds"]["w"], g, plan["lower"], choice["margin_dip"], rule=keep)
    x = before["bounds"]["x"] + choice["off_dip"]
    grab_off_dip = x - before["bounds"]["x"]
    frm = (int(area["x"] + x * s), int(area["y"] + (before["bounds"]["y"] + min(16, g["titleBottom"] / 2)) * s))
    plan = dict(plan, plan_tries=tries, reach_analysis=reach)
    to = (plan["to_x"], frm[1])
    owner = rig.point_owner(*to)
    try:
        panels_now = rig.r.call("panel.list") or []
    except Exception:  # noqa: BLE001
        panels_now = None
    over_panel = point_in_panels(to[0], to[1], panels_now, win["client"]["l"], win["client"]["t"], s) if panels_now is not None else "panel.list 실패"
    endpoint = dict(plan, to_screen=to, owner_pid=owner, own=owner == rig.pid, client=win["client"], monitor=rig.monitor["device"], over_panel=over_panel, grab_choice=choice)
    base = {"tool": "f08v2", "from_screen": frm, "grab_before": g, "grab_off_dip": round(grab_off_dip, 1), "area": area, "scale": s, "endpoint": endpoint,
            "limit_x_dip_est": limit_x, "min_visible_percent": pct, "expected_keep_dip": keep, "side_panels": panels_info}
    if owner != rig.pid:
        # 게이트 우회 금지: 시험 앱 창이 아닌 점으로 끌지 않는다(safe_click 도 같은 조건으로 거부한다)
        res.add("left_remain_measured", dict(base, refused="끝점이 시험 앱 창 위가 아니다 — 끌지 않았다"), False)
        res.add("grab_rule_kept", {"na": "끝점 거부로 미측정(PASS 로 두지 않는다)"}, False)
        return
    if not plan["reachable"]:
        # 기준 완화 금지: 한계에 닿지 못하는 조건이면 끌지 않고 사실로 남긴다
        res.add("left_remain_measured", dict(base, refused="unreachable — 끝점이 한계(창 x %s DIP)에 닿지 못한다(커서 하한 %s px, 목표 %s px)" % (limit_x, plan["lower"], plan["want_x"])), False)
        res.add("grab_rule_kept", {"na": "끝점 도달 불가로 미측정(PASS 로 두지 않는다)"}, False)
        return
    if over_panel:
        # 패널 위 끝점은 합류(PanelJoinDrag)를 유발해 측정 의도(왼쪽 끌기 한계 잔존폭)를 해친다 → 끌지 않고 사실로 남긴다
        res.add("left_remain_measured", dict(base, refused="끝점이 보이는 측면 패널 위(%s) — 합류를 피하려 끌지 않았다" % (over_panel,)), False)
        res.add("grab_rule_kept", {"na": "끝점이 패널 위라 미측정(PASS 로 두지 않는다)"}, False)
        return
    poller = None if args.no_poll else PlacementPoller(rig, doc, args.poll_ms)
    sampler = CursorSampler()
    inj_before = ex2.injection_count()
    if poller:
        poller.start()
    refused = None
    try:
        rig.click(*frm, drag=to, hwnd=rig.main)
    except ri.InputRefused as e:
        refused = str(e)[-300:]
    finally:
        time.sleep(ri.SETTLE_S)
        if poller:
            poller.stop.set()
            poller.join(timeout=5)
        cursor = sampler.finish()
    inj_after = ex2.injection_count()
    st_after = state()
    after = row_of(st_after)
    screen = after.get("screen")
    if screen:
        visible = round((screen["x"] + screen["w"] - st_after["area"]["x"]) / s, 1)
        share = round(visible / DOC_W, 3)
    else:
        visible, share = ex2.left_remain(after["bounds"], DOC_W)
    moved = after["bounds"]["x"] < before["bounds"]["x"]
    rig.grab(res, "f08v2_left")
    # 놓은 직후 문서가 MDI 에 남았는지(패널 합류로 사라지지 않았는지) 기록 — 판정식에는 쓰지 않는다
    try:
        docs_after = rig.r.call("docs.list") or []
        row_after = next((d for d in docs_after if d.get("id") == doc), None)
        docs_kept = {"doc_in_list": row_after is not None, "doc_in_placement": any(d.get("id") == doc for d in st_after.get("docs") or []),
                     "doc_count": len(docs_after), "row": row_after}
    except Exception as e:  # noqa: BLE001
        docs_kept = {"error": repr(e)[:120]}
    xs = [c[1] for c in cursor if isinstance(c, list) and len(c) == 3]
    cursor_min_rel = round((min(xs) - area["x"]) / s, 1) if xs else None
    samples = poller.rows if poller else []
    min_x = min([p["x"] for p in samples if p.get("x") is not None] or [after["bounds"]["x"]])
    cause = classify(before["bounds"]["x"], after["bounds"]["x"], cursor_min_rel, grab_off_dip, limit_x=limit_x)
    diag = dict(base, to_screen=to, refused=refused, cursor_ready=sampler.ready, cursor_samples=len(xs), cursor_min_x=min(xs) if xs else None,
                cursor_min_rel_dip=cursor_min_rel, cursor_tail=cursor[-5:], poll_samples=len(samples), poll_min_x=min_x,
                first_move_ms=first_move_ms(samples, before["bounds"]["x"]), grab_during=[p["grab"] for p in samples[::max(1, len(samples) // 8)]][:10],
                poll_rows=samples[:200], cause=cause, docs_kept=docs_kept, screen_before=before.get("screen"), screen_after=screen,
                mdiMoved=[st_before.get("mdiMoved"), st_after.get("mdiMoved")], mdiEvaluations=[st_before.get("mdiEvaluations"), st_after.get("mdiEvaluations")],
                injections={"before": inj_before[0], "after": inj_after[0], "last": inj_after[1]})
    left_ok, grab_ok = judge(visible, share, moved, keep)
    res.add("left_remain_measured", dict({"before": before["bounds"], "after": after["bounds"], "visible_dip": visible, "share": share}, **diag), left_ok)
    if moved:
        res.add("grab_rule_kept", {"visible_dip": visible, "rule_dip": keep, "min_visible_percent": pct, "tolerance": GRAB_TOL, "cause": cause}, grab_ok)
    else:
        res.add("grab_rule_kept", {"na": "드래그가 창을 옮기지 못해 잔존 폭을 잴 수 없다(PASS 로 두지 않는다)", "visible_dip": visible, "rule_dip": keep, "cause": cause}, False)


# --via app-pointer(11-09 A10): OS 커서는 시험 앱 창 안 제약 때문에 한계에 닿을 수 없다(reach_analysis — min_remain 256 > 160, 수학적으로 불가).
# 그래서 진단 원격 명령 ui.pointerDrag(RemotePointerInputCommands — 히트 요소에 Avalonia 포인터 이벤트를 RaiseEvent, OS 커서·전경 무변화)로
# 제목 칸을 눌러 Dock MdiDocumentWindow 의 실제 끌기 경로(HeaderPressedHandler→BeginDrag(캡처)→DragMove)를 구동한다. 판정식(judge)은 그대로다.
# 제목 칸 안 누를 대상(가운데). 앞이 가려지면(원격 오류) 다음 후보. PART_DocumentHeader 는 가운데 히트가 부모 PART_Grid 라 HitCenter 가 거부한다(11-10 사전 확인 실측)
APP_POINTER_TARGETS = ("PART_Grid", "PART_Header")
APP_POINTER_STEPS = 24


def plan_app_drag(before_x, overshoot_dip, limit_x=LIMIT_X_DIP):
    """앱 내부 포인터 끌기 dx(DIP). 창이 커서를 그대로 따라가면 창 x = before_x + dx = 한계 − 여유가 되도록 잡는다(창 안 제약 없음 → 언제나 도달 가능)."""
    want_x = limit_x - overshoot_dip
    return {"dx": round(want_x - before_x, 1), "dy": 0.0, "window_x_if_follow": want_x, "limit_x_dip_est": limit_x, "overshoot_dip": overshoot_dip}


def app_pointer_drag(rig, doc, dx, dy, steps, targets=APP_POINTER_TARGETS):
    """ui.pointerDrag 를 후보 대상 순서로 시도. 반환 (응답 또는 None, 시도 기록). 가림 오류만 다음 후보로 넘기고 그 밖의 오류도 기록만 한다."""
    tries = []
    for tid in targets:
        try:
            out = rig.r.call("ui.pointerDrag", id=tid, doc=doc, dx=dx, dy=dy, steps=steps)
            if out is None:  # retest_remote.Remote.call 은 원격 오류 때 예외 대신 None 을 돌려준다(11-10 실측)
                tries.append({"id": tid, "ok": False, "error": "원격 오류(None 응답)"})
                continue
            tries.append({"id": tid, "ok": True, "resp": out})
            return out, tries
        except Exception as e:  # noqa: BLE001 — 원격 오류(가림·대상 없음)는 기록하고 다음 후보
            tries.append({"id": tid, "ok": False, "error": repr(e)[:200]})
    return None, tries


def f08v2_app_pointer(rig, res):
    args = rig.args
    panels_info = close_side_panels(rig)  # 놓는 점이 측면 패널 위면 합류(PanelJoinDrag)가 일어나므로 OS 경로와 같이 닫는다
    doc = ri.open_docs(rig)[0]
    rig.r.call("placement.markPlaced", doc=doc, x=300, y=120, w=DOC_W, h=DOC_H)
    time.sleep(ri.SETTLE_S)
    state = lambda: rig.r.call("placement.state")  # noqa: E731
    row_of = lambda st: next(d for d in st["docs"] if d["id"] == doc)  # noqa: E731
    st_before = state()
    before = row_of(st_before)
    pct = st_before.get("minVisiblePercent", 0)  # 잔존 비율 %(11-10). 키 없는 옛 판본은 0 = 원본 기대 158
    keep = expected_keep(DOC_W, pct)
    limit_x = keep - DOC_W
    plan = plan_app_drag(before["bounds"]["x"], args.overshoot_dip, limit_x)
    inj_before = ex2.injection_count()
    resp, tries = app_pointer_drag(rig, doc, plan["dx"], plan["dy"], APP_POINTER_STEPS)
    time.sleep(ri.SETTLE_S)
    inj_after = ex2.injection_count()
    st_after = state()
    after = row_of(st_after)
    s = rig.win(rig.main)["dpi"] / 96.0
    screen = after.get("screen")
    if screen:
        visible = round((screen["x"] + screen["w"] - st_after["area"]["x"]) / s, 1)
        share = round(visible / DOC_W, 3)
    else:
        visible, share = ex2.left_remain(after["bounds"], DOC_W)
    moved = after["bounds"]["x"] < before["bounds"]["x"]
    rig.grab(res, "f08v2_app_pointer_left")
    # 원인 분류는 OS 경로와 같은 함수를 쓴다: 커서 상대 위치 = 따라갈 때 창 x(잡는 점 오프셋 0 으로 환산)
    cause = classify(before["bounds"]["x"], after["bounds"]["x"], before["bounds"]["x"] + plan["dx"], 0.0, limit_x=limit_x) if resp is not None else "undetermined"
    try:
        docs_after = rig.r.call("docs.list") or []
        docs_kept = {"doc_in_list": any(d.get("id") == doc for d in docs_after), "doc_count": len(docs_after)}
    except Exception as e:  # noqa: BLE001
        docs_kept = {"error": repr(e)[:120]}
    diag = {"tool": "f08v2", "via": "app-pointer", "plan": plan, "pointer_tries": tries, "captured": (resp or {}).get("captured"),
            "handled": (resp or {}).get("handled"), "grab_before": before.get("grab"), "grab_after": after.get("grab"),
            "screen_before": before.get("screen"), "screen_after": screen, "area": st_after.get("area"), "scale": s, "cause": cause,
            "side_panels": panels_info, "docs_kept": docs_kept, "limit_x_dip_est": limit_x, "min_visible_percent": pct, "expected_keep_dip": keep,
            "mdiMoved": [st_before.get("mdiMoved"), st_after.get("mdiMoved")],
            "os_input_injections": {"before": inj_before[0], "after": inj_after[0]}}  # 앱 내부 경로라 OS 주입 수는 변하지 않아야 한다(기록)
    if resp is None:
        res.add("left_remain_measured", dict(diag, refused="ui.pointerDrag 가 모든 후보에서 실패 — 끌지 못했다"), False)
        res.add("grab_rule_kept", {"na": "끌기 미구동으로 미측정(PASS 로 두지 않는다)"}, False)
        return
    left_ok, grab_ok = judge(visible, share, moved, keep)
    res.add("left_remain_measured", dict({"before": before["bounds"], "after": after["bounds"], "visible_dip": visible, "share": share, "moved": moved}, **diag), left_ok)
    if moved:
        res.add("grab_rule_kept", {"visible_dip": visible, "rule_dip": keep, "min_visible_percent": pct, "tolerance": GRAB_TOL, "cause": cause, "via": "app-pointer"}, grab_ok)
    else:
        res.add("grab_rule_kept", {"na": "드래그가 창을 옮기지 못해 잔존 폭을 잴 수 없다(PASS 로 두지 않는다)", "visible_dip": visible, "rule_dip": keep, "cause": cause, "via": "app-pointer"}, False)


ITEMS = {"F08": f08v2}
ITEMS_APP_POINTER = {"F08": f08v2_app_pointer}


def self_test(args):
    fails = []
    gate = ri.gate_module(args.session)
    if gate is None:
        print("판정기 없음(--session 확인)")
        return 1
    fails += ["F08 이름이 SPEC 밖: %s" % n for n in REQ["F08"] if n not in gate.SPEC["F08"]["req"]]
    if REQ["F08"] != ex2.REQ["F08"] or GRAB_RULE_DIP != 158 or GRAB_TOL != ex2.GRAB_TOL:
        fails.append("판정 상수가 원본과 다르다")
    # 판정식이 원본 식과 같은지 표본으로 대조(원본 f08 의 두 식을 그대로 옮긴 비교)
    for vis, share, moved in ((159.0, 0.248, True), (421.0, 0.658, True), (157.9, 0.247, True), (160.0, 0.25, True), (160.5, 0.251, True), (300, 0.5, False), (640, 1.0, True)):
        want = (moved and 0 < share < 1, moved and (vis - 108 >= 50 and vis <= ex2.GRAB_RULE_DIP + ex2.GRAB_TOL))
        if judge(vis, share, moved) != tuple(bool(w) for w in want):
            fails.append("판정식 불일치 %s" % ((vis, share, moved),))
    if judge(421.0, 0.658, True) != (True, False):
        fails.append("잔존 421 이 grab_rule_kept 를 통과함")
    # 잔존 비율(11-10): 기대 = min(w, max(ceil(w×비율), 158)). 비율 0·키 없음은 158(원본과 같음), 허용 오차 +2 는 그대로
    for pct_, want in ((0, 158), (None, 158), (25, 160), (50, 320), (90, 576), (95, 576), (-5, 158)):
        if expected_keep(640, pct_) != want:
            fails.append("잔존 기대 %s%% → %s ≠ %s" % (pct_, expected_keep(640, pct_), want))
    if expected_keep(100, 90) != 100 or judge(160.0, 0.25, True) != judge(160.0, 0.25, True, expected_keep(640, 0)):
        fails.append("잔존 기대 창 폭 상한·비율 0 회귀 오류")
    if judge(162.0, 0.253, True, 160) != (True, True) or judge(162.5, 0.254, True, 160) != (True, False) or judge(322.0, 0.5, True, 320) != (True, True):
        fails.append("비율 기대 판정 오류(허용 오차 +2 유지)")
    if classify(300, -480, -300, 227, limit_x=expected_keep(640, 25) - 640) != "reached_limit" or plan_app_drag(300, 40, -320)["dx"] != -660.0:
        fails.append("비율 반영 한계 x(원인 분류·끌기 거리) 오류")
    # 끝점: 원본 상황(잡는 점 227 DIP, 배율 1.5, 영역 x=900, 클라이언트 l=600, 모니터 l=0) → 목표 커서 = −482−40+227 = −295 DIP → 900−442=457 < 하한 612 → 줄임
    p = plan_endpoint(900, 1.5, 227, 600, 0, 100, 40)
    if p["want_x"] != 457 or p["lower"] != 612 or p["to_x"] != 612 or p["reachable"]:
        fails.append("끝점 하한 줄임 오류 %s" % p)
    p = plan_endpoint(1400, 1.0, 227, 600, 0, 100, 40)
    if p["to_x"] != 1400 - 295 or not p["reachable"] or p["window_x_at_end"] != -522.0:
        fails.append("끝점 도달 계산 오류 %s" % p)
    p = plan_endpoint(1000, 1.0, 227, 600, 800, 100, 40)
    if p["lower"] != 900 or p["to_x"] != 900:
        fails.append("모니터 경계 하한 오류 %s" % p)
    for name, args_, want in (("미이동", (300, 300, -300, 227), "not_moved"),
                              ("한계 도달", (300, -481, -300, 227), "reached_limit"),
                              ("도구 끝점 한계(원본 상황)", (300, -219, 8, 227), "tool_endpoint_limit"),
                              ("제품이 안 따라감", (300, -219, -295, 227), "product_not_following"),
                              ("커서 표본 없음", (300, -219, None, 227), "undetermined"),
                              ("값 없음", (300, None, -295, 227), "undetermined")):
        got = classify(*args_)
        if got != want:
            fails.append("원인 분류 %s: %s ≠ %s" % (name, got, want))
    # 패널 닫힘 + 오른쪽 잡기: 창 폭 640, rightInset 108 → 잡을 곳 [20, 532], off = 524. 영역 x = 클라이언트 왼쪽 = 600(배율 1.5), 모니터 l = 0
    gc = choose_grab_offset({"left": 20, "rightInset": 108}, 640, 8)
    pr = plan_endpoint(600, 1.5, gc["off_dip"], 600, 0, 100, 40)  # 여유 40 은 클라이언트 하한(612px)에 걸려 못 닿음 → 여유 0 으로 재계획(본 코드와 같은 순서)
    if pr["reachable"]:
        pr = plan_endpoint(600, 1.5, gc["off_dip"], 600, 0, 100, 0.0)
    elif plan_endpoint(600, 1.5, gc["off_dip"], 600, 0, 100, 0.0)["reachable"]:
        pr = plan_endpoint(600, 1.5, gc["off_dip"], 600, 0, 100, 0.0)
    if gc["kind"] != "right_end" or gc["off_dip"] != 524 or not pr["reachable"] or pr["window_x_at_end"] != LIMIT_X_DIP:
        fails.append("패널 닫힘 + 오른쪽 잡기에서 reachable 아님 %s %s" % (gc, pr))
    pm = plan_endpoint(600, 1.5, choose_grab_offset({"left": 20, "rightInset": 108}, 640, 8)["off_dip"] - 400, 600, 0, 100, 40)
    if pm["reachable"]:
        fails.append("오프셋이 작아 닿지 못하는 조건이 reachable 로 나옴 %s" % pm)
    if choose_grab_offset({"left": 20, "rightInset": 610}, 640, 8)["kind"] != "middle":
        fails.append("좁은 잡을 곳은 가운데로 돌아가야 한다")
    pl = [{"id": "LeftPane", "visible": True, "bounds": {"x": 0, "y": 40, "w": 200, "h": 600}}, {"id": "BottomPane", "visible": False, "bounds": {"x": 0, "y": 600, "w": 900, "h": 100}}]
    # 클라이언트 (1000,100), 배율 1.5: LeftPane 화면 x 1000~1300, y 160~1060
    if (point_in_panels(1100, 500, pl, 1000, 100, 1.5) != "LeftPane" or point_in_panels(1400, 500, pl, 1000, 100, 1.5) is not None
            or point_in_panels(1100, 1100, pl, 1000, 100, 1.5) is not None or point_in_panels(1100, 500, pl[1:], 1000, 100, 1.5) is not None
            or point_in_panels(1100, 500, None, 1000, 100, 1.5) is not None):
        fails.append("패널 위 판정 오류(보이는 패널 안만 패널 id, 숨김·밖은 None)")
    if "panel.close" in CURSOR_PS or "LeftPane" not in SIDE_PANELS:
        fails.append("측면 패널 목록 오류")
    # 이전 실패 실측(F08v2.measure.json): 영역 x=2908=클라이언트 l, 배율 1.0, 클라이언트 하한 2908+12=2920, 모니터 DISPLAY3 l≤2820(모니터 하한 mon_l+100≤2920), 잡는 점 off=390(잡을 곳 [55,398], 마진 8), want=2908+(−482−40+390)=2776→여유 0 이면 2816.
    # 하한 2920 은 클라이언트 규칙(client_l+12)이 정한다 — 모니터 경계 규칙이 아니다. 앱 창을 모니터 왼쪽 끝에서 떼어도 client_l 이 함께 움직여 lower_rel 은 12 DIP 로 불변이다.
    gk = {"left": 55, "rightInset": 242}  # 창 폭 640 − 398
    pl0, ch0, tr0 = plan_with_fallback(2908, 1.0, gk, 640, 2908, 2820, 100, 0, 8)
    if tr0[0]["want_x"] != 2816 or tr0[0]["lower"] != 2920 or tr0[0]["reachable"]:
        fails.append("이전 실패 실측 재현 실패 %s" % tr0)
    if pl0["reachable"]:
        fails.append("실측 조건이 보정 후 도달 가능으로 바뀜(완화) %s" % tr0)
    # 앱 창을 모니터 왼쪽에서 멀리 떼어도(모든 좌표 +1000) 상대 하한은 같아 결과가 같다(client 규칙 구속)
    plm, _, trm = plan_with_fallback(3908, 1.0, gk, 640, 3908, 3820, 100, 0, 8)
    if plm["reachable"] or plm["cursor_rel_dip"] != pl0["cursor_rel_dip"]:
        fails.append("앱 창 이동이 도달 가능성을 바꿈 %s" % trm)
    # 모니터 규칙이 구속하는 가상 조건(클라이언트 l 이 모니터 l 보다 60 안쪽)에서만 앱 창 이동이 효과: 모니터 하한이 client 하한보다 크면 lower_rel 이 커진다
    pm0, _, _ = plan_with_fallback(2908, 1.0, {"left": 55, "rightInset": 60}, 640, 2908, 2900, 100, 0, 8)
    pm1, _, _ = plan_with_fallback(2908, 1.0, {"left": 55, "rightInset": 60}, 640, 2908, 2700, 100, 0, 8)
    if pm0["lower"] <= pm1["lower"]:
        fails.append("모니터 규칙 구속 조건 모의 오류 %s %s" % (pm0["lower"], pm1["lower"]))
    ra0 = reach_analysis(2908, 1.0, 640, gk, 2920, 2.0)
    if ra0["can_reach_limit"] or ra0["lower_rel_dip"] != 12.0 or ra0["min_remain_dip"] != 256.0:
        fails.append("도달 분석 오류 %s" % ra0)
    if reach_analysis(2908, 1.0, 1200, gk, 2920, 2.0)["min_remain_dip"] != ra0["min_remain_dip"]:
        fails.append("창 폭이 도달 분석에 영향(약분 위반)")
    # 보정이 효과를 내는 사례: 기본(여유 40)은 못 닿고 여유 0 으로 닿는다
    pl1, ch1, tr1 = plan_with_fallback(2900, 1.5, {"left": 20, "rightInset": 108}, 640, 2908, 2820, 100, 40, 8)
    if not pl1["reachable"] or tr1[0]["reachable"] or len(tr1) != 2 or pl1["window_x_at_end"] != LIMIT_X_DIP:
        fails.append("여유 보정으로 닿는 사례 오류 %s %s" % (pl1, tr1))
    samples = [{"t_ms": 0, "x": 300}, {"t_ms": 50, "x": 300.2}, {"t_ms": 100, "x": 250}]
    if first_move_ms(samples, 300) != 100 or first_move_ms(samples[:2], 300) is not None:
        fails.append("첫 이동 시각 오류")
    if "-EncodedCommand" in CURSOR_PS or "%(limit)d" not in CURSOR_PS or "SetCursorPos" in CURSOR_PS or "mouse_event" in CURSOR_PS:
        fails.append("커서 표본기는 읽기 전용이어야 한다")
    # --via app-pointer: 끌기 거리·원인 분류·판정식 공유(완화 없음)
    ap = plan_app_drag(300, 40)
    if ap["dx"] != -822.0 or ap["window_x_if_follow"] != LIMIT_X_DIP - 40 or ap["dy"] != 0.0:
        fails.append("app-pointer 끌기 거리 오류 %s" % ap)
    if plan_app_drag(-100, 0)["dx"] != LIMIT_X_DIP + 100:
        fails.append("app-pointer 끌기 거리(시작 x 반영) 오류")
    for name, after_x, want in (("한계 도달", -481, "reached_limit"), ("미이동", 300, "not_moved"), ("제품이 안 따라감", -219, "product_not_following")):
        got = classify(300, after_x, 300 + ap["dx"], 0.0)
        if got != want:
            fails.append("app-pointer 원인 분류 %s: %s ≠ %s" % (name, got, want))
    import inspect
    src_ap = inspect.getsource(f08v2_app_pointer)
    if "judge(visible, share, moved, keep)" not in src_ap or "judge(visible, share, moved, keep)" not in inspect.getsource(f08v2) or "ui.pointerDrag" not in inspect.getsource(app_pointer_drag):
        fails.append("app-pointer 경로가 원본 판정식(judge)·ui.pointerDrag 를 쓰지 않는다")
    if "rig.click" in src_ap or "safe_click" in src_ap:
        fails.append("app-pointer 경로에 OS 입력이 섞였다")

    class _R:  # 원격 호출 모의: 첫 후보 가림(실제 Remote.call 처럼 None) → 둘째 후보 성공
        def __init__(self):
            self.calls = []

        def call(self, name, **kw):
            self.calls.append((name, kw.get("id")))
            if kw.get("id") == APP_POINTER_TARGETS[0]:
                return None
            return {"captured": "MdiDocumentWindow", "handled": {"pressed": True, "released": True}}

    class _Rig:
        r = _R()

    out, tr = app_pointer_drag(_Rig(), "doc1", -822, 0, 24)
    if out is None or len(tr) != 2 or tr[0]["ok"] or not tr[1]["ok"] or _Rig.r.calls[-1] != ("ui.pointerDrag", APP_POINTER_TARGETS[1]):
        fails.append("app-pointer 후보 순회 오류 %s" % tr)
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    p.add_argument("--overshoot-dip", type=float, default=40.0, help="한계 창 x 보다 더 왼쪽으로 보낼 여유(DIP). 커서가 한계를 넘어도 창은 한계에서 멈춰야 한다")
    p.add_argument("--grab-margin-dip", type=float, default=8.0, help="잡는 지점을 잡을 곳 오른쪽 끝에서 안쪽으로 떼는 거리(DIP). 캡션 버튼 영역 밖")
    p.add_argument("--poll-ms", type=int, default=50, help="끌기 중 placement.state 기록 간격(ms)")
    p.add_argument("--no-poll", action="store_true", help="끌기 중 placement.state 기록을 끈다(원격 호출이 끌기에 영향을 주는지 대조할 때)")
    p.add_argument("--via", choices=("os-cursor", "app-pointer"), default="os-cursor",
                   help="끌기 수단. os-cursor=실입력(원래 경로), app-pointer=진단 ui.pointerDrag 로 Dock 끌기 경로를 앱 안에서 구동(OS 커서·전경 무변화). 판정식은 같다")
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    if args.items and [n for n in args.items.split(",") if n.strip() and n.strip() != "F08"]:
        print("거부: 이 판은 F08 만 잰다(나머지는 원본 retest_pcaway_extra2.py)")
        return 3
    args.items = "F08"
    items = ITEMS_APP_POINTER if args.via == "app-pointer" else ITEMS
    return ri.drive(args, items, REQ, "desktop_work", os.path.abspath(__file__), result_cls=ex2.MeasureOut)


if __name__ == "__main__":
    sys.exit(main())

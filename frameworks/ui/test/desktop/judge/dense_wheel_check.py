#!/usr/bin/env python3
# 표·목록·간트의 수식키+휠 4조작(Ctrl·Alt·Shift·Alt+Shift)을 소스 규칙과 자동화 채널 실측값으로 판정하는 스크립트
"""
사용:
  dense_wheel_check.py                 정적 모드(앱 불필요): 공통 부품·화면 연결·문서 반영을 소스에서 확인한다.
  dense_wheel_check.py --live --run-dir <bin/run 복사본> [--out 결과.json] [--shots 폴더] [--min-idle 300] [--wait 7200] [--status 상태파일]
                                       임시 data·profile 로 앱 1개를 화면 밖·비전경으로 띄워 input.wheel·dense.state 실제 값으로 판정하고 자기 PID 만 종료한다.
                                       기동 전 사용자 유휴가 --min-idle 초가 될 때까지 --wait 초 기다린다(읽기 전용, 실입력 없음).
  dense_wheel_check.py --live --real --run-dir <복사본> [--monitor DISPLAY3] [--user-pid N]
                                       위 측정 뒤 시험 앱을 화면에 띄워 전경을 잡고 safe_wheel.ps1 로 실제 OS 휠·수식키를 보낸다. 사용자 Pd 를 확정하지 못하면 거부한다.
                                       결과 이름 real_wheel_{gantt,grid}_{ctrl,alt,shift,alt_shift}, real_wheel_input_lang(시험 전후 입력 언어·되돌림·설치 언어 수), real_wheel_user_pd_untouched, real_wheel_no_leak,
                                       grid_user_drag_width_kept(머리글 실제 끌기 뒤 Alt+휠 왕복).
  dense_wheel_check.py --self-test     입력 없이 판정 논리와 safe_wheel.ps1 의 전송 배열·거부 경로를 확인한다.
rc: 0 통과, 1 위반, 2 판정불가(기동·연결 실패·진단 빌드 아님·명령 없음·--run-dir 없음), 3 유휴 대기 시간 초과 또는 실입력 거부, 5 사용자 입력 감지·복귀.
수치 판정은 --live 에서만 가능하고 빌드 뒤에 실행한다. 이미 떠 있는 앱에는 붙지 않는다(사용자 인스턴스의 문서를 닫으면 안 된다).

조작 규칙(정본 plan/09_화면설계/01_화면설계_공통원칙.md §5.4):
  Ctrl=전체 크기, Alt=가로 배율, Shift=가로 스크롤, Alt+Shift=행 높이.
  행 높이는 1.25 배 단계(-1~+6), 왼쪽 표와 오른쪽 막대는 같은 값, 커서 아래 행 유지, 저장 안 함.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", ".."))
SRC = os.path.join(ROOT, "poc/ui/src")
DOC = os.path.join(ROOT, "plan/09_화면설계/01_화면설계_공통원칙.md")
LINE_LIMIT = 300
ROW_MIN, ROW_MAX = -1, 6
SAFE_WHEEL = os.path.join(ROOT, "poc/ui/test/desktop/measure/safe_wheel.ps1")
PS = "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
DRAG_DIP = 80  # 머리글 끌기 폭. MinWidth=Width 라 넓히는 방향만 된다(GridColumnScaler)
HEADER_GRIP_DIP = 2  # DataGrid 머리글 크기 조절 영역은 오른쪽 끝 5px 안이다(원본 DATAGRIDCOLUMNHEADER_resizeRegionWidth)
GRIP_OFFSETS_DIP = (1, 2, 3, 4, -1, -2, -3, -4)  # 경계에서 클릭 없이 옮겨 볼 거리. 양수=왼쪽(끄는 열 안), 음수=오른쪽(다음 열 왼쪽 끝). 커서가 바뀌는 첫 점을 누른다
GRIP_PROBE = []  # 실입력 축 끌기 전 커서 탐침 기록(drag_measure 결과에 grip_probe 로 남긴다)
REAL_OPS = (("ctrl", "Ctrl", 1), ("alt", "Alt", 1), ("shift", "Shift", -1), ("alt_shift", "Alt+Shift", 1))
REAL_TARGETS = (("gantt", "gantt.canvas", "dayWidth", "offsetX"), ("grid", "grid.dgMain", "columnSum", "hOffset"))


def _read(rel):
    path = rel if os.path.isabs(rel) else os.path.join(SRC, rel)
    try:
        with open(path, encoding="utf-8") as f:
            return f.read()
    except OSError:
        return ""


def static_checks():
    wheel = _read("Pd.Ui/Input/DenseWheel.cs")
    lst = _read("Pd.Ui/Input/DenseList.cs")
    canvas = _read("Pd.Ui.Views/Documents/GanttCanvas.cs")
    dense = _read("Pd.Ui.Views/Documents/GanttCanvas.Dense.cs")
    gantt = _read("Pd.Ui.Views/Documents/GanttDocumentView.axaml.cs")
    target = _read("Pd.Ui.Views/Documents/GridDenseTarget.cs")
    pd = _read("Pd.Ui.Views/Documents/PdDataGrid.cs")
    gridx = _read("Pd.Ui.Views/Documents/GridDocumentView.axaml")
    bench = _read("Pd.Ui.Views/Documents/Bench/GridBenchView.axaml.cs")
    settings = _read("Pd.Poc.Shell/Input/SettingsInput.cs")
    remote = _read("Pd.Poc.Shell/Diagnostics/Remote/RemoteWheelCommands.cs")
    doc = _read(DOC)
    checks = [
        ("wheel_common_part", "enum DenseWheelAction" in wheel and "KeyModifiers.Alt | KeyModifiers.Shift" in wheel),
        ("wheel_no_shell_ref", wheel != "" and "Pd.Poc.Shell" not in wheel and lst != "" and "Pd.Poc.Shell" not in lst),
        ("wheel_delta_x_fallback", "delta.Y" in wheel and "delta.X" in wheel),
        ("gantt_row_scale", "_rowLevel" in dense and "DenseScale.Factor(_rowLevel)" in canvas),
        ("gantt_one_row_height", canvas.count("RowHeight =>") == 1),
        ("gantt_attached", "DenseWheel.Attach(Canvas, Canvas)" in gantt),
        ("gantt_no_own_alt_branch", "HasFlag(KeyModifiers.Alt)" not in canvas),
        ("grid_common_class", "PdDataGrid" in gridx and "class PdDataGrid : DataGrid" in pd and "StyleKeyOverride" in pd),
        ("grid_target_anchor", "RowIndexAt" in target and "StepRows" in target and "StepColumns" in target),
        ("bench_attached", "GridDenseTarget.AttachFixed(BenchGrid" in bench),
        ("lists_attached", all('input:DenseList.Enable="True"' in _read(p) for p in (
            "Pd.Ui.Views/SystemMenu/LogViewerView.axaml",
            "Pd.Ui.Views/SystemMenu/InquiryListView.axaml",
            "Pd.Ui.Views/NotificationPanelView.axaml"))),
        ("ctrl_wheel_command_modifier", "IsCommandModifier(sender, e.KeyModifiers) && e.Delta.Y != 0" in settings),
        ("automation_commands", '"input.wheel"' in remote and '"dense.state"' in remote),
        ("no_row_buttons", "btnRowThin" not in _read("Pd.Ui.Views/Documents/GanttDocumentView.axaml") and "rowThin" not in _read("Pd.Poc.Shell/Localization/doc.ko.json")),
        ("i18n_tip", all("Alt+Shift" in _read("Pd.Poc.Shell/Localization/doc.%s.json" % l) for l in ("ko", "en"))),
        ("doc_table", "### 5.4" in doc and "Alt+Shift+휠" in doc),
        ("doc_grid_decision", "PdDataGrid" in doc and "재검토 조건" in doc),
        ("safe_wheel_tool", "MOUSEEVENTF_WHEEL = 0x0800" in _read(SAFE_WHEEL) and "user_returned.flag" in _read(SAFE_WHEEL)),
    ]
    for rel in ("Pd.Ui/Input/DenseWheel.cs", "Pd.Ui/Input/DenseList.cs", "Pd.Ui.Views/Documents/GanttCanvas.cs",
                "Pd.Ui.Views/Documents/GanttCanvas.Dense.cs", "Pd.Ui.Views/Documents/PdDataGrid.cs",
                "Pd.Ui.Views/Documents/GridDenseTarget.cs", "Pd.Ui.Views/Documents/GridDocumentView.axaml.cs"):
        text = _read(rel)
        checks.append(("lines_le_300:" + os.path.basename(rel), text != "" and text.count("\n") <= LINE_LIMIT))
    return [{"name": n, "pass": bool(p)} for n, p in checks]


class Case:
    def __init__(self, r, id_):
        self.r, self.id = r, id_

    def wheel(self, dy=1, dx=0, **mods):
        args = {"id": self.id, "dy": dy, "dx": dx}
        args.update({k: "true" for k in mods})
        return self.r.result("input.wheel", **args)

    def state(self):
        return self.r.result("dense.state", id=self.id)


def _result(name, ok, **detail):
    d = {"name": name, "pass": bool(ok)}
    d.update(detail)
    return d


def check_rows(c, tag, extra_keep):
    """Alt+Shift: 행 높이만 바뀌고, 커서 아래 행이 유지되며, 범위에서 멈추고, 처음 값으로 돌아온다."""
    out = []
    c.wheel(dy=-1)
    c.wheel(dy=-1)
    c.wheel(dy=-1)
    b = c.state()
    a = c.wheel(dy=1, alt=1, shift=1)["state"]
    out.append(_result(tag + "_rows_grow", a["rowHeight"] > b["rowHeight"], before=b["rowHeight"], after=a["rowHeight"]))
    if "rowAt" in b:
        out.append(_result(tag + "_rows_anchor", a.get("rowAt") is not None and a.get("rowAt") == b.get("rowAt"), before=b.get("rowAt"), after=a.get("rowAt")))
    for key in extra_keep:
        out.append(_result("%s_rows_keep_%s" % (tag, key), abs(a[key] - b[key]) < 0.51, before=b[key], after=a[key]))
    for _ in range(12):
        s = c.wheel(dy=1, alt=1, shift=1)["state"]
    out.append(_result(tag + "_rows_max", s["rowLevel"] == ROW_MAX, level=s["rowLevel"]))
    for _ in range(12):
        s = c.wheel(dy=-1, alt=1, shift=1)["state"]
    out.append(_result(tag + "_rows_min", s["rowLevel"] == ROW_MIN, level=s["rowLevel"]))
    # macOS 는 Shift 와 함께 돌린 휠을 가로 delta 로 보낼 수 있다[확실하지 않음 — 실기 미확인]. 그 입력도 같은 조작이어야 한다.
    s2 = c.wheel(dy=0, dx=1, alt=1, shift=1)["state"]
    out.append(_result(tag + "_rows_delta_x", s2["rowLevel"] == ROW_MIN + 1, level=s2["rowLevel"]))
    # 단계 번호로 계산하므로 왕복하면 정확히 처음 값이다
    out.append(_result(tag + "_rows_back_to_0", s2["rowLevel"] == 0 and abs(s2["rowHeight"] - b["rowHeight"]) < 0.51, base=b["rowHeight"], after=s2["rowHeight"]))
    return out


def check_columns(c, tag, key):
    """Alt: 가로 배율만 바뀌고 행 높이는 그대로다."""
    b = c.state()
    a = c.wheel(dy=1, alt=1)["state"]
    return [_result(tag + "_alt_width", a[key] > b[key], before=b[key], after=a[key]),
            _result(tag + "_alt_keep_row", abs(a["rowHeight"] - b["rowHeight"]) < 0.51, before=b["rowHeight"], after=a["rowHeight"])]


def check_shift(c, tag, key):
    """Shift: 가로 스크롤(내용이 넘칠 때만 움직인다. 앞의 Alt 로 넓혀 둔 뒤 호출)."""
    b = c.state()
    a = c.wheel(dy=-1, shift=1)["state"]
    return [_result(tag + "_shift_hscroll", a[key] != b[key], before=b[key], after=a[key])]


def check_ctrl(r, c, tag):
    """Ctrl: 문서 창에서 전체 크기가 바뀐다(글자 또는 창 배율 — 설정). 판정 뒤 Ctrl+0 으로 되돌린다."""
    w = c.wheel(dy=1, ctrl=1)
    after = r.result("input.ctrlWheel", delta=0)
    return [_result(tag + "_ctrl_handled", w.get("handled") is True), _result(tag + "_ctrl_reset", after.get("handled") is True)]


class UserActive(Exception):
    pass


def set_status(path, text):
    print(text, flush=True)
    if path:
        with open(path, "w", encoding="utf-8") as f:
            f.write(time.strftime("%H:%M:%S ") + text + "\n")


def regress_grid(r, app, d, shots):
    """PdDataGrid 로 옮긴 뒤의 그리드 회귀: 첫 렌더 행 높이·언어·테마. 줄무늬·선택 선은 캡처로 눈 확인한다."""
    res = []
    font = r.result("settings.get")["baseFontSize"]
    s = d.state()
    res.append(_result("grid_first_row_height", s["rowHeight"] == round(font * 2), font=font, rowHeight=s["rowHeight"]))
    doc = r.result("docs.list")
    doc = (doc if isinstance(doc, list) else doc.get("docs", []))[-1]["id"]

    def shot(name):
        if shots:
            r.result("screenshot", doc=doc, path=app.win(os.path.join(shots, name + ".png")))

    shot("grid_initial")
    r.result("grid.select", id=d.id, index=2)
    time.sleep(0.3)
    shot("grid_selected")
    ko = d.state()["columnSum"]
    r.result("lang.set", code="en")
    time.sleep(0.6)
    en = d.state()["columnSum"]
    res.append(_result("grid_lang_en_not_narrower", en >= ko, ko=ko, en=en))
    shot("grid_en")
    r.result("lang.set", code="ko")
    time.sleep(0.6)
    back = d.state()["columnSum"]
    res.append(_result("grid_lang_back", back == ko, ko=ko, after=back))
    # 시작 테마와 상관없이 라이트·다크로 실제 전환해 밝기로 전환을 확인한다(같은 테마로 설정하면 판정이 공허해진다)
    start_theme = r.result("settings.get").get("themeId")
    bright = {}
    for theme in ("pd-light", "pd-dark"):
        r.result("settings.set", themeId=theme)
        time.sleep(0.6)
        shot("grid_" + theme)
        res.append(_result("grid_theme_keep_row_" + theme, d.state()["rowHeight"] == s["rowHeight"]))
        if shots:
            from PIL import Image, ImageStat
            with Image.open(os.path.join(shots, "grid_%s.png" % theme)) as im:
                bright[theme] = ImageStat.Stat(im.convert("L")).mean[0]
    if shots:
        res.append(_result("grid_theme_switched", bright["pd-light"] > bright["pd-dark"] + 50, **bright))
    if start_theme:
        r.result("settings.set", themeId=start_theme)
    time.sleep(0.4)
    return res


def judge_real(op, b, a, sb, sa, width_key, scroll_key):
    """실입력 1회 전후 dense.state(b·a)와 settings.get(sb·sa) 비교. 조작마다 바뀌어야 할 값과 그대로여야 할 값을 함께 본다."""
    same_row = abs(a["rowHeight"] - b["rowHeight"]) < 0.51
    if op == "ctrl":
        return sa.get("baseFontSize") != sb.get("baseFontSize") or sa.get("activeDocZoom") != sb.get("activeDocZoom")
    if op == "alt":
        return a[width_key] > b[width_key] and same_row
    if op == "shift":
        return a[scroll_key] != b[scroll_key] and same_row
    return (a["rowHeight"] > b["rowHeight"] and a["rowLevel"] == b["rowLevel"] + 1 and abs(a[width_key] - b[width_key]) < 0.51
            and a.get("rowAt") is not None and a.get("rowAt") == b.get("rowAt"))


def real_measure(r, wheel_at):
    """간트·그리드에 4조작을 실입력으로 보낸다. wheel_at(id, 수식키, 방향) → safe_wheel 결과. 앞의 준비 조작만 자동화 경로를 쓴다."""
    res, langs, released = [], [], True
    for tag, cid, width_key, scroll_key in REAL_TARGETS:
        r.result("docs.close", all="true")
        r.result("docs.open", kind=tag, title="real wheel " + tag)
        time.sleep(2)
        c = Case(r, cid)
        for _ in range(3):
            c.wheel(dy=-1)  # 커서 아래 행 유지를 보려고 맨 위에서 벗어난다
        if tag == "grid":
            for _ in range(4):
                c.wheel(dy=1, alt=1)  # 열 합계가 표 폭을 넘어야 Shift 가로 스크롤이 움직인다
        for op, mods, clicks in REAL_OPS:
            b, sb = c.state(), r.result("settings.get")
            sent = wheel_at(cid, mods, clicks)
            time.sleep(0.5)
            a, sa = c.state(), r.result("settings.get")
            langs.append(dict(sent, target=tag, op=op))
            released = released and sent.get("modifiers_released") is True
            keys = ("rowLevel", "rowHeight", "rowAt", width_key, scroll_key)
            res.append(_result("real_wheel_%s_%s" % (tag, op), judge_real(op, b, a, sb, sa, width_key, scroll_key),
                               before={k: b.get(k) for k in keys}, after={k: a.get(k) for k in keys},
                               font=[sb.get("baseFontSize"), sa.get("baseFontSize")], zoom=[sb.get("activeDocZoom"), sa.get("activeDocZoom")]))
            if op == "ctrl":
                r.result("input.ctrlWheel", delta=0)  # 다음 조작의 기준을 되돌린다
                time.sleep(0.3)
    changed = [x for x in langs if x.get("layout_changed")]
    # 전환되어도 되돌렸으면 통과다. 전환 여부 자체는 changed 값으로 보고한다(앱이 막을 수 없는 OS 동작이라 합격 조건이 아니다).
    res.append(_result("real_wheel_input_lang", all(x.get("layout_restored") is True for x in changed) and released,
                       changed=[{"target": x["target"], "op": x["op"], "before": x.get("layout_before"), "after": x.get("layout_after"), "final": x.get("layout_final"), "restored": x.get("layout_restored")} for x in changed],
                       changed_any=bool(changed), installed_layouts=max((x.get("layouts_installed") or 0) for x in langs),
                       layouts=sorted({x.get("layout_before") for x in langs}), measured=len(langs), modifiers_released=released))
    return res


def drag_measure(r, drag_header, wheel_at):
    """머리글 경계를 실제로 끌어 넓힌 뒤 Alt+휠 왕복. 끈 폭이 배율에 비례해 남고, 되돌리면 끈 폭으로 돌아와야 한다. 다른 열도 같이 본다."""
    r.result("docs.close", all="true")
    r.result("docs.open", kind="grid", title="real wheel drag")
    time.sleep(2)
    c = Case(r, "grid.dgMain")
    col, other, ratio, tol = 1, 5, 1.25, 1.5  # 11-08: 0번 열은 머리글 최소 폭 바닥(72)이 걸려 선형이 아니므로 바닥 없는 5번 '시작'(100→125) 사용
    s0 = c.state()
    drag_header("grid.dgMain", s0, col, DRAG_DIP)
    time.sleep(0.5)
    s1 = c.state()
    wheel_at("grid.dgMain", "Alt", 1)
    time.sleep(0.5)
    s2 = c.state()
    wheel_at("grid.dgMain", "Alt", -1)
    time.sleep(0.5)
    s3 = c.state()
    key, okey = "colWidth%d" % col, "colWidth%d" % other
    dragged = s1[key] >= s0[key] + DRAG_DIP / 2 and abs(s1[okey] - s0[okey]) <= 0.51
    scaled = abs(s2[key] - s1[key] * ratio) <= tol and abs(s2[okey] - s0[okey] * ratio) <= tol
    back = abs(s3[key] - s1[key]) <= tol and abs(s3[okey] - s0[okey]) <= tol
    return _result("grid_user_drag_width_kept", dragged and scaled and back, dragged=dragged, scaled=scaled, back=back,
                   dragged_col=[s0[key], s1[key], s2[key], s3[key]], other_col=[s0[okey], s1[okey], s2[okey], s3[okey]], ratio=ratio,
                   grip_probe=list(GRIP_PROBE))


def run_real(args, res):
    """실입력 축. 사용자 Pd 를 확정하지 못하거나 복귀 플래그가 있으면 앱을 띄우지 않는다. 돌려주는 값은 rc 규칙과 같다."""
    import mdi_drag_app as app
    import retest_real_input as ri
    import user_away_guard as away
    from ui20_lib import Remote
    if not ri.resolve_user_pid(args):
        return 3
    base = os.popen("wslpath -u '%s'" % app.ps("[IO.Path]::GetTempPath()").strip()).read().strip()
    work = tempfile.mkdtemp(prefix="pd_realwheel_", dir=base)
    rig, code = None, 2
    try:
        away.check()
        before = ri.snapshot_user(args.user_pid)
        rig = ri.Rig(args, work)
        rig.start()
        r = Remote()

        def wheel_at(cid, mods, clicks):
            found = r.result("ui.find", id=cid)
            if not found.get("effectivelyVisible"):
                raise ri.InputRefused("대상이 보이지 않는다: %s" % cid)
            x, y = rig.point(rig.main, found["bounds"])
            rig.check_coord(x, y)
            rig.gate(rig.main)
            done = ri.run_ps_file(SAFE_WHEEL, "-ScreenX", x, "-ScreenY", y, "-ExpectedProcessId", rig.pid, "-Modifiers", mods, "-Clicks", clicks, "-ForbiddenProcessId", args.user_pid or 4)  # 사용자 Pd 부재(None)면 System(4) — safe_wheel [int[]] 변환 실패 방지(retest_pcaway_extra 와 같은 방식)
            if done.returncode != 0:
                raise ri.InputRefused("safe_wheel rc=%s: %s" % (done.returncode, (done.stderr or done.stdout)[-300:]))
            rig.after_input()
            return json.loads(done.stdout.strip().splitlines()[-1])

        def drag_header(cid, state, col, dip):
            b = r.result("ui.find", id=cid)["bounds"]
            edge = b["x"] + sum(state["colWidth%d" % i] for i in range(col + 1))
            y_dip = b["y"] + state["headerHeight"] / 2
            # 2026-11-08 otest-16(team-lead 승인 방향): 누르기 전에 클릭 없이 옮겨 커서 모양으로 크기 조절 영역을 확인한다.
            # 머리글 가운데(조절 영역 밖) 커서를 기준으로 잡고 경계 안쪽 1~4 DIP, 이어서 바깥쪽(다음 열 왼쪽) 1~4 DIP 로 옮기며 커서가 바뀌는 첫 점에서만 누른다(2026-11-08 02:2x 범위 확장 승인).
            mx, y = rig.dip_point(rig.main, edge - state["colWidth%d" % col] / 2, y_dip)
            rig.click(mx, y, hwnd=rig.main, no_click=True, expected=rig.pid)
            time.sleep(0.3)
            base_cursor = rig.cursor_handle()
            probe = []
            for off in GRIP_OFFSETS_DIP:
                x, _ = rig.dip_point(rig.main, edge - off, y_dip)
                rig.click(x, y, hwnd=rig.main, no_click=True, expected=rig.pid)
                time.sleep(0.3)
                h = rig.cursor_handle()
                probe.append({"off_dip": off, "x": x, "cursor": h, "changed": h != base_cursor})
                if h != base_cursor:
                    break
            GRIP_PROBE.append({"base_cursor": base_cursor, "probe": probe})
            hit = next((p for p in probe if p["changed"]), None)
            if hit is None:
                return  # 조절 영역을 못 찾으면 누르지 않는다(정렬 클릭 부작용 방지). 결과는 dragged=false 와 grip_probe 로 남는다
            x2, _ = rig.dip_point(rig.main, edge - hit["off_dip"] + dip, y_dip)
            rig.click(hit["x"], y, hwnd=rig.main, drag=(x2, y))  # safe_click 경로(전경·좌표 게이트·주입 기록, 끌기 중 25단계 이동)

        res += real_measure(r, wheel_at)
        res.append(drag_measure(r, drag_header, wheel_at))
        after = ri.snapshot_user(args.user_pid)
        # 사용자 Pd 가 없을 때(둘 다 alive=False)도 '건드리지 않음' 이다 — 전후 생존·창 사각형이 같으면 통과(2026-11-08 team-lead 승인)
        res.append(_result("real_wheel_user_pd_untouched", (before["alive"] == after["alive"]) and before["rects"] == after["rects"]))
        res.append(_result("real_wheel_no_leak", not rig.leaks, leaks=rig.leaks))
        code = 1 if [x for x in res if x["pass"] is False] else 0
    except away.UserReturned as e:
        print("사용자 복귀 — 실입력 중단 rc=5:", e, flush=True)
        code = 5
    except away.GuardUnavailable as e:
        print("판정불가(복귀 감지 불가):", e, flush=True)
        code = 2
    except (Exception, SystemExit) as e:
        print("판정불가(실입력):", repr(e), flush=True)
        code = 2
    finally:
        if rig:
            rig.stop()
        shutil.rmtree(work, ignore_errors=True)
    return code


def live(args):
    if not args.run_dir:
        print("판정불가: --run-dir 가 없다(이미 떠 있는 앱에는 붙지 않는다)")
        return 2
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "measure"))
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
    import _paths
    _paths.add()
    import mdi_drag_app as app
    import user_away_guard as away

    deadline = time.time() + args.wait
    while True:
        idle = away.read_probe()["idle_ms"] / 1000.0
        if idle >= args.min_idle:
            break
        if time.time() > deadline:
            set_status(args.status, "유휴 대기 시간 초과 rc=3(마지막 유휴 %.0f초)" % idle)
            return 3
        set_status(args.status, "유휴 대기 중 %.0f/%d초" % (idle, args.min_idle))
        time.sleep(max(15, min(60, args.min_idle - idle)))

    win_temp = app.ps("[IO.Path]::GetTempPath()").strip()
    base = os.popen("wslpath -u '%s'" % win_temp).read().strip()
    work = tempfile.mkdtemp(prefix="pd_densewheel_", dir=base)
    res, pid, marker, code = [], None, None, 2
    t0 = time.time()

    def guard():
        probe = away.read_probe()["idle_ms"]
        if probe + 2000 < (time.time() - t0) * 1000:
            raise UserActive("유휴 %.1f초, 경과 %.1f초" % (probe / 1000.0, time.time() - t0))

    try:
        set_status(args.status, "측정 중(유휴 %.0f초에서 시작)" % idle)
        t0 = time.time()
        pid, info, _ = app.launch(args.run_dir, work)
        marker = app.win(os.path.dirname(info))
        from ui20_lib import Remote, Undecidable
        r = Remote()
        names = json.dumps(r.result("help"), ensure_ascii=False)
        if "input.wheel" not in names or "dense.state" not in names:
            raise Undecidable("진단 빌드가 아니거나 input.wheel·dense.state 가 없음(수정 전 빌드)")
        shots = None
        if args.shots:
            shots = os.path.join(work, "shots")
            os.makedirs(shots, exist_ok=True)

        r.result("docs.close", all="true")
        r.result("docs.open", kind="gantt", title="wheel gantt")
        time.sleep(1)
        g = Case(r, "gantt.canvas")
        res += check_rows(g, "gantt", ("dayWidth", "offsetX"))
        res += check_columns(g, "gantt", "dayWidth")
        res += check_shift(g, "gantt", "offsetX")
        b = g.state()
        a = g.wheel(dy=-1)["state"]
        res.append(_result("gantt_plain_vscroll", a["offsetY"] > b["offsetY"], before=b["offsetY"], after=a["offsetY"]))
        res += check_ctrl(r, g, "gantt")
        guard()

        r.result("docs.close", all="true")
        r.result("docs.open", kind="grid", title="wheel grid")
        time.sleep(2)
        d = Case(r, "grid.dgMain")
        res += regress_grid(r, app, d, shots)
        guard()
        res += check_rows(d, "grid", ("columnSum",))
        res += check_columns(d, "grid", "columnSum")
        for _ in range(4):
            d.wheel(dy=1, alt=1)
        res += check_shift(d, "grid", "hOffset")
        res += check_ctrl(r, d, "grid")
        r.result("docs.close", all="true")
        guard()

        r.result("sys.open", view="LogView", activate="false")
        time.sleep(1)
        l = Case(r, "sys.log.list")
        b = l.state()
        if b.get("realized", 0) > 0:
            a = l.wheel(dy=1, alt=1, shift=1)["state"]
            res.append(_result("log_rows_grow", a["rowHeight"] > b["rowHeight"], before=b["rowHeight"], after=a["rowHeight"]))
            a2 = l.wheel(dy=1, alt=1)
            res.append(_result("log_alt_swallowed", a2.get("handled") is True))
            res.append(_result("log_alt_no_change", a2["state"]["rowHeight"] == a["rowHeight"]))
            f0 = a2["state"]["fontSize"]
            a3 = l.wheel(dy=1, ctrl=1)
            res.append(_result("log_ctrl_local_font", a3["state"]["fontSize"] > f0, before=f0, after=a3["state"]["fontSize"]))
        else:
            res.append({"name": "log_rows_grow", "pass": None, "note": "로그 줄 0건 — 판정 불가"})
        r.result("sys.close", name="log")
        guard()

        if shots:
            os.makedirs(args.shots, exist_ok=True)
            for name in os.listdir(shots):
                shutil.copy(os.path.join(shots, name), os.path.join(args.shots, name))
        code = 1 if [x for x in res if x["pass"] is False] else 0
    except UserActive as e:
        print("입력 감지 — 자기 PID 만 종료 rc=5:", e, flush=True)
        code = 5
    except (Exception, SystemExit) as e:  # 기동·연결 실패·Undecidable·명령 오류는 판정불가
        print("판정불가:", repr(e), flush=True)
        code = 2
    finally:
        if pid and marker:
            print(app.stop(pid, marker), flush=True)
    if args.real and code == 0:  # 앱 1개 규칙: 화면 밖 앱을 끈 뒤에 띄운다. 자동화 판정이 깨졌으면 실입력을 보내지 않는다
        time.sleep(1.5)
        code = run_real(args, res)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump({"rc": code, "results": res}, f, ensure_ascii=False, indent=1)
    shutil.rmtree(work, ignore_errors=True)
    for x in res:
        print(("OK  " if x["pass"] else ("?   " if x["pass"] is None else "FAIL")), json.dumps(x, ensure_ascii=False))
    set_status(args.status, "완료 rc=%d 결과=%s" % (code, args.out))
    return code


class FakeRemote:
    """자가검사용 가짜 앱. 입력을 보내지 않고 dense.state·settings.get 만 흉내 낸다."""

    def __init__(self, bug=None):
        self.state = {}
        self.font = 14
        self.bug = bug
        self.base = [70.0, 100.0, 100.0, 100.0, 100.0, 100.0]
        self.level = 0

    def widths(self):
        # 0번 열은 머리글 최소 폭 바닥(72)이 걸린다(실앱 '번호' 70→72, 1.25배 88). 바닥은 선형 배율을 깨므로 비교 열로 쓰면 안 된다
        return {"colWidth%d" % i: float(max(round(w * 1.25 ** self.level), 72 if i == 0 else 0)) for i, w in enumerate(self.base)}

    def result(self, cmd, **args):
        if cmd == "docs.open":
            self.state = {"rowLevel": 0, "rowHeight": 28.0, "rowAt": 5, "dayWidth": 6.0, "offsetX": 0.0, "columnSum": 1000.0, "hOffset": 0.0, "headerHeight": 30.0}
            self.base, self.level = [70.0, 100.0, 100.0, 100.0, 100.0, 100.0], 0
        if cmd == "dense.state":
            return dict(self.state, **self.widths())
        if cmd == "settings.get":
            return {"baseFontSize": self.font, "activeDocZoom": 1.0}
        if cmd == "input.wheel":
            return {"state": dict(self.state, **self.widths()), "handled": True}
        if cmd == "input.ctrlWheel":
            self.font = 14
        return {}

    def apply(self, mods):
        s = self.state
        if mods == "Ctrl":
            self.font += 1
        elif mods == "Alt":
            s["dayWidth"] *= 1.25
            s["columnSum"] *= 1.25
        elif mods == "Shift":
            s["offsetX"] += 60
            s["hOffset"] += 50
        elif mods == "Alt+Shift":
            s["rowLevel"] += 1
            s["rowHeight"] *= 1.25


def _fake_drag(remote):
    def drag_header(cid, state, col, dip):
        if remote.bug != "no_drag":
            remote.base[col] += dip
    return drag_header


def _fake_alt(remote):
    """머리글 끌기 시험용 Alt+휠: 배율 단계만 움직이고, bug 에 따라 사용자 폭을 잃는 결함을 흉내 낸다."""
    def wheel_at(cid, mods, clicks):
        remote.level += clicks
        if remote.bug == "forget" and clicks > 0:
            remote.base[1] = 100.0  # 옛 동작: 배율을 바꾸면 XAML 기준으로 돌아간다
        if remote.bug == "no_back" and clicks < 0:
            remote.base[1] += 30.0
        if remote.bug == "other_col" and clicks > 0:
            remote.base[5] += 40.0
        return {}
    return wheel_at


def _fake_wheel(remote, broken=None, lang=None):
    def wheel_at(cid, mods, clicks):
        if mods != broken:
            remote.apply(mods)
        out = {"modifiers_released": True, "layout_before": "0x4120412", "layout_after": "0x4120412", "layout_final": "0x4120412", "layout_changed": False, "layout_restored": None, "layouts_installed": 2}
        if lang and mods == "Alt+Shift":
            out.update(lang)
        return out
    return wheel_at


def _ps_refuse(tmp, *extra):
    args = [PS, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", _win(SAFE_WHEEL), "-InjectionLog", _win(os.path.join(tmp, "inj.jsonl"))] + [str(a) for a in extra]
    return subprocess.run(args, stdin=subprocess.DEVNULL, capture_output=True, encoding="utf-8", errors="replace", timeout=90)


def _win(path):
    return subprocess.run(["wslpath", "-w", path], capture_output=True, text=True).stdout.strip()


def self_test():
    """실입력 없음. 판정 논리는 가짜 앱으로, safe_wheel.ps1 은 -SelfCheck 와 전송 앞에서 끝나는 거부 경로만 실행한다."""
    fails = []

    def expect(name, ok):
        print("OK  " if ok else "FAIL", name)
        if not ok:
            fails.append(name)

    sleep, time.sleep = time.sleep, lambda _s: None  # 가짜 앱에 대기는 필요 없다
    try:
        fake = FakeRemote()
        rows = real_measure(fake, _fake_wheel(fake))
        names = {x["name"] for x in rows}
        want = {"real_wheel_%s_%s" % (t[0], o[0]) for t in REAL_TARGETS for o in REAL_OPS} | {"real_wheel_input_lang"}
        expect("이름이 K4 와 같다", names == want)
        expect("정상 입력은 전부 통과", all(x["pass"] for x in rows))
        for mods, op in (("Ctrl", "ctrl"), ("Alt", "alt"), ("Shift", "shift"), ("Alt+Shift", "alt_shift")):
            fake = FakeRemote()
            rows = {x["name"]: x["pass"] for x in real_measure(fake, _fake_wheel(fake, broken=mods))}
            expect("%s 가 앱에 안 닿으면 FAIL" % mods, rows["real_wheel_gantt_" + op] is False and rows["real_wheel_grid_" + op] is False)
        fake = FakeRemote()
        lang = real_measure(fake, _fake_wheel(fake, lang={"layout_after": "0x4090409", "layout_final": "0x4120412", "layout_changed": True, "layout_restored": True}))[-1]
        expect("언어 전환 후 되돌림은 통과하고 전환 사실·설치 수는 남는다", lang["pass"] is True and len(lang["changed"]) == 2 and lang["changed_any"] is True and lang["installed_layouts"] == 2)
        fake = FakeRemote()
        drag = drag_measure(fake, _fake_drag(fake), _fake_alt(fake))
        expect("끈 폭이 배율에 비례하고 왕복하면 복귀", drag["name"] == "grid_user_drag_width_kept" and drag["pass"] is True and drag["dragged_col"] == [100.0, 180.0, 225.0, 180.0])
        expect("회귀(11-08): 0번 열은 머리글 바닥으로 선형 배율이 아니고 비교 열 5번은 선형",
               (drag["other_col"] == [100.0, 100.0, 125.0, 100.0]) and (FakeRemote().widths()["colWidth0"] == 72.0)
               and (lambda f: (setattr(f, "level", 1), f.widths()["colWidth0"])[1])(FakeRemote()) == 88.0)
        for bug in ("no_drag", "forget", "no_back", "other_col"):
            fake = FakeRemote(bug)
            expect("끌기 결함 %s 는 FAIL" % bug, drag_measure(fake, _fake_drag(fake), _fake_alt(fake))["pass"] is False)
        fake = FakeRemote()
        lang = real_measure(fake, _fake_wheel(fake, lang={"layout_after": "0x4090409", "layout_final": "0x4090409", "layout_changed": True, "layout_restored": False}))[-1]
        expect("언어를 못 되돌리면 FAIL", lang["pass"] is False)
    finally:
        time.sleep = sleep

    with tempfile.TemporaryDirectory() as tmp:
        one = _ps_refuse(tmp, "-SelfCheck", "-Modifiers", "Alt+Shift", "-Clicks", "-1")
        expect("SelfCheck rc=0", one.returncode == 0)
        try:
            info = json.loads(one.stdout.strip().splitlines()[-1])
            seq = info["seq"]
            order = [(s["vk"], s["up"]) if s["t"] == "key" else ("wheel", s["data"], s["flags"]) for s in seq]
            expect("INPUT 크기 40", info["input_size"] == 40)
            expect("설치 언어 수를 읽는다(읽기 전용) = %s" % info.get("layouts_installed"), isinstance(info.get("layouts_installed"), int) and info["layouts_installed"] >= 1)
            expect("Alt↓ Shift↓ 휠 Shift↑ Alt↑ 순서, 아래 방향 값",
                   order == [(0xA4, False), (0xA0, False), ("wheel", 0xFFFFFF88, 0x0800), (0xA0, True), (0xA4, True)])
        except (ValueError, IndexError, KeyError) as e:
            expect("SelfCheck 출력 해석: %r %s" % (e, one.stderr[-200:]), False)
        none = _ps_refuse(tmp, "-SelfCheck", "-Modifiers", "None", "-Clicks", "1")
        try:
            expect("수식키 없음은 휠 1건", [s["t"] for s in json.loads(none.stdout.strip().splitlines()[-1])["seq"]] == ["wheel"])
        except (ValueError, IndexError, KeyError) as e:
            expect("SelfCheck(None) 출력 해석: %r %s" % (e, none.stderr[-200:]), False)
        flag = os.path.join(tmp, "user_returned.flag")
        absent = os.path.join(tmp, "absent.flag")
        with open(flag, "w", encoding="utf-8") as f:
            f.write("x")
        common = ("-ScreenX", 500, "-ScreenY", 500)
        for name, code, extra in (
                ("지원하지 않는 수식키", "modifiers", ("-Modifiers", "Win+Tab", "-ExpectedProcessId", 4242, "-ForbiddenProcessId", 1, "-FlagPath", _win(absent))),
                ("Clicks 범위", "clicks", ("-Clicks", 3, "-ExpectedProcessId", 4242, "-ForbiddenProcessId", 1, "-FlagPath", _win(absent))),
                ("기대 PID 없음", "expected", ("-ForbiddenProcessId", 1, "-FlagPath", _win(absent))),
                ("입력 금지 PID", "forbidden", ("-ExpectedProcessId", 4242, "-ForbiddenProcessId", 4242, "-FlagPath", _win(absent))),
                ("복귀 플래그", "flag", ("-ExpectedProcessId", 4242, "-ForbiddenProcessId", 1, "-FlagPath", _win(flag))),
                ("전경 불일치", "foreground", ("-ExpectedProcessId", 999999, "-ForbiddenProcessId", 1, "-FlagPath", _win(absent)))):
            done = _ps_refuse(tmp, *(common + extra))
            expect("거부: %s" % name, done.returncode != 0 and ("[%s]" % code) in (done.stderr + done.stdout) and '"sent"' not in done.stdout)
        expect("거부 경로는 주입 기록을 남기지 않는다", not os.path.exists(os.path.join(tmp, "inj.jsonl")))
    print("자가검사 실패 %d건" % len(fails) if fails else "self-test ok")
    return 1 if fails else 0


def main():
    ap = argparse.ArgumentParser(description="수식키+휠 4조작 판정")
    ap.add_argument("--live", action="store_true")
    ap.add_argument("--real", action="store_true", help="--live 뒤 실제 OS 휠·수식키 측정(전경 사용)")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--monitor", default="DISPLAY3", help="실입력 시험 창 모니터(터미널 없는 모니터 중 큰 쪽)")
    ap.add_argument("--user-pid", type=int, default=None, help="생략하면 실행 경로로 찾는다(resolve_user_pid). 주면 그 경로의 프로세스인지 확인한다")
    ap.add_argument("--run-dir")
    ap.add_argument("--out")
    ap.add_argument("--shots")
    ap.add_argument("--status")
    ap.add_argument("--min-idle", type=int, default=300)
    ap.add_argument("--wait", type=int, default=7200)
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if a.real and not a.live:
        ap.error("--real 은 --live 와 함께 쓴다")
    if a.live:
        return live(a)
    res = static_checks()
    for x in res:
        print("OK  " if x["pass"] else "FAIL", x["name"])
    return 1 if any(not x["pass"] for x in res) else 0


if __name__ == "__main__":
    sys.exit(main())

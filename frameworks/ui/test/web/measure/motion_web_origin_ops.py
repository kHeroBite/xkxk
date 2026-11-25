# motion_web_origin_click.py 의 실조작 부품 — 탭 자리 찾기·누른 뒤 취소·모바일형 문서 콤보·남은 창 더 보기 닫기
import time


def visible(run, ctl, **kw):
    r = run.w.call("ui.find", id=ctl, props="IsEffectivelyVisible", **kw)
    if r.get("ok") and r["result"]["bounds"]["w"] and r["result"]["props"].get("IsEffectivelyVisible") == "True":
        return r["result"]["bounds"]
    return None


PAINT_JS = "() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => r(1))))"


def wait_painted(run):
    """브라우저 2프레임(requestAnimationFrame 2회)을 기다린다. UTODO-0021: '보임'은 레이아웃 기준이고 적중 판정은 마지막 합성 화면 기준이라,
    그려지기 전에 누르면 바깥 닫기 층(LightDismissOverlayLayer)이 받아 대화상자가 닫힌다(odev-7 진단)."""
    run.w.page.evaluate(PAINT_JS)


def wait_visible(run, ctl, timeout=5):
    """컨트롤이 보일 때까지 0.1초 간격으로 기다리고, 그려질 때까지 2프레임 더 기다려 bounds 를 돌려준다. 시간 초과면 RuntimeError(고정 sleep 경합 방지)."""
    until = time.time() + timeout
    while time.time() < until:
        b = visible(run, ctl)
        if b:
            wait_painted(run)
            return b
        time.sleep(0.1)
    raise RuntimeError("%.0f초 안에 보이지 않는다: %s" % (timeout, ctl))


def expect_origin(name):
    """사례 이름 → 앱이 기록할 활성화 출처 키(ToKey). nav.combo 는 앱 실측값을 따르므로 None(출처 미대조)."""
    if name.startswith(("list.", "more.", "negative.")):
        return "window_list"
    if name.endswith(".keyboard"):
        return "keyboard"
    if ".tab" in name:
        return "title_tab"
    if name.endswith(".body_default"):
        return "body_click"
    if name == "nav.program":
        return "program"
    return None


def tab_point(run, doc):
    for ctl in ("shell.tab." + doc, "DocTab." + doc, doc):  # 제목 탭 → DOCK 문서 탭
        b = visible(run, ctl)
        if b and 0 < b["h"] < 60:
            return b["x"] + min(25, b["w"] / 2), b["y"] + b["h"] / 2
    for number in range(1, 5):  # 이름 없는 문서 탭은 그 문서의 탭 아이콘 오른쪽(제목 자리)
        b = visible(run, "TabIcon#%d" % number if number > 1 else "TabIcon", doc=doc)
        if b:
            return b["x"] + b["w"] + 18, b["y"] + b["h"] / 2
    raise RuntimeError("탭을 못 찾았다: %s" % doc)


def tab_cancel(run):
    """탭을 누른 채 밖으로 나가 놓고 600ms 안에 프로그램 전환 — 남은 표식이 있으면 효과가 난다(잔류 사례)."""
    act, other = run.pair()
    x, y = tab_point(run, other)
    mouse = run.w.page.mouse

    def cancel_then_program():
        mouse.move(x, y)
        mouse.down()
        mouse.move(x, 2)  # 메뉴 줄 쪽 — 합류 자리가 아니다
        mouse.up()
        if run.check.active(run.w) != act:
            raise RuntimeError("취소했는데 활성이 바뀌었다")
        run.w.call("docs.activate", doc=other)
    return other, cancel_then_program


def combo(run, how):
    """모바일형 상단 문서 콤보. keys=펼친 채 화살표·Enter, closed_key=닫힌 채 화살표, click=항목 클릭."""
    act, other = run.pair()
    ids, keys = [d["id"] for d in run.docs()], run.w.page.keyboard
    arrow = "ArrowDown" if ids.index(other) > ids.index(act) else "ArrowUp"
    b = run.box("shell.cmbDocs")
    run.click("shell.cmbDocs")
    time.sleep(1.5)
    if how == "click":
        x, y = b["x"] + min(60, b["w"] / 2), b["y"] + b["h"] + 4 + 32 * ids.index(other) + 16
        return other, lambda: run.w.page.mouse.click(x, y)
    if how == "closed_key":
        keys.press("Escape")  # 목록만 닫고 초점은 콤보에 남는다
        time.sleep(1)
        return other, lambda: keys.press(arrow)

    def choose():  # 화살표가 곧바로 선택을 바꾸므로 perf.reset 뒤에 함께 누른다
        keys.press(arrow)
        keys.press("Enter")
    return other, choose


def program(run):
    """프로그램 전환 — 목록 선택이 따라 바뀌어도 사용자 선택으로 보면 안 된다(오표식 사례)."""
    other = run.pair()[1]
    return other, lambda: run.w.call("docs.activate", doc=other)


def dismiss(run):
    """시행 뒤 남은 대화상자·펼친 목록을 닫는다. 창 더 보기가 남으면 다음 시행을 가린다. 남아 있었으면 True."""
    if not visible(run, "shell.moreWindowsList"):
        return False
    run.click("shell.moreWindowsCancel")
    time.sleep(1.2)
    if visible(run, "shell.moreWindowsList"):
        raise RuntimeError("창 더 보기가 닫히지 않는다")
    return True

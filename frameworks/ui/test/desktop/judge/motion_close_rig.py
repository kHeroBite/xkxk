# 실제 닫기 판정기의 웹 조작부 — 배치 준비, 누를 자리 찾기, 닫기 뒤 전 프레임 수집
"""
motion_close_judge.py 가 import 한다(직접 실행 없음).

프레임은 재생 시간(320ms)으로 자르지 않는다. headless 화면은 조작 뒤 수백 ms~수 초 늦게 올라오므로
첫 변화(Tc)부터 끝 모습까지를 관찰 시간 안에서 본다.
"""
import time

import judge_session as session
import motion_close_rules as rules

AFTER_S = 5.0        # 닫기 뒤 관찰 시간(3.5초 안에 끝 모습이 안 올라온 회차가 있었다)
KINDS = ("grid", "gantt", "form")  # 남는 문서와 닫히는 문서의 그림이 달라야 중간 프레임을 가를 수 있다. 2개면 간트 위 입력폼
LAST = "__last__"    # 표에 없는 ID — motion.probe 가 표본 재생 없이 lastClose 만 준다
TITLE_H, EDGE = 48, 8  # 겹침 창에서 비교하지 않는 제목줄(✕ hover·눌림)·테두리
TAB_PARTS = 8        # DOCK 탭 줄에서 닫기 자리를 찾아볼 순번 상한
WIDE, NARROW = {"width": 1400, "height": 900}, {"width": 402, "height": 876}
PLACES = {
    "overlap": {"enum": "MdiOverlap", "style": "pc", "view": WIDE, "paths": ("command", "button", "tab")},
    "max": {"enum": "MdiMaximized", "style": "pc", "view": WIDE, "paths": ("command", "button", "tab")},
    "dock": {"enum": "DockTab", "style": "tablet", "view": WIDE, "paths": ("command", "tab")},
    "nav": {"enum": "Nav", "style": "mobile", "view": NARROW, "paths": ("command", "button")},
}


def center(b):
    return b["x"] + b["w"] / 2.0, b["y"] + b["h"] / 2.0


class Rig:
    def __init__(self, s):
        self.s, self.page = s, s.web.page
        self.case, self.layout = "", None
        self.k = None  # 앱 DIP → 화면 표본 CSS 배율(check_layout 이 같은 회차 실측으로 정한다)
        self.scale_mode = "none"  # none(배율 1)·force(Chrome 실제 배율)·emulation(Playwright 흉내) — judge 가 정한다
        self._cdp = None

    def view(self):
        """지금 CSS 뷰포트 {width,height}. 실제 배율(no_viewport)에서는 page.viewport_size 가 없으므로 페이지에서 읽는다."""
        return self.page.viewport_size or self.page.evaluate("() => ({width: innerWidth, height: innerHeight})")

    def set_view(self, size):
        """CSS 뷰포트 크기 바꾸기. 실제 배율에서는 기기 배율을 덮어쓰지 않게 CDP 로 크기만 바꾼다(deviceScaleFactor 0 = 덮어쓰기 안 함)."""
        if self.scale_mode != "force":
            self.page.set_viewport_size(size)
            return
        self._cdp = self._cdp or self.page.context.new_cdp_session(self.page)
        self._cdp.send("Emulation.setDeviceMetricsOverride", {"width": size["width"], "height": size["height"], "deviceScaleFactor": 0, "mobile": False})

    def call(self, cmd, **kw):
        return self.s.call(cmd, **kw)

    def effect(self, key, value):
        if not self.call("style.effect.set", key=key, value=value).get("applied"):
            raise session.Abort("INVALID", "효과 키가 적용되지 않았다: %s=%s" % (key, value))

    def last(self):
        return self.call("motion.probe", effect=LAST).get("lastClose")

    def ids(self):
        return [d["id"] for d in self.call("docs.list")]

    def active(self):
        return self.call("style.state").get("active")

    def setup(self, place, count):
        spec = PLACES[place]
        self.effect("motion.close", "none")
        self.effect("motion.open", "none")
        self.call("docs.close", all=True)
        if self.view() != spec["view"]:
            self.set_view(spec["view"])
            time.sleep(1.5)
        self.s.web.call("ui.style", set=spec["style"])  # 허용 밖이면 check_layout 이 실효값으로 INVALID 처리한다
        time.sleep(1.2)
        for kind in KINDS[len(KINDS) - count:]:
            self.call("docs.open", kind=kind, allowDuplicate=True)
            time.sleep(1.5)
        if place != "nav" and count:
            self.call("docs.arrange", mode="tabbed" if place == "dock" else "cascade")
            time.sleep(1)
        if place == "max":
            for doc in self.ids():
                self.call("docs.state", doc=doc, state="maximized")
            time.sleep(1)
        time.sleep(1.5)
        return self.check_layout(place, count)

    def check_layout(self, place, count):
        """사례마다 실효 배치와 캡처를 같은 회차에 남기고, 기대 배치가 아니면 닫지 않고 INVALID."""
        doc = self.active() if count else None
        style = self.call("ui.style")
        row = next((d for d in self.call("docs.list") if d["id"] == doc), {})
        name = self.case + ".layout"
        self.s.capture(name)
        # ui.find 사각형은 메인 TopLevel DIP, 화면 표본·마우스는 CSS. 배율은 같은 TopLevel 의 ClientSize(windows.list main.w, DIP)로 잰다.
        # ui.style width 는 기기 분류용 창 폭 dp(웹 = CSS ÷ 기기 배율)라 쓰지 않는다 — r11 에서 그 값(622.2)으로 2.25 배 하자 누름이 전부 빗나갔다
        main = next((w for w in self.call("windows.list") if w.get("main")), {})
        env = self.page.evaluate("() => ({dpr: devicePixelRatio, innerWidth: innerWidth})")
        self.k = rules.css_scale(env["innerWidth"], main.get("w"))
        self.layout = {"expected": place, "effective": style.get("effective"), "width": style.get("width"), "dipWidth": main.get("w"), "k": self.k,
                       "scale_mode": self.scale_mode, "dpr": env["dpr"], "innerWidth": env["innerWidth"],
                       "state": row.get("state"), "zReason": row.get("zReason"), "capture": "%s/%s.png" % (self.s.shots, name)}
        if self.k is None:
            raise session.Abort("INVALID", "DIP→CSS 배율을 실측하지 못했다(windows.list main.w=%s)" % main.get("w"))
        bad_scale = rules.scale_problem(self.scale_mode, self.k, env["dpr"], self.s.dpr)
        if bad_scale:
            raise session.Abort("INVALID", "구성 이상 — " + bad_scale)
        bad = rules.layout_problem(place, self.layout, bool(doc))
        if bad:
            raise session.Abort("INVALID", "기대 배치가 아니다: " + bad)
        return doc

    def rect(self, ctl, **kw):
        """컨트롤 사각형을 화면 표본·마우스와 같은 CSS 좌표로(ui.find 는 DIP)."""
        r = self.s.web.call("ui.find", id=ctl, **kw)
        b = (r.get("result") or {}).get("bounds") if r.get("ok") else None
        if not (b and b.get("w") and b.get("h")):
            return None
        if self.k is None:
            raise session.Abort("INVALID", "DIP→CSS 배율 전에 사각형을 읽었다(check_layout 먼저)")
        return rules.to_css(b, self.k)

    def dock_tab_close(self, doc):
        """DOCK 은 제목 탭을 숨긴다. Dock 탭 항목(AutomationId=문서 Id) 안에 중심이 든 닫기 자리를 누른다."""
        tab = self.rect(doc)
        if not tab:
            return None
        for part in ("PART_ClosePresenter", "PART_CompactClosePresenter"):
            for n in range(1, TAB_PARTS + 1):
                b = self.rect(part if n == 1 else "%s#%d" % (part, n))
                if b:
                    x, y = center(b)
                    if tab["x"] <= x <= tab["x"] + tab["w"] and tab["y"] <= y <= tab["y"] + tab["h"]:
                        return b
        return None

    def closer(self, place, path, doc):
        """닫기 조작. 누를 자리를 못 찾으면 None."""
        if path == "command":
            return lambda: self.call("docs.close", doc=doc)
        if path == "tab":
            b = self.dock_tab_close(doc) if place == "dock" else self.rect("shell.tabClose." + doc)
        elif place == "nav":
            b = self.rect("shell.compactClose")
        else:
            b = self.rect("chrome.btnClose", doc=doc)
            if not b:  # 캡션 ✕ 는 이름이 없어 창 테두리 오른쪽 위로 누른다
                o = self.rect("PART_OuterBorder", doc=doc)
                b = o and {"x": o["x"] + o["w"] - 32 * self.k, "y": o["y"] + 8 * self.k, "w": 20 * self.k, "h": 20 * self.k}  # 자리는 DIP 값 × 배율
        if not b:
            return None
        x, y = center(b)
        return lambda: self.page.mouse.click(x, y)

    def body(self, doc):
        """겹침 창의 제목줄·테두리를 뺀 사각형."""
        b = self.rect("PART_OuterBorder", doc=doc)
        if not b:
            return None
        x0, y0, x1, y1 = session.box(b, 1.0)
        v = self.view()
        edge, title = round(EDGE * self.k), round(TITLE_H * self.k)  # 제목줄·테두리 두께는 DIP 값 × 배율(배율 1 이면 그대로)
        inner = (max(0, x0 + edge), max(0, y0 + title), min(v["width"], x1 - edge), min(v["height"], y1 - edge))
        return inner if inner[0] < inner[2] and inner[1] < inner[3] else None

    def area(self, place, doc):
        if place == "overlap":
            inner = self.body(doc)
            if inner:
                return inner
        v = self.view()
        if place == "group":  # 묶음 본문 안쪽만 — 묶음이 풀리며 탭 줄·창 바깥이 덮개와 따로 바뀐다
            return (int(v["width"] * 0.30), int(v["height"] * 0.25), int(v["width"] * 0.72), int(v["height"] * 0.66))
        return (int(v["width"] * 0.25), int(v["height"] * 0.18), int(v["width"] * 0.85), int(v["height"] * 0.8))

    def observe(self, name, act):
        """닫기 앞뒤 lastClose 와 조작 뒤 관찰 시간 안의 전 프레임(시각 순)."""
        start = self.s.capture(name + ".start")[0]
        v = self.view()
        css = (v["width"], v["height"])
        start_cast = self.cast_frame() if start.size != css else None  # 기기 배율 ≠ 1: 기준 그림도 screencast 경로(아래 주석)
        before, at = self.last(), []

        def trigger():
            at.append(time.time() * 1000)
            act()

        shots, _ = self.s.blocked_burst(trigger, AFTER_S)
        after = self.last()
        end = self.s.capture(name + ".end")[0]
        # 기기 배율(--dpr)이 1 이 아니면 screenshot 은 기기 화소(3150x2025), screencast 프레임은 브라우저가 줄인 CSS 크기(1400x900)로 온다.
        # 서로 다른 축소 경로라 같은 화면도 글자·가장자리에서 수천~수만 화소가 달라(r10 DPR 2.25: 끝 장 alpha 1.001 인데 e 2543~27501 고정)
        # 처음·끝 기준 그림도 screencast 한 장으로 받아 프레임과 같은 경로로 맞춘다. DPR 1(크기 같음)은 종전대로 screenshot 이다.
        sizes = sorted({im.size for im, _, _ in shots} | {start.size})
        src = {"start": "screenshot", "end": "screenshot"}
        if start.size != css:
            end_cast = self.cast_frame()
            if start_cast is not None:
                start, src["start"] = start_cast, "screencast"
            if end_cast is not None:
                end, src["end"] = end_cast, "screencast"
        start, end = to_css(start, css), to_css(end, css)
        kept = sorted(((t, to_css(im, css)) for im, t, _ in shots if t > at[0]), key=lambda p: p[0])
        released = self.call("motion.probe", hold="release").get("released")
        return {"before": before, "after": after, "start": start, "end": end, "frames": [im for _, im in kept],
                "ms": [round(t - at[0]) for t, _ in kept], "released": released, "raw_sizes": [list(s) for s in sizes], "ref_source": src}

    def cast_frame(self, wait_s=3.0):
        """지금 화면 screencast 한 장(시작하면 현재 화면을 한 장 보낸다 — 못 받으면 None, 호출 쪽이 screenshot 을 쓴다)."""
        import base64
        import io

        from PIL import Image

        got, cast = [], self.page.context.new_cdp_session(self.page)

        def frame(event):
            got.append(event["data"])
            cast.send("Page.screencastFrameAck", {"sessionId": event["sessionId"]})

        cast.on("Page.screencastFrame", frame)
        cast.send("Page.startScreencast", {"format": "png", "everyNthFrame": 1})
        try:
            until = time.time() + wait_s
            while not got and time.time() < until:
                self.page.wait_for_timeout(50)
        finally:
            cast.send("Page.stopScreencast")
            cast.detach()
        return Image.open(io.BytesIO(base64.b64decode(got[-1]))).convert("RGB") if got else None


def to_css(im, css):
    from PIL import Image

    return im if im.size == css else im.resize(css, Image.LANCZOS)

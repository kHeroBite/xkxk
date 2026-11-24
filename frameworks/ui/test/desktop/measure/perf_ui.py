# PERF-UI 측정 — 원격 제어 명령을 조작당 12회 반복해 문서 1개·20개 조건의 p50/p95 표를 저장하는 스크립트
import json, os, sys, time
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: 저장소 기준 상대 경로
import _paths  # noqa: E402
_paths.add()
import pdctl  # noqa: E402

info = pdctl.load_info()


def call(cmd, **args):
    body = json.dumps({"cmd": cmd, "args": {k: v for k, v in args.items() if v is not None}}, ensure_ascii=False)
    resp = json.loads(pdctl.curl(info, "api/cmd", body, timeout=60))
    return resp.get("result") if resp.get("ok") else {"_error": resp.get("error")}


REPS = 12
ONLY = [s.strip() for s in os.environ.get("PERF_UI_ONLY", "").split(",") if s.strip()]


def open_docs(n):
    call("docs.close", all=True)
    per_g, per_t, per_f = n - 2 * (n // 3), n // 3, n // 3
    for kind, c in (("grid", per_g), ("gantt", per_t), ("form", per_f)):
        if c > 0:
            call("docs.open", kind=kind, count=c)
    call("wait.for", docCount=n, timeout=20000)
    call("docs.arrange", mode="cascade")


def ops(prefix="remote."):
    return {o["name"]: o for o in (call("perf.ops", op=prefix) or [])}


def first_doc():
    return (call("docs.list") or [{}])[0].get("id")


def run_group(name, fn):
    call("perf.reset")
    t0 = time.time()
    errs = 0
    for i in range(REPS):
        r = fn(i)
        if isinstance(r, dict) and r.get("_error"):
            errs += 1
    return {"group": name, "errors": errs, "wall_s": round(time.time() - t0, 2), "ops": ops()}


def scenarios(doc):
    themes = (call("themes.get") or {}).get("builtinThemes") or []
    fonts = [f for f in (call("settings.fonts") or [])][:4]
    orig = call("settings.get") or {}
    g = []
    g.append(("글자 크기 설정 ±(settings.set baseFontSize)", lambda i: call("settings.set", baseFontSize=12 + (i % 2) * 4)))
    g.append(("Ctrl+휠 글자 ±1(FontSize 모드)", lambda i: (call("settings.set", wheelMode="FontSize") if i == 0 else None, call("input.ctrlWheel", delta=1 if i % 2 == 0 else -1, doc=doc))[1]))
    g.append(("Ctrl+0 초기화", lambda i: call("input.ctrlWheel", delta=0, doc=doc)))
    g.append(("창 배율 docs.zoom 0.5/2.0", lambda i: call("docs.zoom", doc=doc, zoom=0.5 if i % 2 == 0 else 2.0)))
    g.append(("테마 전환 theme.select(6종 순환)", lambda i: call("theme.select", id=themes[i % max(len(themes), 1)]) if themes else None))
    g.append(("폰트 변경 settings.set fontName", lambda i: call("settings.set", fontName=fonts[i % len(fonts)]) if fonts else None))
    g.append(("배치 cascade/tile-h/tile-v/tabbed", lambda i: call("docs.arrange", mode=["cascade", "tile-h", "tile-v", "tabbed"][i % 4])))
    g.append(("활성 전환 docs.activate next", lambda i: call("docs.activate", dir="next")))
    g.append(("전체 최소화/복원", lambda i: call("docs.state", all="minimize" if i % 2 == 0 else "restore")))
    g.append(("SDI 분리/재결합 docs.sdi", lambda i: call("docs.sdi", doc=doc, on=(i % 2 == 0))))
    for b in ("chrome.sdi", "chrome.pin", "chrome.sync", "chrome.dup", "chrome.help", "chrome.shadow"):
        g.append(("제목줄 " + b, (lambda bb: (lambda i: call(bb, doc=doc) if bb != "chrome.shadow" else call(bb, on=(i % 2 == 0))))(b)))
    g.append(("레이아웃 저장 layout.save", lambda i: call("layout.save")))
    g.append(("레이아웃 복원 layout.load", lambda i: call("layout.load")))
    g.append(("원격 왕복 ping", lambda i: call("ping")))
    if ONLY:
        # D-24 진단: 환경 변수 PERF_UI_ONLY=zoom|theme|shadow(쉼표로 여럿)면 그 단계 그룹만 돈다(이름 일부로 고름). 없으면 전체(기존과 같음)
        keys = {"zoom": ["창 배율"], "theme": ["테마 전환"], "shadow": ["chrome.shadow"], "font": ["글자 크기 설정", "폰트 변경"], "wheel": ["Ctrl+휠", "Ctrl+0"],
                "arrange": ["배치 cascade"], "activate": ["활성 전환"], "state": ["전체 최소화"], "sdi": ["SDI 분리"], "chrome": ["제목줄 chrome."],
                "layout": ["레이아웃 저장", "레이아웃 복원"], "csdi": ["chrome.sdi"], "cpin": ["chrome.pin"], "csync": ["chrome.sync"], "cdup": ["chrome.dup"],
                "chelp": ["chrome.help"], "save": ["레이아웃 저장"], "load": ["레이아웃 복원"]}
        want = [w for k in ONLY for w in keys.get(k, [])]
        g = [(n, f) for n, f in g if any(w in n for w in want)]
    return g, orig


def restore(orig, doc=None, theme=None):
    """측정 뒤 상태를 되돌린다. 배율·테마·그림자도 되돌린다(2026-11-12 H-01: 첫 문서 배율 2.0 이 남아 뒤따르는 B2 커밋 p95 가 100ms 로 커졌다)."""
    call("settings.set", wheelMode=orig.get("wheelMode", "FontSize"), baseFontSize=orig.get("baseFontSize", 14),
         fontName=orig.get("fontName"))
    call("input.ctrlWheel", delta=0)
    for d in call("docs.list") or []:
        if isinstance(d, dict) and d.get("id") and d.get("zoom") not in (None, 1, 1.0):
            call("docs.zoom", doc=d["id"], zoom=1.0)
    if doc:
        call("docs.zoom", doc=doc, zoom=1.0)
    if theme:
        call("theme.select", id=theme)
    # 시나리오가 i=11 에서 on=False 로 끝난다 — 측정 전 값으로 되돌린다(조회 결과가 bool 이 아니면 기본 켬)
    shadow = ORIG_SHADOW if isinstance(ORIG_SHADOW, bool) else (ORIG_SHADOW.get("on") if isinstance(ORIG_SHADOW, dict) and isinstance(ORIG_SHADOW.get("on"), bool) else True)
    call("chrome.shadow", on=shadow)


results = {}
_tg = call("themes.get") or {}
orig_theme = _tg.get("activeThemeId")  # 측정 전 테마(없으면 되돌리지 않는다)
ORIG_SHADOW = call("chrome.shadow")  # 생략=조회(현재 값)
for n in (1, 20):
    open_docs(n)
    doc = first_doc()
    g, orig = scenarios(doc)
    cond = []
    for name, fn in g:
        cond.append(run_group(name, fn))
        # SDI 로 분리된 상태가 남지 않게 정리
        call("docs.redock", all=True)
        call("docs.state", all="restore")
    restore(orig, doc=doc, theme=orig_theme if isinstance(orig_theme, str) else None)  # 테마 모드(System 등)를 되돌리는 원격 명령은 없다(한계 — 활성 테마만 되돌림)
    results["docs_%d" % n] = cond
    print("docs=%d 완료" % n, flush=True)
out = sys.argv[1] if len(sys.argv) > 1 else "perf_ui.json"
json.dump(results, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("saved", out)

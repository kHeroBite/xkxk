# 제품(PdDiagnostics=false) 산출물(웹 wwwroot·데스크톱 출력 폴더·Android APK)의 '진단 기능 부재'(Q9)를 정적 문자열 검사와 웹 headless 부팅 실측으로 판정하는 스크립트
# 사용: python3 check_product_web.py <웹 wwwroot | 데스크톱 출력 폴더 | .apk> [포트=5183] [--kind web|desktop|apk] [--out 결과.json] [--shots-dir 폴더] [--allowlist 목록.json]
# 웹 조건 ① diag.js 파일 없음 ② 부팅 후 window.pdAutomation·__pdPoc 없음 ③ 어셈블리에 금지 문자열 0건 ④ ?cmd= 등 쿼리가 효과 없음 ⑤ 유휴 rAF 재등록 없음
# Q9 판정 대상 제품판 = prod 채널(-p:PdChannel=prod, team-lead 2026-10-30). PerfProbe 허용은 xkxk.frameworks.ui.shell 메타데이터 PdChannel=prod 일 때만, 아니면 보류.
# 종료 코드: 0=PASS, 1=FAIL, 2=판정불가, 3=보류. 데스크톱·APK 는 ③(어셈블리 금지 문자열 0건)만 본다. 금지·허용 목록은 q9_product_allowlist.json(소스 정적 근거로 고정)에서 읽는다.
# 구조(D-LAYER): 측정(scan_raw·measure_runtime·runtime_checks) / 판정(classify_hits·judge_conditions·judge_verdict) / 기록(print_*·write_json), main 은 순서만 정한다.
import hashlib
import json
import os
import struct
import subprocess
import sys
import time
import zipfile

TOOLS = os.path.dirname(os.path.abspath(__file__))
ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
OUT_DIR = os.path.join(TOOLS, "..", "logs", "otest_w2")  # 기본 캡처 위치(기존 동작). --shots-dir 로 바꾼다
DEFAULT_ALLOWLIST = os.path.join(TOOLS, "q9_product_allowlist.json")


def opt(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def load_rules(path):
    raw = open(path, "rb").read()
    j = json.loads(raw.decode("utf-8"))
    forbidden = {t: cat for cat, toks in j["forbidden"].items() for t in toks}
    allowed = {a["token"]: a["only_encoding"] for a in j["allowed"]}
    pending = {a["token"]: a["only_encoding"] for a in j.get("pending_lead_decision", [])}
    conditional = {a["token"]: (a["only_encoding"], a["requires_channel"]) for a in j.get("allowed_conditional", [])}
    return {"path": os.path.abspath(path), "sha256": hashlib.sha256(raw).hexdigest(), "forbidden": forbidden, "allowed": allowed, "pending": pending, "conditional": conditional}


def read_channel(data):
    """xkxk.frameworks.ui.shell(AppChannel 형식 포함) 어셈블리의 AssemblyMetadata("PdChannel") 값. 커스텀 특성 blob = 길이접두 'PdChannel' 다음 길이접두 값."""
    if b"AppChannel" not in data:
        return None
    i = data.find(b"\x09PdChannel")
    if i < 0:
        return None
    n = data[i + 10]
    return data[i + 11:i + 11 + n].decode("utf-8", "replace") if n < 0x80 else None


def lz4_block(src, size):
    """LZ4 블록 해제(순수 파이썬). size 바이트를 채우면 멈춘다."""
    out, i, n = bytearray(), 0, len(src)
    while i < n and len(out) < size:
        tok = src[i]; i += 1
        lit = tok >> 4
        if lit == 15:
            while True:
                b = src[i]; i += 1; lit += b
                if b != 255:
                    break
        out += src[i:i + lit]; i += lit
        if i >= n or len(out) >= size:
            break
        off = src[i] | (src[i + 1] << 8); i += 2
        ml = tok & 15
        if ml == 15:
            while True:
                b = src[i]; i += 1; ml += b
                if b != 255:
                    break
        ml += 4
        start = len(out) - off
        if off <= 0 or start < 0:
            raise ValueError("잘못된 오프셋")
        while ml > 0:
            chunk = out[start:start + min(ml, off)]
            out += chunk; ml -= len(chunk); start += len(chunk)
    return bytes(out[:size])


def expand_xalz(data, stats):
    """바이트 안의 XALZ(LZ4 압축 어셈블리, .NET Android) 블록을 모두 풀어 돌려준다. 실패는 stats 에 센다."""
    outs, pos = [], data.find(b"XALZ")
    while pos >= 0:
        stats["xalz"] += 1
        try:
            size = struct.unpack_from("<I", data, pos + 8)[0]
            pe = lz4_block(data[pos + 12:pos + 12 + size + size // 255 + 64], size)
            if not pe.startswith(b"MZ"):
                raise ValueError("PE 머리 없음")
            outs.append(pe); stats["xalz_ok"] += 1
        except Exception as ex:
            stats["xalz_fail"].append("%d:%r" % (pos, ex))
        pos = data.find(b"XALZ", pos + 4)
    return outs


def collect_blobs(root, kind, stats):
    """(이름, 바이트, 'asm'|'js') 목록. 어셈블리 계열만 위반 판정, js 는 참고."""
    if kind == "apk":
        z = zipfile.ZipFile(root)
        for info in z.infolist():
            nm = info.filename
            if not (nm.endswith((".so", ".dll", ".blob")) or "/assemblies/" in nm):
                continue
            data = z.read(nm)
            yield nm, data, "asm"
            if "assembl" not in nm and ".dll" not in nm:
                continue  # 런타임 네이티브 라이브러리(libmonodroid.so 등)는 'XALZ' 비교 기계어를 담고 있어 압축 블록으로 보지 않는다
            for k, pe in enumerate(expand_xalz(data, stats)):
                yield "%s#xalz%d" % (nm, k), pe, "asm"
        return
    exts = (".wasm", ".js") if kind == "web" else (".dll", ".exe")
    for dp, _, fs in os.walk(root):
        for f in fs:
            if f.endswith(exts) and not f.endswith((".br", ".gz")):
                p = os.path.join(dp, f)
                yield os.path.relpath(p, root), open(p, "rb").read(), ("js" if f.endswith(".js") else "asm")


def static_scan(root, kind, rules):
    """조립: 정적 측정(scan_raw) → 적중 분류(classify_hits). 반환 형식은 분리 전과 같다."""
    diag_files, raw, files, stats = scan_raw(root, kind, rules)
    hits, tolerated, pending = classify_hits(raw, rules, stats["channel"])
    return diag_files, hits, tolerated, pending, files, stats


def scan_raw(root, kind, rules):
    """측정: diag 파일 목록, 금지 문자열이 들어 있는 위치(raw), 검사 파일 수, 압축 해제·채널 통계(판정 없음)."""
    diag_files = [os.path.relpath(os.path.join(d, f), root) for d, _, fs in os.walk(root) for f in fs if f.lower().startswith("diag")] if kind != "apk" else []
    stats = {"xalz": 0, "xalz_ok": 0, "xalz_fail": [], "shell_assembly_seen": False}
    files, raw, channels = 0, [], set()
    for name, data, cls in collect_blobs(root, kind, stats):
        files += 1
        if cls == "asm" and b"xkxk.frameworks.ui.shell" in data and b"BSJB" in data:
            stats["shell_assembly_seen"] = True
        if cls == "asm":
            ch = read_channel(data)
            if ch is not None:
                channels.add(ch)
        for tok in rules["forbidden"]:
            for enc, needle in (("utf8", tok.encode()), ("utf16", tok.encode("utf-16-le"))):
                if needle in data:
                    raw.append((tok, enc, "%s(%s)" % (name, enc), cls))
    # 채널은 한 값으로만 읽혀야 한다(여럿이거나 없으면 조건부 허용을 적용하지 않는다)
    stats["channel"] = channels.pop() if len(channels) == 1 else ("판독불가:" + ",".join(sorted(channels)) if channels else None)
    return diag_files, raw, files, stats


def classify_hits(raw, rules, channel):
    """판정: 금지 문자열 위치마다 허용(js·허용 목록·채널 조건 충족)/보류/위반으로 나눈다(입출력 없음)."""
    hits, tolerated, pending = {}, {}, {}
    for tok, enc, where, cls in raw:
        cond = rules["conditional"].get(tok)
        if cls == "js":
            tolerated.setdefault("js:" + tok, []).append(where)
        elif rules["allowed"].get(tok) == enc:
            tolerated.setdefault(tok, []).append(where)
        elif cond and cond[0] == enc and channel == cond[1]:
            tolerated.setdefault(tok + "@" + cond[1], []).append(where)
        elif (cond and cond[0] == enc) or rules["pending"].get(tok) == enc:
            pending.setdefault(tok, []).append(where)
        else:
            hits.setdefault(tok, []).append(where)
    return hits, tolerated, pending


def nonwhite(png_path):
    try:
        from PIL import Image
        im = Image.open(png_path).convert("L")
        px = list(im.getdata())
        return sum(1 for v in px if v < 235) / len(px)
    except Exception:
        return None


def boot(pg, url, wait=60):
    pg.goto(url)
    t0 = time.time()
    while time.time() - t0 < wait:
        time.sleep(2)
        shot = os.path.join(OUT_DIR, "_q9_tmp.png")
        pg.screenshot(path=shot)
        frac = nonwhite(shot)
        if frac is not None and frac > 0.02:
            time.sleep(2)
            return True
    return False


def runtime_checks(base):
    from playwright.sync_api import sync_playwright
    res = {}
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=ARGS)
        pg = b.new_page(viewport={"width": 1280, "height": 800})
        logs = []
        pg.on("console", lambda m: logs.append(m.text[:200]))
        pg.add_init_script("window.__rafn = 0; const o = window.requestAnimationFrame.bind(window); window.requestAnimationFrame = (cb) => { window.__rafn++; return o(cb); };")
        booted = boot(pg, base + "/")
        res["booted"] = booted
        res["boot_failed_log"] = [l for l in logs if "기동 실패" in l or "Unhandled" in l][:3]
        shot_plain = os.path.join(OUT_DIR, "q9_product_plain.png")
        pg.screenshot(path=shot_plain)
        res["globals"] = pg.evaluate("() => ({pdAutomation: typeof window.pdAutomation, __pdPoc: typeof window.__pdPoc})")
        n0 = pg.evaluate("window.__rafn")
        time.sleep(6)
        n1 = pg.evaluate("window.__rafn")
        res["idle_raf"] = {"count": n1 - n0, "secs": 6, "per_sec": (n1 - n0) / 6}
        pg.close()
        pg2 = b.new_page(viewport={"width": 1280, "height": 800})
        logs2 = []
        pg2.on("console", lambda m: logs2.append(m.text[:200]))
        boot(pg2, base + "/?open=grid:5,gantt:5&layout=tile&cmd=theme-dark,perf-ui&perf-out=x&exit-after=3")
        time.sleep(4)
        shot_q = os.path.join(OUT_DIR, "q9_product_query.png")
        pg2.screenshot(path=shot_q)
        res["query_perf_json_logged"] = any("PERF_JSON" in l for l in logs2)
        try:
            from PIL import Image, ImageChops
            a, c = Image.open(shot_plain).convert("L"), Image.open(shot_q).convert("L")
            diff = ImageChops.difference(a, c)
            res["query_pixel_diff_frac"] = sum(1 for v in diff.getdata() if v > 24) / (a.size[0] * a.size[1])
        except Exception as ex:
            res["query_pixel_diff_frac"] = None
            res["query_diff_error"] = repr(ex)
        b.close()
    return res


def detect_kind(root):
    if root.lower().endswith(".apk"):
        return "apk"
    return "web" if os.path.isdir(os.path.join(root, "_framework")) else "desktop"


def main():
    global OUT_DIR
    root = os.path.abspath(sys.argv[1])
    port = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith("--") else "5183"
    out = opt("--out")
    kind = opt("--kind") or detect_kind(root)
    OUT_DIR = os.path.abspath(opt("--shots-dir", OUT_DIR))
    rules = load_rules(opt("--allowlist", DEFAULT_ALLOWLIST))
    os.makedirs(OUT_DIR, exist_ok=True)
    # 조립: 정적 측정·분류 → 기록 → (웹) 실행 측정 → 기록 → 판정 → 기록 순서만 정한다
    diag, hits, tol, pend, files, stats = static_scan(root, kind, rules)
    print_static(kind, files, stats, diag, hits, tol, pend)
    rt = None
    if kind == "web":
        rt = measure_runtime(root, port)
        print_runtime(rt)
    v = judge_conditions(diag, hits, rt)
    verdict = judge_verdict(stats, rt, v, pend)
    print("조건별:", v, "=>", verdict)
    if out:
        write_json(out, root, kind, files, stats, rules, diag, hits, tol, pend, rt, v, verdict)
    return exit_code(verdict)


# ── 측정(실행) ──

def measure_runtime(root, port):
    """측정: wwwroot 를 로컬 http.server 로 띄우고 headless 실행 검사(runtime_checks) 값을 돌려준다."""
    srv = subprocess.Popen([sys.executable, "-I", "-m", "http.server", port, "--bind", "127.0.0.1", "--directory", root], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        time.sleep(1.5)
        return runtime_checks("http://127.0.0.1:%s" % port)
    finally:
        srv.terminate()


# ── 판정 ──

def judge_conditions(diag, hits, rt):
    """판정: 조건별 참·거짓. 데스크톱·APK(rt 없음)는 ③만 본다."""
    if rt is None:
        return {"3_command_strings_zero": not hits}
    return {
        "1_diag_file_absent": not diag,
        "2_globals_absent": rt["globals"] == {"pdAutomation": "undefined", "__pdPoc": "undefined"} and rt["booted"],
        "3_command_strings_zero": not hits,
        "4_query_no_effect": rt["booted"] and not rt["query_perf_json_logged"] and (rt.get("query_pixel_diff_frac") is not None and rt["query_pixel_diff_frac"] < 0.02),
        "5_idle_raf_none": rt["booted"] and rt["idle_raf"]["per_sec"] < 1,
    }


def judge_verdict(stats, rt, v, pend):
    """판정: 최종 결론(판정불가·FAIL·보류·PASS)."""
    scan_ok = stats["shell_assembly_seen"] and not stats["xalz_fail"]
    if not scan_ok:
        return "판정불가(어셈블리 미확인 또는 압축 해제 실패)"
    if rt is not None and not rt["booted"]:
        return "판정불가(부팅 실패)"
    if not all(v.values()):
        return "FAIL"
    if pend:
        return "보류(조건부 허용 미충족 — PdChannel=%s, 또는 lead 결정 필요)" % stats["channel"]
    return "PASS"


def exit_code(verdict):
    """판정 결론의 종료 코드: 0=PASS, 1=FAIL, 2=판정불가, 3=보류. 판정 기준과 무관하게 결론만 옮긴다."""
    if verdict == "PASS":
        return 0
    if verdict == "FAIL":
        return 1
    return 2 if verdict.startswith("판정불가") else 3


# ── 기록 ──

def print_static(kind, files, stats, diag, hits, tol, pend):
    print("종류:", kind, "| 검사 파일:", files, "| XALZ 해제:", stats["xalz_ok"], "/", stats["xalz"], "| xkxk.frameworks.ui.shell 어셈블리 확인:", stats["shell_assembly_seen"], "| PdChannel:", stats["channel"])
    print("① diag 파일:", diag or "없음")
    print("③ 금지 문자열 적중:", json.dumps(hits, ensure_ascii=False) or "없음", "| 허용:", json.dumps(tol, ensure_ascii=False), "| 보류(lead 결정 필요):", json.dumps(pend, ensure_ascii=False))


def print_runtime(rt):
    print("부팅:", rt["booted"], rt["boot_failed_log"])
    print("② 전역:", rt["globals"])
    print("⑤ 유휴 rAF:", rt["idle_raf"])
    print("④ 쿼리 효과: PERF_JSON 로그=%s 화면 차이 비율=%s" % (rt["query_perf_json_logged"], rt.get("query_pixel_diff_frac")))


def write_json(out, root, kind, files, stats, rules, diag, hits, tol, pend, rt, v, verdict):
    json.dump({"root": root, "kind": kind, "files_scanned": files, "scan_stats": stats, "allowlist": {"path": rules["path"], "sha256": rules["sha256"]},
               "diag_files": diag, "hits": hits, "tolerated": tol, "pending": pend, "runtime": rt, "conditions": v, "verdict": verdict},
              open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    sys.exit(main())

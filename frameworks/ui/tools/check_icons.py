# 빌드 산출물(Desktop exe·APK·웹 publish)에 임시 앱 아이콘이 들어갔는지 파일 검사로만 판정하는 스크립트(앱을 실행하지 않는다)
"""
사용법(표준 라이브러리만 사용 — python3 -I 로도 동작):
    python3 tools/check_icons.py --exe <Pd.Poc.Desktop.exe> --apk <com.pd.poc-Signed.apk> --web <publish/wwwroot> [--json 결과.json]
세 입력은 각각 생략할 수 있다(생략한 축은 SKIP). rc: 0=전부 PASS, 1=FAIL 있음, 2=입력 오류.
구조(D-LAYER): 축마다 측정(measure_*) → 판정(judge_*) → 기록(print_*·write_json)을 다른 함수로 나누고 main 은 순서만 정한다.

검사 항목
 ① Desktop: exe 의 PE 리소스 RT_GROUP_ICON 존재·크기 목록 == src/Pd.Poc.Desktop/Assets/pd.ico 크기 목록,
    exe 와 같은 폴더의 Pd.Poc.Desktop.dll(진입 어셈블리)·Pd.Poc.Shell.dll 중 하나에 !__AvaloniaDefaultWindowIcon 자원 이름 존재
 ② APK: res/mipmap-*/ic_launcher.png·ic_launcher_round.png(5밀도), ic_launcher_foreground.png, mipmap-anydpi-v26/ic_launcher*.xml,
    aapt2 가 있으면 dump badging 의 application icon 참조(없으면 SKIP 으로 적는다)
 ③ 웹: favicon.ico, icons/ 6개, manifest.webmanifest(display=browser, 아이콘 3개 실존), index.html link 4줄
"""
import argparse
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import zipfile

UI_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF_ICO = os.path.join(UI_DIR, "src", "Pd.Poc.Desktop", "Assets", "pd.ico")
DENSITIES = ["mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi"]
WEB_ICONS = ["icon-16.png", "icon-32.png", "icon-192.png", "icon-512.png", "apple-touch-icon.png", "icon-maskable-512.png"]
WEB_LINKS = [
    r'<link rel="icon" href="\./favicon\.ico"',
    r'<link rel="icon" type="image/png" sizes="32x32" href="\./icons/icon-32\.png"',
    r'<link rel="apple-touch-icon" href="\./icons/apple-touch-icon\.png"',
    r'<link rel="manifest" href="\./manifest\.webmanifest"',
]
AAPT2_CANDIDATES = [
    "/mnt/c/Program Files (x86)/Android/android-sdk/build-tools/36.0.0/aapt2.exe",
    "/mnt/c/Program Files (x86)/Android/android-sdk/build-tools/35.0.0/aapt2.exe",
]

results = []


def row(axis, item, status, detail=""):
    """판정 결과 한 줄(판정 함수가 만들고 main 이 results 에 기록한다)."""
    return {"axis": axis, "item": item, "status": status, "detail": detail}


# ── ICO·PE 파싱 ─────────────────────────────────────────────────────────
def ico_sizes(path):
    with open(path, "rb") as f:
        data = f.read()
    _, typ, count = struct.unpack_from("<HHH", data, 0)
    if typ != 1:
        raise ValueError("ICO 아님")
    out = []
    for i in range(count):
        w, h = struct.unpack_from("<BB", data, 6 + i * 16)
        out.append((w or 256, h or 256))
    return sorted(out)


def pe_resources(path):
    """PE 리소스 디렉터리를 읽어 {형식ID: [(이름ID, 데이터 bytes)]} 를 돌려준다."""
    with open(path, "rb") as f:
        data = f.read()
    if data[:2] != b"MZ":
        raise ValueError("PE 아님")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("PE 서명 없음")
    nsec, = struct.unpack_from("<H", data, pe + 6)
    optsize, = struct.unpack_from("<H", data, pe + 20)
    opt = pe + 24
    magic, = struct.unpack_from("<H", data, opt)
    dd = opt + (96 if magic == 0x10B else 112)
    rsrc_rva, rsrc_size = struct.unpack_from("<II", data, dd + 2 * 8)
    if rsrc_rva == 0:
        return {}
    secs = []
    for i in range(nsec):
        s = opt + optsize + i * 40
        vsize, va, rawsize, rawptr = struct.unpack_from("<IIII", data, s + 8)
        secs.append((va, max(vsize, rawsize), rawptr))

    def off(rva):
        for va, size, raw in secs:
            if va <= rva < va + size:
                return rva - va + raw
        raise ValueError(f"RVA {rva:#x} 섹션 밖")

    base = off(rsrc_rva)

    def entries(dir_off):
        named, ids = struct.unpack_from("<HH", data, dir_off + 12)
        for i in range(named + ids):
            name, target = struct.unpack_from("<II", data, dir_off + 16 + i * 8)
            yield name & 0x7FFFFFFF if not (name & 0x80000000) else ("str", name & 0x7FFFFFFF), target

    out = {}
    for typ, t1 in entries(base):
        if not (t1 & 0x80000000):
            continue
        for nm, t2 in entries(base + (t1 & 0x7FFFFFFF)):
            if not (t2 & 0x80000000):
                continue
            for _, t3 in entries(base + (t2 & 0x7FFFFFFF)):
                rva, size = struct.unpack_from("<II", data, base + (t3 & 0x7FFFFFFF))
                o = off(rva)
                out.setdefault(typ, []).append((nm, data[o:o + size]))
    return out


def group_icon_sizes(blob):
    _, typ, count = struct.unpack_from("<HHH", blob, 0)
    out = []
    for i in range(count):
        w, h = struct.unpack_from("<BB", blob, 6 + i * 14)
        out.append((w or 256, h or 256))
    return sorted(out)


# ── ① Desktop ──────────────────────────────────────────────────────────
def measure_desktop(exe):
    """측정: exe 존재·pd.ico 크기·PE 아이콘 그룹·같은 폴더 dll 의 기본 창 아이콘 자원 이름(판정 없음)."""
    m = {"exe": exe, "exists": os.path.isfile(exe)}
    if not m["exists"]:
        return m
    m["ref"] = ico_sizes(REF_ICO)
    try:
        res = pe_resources(exe)
    except Exception as e:  # 손상·형식 오류
        m["pe_error"] = str(e)
        return m
    groups = res.get(14, [])
    m["group_count"] = len(groups)
    m["sizes"] = group_icon_sizes(groups[0][1]) if groups else None
    d = os.path.dirname(os.path.abspath(exe))
    needle8 = b"!__AvaloniaDefaultWindowIcon"
    needle16 = "!__AvaloniaDefaultWindowIcon".encode("utf-16-le")
    hits, looked = [], []
    for name in ("Pd.Poc.Desktop.dll", "Pd.Poc.Shell.dll"):
        p = os.path.join(d, name)
        if os.path.isfile(p):
            looked.append(name)
            with open(p, "rb") as f:
                blob = f.read()
            if needle8 in blob or needle16 in blob:
                hits.append(name)
    m["dir"], m["looked"], m["hits"] = d, looked, hits
    return m


def judge_desktop(m):
    """판정 ①: 측정값 → 결과 줄 목록."""
    ax = "desktop"
    if not m["exists"]:
        return [row(ax, "exe 존재", "FAIL", m["exe"])]
    if "pe_error" in m:
        return [row(ax, "PE 리소스 읽기", "FAIL", m["pe_error"])]
    rows = []
    if not m["group_count"]:
        rows.append(row(ax, "exe RT_GROUP_ICON", "FAIL", "아이콘 그룹 리소스 없음"))
    else:
        sizes, ref = m["sizes"], m["ref"]
        rows.append(row(ax, "exe RT_GROUP_ICON", "PASS", f"그룹 {m['group_count']}개, 첫 그룹 {len(sizes)}크기"))
        rows.append(row(ax, "exe 아이콘 크기 == pd.ico", "PASS" if sizes == ref else "FAIL",
                        f"exe {[s[0] for s in sizes]} / pd.ico {[s[0] for s in ref]}"))
    d, looked, hits = m["dir"], m["looked"], m["hits"]
    if not looked:
        rows.append(row(ax, "!__AvaloniaDefaultWindowIcon 자원", "FAIL", f"{d} 에 Pd.Poc.Desktop.dll·Pd.Poc.Shell.dll 없음"))
    else:
        rows.append(row(ax, "!__AvaloniaDefaultWindowIcon 자원", "PASS" if hits else "FAIL",
                        f"발견 {hits or '없음'} (검사 {looked})"))
    return rows


# ── ② Android ──────────────────────────────────────────────────────────
def find_aapt2():
    for c in ["aapt2"]:
        p = shutil.which(c)
        if p:
            return p
    for c in AAPT2_CANDIDATES:
        if os.path.isfile(c):
            return c
    return None


def winpath(p):
    try:
        return subprocess.run(["wslpath", "-w", p], capture_output=True, text=True, timeout=10).stdout.strip() or p
    except Exception:
        return p


def measure_android(apk):
    """측정: apk 존재·zip 항목 이름 목록·aapt2 dump badging 출력(판정 없음)."""
    m = {"apk": apk, "exists": os.path.isfile(apk)}
    if not m["exists"]:
        return m
    with zipfile.ZipFile(apk) as z:
        m["names"] = z.namelist()
    aapt = find_aapt2()
    m["aapt"] = aapt
    if not aapt:
        return m
    arg = winpath(apk) if aapt.endswith(".exe") else apk
    try:
        m["aapt_out"] = subprocess.run([aapt, "dump", "badging", arg], capture_output=True, text=True, timeout=60).stdout
    except Exception as e:
        m["aapt_error"] = str(e)
    return m


def judge_android(m):
    """판정 ②: 측정값 → 결과 줄 목록."""
    ax = "android"
    if not m["exists"]:
        return [row(ax, "apk 존재", "FAIL", m["apk"])]
    names, rows = m["names"], []
    for kind in ("ic_launcher.png", "ic_launcher_round.png", "ic_launcher_foreground.png"):
        missing = [d for d in DENSITIES if not any(re.fullmatch(rf"res/mipmap-{d}(-v\d+)?/{re.escape(kind)}", n) for n in names)]
        rows.append(row(ax, f"mipmap 5밀도 {kind}", "FAIL" if missing else "PASS", f"누락 {missing}" if missing else ""))
    for x in ("ic_launcher.xml", "ic_launcher_round.xml"):
        ok = any(re.fullmatch(rf"res/mipmap-anydpi(-v26)?/{re.escape(x)}", n) for n in names)
        rows.append(row(ax, f"mipmap-anydpi-v26/{x}", "PASS" if ok else "FAIL"))
    item = "manifest icon 참조(aapt2)"
    if not m["aapt"]:
        rows.append(row(ax, item, "SKIP", "aapt2 없음 — 건너뜀"))
    elif "aapt_error" in m:
        rows.append(row(ax, item, "SKIP", f"aapt2 실행 실패 — 건너뜀: {m['aapt_error']}"))
    elif not re.search(r"^package: ", m["aapt_out"], re.M):
        rows.append(row(ax, item, "SKIP", "aapt2 출력에 package 줄 없음(실행 실패로 보고 건너뜀)"))
    else:
        mm = re.search(r"^application: .*?icon='([^']*)'", m["aapt_out"], re.M)
        icon = mm.group(1) if mm else ""
        rows.append(row(ax, item, "PASS" if "ic_launcher" in icon else "FAIL", f"application icon='{icon or '없음'}'"))
    return rows


# ── ③ 웹 ───────────────────────────────────────────────────────────────
def measure_web(www):
    """측정: wwwroot 의 favicon·icons/ 파일 유무, manifest 내용·아이콘 실존, index.html 본문(판정 없음)."""
    m = {"www": www, "exists": os.path.isdir(www)}
    if not m["exists"]:
        return m
    m["favicon"] = os.path.isfile(os.path.join(www, "favicon.ico"))
    m["icons_missing"] = [n for n in WEB_ICONS if not os.path.isfile(os.path.join(www, "icons", n))]
    mp = os.path.join(www, "manifest.webmanifest")
    m["manifest_exists"] = os.path.isfile(mp)
    if m["manifest_exists"]:
        try:  # 분리 전과 같은 범위: 표시 값을 읽은 뒤 아이콘 목록에서 실패하면 표시 줄은 남고 오류 줄이 붙는다
            with open(mp, encoding="utf-8") as f:
                man = json.load(f)
            m["display"] = man.get("display")
            srcs = [i.get("src", "") for i in man.get("icons", [])]
            m["srcs"], m["srcs_missing"] = srcs, [s for s in srcs if not os.path.isfile(os.path.join(www, s))]
        except Exception as e:
            m["manifest_error"] = str(e)
    ip = os.path.join(www, "index.html")
    if os.path.isfile(ip):
        with open(ip, encoding="utf-8") as f:
            m["html"] = f.read()
    return m


def judge_web(m):
    """판정 ③: 측정값 → 결과 줄 목록."""
    ax = "web"
    if not m["exists"]:
        return [row(ax, "wwwroot 존재", "FAIL", m["www"])]
    rows = [row(ax, "favicon.ico", "PASS" if m["favicon"] else "FAIL")]
    missing = m["icons_missing"]
    rows.append(row(ax, "icons/ 6개", "FAIL" if missing else "PASS", f"누락 {missing}" if missing else ""))
    if not m["manifest_exists"]:
        rows.append(row(ax, "manifest.webmanifest", "FAIL", "파일 없음"))
    else:
        if "display" in m:
            disp = m["display"]
            rows.append(row(ax, "manifest display=browser", "PASS" if disp == "browser" else "FAIL", f"display={disp}"))
        if "srcs" in m:
            srcs, bad = m["srcs"], m["srcs_missing"]
            rows.append(row(ax, "manifest 아이콘 실존", "PASS" if srcs and not bad else "FAIL", f"{len(srcs)}개, 없음 {bad}"))
        if "manifest_error" in m:
            rows.append(row(ax, "manifest.webmanifest", "FAIL", f"JSON 오류 {m['manifest_error']}"))
    if "html" not in m:
        rows.append(row(ax, "index.html link 4줄", "FAIL", "index.html 없음"))
        return rows
    missing = [p for p in WEB_LINKS if not re.search(p, m["html"])]
    rows.append(row(ax, "index.html link 4줄", "FAIL" if missing else "PASS", f"누락 {len(missing)}줄" if missing else ""))
    return rows


def main():
    ap = argparse.ArgumentParser(description="산출물 아이콘 포함 여부 파일 검사")
    ap.add_argument("--exe")
    ap.add_argument("--apk")
    ap.add_argument("--web")
    ap.add_argument("--json")
    a = ap.parse_args()
    if not (a.exe or a.apk or a.web):
        ap.print_usage()
        return 2
    if not os.path.isfile(REF_ICO) and a.exe:
        print(f"기준 아이콘 없음: {REF_ICO}")
        return 2
    # 조립: 축마다 측정 → 판정 → 기록(results) 순서만 정한다
    axes = ((a.exe, measure_desktop, judge_desktop, "desktop"), (a.apk, measure_android, judge_android, "android"),
            (a.web, measure_web, judge_web, "web"))
    for flag, measure, judge, axis in axes:
        if flag:
            results.extend(judge(measure(flag)))
        else:
            results.append(row(axis, "(입력 생략)", "SKIP"))
    print_results(a, results)
    rc = 1 if any(r["status"] == "FAIL" for r in results) else 0
    print_summary(rc, results)
    if a.json:
        write_json(a, rc, results)
    return rc


# ── 기록: 콘솔·JSON ──

def print_results(a, rows):
    for r in rows:
        print(f"[{r['status']}] {r['axis']}: {r['item']}" + (f" — {r['detail']}" if r["detail"] else ""))


def print_summary(rc, rows):
    print(f"RESULT={'PASS' if rc == 0 else 'FAIL'} rc={rc} (PASS {sum(r['status'] == 'PASS' for r in rows)}"
          f" / FAIL {sum(r['status'] == 'FAIL' for r in rows)} / SKIP {sum(r['status'] == 'SKIP' for r in rows)})")


def write_json(a, rc, rows):
    with open(a.json, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"rc": rc, "inputs": {"exe": a.exe, "apk": a.apk, "web": a.web}, "results": rows}, f, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    sys.exit(main())

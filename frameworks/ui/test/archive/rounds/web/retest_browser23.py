# 웹 그리드 가로 스크롤 빈 칸 결함의 판 이분 탐색 실행기(lead 12:4x) — 판마다 서버를 띄워 retest_browser22 제품 그리드 절차를 dpr 1·2.625 로 돌리고 서버를 내린 뒤 한 줄 요약을 남긴다
# 사용: python3 tools/retest_browser23.py [판이름 ...]   (기본 = i1113 b1126 p1121 p1137 i1201 i1222 i1239bw, 헤드리스, 포트 5210)
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
PUB = os.path.join(ROOT, "src", "Pd.Poc.Browser", "bin", "Release", "net10.0-browser")
OUTD = os.path.join(ROOT, "logs", "odevgc", "hscroll_bisect")
PORT = "5210"
# 판 → (번들 폴더, Avalonia.Browser 유휴 rAF 패치 여부 — avalonia.js 의 stopAnimationFrames 로 실측해 덮어씀)
BUILDS = ["i1113", "b1126", "p1121", "p1137", "i1201", "i1222", "i1239bw"]


def bundle(name):
    return os.path.join(PUB, "publish_" + name, "wwwroot")


def patched(path):
    try:
        with open(os.path.join(path, "_framework", "avalonia.js"), encoding="utf-8", errors="ignore") as f:
            return "stopAnimationFrames" in f.read()
    except OSError:
        return None


def serve(cmd, path=None):
    args = ["bash", os.path.join(HERE, "serve_browser.sh"), cmd, PORT] + ([path] if path else [])
    return subprocess.run(args, capture_output=True, text=True, timeout=60).stdout.strip()


def main():
    names = sys.argv[1:] or BUILDS
    os.makedirs(OUTD, exist_ok=True)
    summary = []
    for name in names:
        path = bundle(name)
        if not os.path.isfile(os.path.join(path, "index.html")):
            summary.append({"build": name, "error": "번들 없음"})
            print(name, "번들 없음", flush=True)
            continue
        serve("stop")
        print(serve("start", path), flush=True)
        for dpr in ("1", "2.625"):
            env = dict(os.environ, PD_URL="http://127.0.0.1:%s/" % PORT, HS_DPR=dpr, HS_OUTD=OUTD, HS_NAME="hscroll_%s_dpr%s" % (name, dpr.replace(".", "_")))
            p = subprocess.run([sys.executable, os.path.join(HERE, "retest_browser22.py"), "Builtin" if name != "i1239bq" else "Pro", "product"],
                               capture_output=True, text=True, timeout=240, env=env)
            line = next((l for l in p.stdout.splitlines() if l.startswith("RESULT")), "RESULT 없음 rc=%s %s" % (p.returncode, p.stderr[-300:]))
            try:
                with open(os.path.join(OUTD, env["HS_NAME"] + ".json"), encoding="utf-8") as f:
                    js = json.load(f)
                row = {"build": name, "patched": patched(path), "dpr": float(dpr), "before": js["steps"][0]["colors"], "min_after": js["min_colors_after"],
                       "blank": js["blank_suspect"], "refilled_by_input": js["refilled_by_input"], "refilled_after_2s": js["refilled_after_2s"],
                       "steps": [(s["step"], s["colors"]) for s in js["steps"]], "json": env["HS_NAME"] + ".json"}
            except (OSError, KeyError, ValueError) as e:
                row = {"build": name, "dpr": float(dpr), "error": str(e), "line": line}
            summary.append(row)
            print(json.dumps(row, ensure_ascii=False), flush=True)
        serve("stop")
    with open(os.path.join(OUTD, "bisect_summary.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()

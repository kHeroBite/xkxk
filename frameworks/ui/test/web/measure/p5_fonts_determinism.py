#!/usr/bin/env python3
# 웹 제품판 두 빌드의 Pd.Fonts wasm sha256·지문 파일명을 실제 파일에서 다시 계산해 P5 결과 JSON 을 쓰는 도구
"""사용: python3 -B p5_fonts_determinism.py --run1 <빌드1 bin/run 복사본> --run2 <빌드2 복사본> --built-at-1 <ISO> --built-at-2 <ISO> --writer otest-8"""
import argparse, glob, hashlib, json, os, sys, time
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", ".."))
OUT = os.path.join(ROOT, "poc", "ui", "logs", "pcaway", "P5.json")
KST = "+09:00"


def fonts(run):
    hits = glob.glob(os.path.join(run, "_framework", "Pd.Fonts.*.wasm"))
    if len(hits) != 1:
        raise SystemExit("Pd.Fonts wasm 이 정확히 1개여야 한다: %s" % hits)
    with open(hits[0], "rb") as f:
        return os.path.basename(hits[0]), hashlib.sha256(f.read()).hexdigest()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--run1", required=True)
    p.add_argument("--run2", required=True)
    p.add_argument("--built-at-1", required=True)
    p.add_argument("--built-at-2", required=True)
    p.add_argument("--writer", required=True)
    a = p.parse_args()
    if not a.writer.startswith("otest"):
        raise SystemExit("writer 는 otest 로 시작해야 한다")
    t0 = time.strftime("%Y-%m-%dT%H:%M:%S", time.localtime()) + KST
    n1, s1 = fonts(a.run1)
    n2, s2 = fonts(a.run2)
    ms = [("fonts_sha_build1", s1), ("fonts_sha_build2", s2), ("fonts_name_build1", n1), ("fonts_name_build2", n2),
          ("built_at_1", a.built_at_1), ("built_at_2", a.built_at_2)]
    doc = {"ac_id": "P5", "verdict": "PASS" if s1 == s2 and n1 == n2 else "FAIL", "writer": a.writer, "run_id": "P5-%s-%d" % (a.writer, int(time.time())),
           "conv_id": "conv_179371711303", "started_at": t0, "finished_at": time.strftime("%Y-%m-%dT%H:%M:%S", time.localtime()) + KST,
           "script": {"path": os.path.relpath(os.path.abspath(__file__), ROOT).replace(os.sep, "/"), "sha256": hashlib.sha256(open(os.path.abspath(__file__), "rb").read()).hexdigest()},
           "measurements": [{"name": k, "value": v, "pass": True if k.startswith("built") else s1 == s2 and n1 == n2} for k, v in ms],
           "detail": {"run1": a.run1, "run2": a.run2}, "human_remaining": []}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    print(doc["verdict"], s1[:12], s2[:12], n1, n2)
    return 0 if doc["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())

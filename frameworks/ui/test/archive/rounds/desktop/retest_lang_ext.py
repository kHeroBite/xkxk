# Q17 '언어 파일 추가만으로 새 언어 확장' 증거 — 코드 변경 없이 시험 사전(<영역>.ja.json)만 더한 진단 스냅숏 빌드에서 ja 가 목록에 나타나고 전환되는지 본다
"""
사용: PD_SLOT_OWNER=<나> python3 test/desktop/measure/retest_lang_ext.py --conf <ja 시험 스냅숏 빌드 구성> [--out logs/odev6r/lang_ext_ja.json]
전제: 시험 사전 logs/odev6r/ja_dict/*.ja.json(9영역, 값="[ja] "+en, lang.self=日本語)을 스냅숏 사본 Localization/ 에만 넣고 빌드(본 소스 무변경).
판정: ① lang.get languages 에 ja(이름 日本語) ② lang.set ja → language=ja·culture=ja-JP ③ i18n.missing lang=ja 0
      ④ 화면 트리에 '[ja] ' 로 시작하는 글자가 실제로 그려짐 ⑤ ko 로 되돌리면 '[ja] ' 0. 사용자 파일은 백업·복원(md5 기록).
기동: run_desktop_bg.sh --kill 의 종료 대상은 그 스크립트가 기록한 자기 PID 뿐이다. 다른 Pd 가 떠 있으면 rc=11 로 바로 실패한다.
"""
import argparse
import datetime
import json
import os
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
TOOLS = _paths.TOOLS  # 옛 위치(poc/ui/tools) — run_desktop_bg.sh·pdctl.py·logs 경로 기준 그대로
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
from retest_remote import Remote  # noqa: E402
from retest_user_files import backup_user_files, user_md5, wait_app_exit  # noqa: E402


def nodes(tree):
    for n in tree:
        yield n
        yield from nodes(n.get("children") or [])


def ja_texts(r):
    t = r.call("tree.get", named=False, depth=400) or {}
    return [n.get("text") for n in nodes(t.get("tree") or []) if (n.get("text") or "").startswith("[ja] ")]


def main(argv):
    p = argparse.ArgumentParser(description="Q17 언어 확장(ja 시험 사전) 실측")
    p.add_argument("--conf", required=True)
    p.add_argument("--out", default=os.path.join(ROOT, "logs", "odev6r", "lang_ext_ja_%s.json" % datetime.datetime.now().strftime("%H%M%S")))
    a = p.parse_args(argv)
    backup = backup_user_files(None, a.out)
    rep = {"conf": a.conf, "md5_before": user_md5(backup)}
    try:
        run = subprocess.run(["bash", os.path.join(TOOLS, "run_desktop_bg.sh"), "--conf", a.conf, "--kill", "--", "--remote-control", "--fresh"],
                             capture_output=True, timeout=80)
        if run.returncode == 11:
            raise RuntimeError("기동 거부(rc=11): 다른 Pd 가 떠 있다. --kill 은 기록된 자기 PID 만 끄므로 그 Pd 는 그대로 둔다")
        r = None
        for _ in range(40):
            try:
                r = Remote()
                if r.call("status") is not None:
                    break
            except SystemExit:
                pass
            time.sleep(1)
        g = r.call("lang.get") or {}
        rep["languages"] = g.get("languages")
        rep["keys"] = g.get("keys")
        s = r.call("lang.set", code="ja") or {}
        time.sleep(1)
        rep["after_set"] = {k: (r.call("lang.get") or {}).get(k) for k in ("language", "culture")}
        rep["missing_ja"] = (r.call("i18n.missing", lang="ja") or {}).get("count")
        jt = ja_texts(r)
        rep["ja_drawn"] = {"count": len(jt), "sample": jt[:10]}
        r.call("lang.set", code="ko")
        time.sleep(1)
        rep["back_to_ko"] = {"language": (r.call("lang.get") or {}).get("language"), "ja_left": len(ja_texts(r))}
        r.call("app.shutdown")
    except Exception as e:  # 실패해도 복원
        rep["error"] = repr(e)
    finally:
        wait_app_exit(30)
        src, dst, saved = backup
        for n, existed in saved.items():
            if existed:
                shutil.copy2(os.path.join(dst, n), os.path.join(src, n))
            elif os.path.exists(os.path.join(src, n)):
                os.remove(os.path.join(src, n))
        rep["md5_after"] = user_md5(backup)
    langs = {x.get("code"): x.get("name") for x in rep.get("languages") or []}
    rep["checks"] = {
        "①_목록에_ja": langs.get("ja") == "日本語",
        "②_전환_ja·ja-JP": rep.get("after_set") == {"language": "ja", "culture": "ja-JP"},
        "③_누락0": rep.get("missing_ja") == 0,
        "④_화면에_ja글자": (rep.get("ja_drawn") or {}).get("count", 0) > 0,
        "⑤_ko복귀": (rep.get("back_to_ko") or {}).get("language") == "ko" and (rep.get("back_to_ko") or {}).get("ja_left") == 0,
        "사용자파일_같음": rep.get("md5_before") == rep.get("md5_after"),
    }
    rep["pass"] = all(rep["checks"].values())
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(rep, f, ensure_ascii=False, indent=2)
    print(json.dumps({k: rep.get(k) for k in ("languages", "after_set", "missing_ja", "ja_drawn", "back_to_ko", "checks", "pass", "error")},
                     ensure_ascii=False, indent=1))
    print("→", a.out)
    return 0 if rep["pass"] else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

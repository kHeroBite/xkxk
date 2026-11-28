# test·tools 의 실행 호스트 정체성 리터럴(실행 파일·패키지·데이터 폴더·경로) 하드코딩을 찾는 판정기
"""정체성 리터럴이 허용 목록 밖에 있으면 rc=1.

사용: python3 -B test/framework/judge/host_paths_check.py [--report] [--self-test]
  (기본)      본 검사. 허용 목록 밖 검출이 있으면 rc=1, 없으면 rc=0.
  --report    검출된 모든 경로(허용 포함, 라이브러리 경로 제외)를 영역 루트 기준 1줄 1경로로 출력.
  --self-test 가짜 트리로 검출·허용·제외 양방향을 확인.
"""
import os
import re
import sys
import tempfile
import shutil

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SCAN_DIRS = ("test", "tools")
SELF_REL = "test/framework/judge/host_paths_check.py"  # 검출 규칙 정의 파일 자신은 제외
EXCLUDE_DIRS = {"bin", "obj", "logs", "archive", "__pycache__"}

# 정체성 리터럴 규칙 R1~R5 (계획서 rg 패턴 P2 와 동일)
PATTERNS = [
    r"xkxk\.frameworks\.ui(?![.\w])",                                   # R1 패키지·접두
    r"xkxk\.frameworks\.ui\.(desktop|browser|android)(?!\w)",           # R2 실행 프로젝트·exe 이름
    r"[\"']xkxk[\"']\s*,\s*[\"']frameworks[\"']",                       # R3 데이터 폴더 세그먼트
    r"xkxk[\\/]+frameworks[\\/]+ui",                                    # R4 경로
    r"frameworks[\\/]+ui[\\/]+testbed",                                 # R5 시험대 경로
]
REGEX = re.compile("|".join("(?:%s)" % p for p in PATTERNS))

# 순수 UI 라이브러리(xkxk.frameworks.ui) 자체의 소스 경로·이름이라 검출 규칙에서 제외(라이브러리 경로 6)
LIB_PATHS = {
    "test/desktop/judge/dense_wheel_check.py",
    "test/desktop/judge/scan_hardcoded_colors.py",
    "test/desktop/judge/title_tab_width_check.py",
    "tools/check_layers.py",
    "tools/check_tour_catalog_rules.py",
    "tools/classify_layers_overrides.json",
    # B11-2: 레이어 검사 규칙·분류기는 프로젝트 이름·소스 폴더(xkxk.frameworks.ui, src/xkxk.frameworks.ui/)를 다루는 것이 목적이다.
    # 실측 매칭이 R1(패키지·접두 = 프로젝트 이름) 뿐이고 R2~R5(실행 파일·데이터 폴더·시험대 경로)는 0건이라 정체성 값이 아니다.
    "tools/check_layers_rules.json",
    "tools/classify_layers.py",
    # B11-2: 같은 근거(실측 R1 만, R2~R5 0건). pcaway_gate.py 는 라이브러리 소스 폴더 src/xkxk.frameworks.ui/ 경로,
    # inspect_specs_ui28.json 은 문서 점검 문구의 라이브러리 이름이다.
    "test/desktop/judge/pcaway_gate.py",
    "test/desktop/judge/inspect_specs_ui28.json",
}

# 허용 목록: 경로 -> 사유 (B11-3 끝에 해당 항목 제거)
ALLOW = {
    "tools/host_identity.json": "값 단일 출처",
    "test/desktop/judge/judge_session.py": "주석·도움말·증거 문자열",
    "test/desktop/measure/h15_binding_check.py": "주석·도움말·증거 문자열",
    "test/desktop/measure/retest_remote_z.py": "주석·도움말·증거 문자열",
    "test/web/measure/web_input_scale_probe.py": "주석·도움말·증거 문자열",
    "tools/q9_product_allowlist.json": "제품판 허용 목록 고정 데이터",
    "test/framework/judge/consumer_docs_selftest.py": "고정 시험 문자열",
    "test/framework/contract/HostingIdentityChecks.cs": "AppDataFolder.UseSegments 계약 시험 고정 기대값(기본 xkxk/frameworks/ui·입력 testbed 세그먼트, AC-301·308)",
    "test/framework/contract/HostOptionsChecks.cs": "빌더 옵션 ApplicationId 계약 시험 고정값",
    "tools/check_assembly_closure.py": "어셈블리 이름 접두 폐포 검사",
    "test/mobile/judge/c5_splash_judge.py": "logcat 표본 고정 시험 문자열",
    "test/mobile/measure/retest_android_pcaway_splash.py": "logcat 표본 고정 시험 문자열",
    "test/mobile/measure/retest_panel_feature_on.py": "docstring",
    "test/web/w24_snap_assist.py": "docstring·저장소 경로 거부 규칙(데이터 폴더 아님)",
    "tools/check_tour_catalog.py": "docstring",
    "tools/build_lock.sh": "주석(공유 라이브러리 이름 나열)",
}


def scan(root, scan_dirs=SCAN_DIRS, lib_paths=LIB_PATHS):
    """정체성 리터럴이 있는 파일의 상대 경로(POSIX) 정렬 목록을 돌려준다."""
    hits = []
    for top in scan_dirs:
        base = os.path.join(root, top)
        for dp, dns, fns in os.walk(base):
            dns[:] = [d for d in dns if d not in EXCLUDE_DIRS and not d.startswith(".")]
            for fn in fns:
                if fn.startswith("."):
                    continue
                full = os.path.join(dp, fn)
                rel = os.path.relpath(full, root).replace(os.sep, "/")
                if rel in lib_paths or rel == SELF_REL:
                    continue
                try:
                    with open(full, "rb") as f:
                        data = f.read()
                except OSError:
                    continue
                if b"\0" in data:
                    continue
                if REGEX.search(data.decode("utf-8", "ignore")):
                    hits.append(rel)
    return sorted(hits)


def violations(root, allow=ALLOW, lib_paths=LIB_PATHS):
    return [p for p in scan(root, lib_paths=lib_paths) if p not in allow]


def self_test():
    tmp = tempfile.mkdtemp(prefix="hostpaths_")
    try:
        def w(rel, text):
            p = os.path.join(tmp, rel)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "w", encoding="utf-8") as f:
                f.write(text)
        # 양성 4: 검출되어야 함
        w("test/a_exe.py", 'EXE = "xkxk.frameworks.ui.desktop.exe"\n')
        w("test/b_pkg.py", 'PKG = "xkxk.frameworks.ui"\n')
        w("tools/c_seg.py", 'S = ["xkxk", "frameworks", "ui"]\n')
        w("tools/d_path.sh", 'D="%LOCALAPPDATA%\\xkxk\\frameworks\\ui"\n')
        # 음성 3: 검출되면 안 됨(무관 문자열·제외 폴더·바이너리 아닌 허용 경로는 아래서 별도 확인)
        w("test/e_clean.py", 'X = "xkxk.frameworks.ui.shell"\n')
        w("test/bin/f_bin.py", 'EXE = "xkxk.frameworks.ui.desktop.exe"\n')
        w("tools/__pycache__/g.py", 'PKG = "xkxk.frameworks.ui"\n')
        hits = scan(tmp)
        want = ["test/a_exe.py", "test/b_pkg.py", "tools/c_seg.py", "tools/d_path.sh"]
        if hits != want:
            print("self-test FAIL: 검출 불일치", hits, "!=", want)
            return 1
        # 허용 양방향
        allow = {"test/a_exe.py": "x", "tools/c_seg.py": "x"}
        v = violations(tmp, allow=allow)
        if v != ["test/b_pkg.py", "tools/d_path.sh"]:
            print("self-test FAIL: 허용 목록 처리 불일치", v)
            return 1
        v = violations(tmp, allow={p: "x" for p in want})
        if v:
            print("self-test FAIL: 전부 허용인데 위반 검출", v)
            return 1
        # 라이브러리 경로 제외
        if "test/b_pkg.py" in scan(tmp, lib_paths={"test/b_pkg.py"}):
            print("self-test FAIL: 라이브러리 경로 제외 실패")
            return 1
        print("self-test PASS (양성 4·음성 3·허용·제외)")
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main(argv):
    if "--self-test" in argv:
        return self_test()
    if "--report" in argv:
        for p in scan(ROOT):
            print(p)
        return 0
    v = violations(ROOT)
    if v:
        print("정체성 리터럴 하드코딩 %d건(허용 목록 밖):" % len(v))
        for p in v:
            print("  " + p)
        return 1
    print("host_paths_check PASS (허용 %d)" % len(ALLOW))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

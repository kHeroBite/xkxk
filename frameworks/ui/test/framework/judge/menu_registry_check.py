# 셸 메뉴 레지스트리 정적 판정기 — 옛 4메뉴 axaml 이관·AutomationId 보존·ShellMenuTable 명령·아이콘·문구 키·모바일 목록 동등·NavMenuCatalog 자체 행 0 을 확인한다(B07 끝 조건)
# 사용: menu_registry_check.py [--root <R>] [--base <커밋, 기본 e8950ea>] [--base-root <git 작업 폴더>] [--self-test] [--help]
#   기준 커밋 파일은 --base-root(없으면 환경 변수 MENU_BASE_ROOT, 그것도 없으면 --root, --root 가 git 작업 폴더가 아니면
#   이 판정기가 든 저장소)에서 git show 로 읽는다
#   ① views/Menus 에 axaml·옛 4메뉴 파일 0, views 아래 axaml 의 AutomationId="shell.mnu…" 0(MainTitleBar 의 shell.mnuMore 제외), MainTitleBar 에 옛 메뉴 요소 0
#   ② 기준 커밋 4 axaml 의 AutomationId 전부가 ShellMenuTable*.cs·views/Menus/ShellMenuSections*.cs 리터럴로 있음(칸 번호 ID 는 접두 리터럴+숫자 허용), 표 안 AutomationId 중복 0
#   ③ 표 행의 CommandId(모바일 포함)가 ShellCommandIds 값, 아이콘이 생성 키 집합 또는 MenuIconCatalog.(Grid|Gantt|Form)IconKey,
#      문구 키(TitleKey·모바일 TitleKey·ReasonKey·TabKey)가 Localization/*.ko.json 합집합과 *.en.json 합집합 양쪽에 있음, 메뉴 ID 중복·없는 Parent 0
#   ④ 표에서 파생한 모바일 목록이 기준 커밋 NavMenuCatalog 행(id·textKey·group·depth·isGroup·applicable·reason·tabOrder·tabKey·icon·desktopIds)과 순서까지 같음
#   ⑤ 현재 NavMenuCatalog.cs 에 자체 행(G(·I(·X(·Tab( 행) 0, ShellMenuTable 참조 있음
# 표 판독 형식: new ShellMenuRow(…)·new(…)(인자 12, 위치·이름 인자), 같은 파일 도우미 'static ShellMenuRow 이름(…) => …', 'with { }',
#   모바일은 new ShellMenuMobileRow(…)·new(…)(인자 11)·도우미·static readonly 필드. 값은 리터럴·Navigation 폴더 const·CmdIconKeys.X·MenuIconCatalog.*IconKey
# 모바일 파생 규칙: 트리 전위 순회 중 Mobile.InDrawer 행(Separator·Section 은 EntryId 있을 때만). id=EntryId ?? AutomationId 의 shell.mnu 뒤 ?? 메뉴 Id,
#   textKey=Mobile.TitleKey ?? TitleKey, group=최상위 조상, depth=조상 수, isGroup=Submenu, reason=Available 이면 null,
#   desktopIds=[AutomationId ?? shell.mnu+id] + Covers(shell. 로 시작하지 않으면 shell.mnu 접두, 중복 제거)
# 출력: FAIL <사유> 줄들, 끝 줄 'menus=<n> automation_ids=<n> commands=<n> nav_parity=<n> result=PASS|FAIL'
# 종료코드: 0=PASS, 1=FAIL(파일 부재·판독 불가 포함), 2=인자 오류
# 구현은 같은 폴더 모듈로 나눴다(AC-015, 각 300줄 이하): menu_registry_lex(상수·토큰화·파서) → menu_registry_eval(값 계산) →
#   menu_registry_parse(표 판독·모바일 파생) → menu_registry_rules(①~⑤·run) → menu_registry_selftest. 공개 이름은 이 모듈에서 그대로 쓸 수 있다
import os
import subprocess
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:  # 다른 폴더에서 import 해도 같은 폴더 분할 모듈을 찾게 한다
    sys.path.insert(0, _HERE)

from menu_registry_selftest import *  # noqa: E402,F401,F403 — 옛 단일 파일의 공개 이름(parse_table_files·derive_nav·leaf_aids·run 등) 유지
from menu_registry_selftest import self_test  # noqa: E402


def main(argv):
    args, opts = argv[1:], {}
    if args in (["--help"], ["-h"]):
        print(USAGE)
        return 0
    if args == ["--self-test"]:
        return self_test()
    while args:
        if args[0] in ("--root", "--base", "--base-root") and len(args) >= 2:
            opts[args[0]] = args[1]
            args = args[2:]
        else:
            print(USAGE, file=sys.stderr)
            return 2
    here = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
    root = os.path.abspath(opts.get("--root") or here)
    base_root = opts.get("--base-root") or os.environ.get("MENU_BASE_ROOT")  # 환경 변수는 verify_framework.sh 를 모의 트리에서 돌릴 때 쓴다
    if not base_root:  # --root 가 git 작업 폴더가 아니면(모의 트리 등) 이 판정기가 든 저장소에서 기준 커밋을 읽는다
        inside = subprocess.run(["git", "-C", root, "rev-parse", "--is-inside-work-tree"], capture_output=True, text=True,
                                env=dict(os.environ, GIT_OPTIONAL_LOCKS="0")).stdout.strip() == "true" if os.path.isdir(root) else False
        base_root = root if inside else here
    base_root = os.path.abspath(base_root)
    return run(root, git_reader(base_root, opts.get("--base") or DEFAULT_BASE))


if __name__ == "__main__":
    sys.exit(main(sys.argv))

#!/bin/bash
# 빌드 직렬화 잠금(source 해서 쓴다). 같은 Configuration 의 빌드는 헤드(Desktop·Browser·Android)·PdDiagnostics·PdChannel 과
# 무관하게 겹치지 않는다. 공유 프로젝트(xkxk.frameworks.ui·xkxk.frameworks.ui.views·xkxk.frameworks.ui.data·xkxk.frameworks.ui.shell)의 obj/<Configuration> 을 함께 쓰기 때문이다.
# 사용: source build_lock.sh; pd_build_lock <ROOT> <Configuration> <LOG>   (실패 시 rc 75 로 종료)
#   Work·Product·R2R 은 Release 로 빌드하므로 Release 잠금을 쓴다(호출 쪽이 Release 로 넘긴다).
#   대기 상한 PD_BUILD_LOCK_WAIT 초(기본 1800). 잠금은 이 스크립트가 끝날 때 fd 9 가 닫히며 풀린다.
#   dotnet 은 반드시 `9>&-` 로 실행한다(MSBuild 노드 재사용 프로세스가 잠금 fd 를 물고 남는 것을 막는다).
pd_build_lock() {
  local root="$1" conf="$2" log="$3"
  mkdir -p "$root/logs"
  exec 9>"$root/logs/.build_${conf}.lock"
  if ! flock -n 9; then
    echo "# 같은 Configuration($conf) 빌드가 진행 중 — 잠금 대기(최대 ${PD_BUILD_LOCK_WAIT:-1800}초)" >>"$log"
    if ! flock -w "${PD_BUILD_LOCK_WAIT:-1800}" 9; then
      echo "# 빌드 잠금 대기 초과: $root/logs/.build_${conf}.lock" >>"$log"
      echo "75" >"$log.rc"
      echo "LOG=$log rc=75"
      exit 75
    fi
    echo "# 잠금 획득 $(date '+%Y-%m-%d %H:%M:%S')" >>"$log"
  fi
}

# 폐포 검사(check_assembly_closure.py) 실행: pd_closure_check <TOOLS> <LOG> <경로...>  → 누락 있으면 rc!=0 반환
pd_closure_check() {
  local tools="$1" log="$2"; shift 2
  echo "# 어셈블리 폐포 검사" >>"$log"
  python3 -I "$tools/check_assembly_closure.py" "$@" 2>&1 | grep -v ' mvid=' >>"$log"
  return "${PIPESTATUS[0]}"
}

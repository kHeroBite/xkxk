#!/bin/bash
# Pd.Poc.Desktop 을 Windows dotnet.exe 로 비증분 빌드하는 스크립트 (WSL 에서 실행)
# 사용: build_desktop.sh [--bg] [Release|Debug|R2R|Work|Product]
#   진단 기능(PdDiagnostics, poc/ui/Directory.Build.props)은 시험 빌드라 기본 true 다. Product 는 false(제품 확인용, bin/Product).
#   환경변수: PD_OUT_NAME=<bin 하위 폴더>(Work/Product 출력 폴더 이름 바꾸기), PD_EXTRA_PROPS="-p:이름=값 …"(대조 빌드 속성)
#   Work: Release 최적화로 bin/Work/net11.0 에만 출력(실행 중이거나 판본 고정된 bin/Release·bin/R2R 를 건드리지 않는 작업 빌드). 실행은 run_desktop_bg.sh --conf Work
#   R2R : Release 를 win-x64 ReadyToRun 으로 게시(bin/R2R/net11.0). 기동 JIT 비용 절감. 실행은 run_desktop_bg.sh --conf R2R
#   --bg : 분리 실행(setsid nohup). 로그 경로와 rc 파일 경로를 출력하고 즉시 반환한다.
# 직렬화: 같은 Configuration 빌드는 build_lock.sh 의 flock 으로 순서를 기다린다(Work·Product·R2R 은 Release 잠금).
# 끝에서 check_assembly_closure.py 로 출력 폴더 Pd.* 어셈블리 참조 폐포를 검사한다(누락이면 rc=4).
# 결과: logs/build_desktop_<시각>.log, 같은 이름 .rc (종료코드). 성공 시 rc=0.
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$TOOLS/.." && pwd)"
DOTNET="${DOTNET_EXE:-/mnt/c/Program Files/dotnet/dotnet.exe}"
# shellcheck source=build_lock.sh
. "$TOOLS/build_lock.sh"

if [ "${1:-}" = "--bg" ]; then
  shift
  mkdir -p "$ROOT/logs"
  LOG="$ROOT/logs/build_desktop_$(date +%Y%m%d_%H%M%S).log"
  BUILD_LOG="$LOG" setsid nohup "$TOOLS/build_desktop.sh" "$@" </dev/null >/dev/null 2>&1 &
  echo "LOG=$LOG"
  echo "RC=$LOG.rc"
  exit 0
fi

CONF="${1:-Release}"
mkdir -p "$ROOT/logs"
LOG="${BUILD_LOG:-$ROOT/logs/build_desktop_$(date +%Y%m%d_%H%M%S).log}"
rm -f "$LOG.rc"
PROJ_WIN="$(wslpath -w "$ROOT/src/Pd.Poc.Desktop/Pd.Poc.Desktop.csproj")"

{
  echo "# build_desktop start $(date '+%Y-%m-%d %H:%M:%S') conf=$CONF"
  echo "# project=$PROJ_WIN"
} >"$LOG"

LOCK_CONF="$CONF"
case "$CONF" in Work|Product|R2R) LOCK_CONF=Release ;; esac
pd_build_lock "$ROOT" "$LOCK_CONF" "$LOG"
cd "$ROOT" || exit 2
DIAG=true
[ "$CONF" = "Product" ] && DIAG=false
# shellcheck disable=SC2206
EXTRA=(-p:PdDiagnostics=$DIAG ${PD_EXTRA_PROPS:-})
echo "# props: ${EXTRA[*]}" >>"$LOG"
if [ "$CONF" = "Work" ] || [ "$CONF" = "Product" ]; then
  # 출력 폴더를 비운 뒤 빌드한다(이전 빌드가 남긴 위성 폴더·고아 파일이 판정에 섞이지 않게). win-x64 고정 = 다른 RID 네이티브 런타임(약 550MB) 복사 방지
  OUT_DIR="$ROOT/src/Pd.Poc.Desktop/bin/${PD_OUT_NAME:-$CONF}"
  rm -rf "${OUT_DIR:?}"
  OUT_WIN="$(wslpath -w "$OUT_DIR/net11.0")"
  "$DOTNET" build "$PROJ_WIN" -c Release -r win-x64 --self-contained false -p:OutDir="$OUT_WIN\\" "${EXTRA[@]}" --no-incremental -nologo -v:minimal -clp:Summary >>"$LOG" 2>&1 9>&-
elif [ "$CONF" = "R2R" ]; then
  R2R_DIR="$ROOT/src/Pd.Poc.Desktop/bin/${PD_OUT_NAME:-R2R}"
  rm -rf "${R2R_DIR:?}"
  OUT_WIN="$(wslpath -w "$R2R_DIR/net11.0")"
  "$DOTNET" publish "$PROJ_WIN" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true "${EXTRA[@]}" -o "$OUT_WIN" -nologo -v:minimal -clp:Summary >>"$LOG" 2>&1 9>&-
else
  "$DOTNET" build "$PROJ_WIN" -c "$CONF" "${EXTRA[@]}" --no-incremental -nologo -v:minimal -clp:Summary >>"$LOG" 2>&1 9>&-
fi
rc=$?
if [ "$rc" -eq 0 ]; then
  case "$CONF" in
    Work|Product) CHK="$OUT_DIR/net11.0" ;;
    R2R) CHK="$R2R_DIR/net11.0" ;;
    *) CHK="$ROOT/src/Pd.Poc.Desktop/bin/$CONF/net11.0" ;;
  esac
  pd_closure_check "$TOOLS" "$LOG" "$CHK" || { echo "# 어셈블리 폐포 누락" >>"$LOG"; rc=4; }
fi
echo "# build_desktop end $(date '+%Y-%m-%d %H:%M:%S') rc=$rc" >>"$LOG"
echo "$rc" >"$LOG.rc"
echo "LOG=$LOG rc=$rc"
exit "$rc"

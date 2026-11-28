#!/bin/bash
# 브라우저 실행 프로젝트(WASM)를 Windows dotnet.exe 로 publish 하여 정적 번들(wwwroot)을 만드는 스크립트
# 사용: build_browser.sh [--bg] [Release|Debug]
#   --bg : 분리 실행(setsid nohup). 로그 경로와 rc 파일 경로를 출력하고 즉시 반환한다.
# 직렬화: 같은 Configuration 빌드는 build_lock.sh 의 flock 으로 순서를 기다린다(다른 헤드·PdDiagnostics 여도 겹치지 않음).
# 끝에서 check_assembly_closure.py 로 번들 안 프레임워크 어셈블리 참조 폐포를 검사한다(누락이면 rc=4).
# 결과: logs/build_browser_<시각>.log, 같은 이름 .rc, 번들 경로는 로그 마지막 줄 BUNDLE=...
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$TOOLS/.." && pwd)"
# 정체성·경로 값은 단일 출처(host_identity.json)에서 조회한다. 실패하면 오류 종료한다.
hid() { local v; v="$(python3 -B "$TOOLS/host_identity.py" get "$1" 2>/dev/null)" && [ -n "$v" ] || { echo "host_identity 조회 실패: $1" >&2; return 70; }; printf '%s' "$v"; }
BROWSER_CSPROJ="$(hid browser.csproj)" || exit 70
BROWSER_DIR="$(hid browser.project_dir)" || exit 70
DOTNET="${DOTNET_EXE:-/mnt/c/Program Files/dotnet/dotnet.exe}"
# shellcheck source=build_lock.sh
. "$TOOLS/build_lock.sh"

if [ "${1:-}" = "--help" ] || [ "${1:-}" = "-h" ]; then
  echo "사용: build_browser.sh [--bg] [Release|Debug]  (환경변수 PD_DIAG=false 제품 확인 빌드)"
  echo "  실험 빌드: PD_EXTRA_PROPS=\"-p:이름=값 ...\" 를 build·publish 에 함께 넘기고, PD_OUT_DIR=<폴더> 면 그 폴더에 게시한다(둘 다 비우면 기존 동작, 예: PD_EXTRA_PROPS=\"-p:WasmEmitSymbolMap=true -p:WasmNativeStrip=false\" 이름 섹션 진단 번들)"
  exit 0
fi

if [ "${1:-}" = "--bg" ]; then
  shift
  mkdir -p "$ROOT/logs"
  LOG="$ROOT/logs/build_browser_$(date +%Y%m%d_%H%M%S).log"
  BUILD_LOG="$LOG" setsid nohup "$TOOLS/build_browser.sh" "$@" </dev/null >/dev/null 2>&1 &
  echo "LOG=$LOG"
  echo "RC=$LOG.rc"
  exit 0
fi

CONF="${1:-Release}"
mkdir -p "$ROOT/logs"
LOG="${BUILD_LOG:-$ROOT/logs/build_browser_$(date +%Y%m%d_%H%M%S).log}"
rm -f "$LOG.rc"
PROJ_DIR="$ROOT/$BROWSER_DIR"
PROJ_WIN="$(wslpath -w "$ROOT/$BROWSER_CSPROJ")"
# 진단 기능은 시험 빌드라 기본 true. PD_DIAG=false 면 제품 확인 빌드로 publish_product 에 낸다(서빙 중인 publish 를 덮지 않음).
DIAG="${PD_DIAG:-true}"
OUT_DIR="$PROJ_DIR/bin/$CONF/net11.0-browser/publish"
[ "$DIAG" = "false" ] && OUT_DIR="$PROJ_DIR/bin/$CONF/net11.0-browser/publish_product"
# 실험 빌드용(기본 비어 있음 — 기존 동작 그대로): PD_EXTRA_PROPS="-p:이름=값 ..." 를 build·publish 에 함께 넘기고, PD_OUT_DIR 이 있으면 그 폴더에 게시한다(동결·서빙 번들을 덮지 않게)
EXTRA_PROPS="${PD_EXTRA_PROPS:-}"
[ -n "${PD_OUT_DIR:-}" ] && OUT_DIR="$PD_OUT_DIR"

{
  echo "# build_browser start $(date '+%Y-%m-%d %H:%M:%S') conf=$CONF"
  echo "# project=$PROJ_WIN"
  [ -n "$EXTRA_PROPS" ] && echo "# extra_props=$EXTRA_PROPS out=$OUT_DIR"
} >"$LOG"

pd_build_lock "$ROOT" "$CONF" "$LOG"
cd "$ROOT" || exit 2
# publish 는 --no-incremental 을 받지 않고(MSB1001), WASM publish 는 --no-build 도 거부한다(NETSDK1085).
# 그래서 비증분 build 로 오류·경고를 판정한 뒤 일반 publish(직전 build 재사용)로 번들을 만든다.
"$DOTNET" build "$PROJ_WIN" -c "$CONF" -p:PdDiagnostics=$DIAG $EXTRA_PROPS --no-incremental -nologo -v:minimal -clp:Summary >>"$LOG" 2>&1 9>&-
rc=$?
if [ "$rc" -eq 0 ]; then
  echo "# publish 단계" >>"$LOG"
  rm -rf "${OUT_DIR:?}" # 이전 publish 의 고아 wasm(해시 이름 바뀐 구판)이 남지 않게 비운 뒤 게시
  mkdir -p "$OUT_DIR"
  "$DOTNET" publish "$PROJ_WIN" -c "$CONF" -p:PdDiagnostics=$DIAG $EXTRA_PROPS -nologo -v:minimal \
    -o "$(wslpath -w "$OUT_DIR")" >>"$LOG" 2>&1 9>&-
  rc=$?
fi
if [ "$rc" -eq 0 ] && [ ! -f "$OUT_DIR/wwwroot/index.html" ]; then
  echo "# 번들 누락: $OUT_DIR/wwwroot/index.html 없음" >>"$LOG"
  rc=3
fi
if [ "$rc" -eq 0 ]; then
  pd_closure_check "$TOOLS" "$LOG" "$OUT_DIR/wwwroot" || { echo "# 어셈블리 폐포 누락" >>"$LOG"; rc=4; }
fi
echo "# build_browser end $(date '+%Y-%m-%d %H:%M:%S') rc=$rc" >>"$LOG"
echo "BUNDLE=$OUT_DIR/wwwroot" >>"$LOG"
echo "$rc" >"$LOG.rc"
echo "LOG=$LOG rc=$rc"
exit "$rc"

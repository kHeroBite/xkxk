#!/bin/bash
# Pd.Poc.Android 를 Windows dotnet.exe 로 비증분 빌드하는 스크립트 (WSL 에서 실행)
# 사용: build_android.sh [--bg] [Debug|Release] [PdDiagnostics=true|false]
#   두 번째 인자 PdDiagnostics 기본 true(시험 빌드). false 면 제품 확인용 진단 제거 빌드.
#   환경변수: PD_EXTRA_PROPS="-p:이름=값 …"(예: -p:PdChannel=prod), PD_BUILD_LOCK_WAIT=<초>
#   --bg : 분리 실행(setsid nohup). 로그 경로와 rc 파일 경로를 출력하고 즉시 반환한다.
# 직렬화: 같은 Configuration 빌드는 build_lock.sh 의 flock 으로 순서를 기다린다(데스크톱·브라우저와 obj 공유).
# 끝에서 check_assembly_closure.py 로 APK 안 Pd.* 어셈블리 참조 폐포를 검사한다(누락이면 rc=4).
# 결과: logs/build_android_<시각>.log, 같은 이름 .rc. 마지막 줄 APK=...
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$TOOLS/.." && pwd)"
DOTNET="${DOTNET_EXE:-/mnt/c/Program Files/dotnet/dotnet.exe}"
# shellcheck source=build_lock.sh
. "$TOOLS/build_lock.sh"

if [ "${1:-}" = "--bg" ]; then
  shift
  mkdir -p "$ROOT/logs"
  LOG="$ROOT/logs/build_android_$(date +%Y%m%d_%H%M%S).log"
  BUILD_LOG="$LOG" setsid nohup "$TOOLS/build_android.sh" "$@" </dev/null >/dev/null 2>&1 &
  echo "LOG=$LOG"
  echo "RC=$LOG.rc"
  exit 0
fi

CONF="${1:-Debug}"
DIAG="${2:-true}"
DIAG="${DIAG#PdDiagnostics=}"
case "$CONF" in Debug|Release) ;; *) echo "구성은 Debug|Release 만 받는다: $CONF" >&2; exit 64 ;; esac
case "$DIAG" in true|false) ;; *) echo "PdDiagnostics 는 true|false: $DIAG" >&2; exit 64 ;; esac
mkdir -p "$ROOT/logs"
# Android SDK·JDK 경로: 환경변수 PD_ANDROID_SDK·PD_JAVA_SDK(Windows 경로)가 비어 있을 때만 사용자 프로필의 pd-android\{sdk,jdk} 후보를 존재 확인 뒤 쓴다.
# csproj 가 이 환경변수를 읽는다(WSLENV 로 Windows dotnet.exe 에 전달). 후보가 없으면 csproj 기본값·워크로드 기본 탐색에 맡긴다.
if [ -z "${PD_ANDROID_SDK:-}" ] || [ -z "${PD_JAVA_SDK:-}" ]; then
  UPROF="$(cmd.exe /c "echo %USERPROFILE%" 2>/dev/null </dev/null | tr -d '\r')"
  UPROF_WSL="$(wslpath -u "$UPROF" 2>/dev/null)"
  if [ -z "${PD_ANDROID_SDK:-}" ] && [ -n "$UPROF_WSL" ] && [ -d "$UPROF_WSL/pd-android/sdk" ]; then PD_ANDROID_SDK="$UPROF\\pd-android\\sdk"; fi
  if [ -z "${PD_JAVA_SDK:-}" ] && [ -n "$UPROF_WSL" ] && [ -d "$UPROF_WSL/pd-android/jdk" ]; then PD_JAVA_SDK="$UPROF\\pd-android\\jdk"; fi
fi
export PD_ANDROID_SDK PD_JAVA_SDK
export WSLENV="${WSLENV:+$WSLENV:}PD_ANDROID_SDK:PD_JAVA_SDK"
LOG="${BUILD_LOG:-$ROOT/logs/build_android_$(date +%Y%m%d_%H%M%S).log}"
rm -f "$LOG.rc"
PROJ_WIN="$(wslpath -w "$ROOT/src/Pd.Poc.Android/Pd.Poc.Android.csproj")"
APK="$ROOT/src/Pd.Poc.Android/bin/$CONF/net11.0-android/com.pd.poc-Signed.apk"

{
  echo "# build_android start $(date '+%Y-%m-%d %H:%M:%S') conf=$CONF PdDiagnostics=$DIAG"
  echo "# project=$PROJ_WIN"
} >"$LOG"

pd_build_lock "$ROOT" "$CONF" "$LOG"
cd "$ROOT" || exit 2
# shellcheck disable=SC2206
EXTRA=(-p:PdDiagnostics=$DIAG ${PD_EXTRA_PROPS:-})
echo "# props: ${EXTRA[*]}" >>"$LOG"
rm -f "$APK" # 이전 판본 APK 가 실패한 빌드의 산출물로 오인되지 않게 지운다
"$DOTNET" build "$PROJ_WIN" -c "$CONF" -f net11.0-android "${EXTRA[@]}" --no-incremental -nologo -v:minimal -clp:Summary >>"$LOG" 2>&1 9>&-
rc=$?
if [ "$rc" -eq 0 ] && [ ! -f "$APK" ]; then
  echo "# APK 누락: $APK" >>"$LOG"
  rc=3
fi
if [ "$rc" -eq 0 ]; then
  pd_closure_check "$TOOLS" "$LOG" "$APK" || { echo "# 어셈블리 폐포 누락" >>"$LOG"; rc=4; }
fi
echo "# build_android end $(date '+%Y-%m-%d %H:%M:%S') rc=$rc" >>"$LOG"
echo "APK=$APK" >>"$LOG"
echo "$rc" >"$LOG.rc"
echo "LOG=$LOG rc=$rc"
exit "$rc"

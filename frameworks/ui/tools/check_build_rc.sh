#!/bin/bash
# 최신 빌드 로그(logs/build_<축>_*.log)의 종료코드 파일(<로그>.log.rc)과 로그의 '오류 0개|0 Error(s)' 표기로 빌드 성공을 판정하는 스크립트
# 배경(UTODO-0016): build_*.sh 는 종료코드를 "$LOG.rc"(= build_x_<시각>.log.rc)에 쓴다.
#   이전 검수 조건은 ${L%.log}.rc(= build_x_<시각>.rc)를 읽어 파일이 없다고 실패했다. 이 스크립트는 .log.rc 를 읽는다.
#   혹시 옛 이름(.rc)만 있으면 그것을 대신 읽고 알린다.
# 사용: tools/check_build_rc.sh [desktop|browser|android|all ...] [--logs <폴더>] [--help]
#   축을 주지 않으면 all(desktop·browser·android 전부). 여러 축을 함께 줄 수 있다.
#   --logs : 로그 폴더(기본 <영역 루트>/logs).
# 출력: 축마다 한 줄 "PASS|FAIL <축> log=<경로> rc=<값> reason=<사유>".
# 종료코드: 0 = 모두 PASS, 1 = 하나 이상 FAIL(rc≠0·rc 파일 없음·오류 표기 없음·로그 없음), 2 = 인자 오류.
# 주의: 빌드가 도는 동안 build_*.sh 는 rc 파일을 지운다. 그때는 'rc 파일 없음(빌드 진행 중일 수 있음)' 으로 FAIL 이다.
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$TOOLS/.." && pwd)"
LOGS="$ROOT/logs"

usage() {
  sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
}

KINDS=""
while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --logs)
      if [ $# -lt 2 ]; then echo "오류: --logs 뒤에 폴더가 필요하다" >&2; exit 2; fi
      LOGS="$2"; shift 2; continue ;;
    desktop|browser|android) KINDS="$KINDS $1" ;;
    all) KINDS="$KINDS desktop browser android" ;;
    *) echo "오류: 알 수 없는 인자 '$1' (--help 참조)" >&2; exit 2 ;;
  esac
  shift
done
[ -z "$KINDS" ] && KINDS="desktop browser android"

if [ ! -d "$LOGS" ]; then
  echo "오류: 로그 폴더 없음: $LOGS" >&2
  exit 2
fi

fail=0
for kind in $KINDS; do
  # 최신 로그(.log 로 끝나는 것만 — .log.rc 는 제외된다)
  L="$(ls -t "$LOGS"/build_"$kind"_*.log 2>/dev/null | head -1)"
  if [ -z "$L" ]; then
    echo "FAIL $kind log=- rc=- reason=로그 없음($LOGS/build_${kind}_*.log)"
    fail=1
    continue
  fi

  RCF="$L.rc"
  note=""
  if [ ! -f "$RCF" ] && [ -f "${L%.log}.rc" ]; then
    RCF="${L%.log}.rc"
    note=" (옛 이름 .rc 사용)"
  fi
  if [ ! -f "$RCF" ]; then
    echo "FAIL $kind log=$L rc=- reason=rc 파일 없음(빌드 진행 중일 수 있음)"
    fail=1
    continue
  fi

  rc="$(tr -d ' \r\n' <"$RCF")"
  if [ "$rc" != "0" ]; then
    echo "FAIL $kind log=$L rc=$rc reason=종료코드가 0 이 아님$note"
    fail=1
    continue
  fi
  if ! grep -qE '오류 0개|0 Error\(s\)' "$L"; then
    echo "FAIL $kind log=$L rc=$rc reason=로그에 '오류 0개|0 Error(s)' 표기 없음$note"
    fail=1
    continue
  fi
  echo "PASS $kind log=$L rc=$rc reason=rc=0·오류 0개$note"
done

exit "$fail"

#!/bin/bash
# 별별UI 프레임워크 검증 일괄 실행기 — contract(계약 시험+카나리아)·layers(레이어 검사)·api(공개 API 표면 검사) 단계를 돌려 요약 JSON 을 남긴다
# 사용: verify_framework.sh [--stage contract,layers,api] [--ci] [--help]
#   contract : 빌드 잠금(Release) 안에서 dotnet.exe run 으로 ContractCheck 를 돌린다 → logs/contract_check.json(rc 0 이어야 함)
#              이어서 --canary 로 한 번 더 돌린다 → logs/contract_check_canary.json(rc 1·failed>=1 이어야 함, 시험 장치 거짓 PASS 방지)
#   layers   : tools/check_layers.py, 같은 파일 --self-test, tools/classify_layers.py --check
#   api      : api_surface_check.py --self-test, 이어서 최신 logs/build_desktop_*.log 를 --log 로 넘긴 본 검사(로그가 없으면 rc=2)
#   --ci     : 예약(지금은 동작 동일)
# 결과: logs/verify_framework_summary.json {"stages":{"<단계>":{"rc":n},...},"rc":n} (고정 경로 덮어쓰기), 종료코드 0=전부 PASS
#       contract 단계의 빌드·실행 출력은 logs/verify_framework_contract.log (고정 경로 덮어쓰기)
# 종료코드: 0 전부 PASS, 1 단계 실패, 2 인자 오류·알 수 없는 단계
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
DOTNET="${DOTNET_EXE:-/mnt/c/Program Files/dotnet/dotnet.exe}"
LOGS="$ROOT/logs"
SUMMARY="$LOGS/verify_framework_summary.json"
STAGES="contract,layers,api"

usage() {
  sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
  case "$1" in
    --stage) [ $# -ge 2 ] || { echo "--stage 값이 없습니다" >&2; exit 2; }; STAGES="$2"; shift 2 ;;
    --stage=*) STAGES="${1#--stage=}"; shift ;;
    --ci) shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "알 수 없는 인자: $1" >&2; usage >&2; exit 2 ;;
  esac
done

IFS=',' read -r -a LIST <<<"$STAGES"
[ "${#LIST[@]}" -gt 0 ] || { echo "단계가 비었습니다" >&2; exit 2; }
for s in "${LIST[@]}"; do
  case "$s" in contract|layers|api) ;; *) echo "알 수 없는 단계: $s (contract|layers|api)" >&2; exit 2 ;; esac
done

mkdir -p "$LOGS"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

stage_contract() {
  local clog="$LOGS/verify_framework_contract.log" proj out canary rc crc
  proj="$(wslpath -w "$ROOT/test/framework/contract/ContractCheck.csproj")"
  out="$(wslpath -w "$LOGS/contract_check.json")"
  canary="$(wslpath -w "$LOGS/contract_check_canary.json")"
  rm -f "$LOGS/contract_check.json" "$LOGS/contract_check_canary.json"
  echo "# verify_framework contract start $(date '+%Y-%m-%d %H:%M:%S')" >"$clog"
  (
    # shellcheck source=../../tools/build_lock.sh
    . "$ROOT/tools/build_lock.sh"
    pd_build_lock "$ROOT" Release "$clog"
    cd "$ROOT" || exit 2
    "$DOTNET" run --project "$proj" -c Release -- --out "$out" >>"$clog" 2>&1 9>&-
    echo "$?" >"$TMP/contract.rc"
    "$DOTNET" run --project "$proj" -c Release --no-build -- --canary --out "$canary" >>"$clog" 2>&1 9>&-
    echo "$?" >"$TMP/canary.rc"
  )
  rc="$(cat "$TMP/contract.rc" 2>/dev/null || echo 2)"
  crc="$(cat "$TMP/canary.rc" 2>/dev/null || echo 2)"
  grep -E '^(PASS|FAIL) C[0-9]{2} |^요약:' "$clog" | sed 's/^/  /'
  echo "# verify_framework contract end rc=$rc canary_rc=$crc" >>"$clog"
  if [ "$rc" != "0" ] || [ ! -s "$LOGS/contract_check.json" ]; then
    echo "  contract: 계약 시험 실패 rc=$rc (출력 $clog)"
    return 1
  fi
  if [ "$crc" != "1" ] || ! grep -Eq '"failed": *[1-9]' "$LOGS/contract_check_canary.json" 2>/dev/null; then
    echo "  contract: 카나리아가 실패를 보고하지 않음 rc=$crc (시험 장치 거짓 PASS 의심)"
    return 1
  fi
  echo "  contract: 계약 시험 rc=0, 카나리아 rc=1(정상)"
  return 0
}

stage_layers() {
  local rc=0
  (cd "$ROOT" && python3 -B tools/check_layers.py) || rc=1
  (cd "$ROOT" && python3 -B tools/check_layers.py --self-test) || rc=1
  (cd "$ROOT" && python3 -B tools/classify_layers.py --check) || rc=1
  return "$rc"
}

stage_api() {
  local tool="$ROOT/test/framework/judge/api_surface_check.py" log
  python3 -B "$tool" --self-test || return 1
  log="$(ls -t "$LOGS"/build_desktop_*.log 2>/dev/null | head -1)"
  if [ -z "$log" ]; then
    echo "  api: 데스크톱 빌드 로그(logs/build_desktop_*.log)가 없습니다 — 부재는 통과가 아님"
    return 2
  fi
  echo "  api: 빌드 로그 $log"
  python3 -B "$tool" --root "$ROOT" --log "$log"
}

TOTAL=0
JSON=""
for s in "${LIST[@]}"; do
  echo "== $s"
  "stage_$s"
  rc=$?
  echo "== $s rc=$rc"
  [ "$rc" -eq 0 ] || TOTAL=1
  JSON="${JSON:+$JSON,}\"$s\":{\"rc\":$rc}"
done

printf '{"stages":{%s},"rc":%d,"generated_at":"%s"}\n' "$JSON" "$TOTAL" "$(date '+%Y-%m-%dT%H:%M:%S%z')" >"$TMP/summary.json"
cp "$TMP/summary.json" "$SUMMARY"
echo "요약: $SUMMARY rc=$TOTAL"
exit "$TOTAL"

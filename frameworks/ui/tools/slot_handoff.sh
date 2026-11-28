#!/bin/bash
# 데스크톱 실행 슬롯을 새 소유자에게 이관하는 스크립트 — slot_owner.txt 첫 줄 교체·이력 추가·다시 읽어 일치 확인(불일치 rc=1)
# 사용: tools/slot_handoff.sh <새 소유자|없음> [메모]
# 규칙: 슬롯 이관은 이 스크립트로만 한다(run_desktop_bg.sh 는 첫 줄만 소유자로 읽는다 — 03:36 교착 재발 방지).
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
hi_req() { # 호스트 정체성 값을 조회해 변수에 담는다(조회 실패·빈 값이면 오류 종료)
  local _v
  _v="$(python3 -B "$TOOLS/host_identity.py" get "$2" 2>/dev/null)" && [ -n "$_v" ] || { echo "host_identity 조회 실패: $2" >&2; exit 2; }
  printf -v "$1" '%s' "$_v"
}
hi_req EXE_NAME desktop.exe_name
NEW="${1:?새 소유자 이름이 필요하다(예: otest-1, 없음)}"
MEMO="${2:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
F="$ROOT/logs/odev4b/slot_owner.txt"
TMP="$F.tmp.$$"
TS="$(date '+%Y-%m-%dT%H:%M:%S')"

# 앱이 떠 있으면 이관하지 않는다(이전 소유자 반납 확인 전제)
if tasklist.exe /FI "IMAGENAME eq $EXE_NAME" /FO CSV /NH 2>/dev/null | grep -qi "^\"$EXE_NAME\""; then
  echo "HANDOFF=REFUSED 데스크톱 앱이 실행 중 — 이전 소유자 반납(프로세스 0개) 확인 뒤 다시 실행"
  exit 2
fi

OLD="$(head -1 "$F" 2>/dev/null)"
{
  echo "$NEW $TS"
  tail -n +2 "$F" 2>/dev/null
  echo "$TS 이관 [$OLD] → $NEW${MEMO:+ ($MEMO)}"
} > "$TMP" && mv "$TMP" "$F"

GOT="$(head -1 "$F")"
GOT_OWNER="${GOT%% *}"
echo "첫줄=$GOT"
if [ "$GOT_OWNER" = "$NEW" ]; then
  echo "HANDOFF=OK owner=$NEW"
  exit 0
fi
echo "HANDOFF=FAIL 기대=$NEW 실제=$GOT_OWNER"
exit 1

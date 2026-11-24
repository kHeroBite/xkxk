# otest-12 포그라운드 실입력 공용 함수 — 매 입력 직전 전경 pid 게이트·safe_click 경유·경계 100px 검사·client→화면 좌표 변환(사용: bash -c 'source fg_input.sh; ...')
U=$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")/../../../.." && pwd)  # 영역 루트 = 이 파일 위치 기준 네 단계 위, source 해도 BASH_SOURCE 로 판정(UTODO-0012)
D=$U/logs/otest/desktop
FGI=$(wslpath -w "$U/test/common/measure/fg_info.ps1")  # D-TESTDIR: 옛 $D/ui/fg_info.ps1
SC=$(wslpath -w "$U/tools/safe_click.ps1")  # 옛 C:\DATA\Project\pd\.claude\scripts\safe_click.ps1(2026-11-24 영역 tools/ 로 이전)
FB=$(wslpath -w "$U/test/common/measure/frame_bounds.ps1")  # D-TESTDIR: 옛 logs/odev4b/frame_bounds.ps1
# 필수 환경: PID(대상 프로세스) H(전경으로 가져올 hwnd) OX OY(메인 클라이언트 원점)
ps1() { powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$@" 2>&1 | tr -d '\r' | iconv -f cp949 -t utf-8 -c; return ${PIPESTATUS[0]}; }
# 주 모니터 허용 영역(경계 100px 밖): 기본 x 100~2460, y 100~1292. 모니터가 바뀌면 환경변수 ZXMAX·ZYMAX 로 넘긴다
inzone() { [ "$1" -ge 100 ] && [ "$1" -le "${ZXMAX:-2460}" ] && [ "$2" -ge 100 ] && [ "$2" -le "${ZYMAX:-1292}" ]; }
gate() {
  local g; g=$(ps1 "$FGI" -Restore "${1:-$H}" | tail -1)
  echo "$g" | grep -q "pid=$PID " || { echo "GATE_FAIL $g"; return 9; }
  LASTCUR=$(echo "$g" | sed -E 's/.*cursor=\(([-0-9]+,[-0-9]+)\).*/\1/')
}
# 화면 좌표 드래그: sx sy ex ey [hwnd]
sdrag() {
  inzone "$1" "$2" && inzone "$3" "$4" || { echo "ZONE_REFUSE $*"; return 8; }
  gate "$5" || return 9
  local o; o=$(ps1 "$SC" -ScreenX "$1" -ScreenY "$2" -DragToX "$3" -DragToY "$4" -ExpectedProcessId "$PID"); local rc=$?
  echo "drag ($1,$2)->($3,$4) rc=$rc precursor=$LASTCUR"; [ $rc -ne 0 ] && echo "$o" | grep -E "가드 차단" | head -1
  sleep 1; return $rc
}
# client 좌표 드래그: cx cy ex ey
cdrag() { sdrag $((OX+$1)) $((OY+$2)) $((OX+$3)) $((OY+$4)); }
# client 좌표 클릭(3번째 인자 dbl 이면 더블클릭)
cclick() {
  local x=$((OX+$1)) y=$((OY+$2)); inzone $x $y || { echo "ZONE_REFUSE $x,$y"; return 8; }
  gate || return 9
  local extra=""; [ "$3" = dbl ] && extra="-DoubleClick"
  local o; o=$(ps1 "$SC" -ScreenX $x -ScreenY $y $extra -ExpectedProcessId "$PID"); local rc=$?
  echo "click$3 ($x,$y) rc=$rc precursor=$LASTCUR"; [ $rc -ne 0 ] && echo "$o" | grep -E "가드 차단" | head -1
  sleep 0.8; return $rc
}
c() { python3 -B "$U/test/desktop/measure/fg_drive.py" call "$1" "${2:-{\}}" | sed 's/^RESULT_JSON //'; }
tabs() { c panel.tabs '{"id":"LeftPane"}' | python3 -B -c 'import sys,json;r=json.loads(sys.stdin.read())["result"];print("tabs",[(t["id"],t["active"]) for t in r["tabs"]])'; }
docs() { c docs.list | python3 -B -c 'import sys,json;r=json.loads(sys.stdin.read())["result"];print("docs",[(d["id"],d.get("floating"),d.get("zIndex"),d.get("zReason"),(d.get("dockWindow") or {}).get("host")) for d in r])'; }
wins() { ps1 "$FB" | grep hwnd; }

#!/bin/bash
# 데스크톱 측정 래퍼 공용 함수(기동·PID·종료·부하 게이트·전경 감시·사용자 파일 md5). 실입력 없음, 항상 --offscreen-guard on
U=/mnt/c/DATA/Project/xkxk/frameworks/ui
O=$U/logs/otest/desktop/i2400
FG=$U/test/common/measure/fgwatch2.ps1  # D-TESTDIR: 옛 logs/otest/desktop/i1835rc/fgwatch2.ps1
CONF=${CONF:-i2400rc_diag}   # 환경변수 CONF 로 판 지정(사본 없이 확장). 기본 i2400rc_diag
OWNER=otest-11
l1() { cut -d' ' -f1 /proc/loadavg; }
low() { awk -v l="$(l1)" 'BEGIN{exit !(l<2.0)}'; }
apps() { tasklist.exe 2>/dev/null | grep -ci Pd.Poc.Desktop; }
dnet() { tasklist.exe 2>/dev/null | grep -ci dotnet.exe; }
userdir() { echo "$(wslpath "$(cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')")/PdPoc"; }
usermd5() { local d; d=$(userdir); echo "layout=$(md5sum "$d/layout.json" | cut -c1-8) settings=$(md5sum "$d/settings.json" | cut -c1-8)"; }

# 게이트: dotnet.exe 0개 + load1<2.0 을 최대 300초 기다린다. 결과는 GATE=ok|ref, 대기·load 는 한 줄로 남긴다
gate() {
  local w=0
  while { ! low || [ "$(dnet)" -ne 0 ]; } && [ $w -lt 300 ]; do sleep 10; w=$((w+10)); done
  GATE=ok; { low && [ "$(dnet)" -eq 0 ]; } || GATE=ref
  echo "$1 gate=$GATE waited=${w}s load=$(cut -d' ' -f1-3 /proc/loadavg) dotnet_win=$(dnet) at=$(date +%T)" >> "$RUNS"
}

# 전경 감시 시작/종료(HIT 가 나오면 호출자가 team-lead 에 즉시 보고해야 한다)
fg_start() {
  FGOUT=$O/fgwatch_$1.txt; FGSTOP=$O/fgstop_$1.flag; rm -f "$FGSTOP"; : > "$FGOUT"
  setsid nohup powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w "$FG")" -Seconds 7200 -Out "$(wslpath -w "$FGOUT")" -StopFile "$(wslpath -w "$FGSTOP")" > /dev/null 2>&1 < /dev/null &
}
fg_stop() { : > "$FGSTOP"; sleep 3; echo "$1 fgwatch HIT=$(grep -c HIT "$FGOUT")" >> "$RUNS"; }

# 기동: 화면 밖·가드 on·원격 제어·fresh. 성공하면 PID 를 구한다
launch() {
  PD_SLOT_OWNER=$OWNER bash "$U/tools/run_desktop_bg.sh" --conf $CONF -- --remote-control --fresh --offscreen-guard on $LAUNCH_EXTRA > "$O/launch_$1.out" 2>&1
  grep -q RESULT=RUNNING "$O/launch_$1.out" || { echo "$1 기동 실패 $(grep RESULT "$O/launch_$1.out")" >> "$RUNS"; return 1; }
  PID=; for k in $(seq 1 20); do PID=$(tasklist.exe /FI "IMAGENAME eq Pd.Poc.Desktop.exe" /FO CSV /NH 2>/dev/null | grep -i Pd.Poc | head -1 | cut -d, -f2 | tr -d '"\r'); [ -n "$PID" ] && break; sleep 1; done
  sleep 4; [ -n "$PID" ]
}

# 종료: app.shutdown → 최대 15초 → 남으면 taskkill
shutdown_app() {
  (cd "$U" && python3 tools/pdctl.py app.shutdown > /dev/null 2>&1)
  for k in $(seq 1 15); do [ "$(apps)" -eq 0 ] && break; sleep 1; done
  [ "$(apps)" -gt 0 ] && [ -n "$PID" ] && taskkill.exe /PID "$PID" /F > /dev/null 2>&1   # 자기 PID 만 종료(otest-11, team-lead 승인 — /IM 전체 종료 금지)
  sleep 2
}

# 시작 전 점검(앱 0개·dotnet 기록·사용자 파일 md5)
preflight() {
  echo "$1 시작전 apps=$(apps) dotnet_win=$(dnet) $(usermd5) load=$(cut -d' ' -f1-3 /proc/loadavg) owner=$(head -1 "$U/logs/odev4b/slot_owner.txt")" >> "$RUNS"
  [ "$(apps)" -eq 0 ]
}
postflight() { echo "$1 종료후 apps=$(apps) $(usermd5) at=$(date +%T)" >> "$RUNS"; }

# C4 전용 기동: --fresh 가 --profile-dir 보다 우선하므로 --fresh 를 빼고 빈 임시 폴더(절대경로)를 profile-dir 로 준다. 사용자 파일은 읽지도 쓰지도 않음
launch_prof() {  # $1=태그 $2=프로필 폴더(절대경로, 호출자가 비운 새 폴더)
  PD_SLOT_OWNER=$OWNER bash "$U/tools/run_desktop_bg.sh" --conf $CONF -- --remote-control --profile-dir "$(wslpath -w "$2")" --offscreen-guard on $PROF_EXTRA > "$O/launch_$1.out" 2>&1
  grep -q RESULT=RUNNING "$O/launch_$1.out" || { echo "$1 기동 실패 $(grep RESULT "$O/launch_$1.out")" >> "$RUNS"; return 1; }
  PID=; for k in $(seq 1 20); do PID=$(tasklist.exe /FI "IMAGENAME eq Pd.Poc.Desktop.exe" /FO CSV /NH 2>/dev/null | grep -i Pd.Poc | head -1 | cut -d, -f2 | tr -d '"\r'); [ -n "$PID" ] && break; sleep 1; done
  sleep 4; [ -n "$PID" ]
}

# 사용자 원본 파일 불변 확인(trayc 등 --fresh 예외 기동 뒤 필수): layout 640aee92·settings f7aa276a 가 아니면 FAIL 을 RUNS 에 남기고 1 반환
check_userfiles() {
  local m; m=$(usermd5)
  case "$m" in *layout=640aee92*settings=f7aa276a*) echo "$1 userfiles OK $m" >> "$RUNS"; return 0;; esac
  echo "$1 userfiles FAIL $m" >> "$RUNS"; return 1
}

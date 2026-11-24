#!/bin/bash
# xkxk.frameworks.ui.desktop 을 비전경(화면 밖 --bg-pos)·stdio 분리로 기동하고 프로세스 실존을 2회 확인하는 스크립트
# 사용: run_desktop_bg.sh [--conf Release|Debug|R2R|JIT] [--kill|--no-kill] -- <앱 인자...>   (R2R = build_desktop.sh R2R 게시본)
#   기본: 다른 xkxk.frameworks.ui.desktop 프로세스가 있으면 종료하지 않고 실패(rc=3) — 실행 슬롯 충돌 방지.
#   --kill: 이 호출자(PD_SLOT_OWNER)가 이 스크립트로 띄워 기록한 자기 PID 만 종료하고 실행(다른 Pd 는 건드리지 않는다). 다른 Pd 가 남아 있으면 --multi-instance 없이는 rc=11.
#   --no-kill: 기존 프로세스를 그대로 두고 함께 실행. 기존 Pd 가 있으면 --multi-instance on(진단판·lead 승인)이 필요하고, 없으면 rc=11(단일 인스턴스 신호 차단).
#   결과는 자기 PID 기준이다: 성공이면 PID=<Windows PID> 와 RESULT=RUNNING, 단일 인스턴스에 걸리면 RESULT=SIGNALLED_EXISTING(rc=10). --multi-instance 기동은 logs/user_files/pending 을 덮지 않는다.
#   앱 인자 예: --open grid:5,gantt:5,form:10 --layout tile --screenshot /mnt/c/.../a.png --perf-out /mnt/c/.../p.json
#   --exit-after 미지정 시 30 을(앱 인자에 --remote-control 이 있으면 생략 — 종료는 pdctl app.shutdown), --bg-pos 미지정 시 auto(VirtualScreen 밖)를 자동 추가한다.
#   '/' 로 시작하는 인자는 Windows 경로로 변환한다. 앱 로그는 --log 로 logs/desktop_<시각>.log 에 남긴다.
# 금지: cmd.exe /c start, powershell Start-Process (L-022 — stdio 점유로 셸 무한 대기)
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$TOOLS/.." && pwd)"
CONF="Release"
KILL=check
while [ $# -gt 0 ]; do
  case "$1" in
    --conf) CONF="$2"; shift 2 ;;
    --no-kill) KILL=0; shift ;;
    --kill) KILL=1; shift ;;
    --) shift; break ;;
    *) break ;;
  esac
done

EXE="$ROOT/src/xkxk.frameworks.ui.desktop/bin/$CONF/net11.0/xkxk.frameworks.ui.desktop.exe"
IMAGE="xkxk.frameworks.ui.desktop.exe"
TS="$(date +%Y%m%d_%H%M%S_%3N)" # ms 까지 — 같은 초 동시 기동 시 로그 파일 충돌 방지
mkdir -p "$ROOT/logs"
APP_LOG="$ROOT/logs/desktop_$TS.log"
STDIO_LOG="$ROOT/logs/desktop_${TS}_stdio.log"

[ -f "$EXE" ] || { echo "실행파일 없음: $EXE (build_desktop.sh 먼저)"; exit 2; }

proc_count() { tasklist.exe /FI "IMAGENAME eq $IMAGE" /FO CSV /NH 2>/dev/null | grep -ci "^\"$IMAGE\""; }
# LEGACY: 사용자 Pd 앱 실행 파일 이름(Pd.Poc.Desktop.exe) — 같은 %LOCALAPPDATA%\PdPoc 폴더·단일 인스턴스 이름을 쓰므로 사용자 파일 대조 건너뜀·신호 차단 판정에만 센다(BUSY·--kill 대상은 새 이름만).
LEGACY_IMAGE="Pd.Poc.Desktop.exe" # LEGACY
legacy_count() { tasklist.exe /FI "IMAGENAME eq $LEGACY_IMAGE" /FO CSV /NH 2>/dev/null | grep -ci "^\"$LEGACY_IMAGE\""; }
any_count() { echo $(( $(proc_count) + $(legacy_count) )); }

# ⓪ 슬롯 소유자 확인(team-lead 02:2x 지시) — logs/odev4b/slot_owner.txt 첫 줄 '소유자 시각'. 호출자는 PD_SLOT_OWNER=<이름> 을 준다.
#    소유자가 아니면 기동하지 않는다(rc=6). 슬롯 관리자(odev-4b)가 넘길 때 이전 소유자의 회수 확인 뒤 이 파일을 바꾼다.
SLOT_FILE="$ROOT/logs/odev4b/slot_owner.txt"
SLOT_OWNER="$(head -1 "$SLOT_FILE" 2>/dev/null | awk '{print $1}')"
# PD_RUN_DRYRUN=1: 인자·화면 안 판정만 하고 기동하지 않는다(자체시험용 — 슬롯·실행 중 검사도 건너뜀, 앱을 띄우지 않으므로 안전)
DRY="${PD_RUN_DRYRUN:-}"
if [ -z "$DRY" ] && [ -n "$SLOT_OWNER" ] && [ "${PD_SLOT_OWNER:-}" != "$SLOT_OWNER" ]; then
  echo "데스크톱 슬롯 소유자가 아님: 현재 소유자=$SLOT_OWNER, 요청자=${PD_SLOT_OWNER:-(미지정)} — odev-4b 에 슬롯을 요청하라."
  echo "RESULT=NOT_OWNER"; exit 6
fi
[ -z "$DRY" ] && echo "$(date '+%Y-%m-%d %H:%M:%S') owner=${PD_SLOT_OWNER:-} conf=$CONF kill=$KILL args=$*" >> "$ROOT/logs/odev4b/slot_runs.log"

# ⓪-b 사용자 파일 보호(lead 10:2x — 제품판은 --fresh·--exit-after 를 무시하고 사용자 %LOCALAPPDATA%\PdPoc 를 쓴다)
#      직전 제품판 기동의 사용자 파일 md5 대조가 남아 있으면(앱 0개일 때) 먼저 대조한다. 다르면 기동 거부(rc=9) — 백업에서 확인·복구 후
#      logs/user_files/pending 을 지워야 다음 기동이 된다.
UF_DIR="$ROOT/logs/user_files"
USER_PD="$(wslpath "$(cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')" 2>/dev/null)/PdPoc"
# 대상: layout.json·settings.json·layouts/*(진단 원격 제어가 쓰는 remote-control.json 은 제외 — 진단판 기동마다 바뀐다)
# + 사용자 폴더 전체의 파일·폴더 이름 목록(logs\ 하위 포함, lead 10:4x — 새 파일·폴더가 생기면 불일치). remote-control.json 은 목록에서도 제외
#   일자 로그 logs/pd-YYYYMMDD.log(숫자 정확히 8자리)도 목록에서 제외(사용자 결정 Q2, 2026-11-24 — 앱이 매일 새로 만드는 파일이라 거짓 불일치). settings.json·layout.json md5 대조는 유지
uf_md5() { [ -d "$USER_PD" ] && (cd "$USER_PD" && find . -maxdepth 2 -type f \( -name layout.json -o -name settings.json -o -path './layouts/*' \) | sort | xargs -r md5sum; find . -mindepth 1 ! -name remote-control.json ! -path './logs/pd-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9].log' -printf 'entry %P\n' | sort) ; }
if [ -z "$DRY" ] && [ -f "$UF_DIR/pending" ] && [ "$(any_count)" -eq 0 ]; then
  PREV_TS="$(cat "$UF_DIR/pending")"
  BASE="$UF_DIR/$PREV_TS.md5"
  # 이름 목록(entry) 도입 전 기준 파일이면 md5 줄만 대조한다(형식 호환)
  # 기준 쪽에서도 일자 로그 entry 줄을 뺀다(이 제외 도입 전에 만든 기준 파일과 형식 호환)
  if diff -q <( if grep -q '^entry ' "$BASE"; then uf_md5; else uf_md5 | grep -v '^entry '; fi ) <(grep -vE '^entry logs/pd-[0-9]{8}\.log$' "$BASE") >/dev/null; then
    echo "사용자 파일 대조 OK(직전 제품판 $PREV_TS)"; rm -f "$UF_DIR/pending"
  else
    echo "사용자 파일 변경 감지 — 직전 제품판 기동($PREV_TS) 전후 md5 불일치. 백업: $UF_DIR/$PREV_TS/ — 확인·복구 후 $UF_DIR/pending 삭제."
    echo "RESULT=USER_FILES_CHANGED"; exit 9
  fi
elif [ -z "$DRY" ] && [ -f "$UF_DIR/pending" ]; then
  # 대조 건너뜀 기록 — Pd 가 1개 이상 떠 있으면 사용자 파일 대조(위)를 하지 않는다. 건너뛴 사실을 남겨 빈틈을 눈에 보이게 한다(검출을 늘리는 방향만).
  echo "사용자 파일 대조 건너뜀: $IMAGE 또는 $LEGACY_IMAGE 실행 중($(any_count)개) — 직전 제품판 기동 $(cat "$UF_DIR/pending") 기준 pending 유지. 앱 종료 후 다음 기동에서 대조된다."
  echo "$(date '+%Y-%m-%d %H:%M:%S') owner=${PD_SLOT_OWNER:-} conf=$CONF pending=$(cat "$UF_DIR/pending") proc=$(any_count) 대조 건너뜀" >> "$UF_DIR/skipped_compare.log" 2>/dev/null
fi
# ① 사전 확인 — 기본은 실행 중이면 실패(BUSY, 아무것도 죽이지 않음), --kill 은 슬롯 소유자만(위에서 확인) 자기가 기록한 PID 를 종료(①-1)
if [ -z "$DRY" ] && [ "$KILL" = "check" ] && [ "$(proc_count)" -gt 0 ]; then
  echo "실행 중인 $IMAGE 가 있어 기동하지 않음(실행 슬롯 사용 중). 종료 후 재시도하거나 --kill/--no-kill 을 명시하라."
  echo "RESULT=BUSY"; exit 3
fi
# ①-0 단일 인스턴스 신호 차단(team-lead 16:2x) — --multi-instance 없이 기존 Pd 옆에 띄우면 새 프로세스가 기존 창(사용자 Pd 일 수 있음)에 복원·활성화 신호를 보내고 끝난다.
HAS_MULTI=0
for a in "$@"; do case "$a" in --multi-instance|--multi-instance=*) HAS_MULTI=1 ;; esac; done
# ①-1 --kill 은 이 스크립트가 이 호출자(PD_SLOT_OWNER)로 띄워 기록한 PID 만 종료한다(team-lead 16:4x — 이미지 전체 종료는 사용자 Pd 까지 죽인다).
#      기록 한 줄 = 'PID 로그파일이름'. PID 재사용 오종료를 막으려고 그 프로세스 명령줄에 로그파일이름이 있을 때만 자기 것으로 본다. 대상이 없으면 아무것도 죽이지 않는다.
PID_DIR="$ROOT/logs/run_desktop_bg_pids"
PID_FILE="$PID_DIR/$(printf '%s' "${PD_SLOT_OWNER:-default}" | tr -c 'A-Za-z0-9_-' '_').txt"
kill_recorded() {
  if [ ! -s "$PID_FILE" ]; then echo "--kill: 기록된 자기 PID 없음 — 아무것도 종료하지 않음($PID_FILE)"; return 0; fi
  local pid base cmdline
  while read -r pid base; do
    case "$pid" in ''|*[!0-9]*) continue ;; esac
    cmdline="$(powershell.exe -NoProfile -Command "(Get-CimInstance Win32_Process -Filter 'ProcessId=$pid').CommandLine" 2>/dev/null | tr -d '\r')"
    if [ -z "$base" ] || [ "${cmdline#*"$base"}" = "$cmdline" ]; then
      echo "--kill: pid=$pid 는 자기 기동이 아니거나 이미 끝남 — 종료하지 않음"
      continue
    fi
    if [ -n "$DRY" ]; then echo "--kill: 종료 대상 pid=$pid ($base) (점검 모드라 종료하지 않음)"; continue; fi
    taskkill.exe /PID "$pid" /FI "IMAGENAME eq $IMAGE" /F >/dev/null 2>&1
    sleep 1
    if [ "$(tasklist.exe /FI "PID eq $pid" /FO CSV /NH 2>/dev/null | grep -ci "^\"$IMAGE\"")" -gt 0 ]; then echo "자기 프로세스 종료 실패: pid=$pid"; return 4; fi
    echo "--kill: 자기 프로세스 종료 pid=$pid"
  done < "$PID_FILE"
  [ -n "$DRY" ] || rm -f "$PID_FILE"
  return 0
}
if [ "$KILL" = "1" ]; then kill_recorded || exit 4; fi
# 옛 이름(사용자 Pd 앱)은 KILL 모드와 무관하게 거부한다 — 단일 인스턴스 이름 PdPoc.{채널}.Instance 를 공유하므로 신호가 사용자 창으로 간다(UTODO-0014).
if [ -z "$DRY" ] && [ "$HAS_MULTI" -eq 0 ] && { { [ "$KILL" != "check" ] && [ "$(proc_count)" -gt 0 ]; } || [ "$(legacy_count)" -gt 0 ]; }; then
  echo "실행 중인 $IMAGE 또는 $LEGACY_IMAGE 가 있어 기동하지 않음 — 그대로 띄우면 기존 창에 활성화 신호가 간다. 함께 띄우려면 --multi-instance on(진단판·lead 승인)을 준다."
  echo "RESULT=EXISTING_INSTANCE"; exit 11
fi

# 경로 인자 변환: 존재하지 않는 파일도 상위 폴더 기준으로 변환한다
to_win() {
  local p="$1" d b
  d="$(dirname "$p")"; b="$(basename "$p")"
  mkdir -p "$d" 2>/dev/null
  printf '%s\\%s' "$(wslpath -w "$d")" "$b"
}

ARGS=()
HAS_EXIT=0
HAS_POS=0
POS_VAL=""
PREV=""
for a in "$@"; do
  # '--bg-pos=값' 은 호스트가 해석하지 못해(정확히 '--bg-pos 값' 만 인식) 화면 안 일반 기동이 된다 → 두 토큰으로 바꾼다(odev-6r 05:1x 지적)
  if [ "${a#--bg-pos=}" != "$a" ]; then HAS_POS=1; POS_VAL="${a#--bg-pos=}"; ARGS+=(--bg-pos "$POS_VAL"); PREV=""; continue; fi
  case "$a" in
    --exit-after|--remote-control|--remote-control=*) HAS_EXIT=1 ;;
    --bg-pos) HAS_POS=1 ;;
  esac
  [ "$PREV" = "--bg-pos" ] && POS_VAL="$a"
  PREV="$a"
  if [ "${a#/}" != "$a" ]; then ARGS+=("$(to_win "$a")"); else ARGS+=("$a"); fi
done
[ -n "${PD_REMOTE_CONTROL:-}" ] && HAS_EXIT=1 # 환경변수 원격 제어(사용자 경로 시험)도 상시 실행

# ①-a 제품판 데이터 폴더 강제(lead 10:2x). 제품판 판별 = 구성 이름에 product 가 있거나 Desktop.dll 에 진단 호스트 인자 문자열이 없음.
#      제품판은 항상 임시 데이터 폴더(--data-dir logs/proddata/<시각>)로 띄운다. 이 기능이 없는 옛 제품판(문자열 '--data-dir' 없음)과
#      사용자 폴더를 가리키는 --data-dir 은 거부(rc=8). 단일 인스턴스 우회(--multi-instance on)는 기본으로 붙이지 않는다(연속 기동 시험만).
has_u16() { python3 -c "import sys;print(int(sys.argv[2].encode('utf-16-le') in open(sys.argv[1],'rb').read()))" "$1" "$2" 2>/dev/null; }
DLL="$(dirname "$EXE")/xkxk.frameworks.ui.desktop.dll"
IS_PRODUCT=0
case "$CONF" in *product*) IS_PRODUCT=1 ;; esac
[ "$IS_PRODUCT" -eq 0 ] && [ "$(has_u16 "$DLL" "--offscreen-guard")" != "1" ] && IS_PRODUCT=1
# 진단판도 --data-dir 를 지원하는 판(i1023b~)이면 똑같이 임시 데이터 폴더를 붙인다(lead 10:4x — 새 로그 기능이 사용자 폴더 logs\ 를 만들었다).
# 지원하지 않는 옛 진단판은 로그 기능도 없어 그대로 기동한다. 옛 제품판은 거부(rc=8).
HAS_DD="$(has_u16 "$DLL" "--data-dir")"
[ "$IS_PRODUCT" -eq 1 ] && [ "$HAS_DD" != "1" ] && { echo "제품판($CONF)이 데이터 폴더 재지정(--data-dir)을 지원하지 않음 — 사용자 파일 보호로 기동 거부."; echo "RESULT=PRODUCT_NO_DATADIR"; exit 8; }
if [ "$HAS_DD" = "1" ]; then
  DD=""; PREV=""
  for a in "${ARGS[@]}"; do [ "$PREV" = "--data-dir" ] && DD="$a"; PREV="$a"; done
  if [ -z "$DD" ]; then
    DDW="$ROOT/logs/proddata/$TS"; mkdir -p "$DDW"; ARGS+=(--data-dir "$(wslpath -w "$DDW")"); DD="$(wslpath -w "$DDW")"
    [ -n "$DRY" ] && rmdir "$DDW" 2>/dev/null # 점검 모드는 빈 폴더를 남기지 않는다
  fi
  case "$(printf '%s' "$DD" | tr 'A-Z' 'a-z' | tr '/' '\\')" in
    *appdata\\local\\pdpoc|*appdata\\local\\pdpoc\\) echo "--data-dir 가 사용자 폴더($DD) — 기동 거부."; echo "RESULT=PRODUCT_USER_DATADIR"; exit 8 ;;
  esac
  echo "임시 데이터 폴더: $DD"
  if [ -z "$DRY" ]; then # 기동 전 사용자 파일 백업·md5(다음 기동 때 자동 대조)
    mkdir -p "$UF_DIR/$TS"; uf_md5 > "$UF_DIR/$TS.md5"; cp -p "$USER_PD"/layout.json "$USER_PD"/settings.json "$UF_DIR/$TS/" 2>/dev/null; [ -d "$USER_PD/layouts" ] && cp -rp "$USER_PD/layouts" "$UF_DIR/$TS/" 2>/dev/null
    # --multi-instance 는 사용자 Pd 와 함께 도는 기동이라 대조 기준(pending)을 덮지 않는다 — 실행 중 저장과 시험 변경을 구분할 수 없고 앞선 기준을 잃는다(team-lead 16:2x)
    #      단 pending 이 아직 없으면 --multi-instance 기동도 기준을 기록한다(없을 때만 — 있으면 덮지 않는다). 기준이 비어 대조 자체가 안 되는 빈틈을 막는다.
    PEND_STATE="유지"
    if [ "$HAS_MULTI" -eq 0 ]; then echo "$TS" > "$UF_DIR/pending"; PEND_STATE="갱신"
    elif [ ! -f "$UF_DIR/pending" ]; then echo "$TS" > "$UF_DIR/pending"; PEND_STATE="신규(없어서 기록)"; fi
    echo "사용자 파일 백업: $UF_DIR/$TS/ ($(wc -l < "$UF_DIR/$TS.md5")개) pending=$PEND_STATE"
    # 자기 PID 외 Pd 가 떠 있는데 --data-dir 격리 없이 기동하면 사용자 폴더를 같이 쓰므로 경고한다(경고만, 동작 불변). 이 분기는 --data-dir 지원판만 들어온다.
    [ "$HAS_MULTI" -eq 1 ] && [ "$(any_count)" -gt 0 ] && echo "경고: 다른 $IMAGE 또는 $LEGACY_IMAGE 가 실행 중인 상태의 --multi-instance 기동 — 사용자 폴더 변경은 이후 대조(pending=$(cat "$UF_DIR/pending" 2>/dev/null))에서 검출된다."
  fi
fi
[ "$HAS_EXIT" -eq 1 ] || ARGS+=(--exit-after 30)
[ "$HAS_POS" -eq 1 ] || { ARGS+=(--bg-pos auto); POS_VAL="auto"; }

# ①-b 화면 안 기동 차단(lead 05:1x — 사용자 화면 노출 물리 차단). --bg-pos 가 off 이거나 x,y 기준 창(1500x1000 가정)이
#      가상 화면(모든 모니터 합집합)과 겹치면 거부(rc=7). 예외: PD_ALLOW_ONSCREEN=1 + PD_ONSCREEN_REASON(10자 이상) → logs/onscreen_runs.log 기록 후 진행.
ONSCREEN=""
case "$POS_VAL" in
  auto) ;; # 가상 화면 오른쪽 바깥(앱 ResolveOrigin) — 실제 위치는 기동 뒤 창 사각형 로그로 확인
  off|"") ONSCREEN="--bg-pos ${POS_VAL:-(값 없음)} = 화면 안 일반 기동" ;;
  *)
    if printf '%s' "$POS_VAL" | grep -Eq '^-?[0-9]+,-?[0-9]+$'; then
      PX="${POS_VAL%,*}"; PY="${POS_VAL#*,}"
      VS="$(powershell.exe -NoProfile -Command 'Add-Type -AssemblyName System.Windows.Forms; $v=[System.Windows.Forms.SystemInformation]::VirtualScreen; "$($v.X) $($v.Y) $($v.Width) $($v.Height)"' 2>/dev/null | tr -d '\r')"
      read -r VX VY VW VH <<<"$VS"
      if [ -z "${VW:-}" ]; then
        ONSCREEN="가상 화면 조회 실패 — 안전을 위해 거부"
      elif [ $((PX + 1500)) -gt "$VX" ] && [ "$PX" -lt $((VX + VW)) ] && [ $((PY + 1000)) -gt "$VY" ] && [ "$PY" -lt $((VY + VH)) ]; then
        ONSCREEN="--bg-pos $POS_VAL(창 1500x1000 가정)이 가상 화면 $VX,$VY,${VW}x$VH 와 겹침"
      fi
    else
      ONSCREEN="--bg-pos 값 형식 오류('$POS_VAL') — x,y 또는 auto"
    fi ;;
esac
if [ -n "$ONSCREEN" ]; then
  REASON="${PD_ONSCREEN_REASON:-}"
  if [ "${PD_ALLOW_ONSCREEN:-}" = "1" ] && [ "${#REASON}" -ge 10 ]; then
    echo "$(date '+%Y-%m-%d %H:%M:%S') owner=${PD_SLOT_OWNER:-} conf=$CONF pos=$POS_VAL dry=${DRY:-0} reason=$REASON detail=$ONSCREEN" >> "$ROOT/logs/onscreen_runs.log"
    echo "화면 안 기동 허용(예외): $ONSCREEN — 사유: $REASON (logs/onscreen_runs.log 기록)"
  else
    echo "화면 안 기동 거부: $ONSCREEN. 사용자 화면 노출 금지 — 화면 밖 --bg-pos(auto 또는 가상 화면 밖 x,y)를 쓰라. 예외는 PD_ALLOW_ONSCREEN=1 + PD_ONSCREEN_REASON(10자 이상, lead 승인 사용처만)."
    echo "RESULT=ONSCREEN_REFUSED"; exit 7
  fi
fi
[ -n "$DRY" ] && { echo "RESULT=DRYRUN_OK pos=$POS_VAL args=${ARGS[*]}"; exit 0; }
ARGS+=(--log "$(to_win "$APP_LOG")")

START_EPOCH="$(date +%s)"
echo "# run $(date '+%Y-%m-%d %H:%M:%S') args: ${ARGS[*]}" >"$STDIO_LOG"
cd "$(dirname "$EXE")" || exit 2
setsid nohup "$EXE" "${ARGS[@]}" </dev/null >>"$STDIO_LOG" 2>&1 &
echo "기동 요청 pid(wsl)=$! 로그=$APP_LOG stdio=$STDIO_LOG"

# ④-b 자기 PID 확정(team-lead 16:2x) — 이미지 이름으로 세면 다른 Pd 를 자기 앱으로 보고한다. 앱 로그의 'host start pid=' 만 믿는다(시스템이 밀리면 늦게 나오므로 최대 60초)
OWN_PID=""
for _ in $(seq 1 120); do
  OWN_PID="$(grep -m1 -oE 'host start pid=[0-9]+' "$APP_LOG" 2>/dev/null | grep -oE '[0-9]+$')"
  [ -n "$OWN_PID" ] && break
  sleep 0.5
done
if [ -z "$OWN_PID" ]; then echo "앱 로그에서 자기 PID 를 찾지 못함: $APP_LOG"; echo "RESULT=FAILED"; exit 5; fi
own_count() { tasklist.exe /FI "PID eq $OWN_PID" /FO CSV /NH 2>/dev/null | grep -ci "^\"$IMAGE\""; }

# ⑤ 프로세스 실존 2회 확인(자기 PID)
sleep 2
C1="$(own_count)"
sleep 3
C2="$(own_count)"
echo "프로세스 확인 pid=$OWN_PID 1회=$C1 2회=$C2"
# ⑤-b 단일 인스턴스에 걸려 기존 창에 신호를 보내고 끝났으면 성공으로 보고하지 않는다
if grep -q "single-instance already running" "$APP_LOG" 2>/dev/null; then
  echo "단일 인스턴스 신호 발생 — 기존 Pd 에 활성화 신호가 갔다: $(grep -m1 'single-instance already running' "$APP_LOG")"
  echo "RESULT=SIGNALLED_EXISTING"; exit 10
fi
mkdir -p "$PID_DIR" && echo "$OWN_PID $(basename "$APP_LOG")" >> "$PID_FILE" # --kill 대상 기록(자기 PID 만)
# ③ 실제 창 사각형 기록(lead 05:1x — 기본 auto 가 정말 화면 밖인지 확인). 앱 로그에도 남긴다.
FB="$ROOT/test/common/measure/frame_bounds.ps1"  # D-TESTDIR: 옛 logs/odev4b/frame_bounds.ps1
if [ "$C2" -gt 0 ] && [ -f "$FB" ]; then
  RECTS="$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w "$FB")" -ProcId "$OWN_PID" 2>/dev/null | tr -d '\r' | grep 'hwnd=')"
  printf '%s\n' "$RECTS" | sed 's/^/창 사각형: /'
  # 앱이 로그 파일을 잠그므로(Permission denied) 같은 폴더의 별도 파일에 남긴다(lead 09:5x)
  printf '%s\n' "$RECTS" | sed "s|^|$(date '+%Y-%m-%d %H:%M:%S.000') [run] $(basename "$APP_LOG") window-rect |" >> "$(dirname "$APP_LOG")/window_rect.log" 2>/dev/null
fi

# ⑥ 이번 기동 이후 앱 로그 존재 확인
if [ -f "$APP_LOG" ] && [ "$(stat -c %Y "$APP_LOG")" -ge "$START_EPOCH" ]; then
  echo "앱 로그 있음: $(wc -l <"$APP_LOG")줄 — 마지막: $(tail -1 "$APP_LOG")"
  LOG_OK=1
else
  echo "앱 로그 없음: $APP_LOG"
  LOG_OK=0
fi

if [ "$C1" -gt 0 ] && [ "$C2" -gt 0 ] && [ "$LOG_OK" -eq 1 ]; then
  echo "PID=$OWN_PID"; echo "RESULT=RUNNING"; exit 0
fi
if [ "$C1" -gt 0 ] && [ "$C2" -eq 0 ] && [ "$LOG_OK" -eq 1 ] && grep -q "host exit code=0" "$APP_LOG"; then
  echo "RESULT=EXITED_NORMALLY"; exit 0
fi
echo "RESULT=FAILED"; exit 5

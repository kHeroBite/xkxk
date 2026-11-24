#!/bin/bash
# RETEST 1차 시나리오(logs/otest/desktop/run_retest2.sh)를 현재 가드 인자로 옮긴 실행기 — 원본은 고치지 않고 --multi-instance·임시 프로필·화면 밖·가드 on 으로 순차 실행한다
# 사용: retest2_port.sh <conf> <출력 폴더(절대경로)> [시나리오 접두어 필터]  (성능 시나리오 p01~p03 은 사용자 지시로 제외)
set -u
CONF="$1"
D="$2"
FILTER="${3:-}"
U=/mnt/c/DATA/Project/xkxk/frameworks/ui
R=$U/tools/run_desktop_bg.sh
mkdir -p "$D" "$D/prof"
: > "$D/run.log"
run() {
  local n="$1"; shift
  if [ -n "$FILTER" ] && [[ "$n" != $FILTER* ]]; then return 0; fi
  echo "=== $n: $*" >> "$D/run.log"
  PD_SLOT_OWNER=otest-2 timeout 85 bash "$R" --conf "$CONF" --no-kill -- "$@" --multi-instance on --profile-dir "$D/prof" --offscreen-guard on \
    --screenshot "$D/$n.png" --perf-out "$D/$n.perf.json" --exit-after 2 >> "$D/run.log" 2>&1
  for i in $(seq 1 25); do tasklist.exe /FI "IMAGENAME eq Pd.Poc.Desktop.exe" /NH 2>/dev/null | grep -qi Pd.Poc || break; sleep 1; done
  sleep 1
  echo "--- end $n" >> "$D/run.log"
}
L() { echo --layout-file "$D/$1.json"; }
run r01_redock --open grid:3 --sdi 2 --cmd redock-all --save-layout $(L r01)
run r01b_redock_load --load-layout $(L r01)
run r02_min_save --open grid:3,form:1 --cmd minimize-all --save-layout $(L r02)
run r02_min_load --load-layout $(L r02)
run r03_next_save --open grid:3 --layout tile --cmd next --save-layout $(L r03)
run r03_next_load --load-layout $(L r03)
run r04_max --open grid:3 --cmd maximize --save-layout $(L r04)
run r04_max_load --load-layout $(L r04)
run r04b_restore_max --open grid:3 --cmd maximize,restore-max --save-layout $(L r04b)
run r05_pin --open grid:2 --sdi 1 --cmd pin --save-layout $(L r05)
run r05b_unpin --open grid:2 --sdi 1 --cmd pin,unpin --save-layout $(L r05b)
run r05c_pin_load --load-layout $(L r05)
run r06_tray_on --open grid:1 --cmd tray-on
run r06b_tray_off --open grid:1 --cmd tray-on,tray-off
run r07_esc --open grid:3 --cmd esc-close
run r08_dbl --open grid:3 --cmd dblclick-close
run r09_tabbed_save --open grid:2,form:1 --layout tabbed --save-layout $(L r09)
run r09_tabbed_load --load-layout $(L r09)
run g01_open --open grid:1,gantt:1,form:1
run g02_dedupe --open grid:2,gantt:2 --dedupe
run g03_close_others --open grid:5 --cmd close-others
run g04_close_all --open grid:3 --cmd close-all
run g05_min_restore --open grid:3,form:2 --cmd minimize-all,restore-all
run g06_next --open grid:3 --cmd next,next
run g06b_prev --open grid:3 --cmd prev
run g07_cascade --open grid:3,gantt:2,form:2 --layout cascade
run g07_tile --open grid:3,gantt:2,form:2 --layout tile
run g07_tile_h --open grid:3,gantt:2,form:2 --layout tile-h
run g07_tabbed --open grid:3,gantt:2,form:2 --layout tabbed
run g08_sdi --open grid:3 --sdi 2
echo ALLDONE >> "$D/run.log"

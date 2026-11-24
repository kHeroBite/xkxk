#!/bin/bash
# WASM 정적 번들을 WSL python3(serve_browser.py)로 127.0.0.1 에 서빙하는 스크립트
# 사용: serve_browser.sh [start|stop|status] [포트=5180] [번들경로]
# 시나리오 URL 예: http://127.0.0.1:5180/?open=grid:5,gantt:5,form:10&layout=tile&save-layout&exit-after=10
set -u
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$TOOLS/.." && pwd)"
CMD="${1:-start}"
PORT="${2:-5180}"
BUNDLE="${3:-$ROOT/src/Pd.Poc.Browser/bin/Release/net11.0-browser/publish/wwwroot}"
PIDF="$ROOT/logs/serve_browser_$PORT.pid"
LOG="$ROOT/logs/serve_browser_$PORT.log"
mkdir -p "$ROOT/logs"

is_alive() { [ -f "$PIDF" ] && kill -0 "$(cat "$PIDF")" 2>/dev/null; }

case "$CMD" in
  start)
    if is_alive; then echo "이미 실행 중 pid=$(cat "$PIDF") url=http://127.0.0.1:$PORT/"; exit 0; fi
    if [ ! -f "$BUNDLE/index.html" ]; then echo "번들 없음: $BUNDLE (build_browser.sh 먼저 실행)"; exit 2; fi
    # serve_browser.py: .br 사전 압축 전송(Content-Encoding: br, Vary), 지문 붙은 _framework 불변 캐시, index.html 등은 재검증
    setsid nohup python3 -I "$TOOLS/serve_browser.py" "$PORT" "$BUNDLE" \
      </dev/null >"$LOG" 2>&1 &
    echo $! >"$PIDF"
    for _ in 1 2 3 4 5 6 7 8 9 10; do
      if curl -s -o /dev/null --max-time 1 "http://127.0.0.1:$PORT/index.html"; then
        echo "서빙 시작 pid=$(cat "$PIDF") url=http://127.0.0.1:$PORT/ bundle=$BUNDLE"; exit 0
      fi
      sleep 0.5
    done
    echo "서빙 응답 없음 — 로그: $LOG"; exit 3 ;;
  stop)
    if is_alive; then kill "$(cat "$PIDF")"; echo "중지 pid=$(cat "$PIDF")"; else echo "실행 중 아님"; fi
    rm -f "$PIDF" ;;
  status)
    if is_alive; then echo "실행 중 pid=$(cat "$PIDF") url=http://127.0.0.1:$PORT/"; else echo "중지됨"; exit 1; fi ;;
  *) echo "사용: $0 [start|stop|status] [포트] [번들경로]"; exit 64 ;;
esac

#!/usr/bin/env python3
# WSL 에서 실행 중인 PoC 앱(원격 제어 서버)에 명령을 보내는 얇은 클라이언트 — 연결 정보 파일의 포트·토큰을 읽어 Windows curl.exe 로 호출
"""
사용:
  pdctl.py <명령> [키=값 ...]            예) pdctl.py docs.open kind=grid count=3
  pdctl.py screenshot.download out=a.png [window=0|doc=1]   PNG 바이트를 WSL 파일로 저장
  pdctl.py help                          명령 목록
  pdctl.py --raw '{"cmd":"status"}'      요청 JSON 을 그대로 전송
값은 문자열로 보내고 서버가 숫자·불리언으로 해석한다. 종료 코드: 0=ok, 1=명령 실패(연결 정보 파일 없음 포함 — 원격 서버 없는 제품판 포함), 2=연결 실패(연결 시간 초과·curl 실패).

연결 정보: %LOCALAPPDATA%\\xkxk\\frameworks\\ui\\remote-control.json(앱이 기동 시 기록). 환경변수 PD_REMOTE_INFO 로 경로 지정 가능.
WSL2 NAT 모드에서는 WSL 의 curl 이 Windows localhost 에 닿지 않는다(Mars INFRA.md 실측과 동일) → Windows curl.exe 를 쓴다.
"""
import glob
import json
import os
import subprocess
import sys

CURL_EXE = "/mnt/c/Windows/System32/curl.exe"


ISOLATED_GLOB = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "logs", "proddata", "*", "remote-control.json")


def info_path():
    """연결 정보 파일 우선순위: ① PD_REMOTE_INFO ② 격리 폴더(--data-dir 로 기동한 시험의
    <영역 루트>/logs/proddata/*/remote-control.json 중 최신) ③ 사용자 폴더(%LOCALAPPDATA%\\xkxk\\frameworks\\ui, 최신).
    격리(--data-dir)로 앱을 띄우는 시험 도구는 반드시 PD_REMOTE_INFO 를 export 해야 한다.
    안 하면 ②에 파일이 아직 없을 때(기동 직후) ③의 사용자 인스턴스에 잘못 붙을 수 있다."""
    if os.environ.get("PD_REMOTE_INFO"):
        return os.environ["PD_REMOTE_INFO"]
    isolated = glob.glob(ISOLATED_GLOB)
    if isolated:
        return max(isolated, key=os.path.getmtime)
    found = glob.glob("/mnt/c/Users/*/AppData/Local/xkxk/frameworks/ui/remote-control.json")
    if not found:
        sys.exit("연결 정보 파일이 없습니다. 앱을 --remote-control 로 기동했는지 확인하세요.")
    return max(found, key=os.path.getmtime)


def load_info():
    with open(info_path(), encoding="utf-8") as f:
        return json.load(f)


def curl(info, path, body, out_binary=False, timeout=60):
    curl = CURL_EXE if os.path.exists(CURL_EXE) else "curl"
    cmd = [curl, "-s", "--max-time", str(timeout), "-X", "POST",
           "-H", "Content-Type: application/json", "-H", "X-Pd-Token: " + info["token"],
           "--data-binary", "@-", "http://localhost:%d/%s" % (info["port"], path)]
    try:
        proc = subprocess.run(cmd, input=body.encode("utf-8"), capture_output=True, timeout=timeout + 5)
    except subprocess.TimeoutExpired:
        print("연결 시간 초과", file=sys.stderr)
        sys.exit(2)
    if proc.returncode != 0:
        print("curl 실패 rc=%d %s" % (proc.returncode, proc.stderr.decode("utf-8", "replace")), file=sys.stderr)
        sys.exit(2)
    return proc.stdout if out_binary else proc.stdout.decode("utf-8", "replace")


def parse_args(pairs):
    args = {}
    for p in pairs:
        if "=" not in p:
            sys.exit("인자는 키=값 형식입니다: " + p)
        k, v = p.split("=", 1)
        args[k] = v
    return args


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 0
    info = load_info()
    if argv[0] == "--raw":
        text = curl(info, "api/cmd", argv[1])
    elif argv[0] == "screenshot.download":
        args = parse_args(argv[1:])
        out = args.pop("out", "pd_screenshot.png")
        data = curl(info, "api/screenshot/download", json.dumps(args), out_binary=True)
        if not data.startswith(b"\x89PNG"):
            print(data.decode("utf-8", "replace"))
            return 1
        with open(out, "wb") as f:
            f.write(data)
        print(json.dumps({"ok": True, "out": os.path.abspath(out), "bytes": len(data)}, ensure_ascii=False))
        return 0
    else:
        timeout = 60
        args = parse_args(argv[1:])
        if argv[0] == "wait.for" and "timeout" in args:
            timeout = int(int(args["timeout"]) / 1000) + 10
        text = curl(info, "api/cmd", json.dumps({"cmd": argv[0], "args": args}, ensure_ascii=False), timeout=timeout)
    print(text)
    try:
        return 0 if json.loads(text).get("ok") else 1
    except ValueError:
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

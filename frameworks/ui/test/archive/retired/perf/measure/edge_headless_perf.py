# Windows Edge 를 --headless=new 로 띄워 ANGLE D3D11(GPU)로 웹 perf-ui 를 돌리고 콘솔의 PERF_JSON·렌더러 문자열을 회수하는 스크립트
# 사용: python3 test/perf/measure/edge_headless_perf.py probe              (렌더러만 확인: 창 없음, 수 초)
#       python3 test/perf/measure/edge_headless_perf.py perf [URL] [초=600] (perf-ui short 실행, 기본 URL http://127.0.0.1:5184/)
# 사유: WSL 의 Chromium 은 GPU 가 없어 SwiftShader(소프트웨어)로만 렌더된다. GPU 렌더는 Windows 실행파일(msedge.exe)만 가능하다.
#       사용자 화면에는 창이 뜨지 않는다(--headless=new). 실행은 odev-4b 성능 구간 종료 통지 뒤에만 한다.
# 콘솔 회수: --enable-logging=stderr --v=1 로 console.log 가 stderr 에 'CONSOLE' 줄로 나오는 것을 파일로 받아 파싱한다(CDP 는 WSL NAT 에서 닿지 않음).
import json
import os
import re
import subprocess
import sys
import time

EDGE = "/mnt/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "tools")  # D-TESTDIR: 옛 위치(poc/ui/tools) 기준 그대로
OUT = os.path.join(HERE, "..", "logs", "otest_w2")
PROBE_HTML = """<!doctype html><meta charset=utf-8><script>
try {
  const c = document.createElement('canvas');
  const g = c.getContext('webgl2', {failIfMajorPerformanceCaveat: true});
  if (!g) { console.log('RENDERER: webgl2 실패(canvas2d 대체)'); }
  else { const e = g.getExtension('WEBGL_debug_renderer_info'); console.log('RENDERER: ' + (e ? g.getParameter(e.UNMASKED_RENDERER_WEBGL) : 'webgl2 OK(정보 없음)')); }
} catch (x) { console.log('RENDERER: 오류 ' + x); }
</script>"""


def win_path(p):
    return subprocess.check_output(["wslpath", "-w", p]).decode().strip()


def my_edge_pids(prof_win):
    """명령줄에 자기 --user-data-dir(prof_win)를 가진 msedge.exe 의 Windows PID 목록(D-EDGEKILL — w14_edge_perfui.sh mine() 과 같은 방식)."""
    needle = prof_win.replace("'", "''")
    ps = ("Get-CimInstance Win32_Process -Filter \"Name='msedge.exe'\" | Where-Object { $_.CommandLine -like '*%s*' }"
          " | ForEach-Object { $_.ProcessId }" % needle)
    try:
        out = subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", ps], stderr=subprocess.DEVNULL, timeout=60)
    except (subprocess.SubprocessError, OSError):
        return []
    return [p for p in out.decode("utf-8", "replace").split() if p.isdigit()]


def kill_my_edge(prof_win):
    """자기 프로필 msedge 만 끈다(다른 Edge·사용자 브라우저는 건드리지 않는다). Popen 의 pid 는 WSL PID 라 taskkill 대상이 아니다."""
    pids = my_edge_pids(prof_win)
    for pid in pids:
        subprocess.call(["taskkill.exe", "/PID", pid, "/T", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return pids


def run_edge(url, secs, tag):
    log = os.path.join(OUT, "edge_%s.log" % tag)
    prof = "/mnt/c/DATA/Project/pd/poc/ui/logs/otest_w2/edge_profile_%s" % tag
    os.makedirs(prof, exist_ok=True)
    prof_win = win_path(prof)
    cmd = [EDGE, "--headless=new", "--user-data-dir=" + prof_win, "--use-angle=d3d11", "--ignore-gpu-blocklist",
           "--enable-logging=stderr", "--v=1", "--no-first-run", "--disable-extensions", url]
    with open(log, "wb") as f:
        pr = subprocess.Popen(cmd, stdout=f, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, start_new_session=True)
        t0 = time.time()
        while time.time() - t0 < secs:
            time.sleep(3)
            txt = open(log, "rb").read().decode("utf-8", "replace")
            if "PERF_JSON:" in txt or ("RENDERER:" in txt and tag == "probe"):
                break
        pr.terminate()
        time.sleep(1)
        kill_my_edge(prof_win)
    return open(log, "rb").read().decode("utf-8", "replace"), time.time() - t0


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "probe"
    os.makedirs(OUT, exist_ok=True)
    if mode == "probe":
        html = os.path.join(OUT, "edge_probe.html")
        open(html, "w", encoding="utf-8").write(PROBE_HTML)
        txt, sec = run_edge("file:///" + win_path(html).replace("\\", "/"), 30, "probe")
        m = re.search(r"RENDERER: ([^\r\n\"]+)", txt)
        print("renderer:", m.group(1) if m else "없음(로그 확인: edge_probe.log)", "| %ds" % sec)
        return
    url = (sys.argv[2] if len(sys.argv) > 2 else "http://127.0.0.1:5184/") + "?cmd=perf-ui&profile=short&perf-out=perf_short.json"
    secs = int(sys.argv[3]) if len(sys.argv) > 3 else 600
    txt, sec = run_edge(url, secs, "perf")
    m = re.search(r"PERF_JSON:(\{.*\})", txt)
    if not m:
        print("PERF_JSON 없음 (%ds) — edge_perf.log 확인" % sec)
        return
    d = json.loads(m.group(1))
    json.dump(d, open(os.path.join(OUT, "perf_ui_web_edge_gpu.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("완주 %ds, 조작 %d종" % (sec, len(d.get("operations") or [])))


if __name__ == "__main__":
    main()

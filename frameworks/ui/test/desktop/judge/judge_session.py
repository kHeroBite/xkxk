# 판정기 3종(자기시험·불러오는 중·효과 단계)의 공용 연결부 — 이미 뜬 데스크톱 인스턴스 또는 headless 웹 판에 붙는다
"""
selftest_judge.py · window_load_judge.py · effect_level_judge.py 가 import 한다(직접 실행 없음).

데스크톱  --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json>
          앱을 띄우지 않는다. perf_effect_common.Session 의 첫 조작 전 확인(PID 기록·인자·layout-file 격리·비움)을 그대로 쓴다.
웹        --bundle <wwwroot> --port <포트> --out <결과.json>
          headless Chrome 에 새 프로필로 띄운다(5180·5220·5242 거부). 결과에 scope=web 을 남기며 데스크톱 판정으로 쓰지 않는다.

종료 코드는 perf_effect_common.RC 와 같다. PASS 0 · FAIL 1 · INVALID 2 · DEFERRED 3 · RECORDED 4 · STOPPED 5
"""
import base64
import io
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
import perf_effect_common as common  # noqa: E402

Abort = common.Abort
run_cases = common.run_cases
WEB_REFUSED_PORTS = common.USER_PORTS + (5242,)
SETTINGS_KEY = "pd-poc-settings"  # xkxk.frameworks.ui.browser/Program.cs SettingsKey
PIXEL_DELTA = 12  # 밝기 차가 이보다 크면 다른 화소(옅은 꼬리 점·낮은 대비까지 센다)
WORST = ("STOPPED", "FAIL", "INVALID", "DEFERRED", "RECORDED", "PASS")

# 대상 PID 의 창 핸들 찾기 + PrintWindow 준비. (Get-Process).MainWindowHandle 은 이 앱에서 0 이라(r14 실측) PID 로 EnumWindows 해서
# 보이는 최상위 창 중 클라이언트 영역이 가장 큰 것을 고른다. 소유자가 있어도 그 소유자가 같은 PID 의 보이지 않는 창이면 받는다
# (--offscreen-guard 는 ShowInTaskbar=false 라 메인 창에 숨은 소유자 창이 붙는다 — r14 재측정 실측). 못 찾으면 'NOHWND' 를 내고 끝낸다
PW_TYPES_PS = r'''
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class PwBurst {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  // 후보 규칙(자체 시험이 직접 부른다): 같은 PID·보이는 창이고, 소유자가 없거나 소유자가 같은 PID 의 보이지 않는 창
  public static bool Accept(uint pid, uint winPid, bool visible, bool hasOwner, uint ownerPid, bool ownerVisible) {
    if (winPid != pid || !visible) return false;
    return !hasOwner || (ownerPid == pid && !ownerVisible);
  }
  public static IntPtr FindMain(uint pid) {
    IntPtr best = IntPtr.Zero; long area = 0;
    EnumWindows(delegate(IntPtr h, IntPtr p) {
      uint winPid; GetWindowThreadProcessId(h, out winPid);
      IntPtr o = GetWindow(h, 4);  // 4 = GW_OWNER
      uint ownerPid = 0; if (o != IntPtr.Zero) GetWindowThreadProcessId(o, out ownerPid);
      if (!Accept(pid, winPid, IsWindowVisible(h), o != IntPtr.Zero, ownerPid, o != IntPtr.Zero && IsWindowVisible(o))) return true;
      RECT r; GetClientRect(h, out r);
      long a = (long)(r.R - r.L) * (r.B - r.T);
      if (a > area) { area = a; best = h; }
      return true;
    }, IntPtr.Zero);
    return best;
  }
}
"@
'''
PW_PREP_PS = PW_TYPES_PS + r'''
$h = [PwBurst]::FindMain(%PID%)
if ($h -eq [IntPtr]::Zero) { 'NOHWND'; exit }
$r = New-Object PwBurst+RECT
[void][PwBurst]::GetClientRect($h, [ref]$r)
'''

# 창 1개를 간격마다 PrintWindow 로 찍고 '파일|시작|끝'(유닉스 ms)을 한 줄씩 낸다. 입력 API 는 쓰지 않는다
BURST_PS = PW_PREP_PS + r'''
'READY'
for ($i = 0; $i -lt %COUNT%; $i++) {
  $t0 = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
  $b = New-Object System.Drawing.Bitmap $r.R, $r.B
  $g = [System.Drawing.Graphics]::FromImage($b); $dc = $g.GetHdc()
  $ok = [PwBurst]::PrintWindow($h, $dc, 3)
  $g.ReleaseHdc($dc); $g.Dispose()
  $t1 = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
  $f = '%DIR%\burst_' + $i + '.png'
  if ($ok) { $b.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); "$f|$t0|$t1" }
  $b.Dispose()
  Start-Sleep -Milliseconds %GAP%
}
'''

# 표준 입력으로 받은 저장 경로마다 창 1장을 PrintWindow 3(PW_CLIENTONLY|PW_RENDERFULLCONTENT = OS 합성 결과)으로 찍고
# 'OK|파일' 또는 'FAIL|파일' 을 낸다. 한 프로세스를 계속 쓴다(장마다 powershell 기동 지연이 불러오는 중 구간을 넘기지 않게)
SHOT_PS = PW_PREP_PS + r'''
'READY'
while ($null -ne ($f = [Console]::In.ReadLine())) {
  [void][PwBurst]::GetClientRect($h, [ref]$r)
  $b = New-Object System.Drawing.Bitmap $r.R, $r.B
  $g = [System.Drawing.Graphics]::FromImage($b); $dc = $g.GetHdc()
  $ok = [PwBurst]::PrintWindow($h, $dc, 3)
  $g.ReleaseHdc($dc); $g.Dispose()
  if ($ok) { $b.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); "OK|$f" } else { "FAIL|$f" }
  $b.Dispose()
}
'''
ICON_CAPTURE = "PrintWindow flag 3(PW_CLIENTONLY|PW_RENDERFULLCONTENT) — OS 합성 캡처"

# --burst-capture screen: PrintWindow 는 대상 UI 스레드가 그려야 끝나서 UI 막힘 동안 찍히지 않는다(r14 재측정 실측: 1351→2700ms 대기).
# 그래서 창을 지정 화면 좌표에 TOPMOST·NOACTIVATE 로만 올리고(실입력·전경·커서 없음) 아이콘 칸의 화면 픽셀을 CopyFromScreen 으로 찍는다.
# 이동은 막힘 전, 되돌리기는 끝난 뒤(finally)다. 이동이 실제로 안 됐으면(예: --offscreen-guard 가 막음) 'NOMOVE' 를 내고 찍지 않는다
# 'MOVED|시각|원래x,y' · 'AREA|화면x|화면y|w|h|배율비' · 'READY' · '파일|시작|끝'… · 'RESTORED|시각'
SCREEN_BURST_PS = PW_PREP_PS + r'''
$w0 = $r.R
$wr = New-Object PwBurst+RECT; [void][PwBurst]::GetWindowRect($h, [ref]$wr)
[void][PwBurst]::SetWindowPos($h, [IntPtr](-1), %X%, %Y%, 0, 0, 0x0051)  # HWND_TOPMOST, NOSIZE|NOACTIVATE|SHOWWINDOW
$moved = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
"MOVED|$moved|$($wr.L),$($wr.T)"
try {
  $now = New-Object PwBurst+RECT; [void][PwBurst]::GetWindowRect($h, [ref]$now)
  if ($now.L -ne %X% -or $now.T -ne %Y%) { "NOMOVE|$($now.L),$($now.T)"; return }
  Start-Sleep -Milliseconds %SETTLE%
  [void][PwBurst]::GetClientRect($h, [ref]$r)
  $k = if ($w0 -gt 0) { $r.R / $w0 } else { 1 }
  $p = New-Object PwBurst+POINT; [void][PwBurst]::ClientToScreen($h, [ref]$p)
  $sx = $p.X + %BX%; $sy = $p.Y + %BY%
  "AREA|$sx|$sy|%BW%|%BH%|$k"
  'READY'
  for ($i = 0; $i -lt %COUNT%; $i++) {
    $t0 = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $b = New-Object System.Drawing.Bitmap %BW%, %BH%
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($sx, $sy, 0, 0, $b.Size); $g.Dispose()
    $t1 = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $f = '%DIR%\sburst_' + $i + '.png'
    $b.Save($f, [System.Drawing.Imaging.ImageFormat]::Png); "$f|$t0|$t1"
    $b.Dispose()
    Start-Sleep -Milliseconds %GAP%
  }
} finally {
  [void][PwBurst]::SetWindowPos($h, [IntPtr](-2), $wr.L, $wr.T, 0, 0, 0x0011)  # HWND_NOTOPMOST, NOSIZE|NOACTIVATE
  "RESTORED|" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}
'''
BURST_CAPTURES = ("printwindow", "screen")


def parse_pos(text):
    """'x,y'(가상 화면 좌표, 음수 허용) → (x, y). 형식이 틀리면 ValueError."""
    parts = [p.strip() for p in (text or "").split(",")]
    if len(parts) != 2:
        raise ValueError("x,y 형식이 아니다: %r" % text)
    return int(parts[0]), int(parts[1])


def parse_screen_burst(lines):
    """SCREEN_BURST_PS 출력 → (meta, 캡처 줄들). 캡처 줄은 '파일|시작|끝'."""
    meta, shots = {}, []
    for line in lines:
        part = line.strip().split("|")
        if part[0] == "MOVED" and len(part) == 3:
            meta.update(moved_ms=float(part[1]), original_pos=part[2])
        elif part[0] == "NOMOVE":
            meta["nomove_at"] = part[1] if len(part) > 1 else ""
        elif part[0] == "AREA" and len(part) == 6:
            meta.update(screen_area=[int(part[1]), int(part[2]), int(part[3]), int(part[4])], dpi_ratio=float(part[5]))
        elif part[0] == "RESTORED" and len(part) == 2:
            meta["restored_ms"] = float(part[1])
        elif len(part) == 3 and part[0] not in ("MOVED",):
            shots.append(part)
    if "moved_ms" in meta and "restored_ms" in meta:
        meta["exposure_ms"] = meta["restored_ms"] - meta["moved_ms"]
    return meta, shots


def place_on_canvas(icon, area):
    """아이콘 칸 화면 캡처를 클라이언트 좌표 캔버스의 area 자리에 놓는다(판정기가 같은 crop(area) 로 잘라 쓰게). 크기는 바꾸지 않는다."""
    from PIL import Image

    canvas = Image.new("RGB", (area[2], area[3]))
    canvas.paste(icon, (area[0], area[1]))
    return canvas


def burst_self_test():
    """화면 burst 모드의 파싱·캔버스 규칙. powershell 은 쓰지 않는다(창 후보 시험이 같은 C# 형식 컴파일을 본다)."""
    from PIL import Image

    lines = ["MOVED|1000|21500,100", "AREA|-2390|230|16|16|1", "READY", "C:\\a\\sburst_0.png|1100|1102", "RESTORED|3500"]
    meta, shots = parse_screen_burst(lines)
    icon = Image.new("RGB", (16, 16), (200, 10, 10))
    crop = place_on_canvas(icon, (30, 40, 46, 56)).crop((30, 40, 46, 56))
    bad = []
    for text in ("1,2,3", "a,b", ""):
        try:
            parse_pos(text)
            bad.append(text)
        except ValueError:
            pass
    return run_cases([
        ("좌표 음수 허용", parse_pos("-2400, 200") == (-2400, 200)),
        ("좌표 형식 오류 거부", not bad),
        ("노출 시간 = 되돌림 - 올림", meta.get("exposure_ms") == 2500.0),
        ("원래 위치 기록", meta.get("original_pos") == "21500,100"),
        ("화면 칸·배율비", meta.get("screen_area") == [-2390, 230, 16, 16] and meta.get("dpi_ratio") == 1.0),
        ("캡처 줄만 장면", len(shots) == 1 and shots[0][1] == "1100"),
        ("이동 막힘 표시", "nomove_at" in parse_screen_burst(["MOVED|1|0,0", "NOMOVE|0,0", "RESTORED|2"])[0]),
        ("캔버스 같은 자리 같은 화소", list(crop.getdata()) == list(icon.getdata())),
    ])

# 창 후보 규칙 사례: (이름, pid, 창 pid, 보임, 소유자 있음, 소유자 pid, 소유자 보임, 기대)
PICK_CASES = [
    ("소유자 없는 보이는 메인 창", 7, 7, True, False, 0, False, True),
    ("숨은 소유자가 있는 메인 창(offscreen-guard)", 7, 7, True, True, 7, False, True),
    ("보이는 소유자가 있는 창(대화상자·팝업)", 7, 7, True, True, 7, True, False),
    ("다른 PID 소유자", 7, 7, True, True, 9, False, False),
    ("보이지 않는 창", 7, 7, False, False, 0, False, False),
    ("다른 PID 창", 7, 9, True, False, 0, False, False),
]


def pick_self_test():
    """FindMain 의 후보 규칙(C# Accept)을 PowerShell 로 직접 불러 사례별로 본다. powershell.exe 가 없으면 건너뛴다."""
    # 사례는 번호로 주고받는다(powershell 출력은 콘솔 코드 페이지라 한글 이름이 깨진다)
    lines = ["'%d|' + [PwBurst]::Accept(%d, %d, $%s, $%s, %d, $%s)" % (i, p, w, str(v).lower(), str(ho).lower(), op, str(ov).lower())
             for i, (_, p, w, v, ho, op, ov, _) in enumerate(PICK_CASES)]
    try:
        out = subprocess.run(["powershell.exe", "-NoProfile", "-Command", PW_TYPES_PS + "\n".join(lines)],
                             capture_output=True, timeout=60).stdout.decode("ascii", "replace")
    except (OSError, subprocess.SubprocessError) as e:
        print("SKIP 창 후보 규칙(powershell 없음: %r)" % e)
        return 0
    got = dict(line.strip().rsplit("|", 1) for line in out.splitlines() if "|" in line)
    return run_cases([("창 후보: " + c[0], got.get(str(i)) == str(c[-1])) for i, c in enumerate(PICK_CASES)])


def delta(a, b):
    """잘라 낸 두 아이콘에서 밝기 차가 큰 화소 수."""
    from PIL import ImageChops

    if a.size != b.size:
        return a.size[0] * a.size[1]
    return sum(ImageChops.difference(a, b).convert("L").histogram()[PIXEL_DELTA + 1:])


def shape(crop):
    """가장 많은 색(바탕)과 다른 화소 수. 0 이면 빈 칸이다(평상시 아이콘과 다르다는 것만으로는 스피너가 아니다)."""
    from PIL import Image

    ground = max(crop.getcolors(crop.width * crop.height))[1]
    return delta(crop, Image.new("RGB", crop.size, ground))


def box(bounds, scale):
    x, y = round(bounds["x"] * scale), round(bounds["y"] * scale)
    return (x, y, x + round(bounds["w"] * scale), y + round(bounds["h"] * scale))


def parser(doc):
    ap = common.parser(doc)
    ap.add_argument("--bundle", help="웹 판 wwwroot(주면 웹으로 판정)")
    ap.add_argument("--port", type=int, default=5744, help="웹 정적 서버 포트")
    ap.add_argument("--dpr", type=float, default=1.0, help="웹 기기 배율. 1 이 아니면 화면 표본을 CSS 크기로 줄여 판정한다")
    ap.add_argument("--scale-mode", choices=SCALE_MODES, default="force",
                    help="--dpr ≠ 1 일 때 배율 방식: force=Chrome 실제 배율(--force-device-scale-factor·no_viewport, 기본), "
                         "emulation=Playwright device_scale_factor 흉내(앱 창이 622.2 DIP 로 줄고 그리기·누름이 어긋난다 — 참고용)")
    ap.add_argument("--burst-capture", choices=BURST_CAPTURES, default="printwindow",
                    help="데스크톱 UI 막힘 구간 캡처 수단. printwindow(기본, 화면 밖 그대로) | screen(창을 --burst-screen-pos 에 "
                         "TOPMOST·NOACTIVATE 로 잠시 올려 아이콘 칸 화면 픽셀을 찍고 되돌린다. --offscreen-guard 인스턴스는 이동이 막혀 DEFERRED)")
    ap.add_argument("--burst-screen-pos", help="screen 모드에서 창 왼쪽 위를 둘 가상 화면 좌표 x,y(터미널이 없는 모니터 안). "
                                               "음수는 등호로 붙인다: --burst-screen-pos=-2400,200")
    ap.add_argument("--burst-screen-label", default="", help="screen 모드 결과 meta 에 남길 모니터 이름(예: DISPLAY1)")
    ap.add_argument("--burst-settle-ms", type=int, default=300, help="screen 모드에서 올린 뒤 찍기 전 대기")
    return ap


SCALE_MODES = ("force", "emulation")


def scale_mode_of(dpr, requested):
    """실효 배율 방식: 배율 1 이면 none, 아니면 요청(force·emulation). r11 scale_probe 로 force 만 실제 배율과 같음을 확인했다."""
    return "none" if abs(float(dpr) - 1) < 0.001 else requested


def overall(cases):
    """사례 판정 중 가장 나쁜 값. 사례가 없으면 INVALID."""
    found = {c["verdict"] for c in cases.values()}
    return next((v for v in WORST if v in found), "INVALID")


def shots_dir(args, name):
    path = os.path.join(os.path.dirname(os.path.abspath(args.out)), name + "_shots")
    os.makedirs(path, exist_ok=True)
    return path


class Desktop(common.Session):
    scope = "desktop"

    def __init__(self, args, name):
        super().__init__(args)
        self.shots = shots_dir(args, name)
        self.pid = int(self.call("status")["pid"])

    def call(self, cmd, **kw):
        if hasattr(self, "pid"):
            self.away(True)
        return super().call(cmd, **kw)

    def capture(self, name):
        from PIL import Image

        path = self.call("screenshot")["path"]
        local = subprocess.check_output(["wslpath", "-u", path], text=True).strip()
        with Image.open(local) as im:
            shot = im.convert("RGB")
        shot.save(os.path.join(self.shots, name + ".png"))
        return shot, shot.width / float(self.call("windows.list")[0]["w"])

    def capture_icon(self, name):
        """아이콘 칸 판정용 OS 합성 캡처. 앱 screenshot(RenderTargetBitmap)은 렌더 스레드 컴포지션 비주얼(창 아이콘 스피너)을
        담지 않는다(r14 실측: 같은 순간 RTB 0 · PrintWindow 2 는 95 화소). 좌표는 capture 와 같은 클라이언트 영역 기준이다."""
        from PIL import Image

        proc = getattr(self, "_shot_proc", None)
        if proc is None:
            script = SHOT_PS.replace("%PID%", str(self.pid))
            proc = self._shot_proc = subprocess.Popen(["powershell.exe", "-NoProfile", "-Command", script], stdin=subprocess.PIPE,
                                                      stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
            ready = proc.stdout.readline().strip()
            if ready != "READY":
                raise Abort("INVALID", "OS 캡처 준비 실패(%s) — 창 핸들을 찾지 못했다" % (ready or "응답 없음"))
            self.meta["icon_capture"] = ICON_CAPTURE
        self.away(True)
        path = os.path.join(self.shots, name + ".png")
        win = subprocess.check_output(["wslpath", "-w", self.shots], text=True).strip() + "\\" + name + ".png"
        proc.stdin.write(win + "\n")
        proc.stdin.flush()
        answer = proc.stdout.readline().strip()
        if not answer.startswith("OK|"):
            raise Abort("INVALID", "OS 캡처 실패: %s" % (answer or "응답 없음"))
        with Image.open(path) as im:
            shot = im.convert("RGB")
        return shot, shot.width / float(self.call("windows.list")[0]["w"])

    def blocked_burst(self, trigger, seconds, area=None):
        """trigger 뒤 seconds 동안 앱 밖 캡처와 응답 지연(=막힘 구간)을 함께 모은다.
        area = 아이콘 칸(클라이언트 화소 상자). --burst-capture screen 이면 그 칸만 화면에서 찍어 같은 자리 캔버스로 돌려준다."""
        from PIL import Image

        screen = getattr(self.args, "burst_capture", "printwindow") == "screen"
        out = subprocess.check_output(["wslpath", "-w", self.shots], text=True).strip().replace("'", "''")
        if screen:
            if area is None or not getattr(self.args, "burst_screen_pos", None):
                raise Abort("DEFERRED", "screen 캡처에는 --burst-screen-pos 와 아이콘 칸이 필요하다")
            x, y = parse_pos(self.args.burst_screen_pos)
            script = (SCREEN_BURST_PS.replace("%PID%", str(self.pid)).replace("%X%", str(x)).replace("%Y%", str(y))
                      .replace("%BX%", str(area[0])).replace("%BY%", str(area[1]))
                      .replace("%BW%", str(area[2] - area[0])).replace("%BH%", str(area[3] - area[1]))
                      .replace("%SETTLE%", str(int(self.args.burst_settle_ms))).replace("%COUNT%", str(int(seconds * 12)))
                      .replace("%GAP%", "40").replace("%DIR%", out))
        else:
            script = (BURST_PS.replace("%PID%", str(self.pid)).replace("%COUNT%", str(int(seconds * 12)))
                      .replace("%GAP%", "40").replace("%DIR%", out))
        proc = subprocess.Popen(["powershell.exe", "-NoProfile", "-Command", script], stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, text=True)
        head = []
        try:
            while True:  # screen 은 MOVED·AREA 줄 뒤에 READY 가 온다
                line = proc.stdout.readline().strip()
                head.append(line)
                if line == "READY":
                    break
                if not line or line == "NOHWND" or line.startswith("NOMOVE") or len(head) > 4:
                    rest = proc.communicate(timeout=30)[0].splitlines()  # screen 은 finally 의 RESTORED 줄까지 받는다
                    if screen:
                        self.screen_note(parse_screen_burst(head + rest)[0], "not_ready")
                    return [], []
            trigger()
            gaps, until = [], time.time() + seconds
            while time.time() < until:
                t0 = time.time()
                self.rig.call("ping")
                if time.time() - t0 >= 0.4:
                    gaps.append((t0 * 1000, time.time() * 1000))
                time.sleep(0.05)
            lines = proc.communicate(timeout=30)[0].splitlines()
        finally:
            if proc.poll() is None:
                proc.kill()  # 정상이면 이미 끝났다. 남았으면 screen 모드는 되돌리기 전일 수 있다 — meta 의 restored 부재로 드러난다
        if screen:
            meta, parts = parse_screen_burst(head + lines)
            self.screen_note(meta, "done")
        else:
            parts = [p for p in (line.strip().split("|") for line in lines) if len(p) == 3]
        shots = []
        for part in parts:
            with Image.open(subprocess.check_output(["wslpath", "-u", part[0]], text=True).strip()) as im:
                img = im.convert("RGB")
            shots.append((place_on_canvas(img, area) if screen else img, float(part[1]), float(part[2])))
        if screen and abs(self.meta["burst_screen"].get("dpi_ratio", 1.0) - 1.0) > 0.01:
            self.meta["burst_screen"]["why"] = "올린 모니터의 배율이 달라 아이콘 칸 크기가 바뀐다 — 판정에 쓰지 않는다(같은 배율 모니터를 고를 것)"
            return [], gaps
        return shots, gaps

    def screen_note(self, meta, stage):
        """screen 모드 노출 기록(결과 meta.burst_screen). 노출 시간은 올림~되돌림."""
        self.meta["burst_screen"] = dict(meta, stage=stage, pos=self.args.burst_screen_pos, monitor=self.args.burst_screen_label,
                                         input="없음(SetWindowPos TOPMOST·NOACTIVATE, 실입력·전경·커서 이동 없음)",
                                         restored="restored_ms" in meta)

    def close(self):
        # 기동·종료는 부른 쪽(run_desktop_bg.sh·pdctl) 몫. 여기서는 자기가 띄운 OS 캡처 powershell 만 끝낸다
        proc = getattr(self, "_shot_proc", None)
        if proc is not None and proc.poll() is None:
            try:
                proc.stdin.close()
                proc.wait(timeout=10)
            except (OSError, subprocess.SubprocessError):
                proc.kill()


class Web:
    scope = "web"
    block_ms, settle_ms = 9000, 5000  # 223229 판 실측: 막히기 전 프레임이 3.4초까지 늦게 올라왔다(headless 소프트웨어 GL)

    def __init__(self, args, name, settings=None):
        if not args.out:
            raise Abort("DEFERRED", "필수 인자 없음: --out")
        if args.port in WEB_REFUSED_PORTS:
            raise Abort("DEFERRED", "쓰면 안 되는 포트 %d" % args.port)
        if not os.path.isfile(os.path.join(args.bundle, "index.html")):
            raise Abort("DEFERRED", "번들이 없다: %s" % args.bundle)
        from playwright.sync_api import sync_playwright
        import ui28_web

        self.shots = shots_dir(args, name)
        self.dpr = float(getattr(args, "dpr", 1.0) or 1.0)
        self.scale_mode = scale_mode_of(self.dpr, getattr(args, "scale_mode", "force") or "force")
        self.meta = {"bundle": os.path.abspath(args.bundle), "started": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "dpr": self.dpr,
                     "scale_mode": self.scale_mode}
        self.web = ui28_web.Web(args.bundle, args.port, self.shots)
        self.play = sync_playwright().start()
        try:
            w = self.web
            if self.scale_mode == "force":  # Chrome 실제 배율 — Playwright 가 배율·뷰포트를 덮어쓰지 않게 no_viewport, 창 크기는 --window-size
                w.browser = self.play.chromium.launch(headless=True, args=ui28_web.ARGS + [
                    "--force-device-scale-factor=%s" % self.dpr, "--window-size=%d,%d" % w.viewport])
                w.page = w.browser.new_context(no_viewport=True).new_page()
            else:
                w.browser = self.play.chromium.launch(headless=True, args=ui28_web.ARGS)
                w.page = w.browser.new_page(viewport={"width": w.viewport[0], "height": w.viewport[1]}, device_scale_factor=self.dpr)
            w.page.on("pageerror", lambda e: w.errors.append("pageerror: " + str(e)[:300]))
            w.page.on("console", lambda m: w.errors.append(m.text[:300]) if m.type == "error" else None)
            if settings:
                body = json.dumps(json.dumps(settings))
                w.page.add_init_script("if (!localStorage.getItem('%s')) localStorage.setItem('%s', %s);"
                                       % (SETTINGS_KEY, SETTINGS_KEY, body))
            # ?fresh 는 저장 설정을 읽지 않는다. 프로필이 새것이라 설정을 심을 때는 빼도 깨끗하다
            w.page.goto("http://127.0.0.1:%d/%s" % (args.port, "" if settings else "?fresh"), wait_until="load", timeout=90000)
            w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
            until = time.time() + 60
            while not w.call("status").get("ok"):
                if time.time() > until:
                    raise Abort("INVALID", "앱이 준비되지 않았다")
                time.sleep(1)
            time.sleep(1.5)
            self.state0 = self.call("style.state")
        except BaseException:
            self.close()
            raise

    @property
    def errors(self):
        return self.web.errors

    def call(self, cmd, **kw):
        r = self.web.call(cmd, **kw)
        if not r.get("ok"):
            raise Abort("INVALID", "%s 실패: %s" % (cmd, r.get("error")))
        return r.get("result")

    def capture(self, name):
        return self.web.shot(name), 1.0

    capture_icon = capture  # 웹 화면 캡처는 컴포지션까지 담는다(9·10차 PASS)

    def blocked_burst(self, trigger, seconds, area=None):  # area 는 데스크톱 screen 모드 전용(웹은 screencast 전체 화면)
        """페이지 심장 박동의 빈 구간 = 막힘. 캡처가 막힘 안에서 끝나는지는 시각으로만 판단한다."""
        from PIL import Image

        page = self.web.page
        page.evaluate("""() => { window.__gaps = []; let last = Date.now();
          window.__beat = setInterval(() => { const n = Date.now(); if (n - last >= 400) window.__gaps.push([last, n]); last = n; }, 20); }""")
        # screenshot 은 막힘이 끝나야 돌아온다. 화면이 실제로 바뀐 때만 오는 screencast 프레임(시각 포함)을 모은다
        shots, cast = [], page.context.new_cdp_session(page)

        def frame(event):
            at = event["metadata"]["timestamp"] * 1000
            shots.append((Image.open(io.BytesIO(base64.b64decode(event["data"]))).convert("RGB"), at, at))
            cast.send("Page.screencastFrameAck", {"sessionId": event["sessionId"]})

        cast.on("Page.screencastFrame", frame)
        cast.send("Page.startScreencast", {"format": "png", "everyNthFrame": 1})
        try:
            trigger()
            page.wait_for_timeout(seconds * 1000)
        finally:
            cast.send("Page.stopScreencast")
        gaps = page.evaluate("() => { clearInterval(window.__beat); return window.__gaps; }")
        return shots, [tuple(g) for g in gaps]

    def close(self):
        try:
            if getattr(self.web, "browser", None):
                self.web.browser.close()
        finally:
            self.web.httpd.shutdown()
            self.web.httpd.server_close()
            self.play.stop()


def run(args, name, judge, settings=None):
    """judge(session) → cases. settings 는 웹 새 프로필에만 심는 설정(데스크톱은 기동한 쪽이 격리 폴더에 준비)."""
    def body():
        s = None
        try:
            s = Web(args, name, settings) if args.bundle else Desktop(args, name)
            cases = judge(s)
            out = {"verdict": overall(cases), "scope": s.scope, "cases": cases, "meta": s.meta}
            if s.scope == "web":
                out.update(console_errors=s.errors, note="웹 결과 — 데스크톱 판정으로 쓰지 않는다")
            return out
        except Abort:
            raise
        except Exception as e:  # 실행 오류가 종료 코드 1(FAIL)로 보이지 않게 한다
            raise Abort("INVALID", "실행 오류: %r" % e)
        finally:
            if s:
                s.close()

    return common.finish(args, name, body)

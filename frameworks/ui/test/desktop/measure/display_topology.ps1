# 표시 토폴로지 시험 도구 — DISPLAY1 만 저장 없이 임시 제거하고 독립 감시 프로세스(display_watchdog.ps1)로 자동 원복하며 전후 창 사각형을 대조한다
# 사용: powershell.exe -NoProfile -ExecutionPolicy Bypass -File <display_topology.ps1> -Action <동작> [옵션]
#   snapshot         경로·모드·전 최상위 창 사각형을 저장(-OutFile). 변경 없음
#   validate         SDC_VALIDATE 만 실행(현 구성·DISPLAY1 제거). 변경 없음
#   preflight        적용 선행 조건 전부 검사. 변경 없음
#   apply            분리 기동한 세션이 선행 조건→감시 기동→임시 적용→유지→원복→대조를 한다. 즉시 반환하므로 status 로 폴링한다
#                    -DryRun 이면 적용·원복을 SDC_VALIDATE 로 대체해 전 경로를 변경 없이 잰다
#   status           -RunId 세션의 상태·결과·감시 결과를 읽는다
#   request-restore  -RunId 세션에 원복을 요청한다
#   restore          -RunId 또는 -SnapshotFile 구성으로 되돌리고 창을 대조한다(감시가 원복한 뒤의 창 복구에도 쓴다)
#   compare          -RunId 또는 -SnapshotFile 구성과 지금을 대조한다. 변경 없음
#   self-test        표시 구성을 바꾸지 않는 자가시험
# 안전: 대상은 DISPLAY1 고정이다. DISPLAY2(터미널)·DISPLAY3(사용자 Pd)는 어떤 경로로도 제거하지 않는다. 저장 플래그를 쓰지 않으므로 최악에도 재부팅이 저장 구성으로 돌린다.
# 사용자 Pd(-UserPdPid) 창은 어떤 경우에도 옮기지 않는다. 겹치면 적용을 거부하고, 움직였으면 결과에 FAIL 값으로 남긴다.
param(
    [ValidateSet('snapshot', 'validate', 'preflight', 'apply', 'session', 'status', 'request-restore', 'restore', 'compare', 'self-test', 'lib')]
    [string]$Action = 'status',
    [string]$WorkDir = '',
    [string]$RunId = '',
    [string]$RunDir = '',
    [int]$UserPdPid,                            # 기본값 없음. preflight·apply·self-test 는 빠지면 rc=3 으로 거부한다
    [string]$TestPids = '',                     # 시험 앱 PID 쉼표 목록(자기 PID 만 — DISPLAY1 에 있어도 된다)
    [string[]]$PdProcessNames = @($(try { $hiv = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\..\tools\host_identity.json') -Raw -Encoding UTF8 | ConvertFrom-Json).'desktop.process_name' } catch { $hiv = $null }; if ($hiv) { $hiv } else { throw 'host_identity.json 을 읽지 못했거나 desktop.process_name 키가 없다' })),
    [int]$DeadlineSec = 120,
    [int]$HoldSec = 30,
    [int]$SettleMs = 3000,
    [string]$GuardCommand = '',                 # 비우면 wsl.exe 로 user_away_guard.py check. 'none' 은 -DryRun 에서만 허용
    [string]$UserReturnedFlag = '',
    [string]$SnapshotFile = '',
    [string]$OutFile = '',
    [switch]$DryRun,
    [switch]$ParseOnly
)
if ($ParseOnly) { 'PARSE_OK'; exit 0 }
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not ('PdCcd' -as [type])) {
    Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public class PdRemoveResult { public byte[] Paths; public uint NumPaths; public byte[] Modes; public uint NumModes; public int Removed; }
public class PdWin { public int Z; public long Hwnd; public int Pid; public string Class; public string Title; public bool Visible; public bool Iconic; public bool Zoomed; public int Cloaked; public int L,T,R,B,NL,NT,NR,NB; }
public static class PdCcd {
  public const int PathSize = 72, ModeSize = 64; const uint Invalid = 0xffffffff;
  [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint f, out uint np, out uint nm);
  [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint f, ref uint np, [In,Out] byte[] p, ref uint nm, [In,Out] byte[] m, IntPtr t);
  [DllImport("user32.dll")] static extern int SetDisplayConfig(uint np, byte[] p, uint nm, byte[] m, uint f);
  [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(byte[] r);
  [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] struct PLACE { public int Len, Flags, Show, MinX, MinY, MaxX, MaxY; public RECT Normal; }
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref PLACE p);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint f);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);

  public static void InitDpi() { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } }

  public static int Query(out byte[] p, out uint np, out byte[] m, out uint nm) {
    p = null; m = null; np = 0; nm = 0; int rc = 122;
    for (int i = 0; i < 5 && rc == 122; i++) {
      rc = GetDisplayConfigBufferSizes(2, out np, out nm); if (rc != 0) return rc;
      p = new byte[np * PathSize]; m = new byte[nm * ModeSize];
      rc = QueryDisplayConfig(2, ref np, p, ref nm, m, IntPtr.Zero); // 2 = QDC_ONLY_ACTIVE_PATHS
    }
    return rc;
  }
  public static int Set(byte[] p, uint np, byte[] m, uint nm, uint flags) { return SetDisplayConfig(np, p, nm, m, flags); }

  static byte[] Req(int type, int size, byte[] src, int luidOffset, uint id) {
    var r = new byte[size];
    BitConverter.GetBytes(type).CopyTo(r, 0); BitConverter.GetBytes(size).CopyTo(r, 4);
    Array.Copy(src, luidOffset, r, 8, 8); BitConverter.GetBytes(id).CopyTo(r, 16);
    return r;
  }
  static string Str(byte[] r, int off, int len) { var s = Encoding.Unicode.GetString(r, off, len); int z = s.IndexOf('\0'); return z >= 0 ? s.Substring(0, z) : s; }
  public static string SourceName(byte[] p, int o) {
    var r = Req(1, 84, p, o, BitConverter.ToUInt32(p, o + 8));
    if (DisplayConfigGetDeviceInfo(r) != 0) return "";
    return Str(r, 20, 64).Replace("\\\\.\\", "");
  }
  public static string TargetName(byte[] p, int o) {
    var r = Req(2, 420, p, o + 20, BitConverter.ToUInt32(p, o + 28));
    if (DisplayConfigGetDeviceInfo(r) != 0) return "";
    return Str(r, 36, 128);
  }
  // 경로마다 "DISPLAY1|tgt=..|x|y|w|h|rot=..|hz=..|이름" 정규형. 배열 순서·mode 색인과 무관하게 비교하려고 정렬한다
  public static string[] Normalize(byte[] p, uint np, byte[] m, uint nm) {
    var l = new List<string>();
    for (int i = 0; i < np; i++) {
      int o = i * PathSize; uint sx = BitConverter.ToUInt32(p, o + 12);
      int x = 0, y = 0, w = 0, h = 0;
      if (sx != Invalid && sx < nm && BitConverter.ToInt32(m, (int)sx * ModeSize) == 1) {
        int a = (int)sx * ModeSize;
        w = BitConverter.ToInt32(m, a + 16); h = BitConverter.ToInt32(m, a + 20); x = BitConverter.ToInt32(m, a + 28); y = BitConverter.ToInt32(m, a + 32);
      }
      l.Add(SourceName(p, o) + "|tgt=" + BitConverter.ToUInt32(p, o + 28) + "|" + x + "|" + y + "|" + w + "|" + h
        + "|rot=" + BitConverter.ToUInt32(p, o + 40) + "|hz=" + BitConverter.ToUInt32(p, o + 48) + "/" + BitConverter.ToUInt32(p, o + 52) + "|" + TargetName(p, o));
    }
    l.Sort(StringComparer.Ordinal);
    return l.ToArray();
  }
  // name 경로를 빼고 쓰이는 mode 만 남겨 색인을 다시 매긴다. 원본 배열은 건드리지 않는다
  public static PdRemoveResult Remove(byte[] p, uint np, byte[] m, uint nm, string name) {
    var keep = new List<int>(); int removed = 0;
    for (int i = 0; i < np; i++) { if (SourceName(p, i * PathSize) == name) removed++; else keep.Add(i); }
    var map = new Dictionary<uint, uint>(); var modes = new List<byte[]>();
    var np2 = new byte[keep.Count * PathSize]; int n = 0;
    foreach (int i in keep) {
      Array.Copy(p, i * PathSize, np2, n * PathSize, PathSize);
      foreach (int off in new[] { 12, 32 }) {
        int at = n * PathSize + off; uint idx = BitConverter.ToUInt32(np2, at);
        if (idx == Invalid) continue;
        if (idx >= nm) throw new InvalidOperationException("mode 색인 범위 초과: " + idx);
        uint nw;
        if (!map.TryGetValue(idx, out nw)) { nw = (uint)modes.Count; map[idx] = nw; var b = new byte[ModeSize]; Array.Copy(m, (int)idx * ModeSize, b, 0, ModeSize); modes.Add(b); }
        BitConverter.GetBytes(nw).CopyTo(np2, at);
      }
      n++;
    }
    var nm2 = new byte[modes.Count * ModeSize];
    for (int i = 0; i < modes.Count; i++) modes[i].CopyTo(nm2, i * ModeSize);
    return new PdRemoveResult { Paths = np2, NumPaths = (uint)keep.Count, Modes = nm2, NumModes = (uint)modes.Count, Removed = removed };
  }
  public static List<PdWin> Windows() {
    var r = new List<PdWin>(); int z = 0;
    EnumWindows((h, l) => {
      var c = new StringBuilder(256); GetClassName(h, c, 256);
      var t = new StringBuilder(512); GetWindowText(h, t, 512);
      uint pid; GetWindowThreadProcessId(h, out pid);
      RECT w; GetWindowRect(h, out w);
      var pl = new PLACE(); pl.Len = Marshal.SizeOf(typeof(PLACE)); GetWindowPlacement(h, ref pl);
      int cl; if (DwmGetWindowAttribute(h, 14, out cl, 4) != 0) cl = 0;
      r.Add(new PdWin { Z = z++, Hwnd = h.ToInt64(), Pid = (int)pid, Class = c.ToString(), Title = t.ToString(), Visible = IsWindowVisible(h), Iconic = IsIconic(h), Zoomed = IsZoomed(h), Cloaked = cl,
        L = w.L, T = w.T, R = w.R, B = w.B, NL = pl.Normal.L, NT = pl.Normal.T, NR = pl.Normal.R, NB = pl.Normal.B });
      return true;
    }, IntPtr.Zero);
    return r;
  }
  // 비활성 이동만 한다. 0x10 NOACTIVATE, 0x4 NOZORDER
  public static bool MoveNoActivate(long hwnd, int x, int y, int w, int h) { return SetWindowPos(new IntPtr(hwnd), IntPtr.Zero, x, y, w, h, 0x14); }
}
"@
}
[PdCcd]::InitDpi()

$TOPO_TARGET = 'DISPLAY1'                       # 고정 대상. 매개변수로 바꿀 수 없다
$TOPO_FORBIDDEN = @('DISPLAY2', 'DISPLAY3')     # 터미널·사용자 Pd 모니터
$SDC_USE_SUPPLIED = 0x20; $SDC_VALIDATE = 0x40; $SDC_APPLY = 0x80; $SDC_ALLOW_CHANGES = 0x400; $SDC_USE_DATABASE_CURRENT = 0xF
$FLAGS_VALIDATE = $SDC_VALIDATE -bor $SDC_USE_SUPPLIED -bor $SDC_ALLOW_CHANGES
$FLAGS_APPLY = $SDC_APPLY -bor $SDC_USE_SUPPLIED -bor $SDC_ALLOW_CHANGES       # 저장 플래그 없음
$FLAGS_DATABASE = $SDC_APPLY -bor $SDC_USE_DATABASE_CURRENT
$PS_EXE = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$TERMINAL_PROCS = @('WindowsTerminal', 'OpenConsole', 'conhost', 'powershell', 'pwsh', 'cmd', 'wezterm-gui', 'alacritty', 'mintty')
$SHELL_CLASSES = @('Progman', 'WorkerW', 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd', 'TopLevelWindowForOverflowXamlIsland', 'XamlExplorerHostIslandWindow')
$SHELL_PROCS = @('dwm', 'ShellExperienceHost', 'StartMenuExperienceHost', 'SearchHost', 'TextInputHost')

$POC_UI = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if ($WorkDir -eq '') { $WorkDir = Join-Path $POC_UI 'logs\pcaway\topology' }
if ($UserReturnedFlag -eq '') { $UserReturnedFlag = Join-Path $POC_UI 'logs\pcaway\user_returned.flag' }

function Write-Text([string]$path, [string]$text) {
    $dir = Split-Path -Parent $path
    if ($dir -and -not (Test-Path $dir)) { [void][IO.Directory]::CreateDirectory($dir) }
    $tmp = "$path.tmp$PID"
    [IO.File]::WriteAllText($tmp, $text, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $path -Force
}
function Write-Json([string]$path, $obj) { Write-Text $path ($obj | ConvertTo-Json -Depth 8) }
function Read-Json([string]$path) { if (Test-Path -LiteralPath $path) { return [IO.File]::ReadAllText($path) | ConvertFrom-Json } return $null }
function Get-Stamp { return (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffzzz') }

function Get-Config {
    $p = $null; $np = [uint32]0; $m = $null; $nm = [uint32]0
    $rc = [PdCcd]::Query([ref]$p, [ref]$np, [ref]$m, [ref]$nm)
    if ($rc -ne 0) { throw "QueryDisplayConfig rc=$rc" }
    return [pscustomobject]@{ paths = $p; np = $np; modes = $m; nm = $nm; topology = @([PdCcd]::Normalize($p, $np, $m, $nm)) }
}
function Get-Monitors([string[]]$topology) {
    return @($topology | ForEach-Object {
            $f = $_ -split '\|'
            [pscustomobject]@{ name = $f[0]; target = $f[1]; l = [int]$f[2]; t = [int]$f[3]; r = [int]$f[2] + [int]$f[4]; b = [int]$f[3] + [int]$f[5]; friendly = $f[8] }
        })
}
function Get-WindowList {
    $names = @{}
    foreach ($pr in Get-Process) { $names[[int]$pr.Id] = $pr.ProcessName }
    return @([PdCcd]::Windows() | ForEach-Object {
            [pscustomobject]@{
                z = $_.Z; hwnd = $_.Hwnd; pid = $_.Pid; process = $names[[int]$_.Pid]; class = $_.Class; title = $_.Title
                visible = $_.Visible; iconic = $_.Iconic; zoomed = $_.Zoomed; cloaked = $_.Cloaked
                rect = [pscustomobject]@{ l = $_.L; t = $_.T; r = $_.R; b = $_.B }
                normal = [pscustomobject]@{ l = $_.NL; t = $_.NT; r = $_.NR; b = $_.NB }
            }
        })
}
function New-Snapshot {
    $c = Get-Config
    return [pscustomobject]@{
        tool = 'display_topology.ps1'; at = (Get-Stamp); np = $c.np; nm = $c.nm
        paths_b64 = [Convert]::ToBase64String($c.paths); modes_b64 = [Convert]::ToBase64String($c.modes)
        topology = $c.topology; monitors = (Get-Monitors $c.topology); windows = (Get-WindowList)
    }
}
function Get-EffRect($w) { if ($w.iconic) { return $w.normal } return $w.rect }   # 최소화 창은 복원 위치로 판단한다
# 최대화 창의 사각형은 보이지 않는 테두리만큼 모니터 밖으로 나간다(실측 -8~2568). 옆 모니터 겹침으로 오판하지 않게 그만큼 줄인다(150% 배율 여유 포함 16)
function Get-JudgeRect($w) {
    $r = Get-EffRect $w
    if ($w.zoomed -and -not $w.iconic) { return [pscustomobject]@{ l = $r.l + 16; t = $r.t + 16; r = $r.r - 16; b = $r.b - 16 } }
    return $r
}
function Test-Overlap($a, $b) { return ($a.l -lt $b.r) -and ($a.r -gt $b.l) -and ($a.t -lt $b.b) -and ($a.b -gt $b.t) }
function Same-Rect($a, $b) { return ($a.l -eq $b.l) -and ($a.t -eq $b.t) -and ($a.r -eq $b.r) -and ($a.b -eq $b.b) }
function Same-Topology($a, $b) { return (@($a) -join "`n") -ceq (@($b) -join "`n") }
function Brief($w) { return [pscustomobject]@{ hwnd = $w.hwnd; pid = $w.pid; process = $w.process; class = $w.class; title = $w.title; visible = $w.visible; iconic = $w.iconic; zoomed = $w.zoomed; rect = $w.rect; normal = $w.normal } }

# 제거 구성. 대상은 고정이고 금지 모니터·주 모니터는 여기서 예외로 막는다. 이 함수만 제거 구성을 만든다
function New-RemovalConfig($cfg, [string]$display) {
    if ($display -cne $TOPO_TARGET -or $TOPO_FORBIDDEN -contains $display) { throw "대상 아님: $display (고정 대상 $TOPO_TARGET)" }
    $mon = @(Get-Monitors $cfg.topology | Where-Object { $_.name -ceq $display })
    if ($mon.Count -eq 0) { throw "모니터 없음: $display" }
    if (@($mon | Where-Object { $_.l -eq 0 -and $_.t -eq 0 }).Count -gt 0) { throw "주 모니터는 대상이 아니다: $display" }
    $r = [PdCcd]::Remove($cfg.paths, $cfg.np, $cfg.modes, $cfg.nm, $display)
    if ($r.Removed -lt 1 -or $r.NumPaths -lt 1) { throw "제거 구성 생성 실패: removed=$($r.Removed) left=$($r.NumPaths)" }
    return $r
}

# 사용자 Pd 소유 창 검사(MonitorChangeWatcher.Evaluate 가 창을 옮기면 layout.json 이 자동 저장되므로 적용 전에 막는다)
# 숨은 창(비가시·비최소화)은 제외한다. 근거는 Evaluate 가 w.IsVisible 인 창만 옮기는 것이다. cloaked(다른 가상 데스크톱)·최소화·최대화 창은 IsVisible 이므로 포함한다.
function Test-UserPdWindows($windows, $targetRect, [int[]]$pdPids) {
    $pd = @($windows | Where-Object { $pdPids -contains [int]$_.pid })
    $shown = @($pd | Where-Object { $_.visible -or $_.iconic })
    $on = @($shown | Where-Object { Test-Overlap (Get-JudgeRect $_) $targetRect })
    return [pscustomobject]@{
        ok = ($on.Count -eq 0); checked_count = $shown.Count; hidden_excluded_count = ($pd.Count - $shown.Count)
        hidden_policy = '숨은 창(비가시·비최소화) 제외, cloaked·최소화(복원 위치 기준)·최대화 포함'
        on_target = @($on | ForEach-Object { Brief $_ }); windows = @($shown | ForEach-Object { Brief $_ })
    }
}
function Get-TestPidList([string]$s) { if ($s -eq '') { return @() } return @($s -split ',' | ForEach-Object { [int]$_ }) }
function Get-PdPids([int]$userPid, [string[]]$names, [int[]]$testPids) {
    $all = @($userPid)
    foreach ($n in $names) { $all += @(Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object { [int]$_.Id }) }
    return @($all | Sort-Object -Unique | Where-Object { $testPids -notcontains $_ })
}
function Test-ShellWindow($w) { return ($SHELL_CLASSES -contains $w.class) -or ($SHELL_PROCS -contains $w.process) }
# 터미널 없는 모니터의 사용자 창 목록(목록만 남기고 건드리지 않는다)
function Get-UserWindowsOnFreeMonitors($windows, $monitors, [int[]]$pdPids, [int[]]$testPids) {
    $seen = @($windows | Where-Object { $_.visible -and $_.cloaked -eq 0 -and -not $_.iconic -and ($_.rect.r -gt $_.rect.l) -and -not (Test-ShellWindow $_) })
    $out = @()
    foreach ($m in $monitors) {
        $here = @($seen | Where-Object { Test-Overlap (Get-JudgeRect $_) $m })
        if (@($here | Where-Object { $TERMINAL_PROCS -contains $_.process }).Count -gt 0) { continue }
        $out += [pscustomobject]@{ monitor = $m.name; windows = @($here | Where-Object { $pdPids -notcontains [int]$_.pid -and $testPids -notcontains [int]$_.pid } | ForEach-Object { Brief $_ }) }
    }
    return $out
}

function Get-DefaultGuard {
    $py = (Join-Path $PSScriptRoot 'user_away_guard.py')
    $wsl = '/mnt/' + $py.Substring(0, 1).ToLower() + ($py.Substring(2) -replace '\\', '/')
    return "wsl.exe -e python3 `"$wsl`" check"
}
function Start-Guard([string]$cmd) {
    if ($cmd -notmatch '^\s*("[^"]+"|\S+)\s*(.*)$') { throw "가드 명령 형식 오류: $cmd" }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Matches[1].Trim('"'); $psi.Arguments = $Matches[2]; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    return [System.Diagnostics.Process]::Start($psi)
}
# rc 0 = 부재(계속). 그 외(3 복귀, 2 판단 불가, 시간 초과, 기동 실패)는 전부 복귀로 다룬다(안전 쪽)
function Invoke-Guard([string]$cmd, [int]$timeoutMs = 65000) {
    if ($cmd -eq 'none') { return [pscustomobject]@{ rc = 0; skipped = $true } }
    try {
        $p = Start-Guard $cmd
        if (-not $p.WaitForExit($timeoutMs)) { try { $p.Kill() } catch { }; return [pscustomobject]@{ rc = 124; skipped = $false } }
        return [pscustomobject]@{ rc = $p.ExitCode; skipped = $false }
    }
    catch { return [pscustomobject]@{ rc = 127; skipped = $false; error = "$_" } }
}

function Test-Preflight($snap, [int]$userPid, [string[]]$names, [int[]]$testPids, [string]$guard, [string]$flag) {
    $why = @()
    $mons = @($snap.monitors)
    $target = @($mons | Where-Object { $_.name -ceq $TOPO_TARGET }) | Select-Object -First 1
    if ($null -eq $target) { $why += 'target_missing' }
    if ($testPids -contains $userPid) { $why += 'user_pd_in_test_pids' }
    $userProc = Get-Process -Id $userPid -ErrorAction SilentlyContinue
    $alive = $null -ne $userProc -and $names -contains $userProc.ProcessName  # PID 가 재사용돼 다른 프로세스면 사용자 Pd 를 못 찾은 것이다
    if (-not $alive) { $why += 'user_pd_not_alive' }
    $pdPids = @(Get-PdPids $userPid $names $testPids)
    $pdCheck = $null; $terminals = @()
    if ($null -ne $target) {
        $pdCheck = Test-UserPdWindows $snap.windows $target $pdPids
        if (-not $pdCheck.ok) { $why += 'user_pd_window_on_target' }
        $terminals = @($snap.windows | Where-Object { $_.visible -and -not $_.iconic -and $_.cloaked -eq 0 -and $TERMINAL_PROCS -contains $_.process -and (Test-Overlap (Get-JudgeRect $_) $target) } | ForEach-Object { Brief $_ })
        if ($terminals.Count -gt 0) { $why += 'terminal_on_target' }
    }
    $cfg = Get-Config
    $rcCurrent = [PdCcd]::Set($cfg.paths, $cfg.np, $cfg.modes, $cfg.nm, $FLAGS_VALIDATE)
    $rcRemove = $null; $removeError = $null
    try { $r = New-RemovalConfig $cfg $TOPO_TARGET; $rcRemove = [PdCcd]::Set($r.Paths, $r.NumPaths, $r.Modes, $r.NumModes, $FLAGS_VALIDATE) }
    catch { $removeError = "$_"; $why += 'removal_config_refused' }
    if ($rcCurrent -ne 0) { $why += 'validate_current_failed' }
    if ($null -ne $rcRemove -and $rcRemove -ne 0) { $why += 'validate_remove_failed' }
    $flagged = Test-Path -LiteralPath $flag
    if ($flagged) { $why += 'user_returned_flag' }
    $g = if ($guard -eq '') { [pscustomobject]@{ rc = $null; skipped = $true } } else { Invoke-Guard $guard }
    if ($guard -ne '' -and $g.rc -ne 0) { $why += "guard_rc_$($g.rc)" }
    return [pscustomobject]@{
        ok = ($why.Count -eq 0); refuse_reasons = $why; target = $target
        user_pd_alive = $alive; pd_pids_checked = $pdPids; test_pids = $testPids
        user_pd_windows_outside_target = ($null -ne $pdCheck -and $pdCheck.ok); user_pd_window_check = $pdCheck
        terminal_windows_on_target = $terminals; user_windows_on_free_monitors = @(Get-UserWindowsOnFreeMonitors $snap.windows $mons $pdPids $testPids)
        validate_current_rc = $rcCurrent; validate_remove_rc = $rcRemove; removal_error = $removeError
        guard_rc = $g.rc; guard_skipped = $g.skipped; user_returned_flag = $flagged
    }
}

function Load-Snapshot([string]$path) {
    $s = Read-Json $path
    if ($null -eq $s) { throw "스냅샷 없음: $path" }
    return $s
}
# 저장 구성으로 되돌린다. 실패하면 저장 없는 DB 구성으로 돌아간다. DryRun 은 저장 구성을 검증만 한다. 감시·세션이 동시에 불러도 뮤텍스로 직렬화한다
function Restore-Topology($snap, [bool]$dry) {
    $mx = New-Object System.Threading.Mutex($false, 'Local\PdDisplayTopoRestore')
    $got = $false; $log = @()
    try {
        try { $got = $mx.WaitOne(60000) } catch [System.Threading.AbandonedMutexException] { $got = $true }
        $saved = @($snap.topology); $p = [Convert]::FromBase64String($snap.paths_b64); $m = [Convert]::FromBase64String($snap.modes_b64)
        $np = [uint32]$snap.np; $nm = [uint32]$snap.nm
        if ($dry) {
            $rc = [PdCcd]::Set($p, $np, $m, $nm, $FLAGS_VALIDATE)
            return [pscustomobject]@{ method = 'validate_only'; equal = (Same-Topology (Get-Config).topology $saved); attempts = @(@{ flags = 'validate'; rc = $rc }); rc = $rc }
        }
        if (Same-Topology (Get-Config).topology $saved) { return [pscustomobject]@{ method = 'already_equal'; equal = $true; attempts = @(); rc = 0 } }
        for ($i = 0; $i -lt 3; $i++) {
            $rc = [PdCcd]::Set($p, $np, $m, $nm, $FLAGS_APPLY)
            Start-Sleep -Milliseconds 1500
            $eq = Same-Topology (Get-Config).topology $saved
            $log += @{ flags = 'apply_saved'; rc = $rc; equal = $eq }
            if ($rc -eq 0 -and $eq) { return [pscustomobject]@{ method = 'saved_config'; equal = $true; attempts = $log; rc = 0 } }
            Start-Sleep -Milliseconds 1000
        }
        $rc = [PdCcd]::Set($null, 0, $null, 0, $FLAGS_DATABASE)
        Start-Sleep -Milliseconds 2000
        $eq = Same-Topology (Get-Config).topology $saved
        $log += @{ flags = 'apply_database_current'; rc = $rc; equal = $eq }
        return [pscustomobject]@{ method = 'database_current'; equal = $eq; attempts = $log; rc = $rc }
    }
    finally { if ($got) { $mx.ReleaseMutex() }; $mx.Dispose() }
}
# 원복 뒤 달라진 창. 사용자 창은 비활성 이동으로 원위치를 시도한다. 사용자 Pd·시험 앱·셸 창은 옮기지 않는다
function Compare-AndRepairWindows($before, [int[]]$pdPids, [int[]]$testPids, [bool]$repair, [int]$settleMs) {
    $nowList = @(Get-WindowList); $now = @{}
    foreach ($w in $nowList) { $now[[long]$w.hwnd] = $w }
    $changed = @()
    foreach ($b in @($before.windows | Where-Object { ($_.visible -or $_.iconic) -and $_.cloaked -eq 0 })) {
        $a = $now[[long]$b.hwnd]
        if ($null -eq $a) { $changed += [pscustomobject]@{ before = (Brief $b); after = $null; missing = $true }; continue }
        if (-not (Same-Rect (Get-EffRect $b) (Get-EffRect $a)) -or $b.iconic -ne $a.iconic -or $b.zoomed -ne $a.zoomed) { $changed += [pscustomobject]@{ before = (Brief $b); after = (Brief $a); missing = $false } }
    }
    $isPd = { param($c) $pdPids -contains [int]$c.before.pid }
    $isTest = { param($c) $testPids -contains [int]$c.before.pid }
    $pdMoved = @($changed | Where-Object { & $isPd $_ })
    $userChanged = @($changed | Where-Object { -not (& $isPd $_) -and -not (& $isTest $_) -and -not (Test-ShellWindow $_.before) })
    $repairs = @()
    if ($repair) {
        foreach ($c in @($userChanged | Where-Object { -not $_.missing -and -not $_.after.iconic })) {
            $t = Get-EffRect $c.before
            if ($c.before.iconic) { $repairs += [pscustomobject]@{ hwnd = $c.before.hwnd; title = $c.before.title; attempted = $false; note = '시험 전 최소화 창' }; continue }
            $ok = [PdCcd]::MoveNoActivate([long]$c.before.hwnd, $t.l, $t.t, $t.r - $t.l, $t.b - $t.t)
            $repairs += [pscustomobject]@{ hwnd = $c.before.hwnd; process = $c.before.process; title = $c.before.title; attempted = $true; api_ok = $ok }
        }
        if ($repairs.Count -gt 0) { Start-Sleep -Milliseconds $settleMs }
    }
    $stillDiff = @()
    if ($repairs.Count -gt 0) {
        $now2 = @{}; foreach ($w in @(Get-WindowList)) { $now2[[long]$w.hwnd] = $w }
        foreach ($c in $userChanged) {
            $a = $now2[[long]$c.before.hwnd]
            if ($null -eq $a -or -not (Same-Rect (Get-EffRect $c.before) (Get-EffRect $a))) { $stillDiff += $c }
        }
    }
    else { $stillDiff = $userChanged }
    return [pscustomobject]@{
        windows_changed_count = $changed.Count; user_windows_changed = $userChanged; user_windows_repairs = $repairs; user_windows_still_different = $stillDiff
        user_pd_windows_moved = $pdMoved; user_pd_windows_unmoved = ($pdMoved.Count -eq 0); test_windows_changed = @($changed | Where-Object { & $isTest $_ })
    }
}

function Get-RunDir([string]$id) { return Join-Path $WorkDir $id }
function Start-Detached([string]$file, [string]$argLine) {
    return Start-Process -FilePath $PS_EXE -ArgumentList "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$file`" $argLine" -WindowStyle Hidden -PassThru
}

function Invoke-Session([string]$dir) {
    $cfgFile = Join-Path $dir 'config.json'
    $cfg = Read-Json $cfgFile
    $dry = [bool]$cfg.dry_run; $guard = [string]$cfg.guard; $flag = [string]$cfg.user_returned_flag
    $testPids = @($cfg.test_pids | ForEach-Object { [int]$_ })
    $names = @($cfg.pd_process_names); $userPid = [int]$cfg.user_pd_pid; $settle = [int]$cfg.settle_ms
    $state = { param($phase) Write-Json (Join-Path $dir 'state.json') ([pscustomobject]@{ phase = $phase; at = (Get-Stamp); session_pid = $PID; dry_run = $dry }) }
    $res = [ordered]@{ tool = 'display_topology.ps1'; run_id = $cfg.run_id; dry_run = $dry; started_at = (Get-Stamp); session_pid = $PID; target = $TOPO_TARGET; applied = $false
        applied_flags = ('0x{0:X}' -f $FLAGS_APPLY); no_save_to_database = (($FLAGS_APPLY -band 512) -eq 0); user_pd_pid = $userPid }
    $finish = { param($phase, [int]$code) $res.phase = $phase; $res.ended_at = (Get-Stamp); Write-Json (Join-Path $dir 'result.json') $res; & $state $phase; exit $code }
    $mx = New-Object System.Threading.Mutex($false, 'Local\PdDisplayTopoSession')
    $got = $false
    try { $got = $mx.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $got = $true }
    if (-not $got) { $res.refuse_reasons = @('session_already_running'); & $finish 'refused' 3 }

    & $state 'preflight'
    $snapFile = Join-Path $dir 'snapshot_before.json'
    $before = New-Snapshot
    Write-Json $snapFile $before
    $before = Load-Snapshot $snapFile
    $pf = Test-Preflight $before $userPid $names $testPids $guard $flag
    $res.preflight = $pf; $res.user_pd_windows_outside_target = $pf.user_pd_windows_outside_target
    if (-not $pf.ok) { $res.refuse_reasons = $pf.refuse_reasons; & $finish 'refused' 3 }

    # 감시 먼저 기동한다. 살아 있음을 확인하지 못하면 적용하지 않는다
    $wdFile = Join-Path $PSScriptRoot 'display_watchdog.ps1'
    $armed = Join-Path $dir 'watchdog_armed.json'
    $wdArgs = "-ParentPid $PID -SnapshotFile `"$snapFile`" -RunDir `"$dir`" -DeadlineSec $([int]$cfg.deadline_sec) -UserReturnedFlag `"$flag`""
    if ($dry) { $wdArgs += ' -DryRun' }
    $wd = Start-Detached $wdFile $wdArgs
    $res.watchdog_pid = $wd.Id
    $until = (Get-Date).AddSeconds(30)
    while (-not (Test-Path -LiteralPath $armed) -and -not $wd.HasExited -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path -LiteralPath $armed) -or $wd.HasExited) { $res.refuse_reasons = @('watchdog_not_armed'); & $finish 'refused' 3 }

    # 감시 기동에 수 초 걸리므로 직전에 다시 확인한다
    $recheck = New-Snapshot
    $pdPids = @(Get-PdPids $userPid $names $testPids)
    $mon = @($recheck.monitors | Where-Object { $_.name -ceq $TOPO_TARGET }) | Select-Object -First 1
    $last = Test-UserPdWindows $recheck.windows $mon $pdPids
    $g = Invoke-Guard $guard
    $res.last_check = [pscustomobject]@{ user_pd_windows_outside_target = $last.ok; guard_rc = $g.rc; at = (Get-Stamp) }
    if (-not $last.ok -or $g.rc -ne 0 -or (Test-Path -LiteralPath $flag)) {
        $res.user_pd_windows_outside_target = $last.ok
        $res.refuse_reasons = @('last_check_failed')
        Write-Text (Join-Path $dir 'session_done') (Get-Stamp)
        & $finish 'refused' 3
    }

    $c = Get-Config
    $rm = New-RemovalConfig $c $TOPO_TARGET
    $flagsUse = if ($dry) { $FLAGS_VALIDATE } else { $FLAGS_APPLY }
    $res.apply_rc = [PdCcd]::Set($rm.Paths, $rm.NumPaths, $rm.Modes, $rm.NumModes, $flagsUse)
    $res.applied_at = (Get-Stamp)
    if ($res.apply_rc -ne 0) {
        $res.refuse_reasons = @("apply_rc_$($res.apply_rc)")
        $res.restore = Restore-Topology $before $dry
        if ($res.restore.equal) { Write-Text (Join-Path $dir 'session_done') (Get-Stamp) }
        & $finish 'apply_failed' 4
    }
    $res.applied = -not $dry
    & $state 'applied'

    # 유지. 복귀·감시 소멸·원복 요청이 오면 바로 끝낸다. 감시 마감보다 먼저 끝나게 잘라 둔다
    $hold = [Math]::Min([int]$cfg.hold_sec, [int]$cfg.deadline_sec - 15)
    if ($hold -lt 1) { $hold = 1 }
    $res.hold_sec_effective = $hold
    $end = (Get-Date).AddSeconds($hold); $ended = 'hold_elapsed'
    $req = Join-Path $dir 'restore_request'
    $snapped = $false; $appliedAt = Get-Date; $gp = $null; $gpAt = $null; $res.guard_rcs_during_hold = @()
    while ((Get-Date) -lt $end) {
        if (Test-Path -LiteralPath $req) { $ended = 'restore_requested'; break }
        if (Test-Path -LiteralPath $flag) { $ended = 'user_returned'; break }
        if ($wd.HasExited) { $ended = 'watchdog_gone'; break }
        if ($guard -ne 'none') {
            if ($null -eq $gp) { try { $gp = Start-Guard $guard; $gpAt = Get-Date } catch { $ended = 'guard_unavailable'; break } }
            elseif ($gp.HasExited) {
                $res.guard_rcs_during_hold += $gp.ExitCode
                if ($gp.ExitCode -ne 0) { $ended = if ($gp.ExitCode -eq 3) { 'user_returned' } else { 'guard_unavailable' }; break }
                $gp = $null
            }
            elseif (((Get-Date) - $gpAt).TotalSeconds -gt 65) { try { $gp.Kill() } catch { }; $ended = 'guard_unavailable'; break }
        }
        if (-not $snapped -and ((Get-Date) - $appliedAt).TotalMilliseconds -ge $settle) {
            $snapped = $true
            Write-Json (Join-Path $dir 'snapshot_applied.json') (New-Snapshot)
            & $state 'holding'
        }
        Start-Sleep -Milliseconds 500
    }
    $res.ended_by = $ended
    & $state 'restoring'
    $res.restore = Restore-Topology $before $dry
    $res.topology_equal_after_restore = [bool]$res.restore.equal
    # 원복에 실패하면 session_done 을 남기지 않는다. 감시가 계속 재시도한다
    if ($res.restore.equal) { Write-Text (Join-Path $dir 'session_done') (Get-Stamp) }
    Start-Sleep -Milliseconds $settle
    $cmp = Compare-AndRepairWindows $before $pdPids $testPids (-not $dry) $settle
    foreach ($k in 'windows_changed_count', 'user_windows_changed', 'user_windows_repairs', 'user_windows_still_different', 'user_pd_windows_moved', 'user_pd_windows_unmoved', 'test_windows_changed') { $res[$k] = $cmp.$k }
    Write-Json (Join-Path $dir 'snapshot_after.json') (New-Snapshot)
    $res.safety = [pscustomobject]@{ user_returned = (Test-Path -LiteralPath $flag); user_pd_alive = ($null -ne (Get-Process -Id $userPid -ErrorAction SilentlyContinue)) }
    & $finish 'done' $(if ($res.restore.equal -and $cmp.user_pd_windows_unmoved) { 0 } else { 5 })
}

function Resolve-Snapshot {
    if ($SnapshotFile -ne '') { return $SnapshotFile }
    if ($RunId -ne '') { return (Join-Path (Get-RunDir $RunId) 'snapshot_before.json') }
    throw '-RunId 또는 -SnapshotFile 이 필요하다'
}
function Emit($obj, [int]$code = 0) {
    $text = $obj | ConvertTo-Json -Depth 8
    if ($OutFile -ne '') { Write-Text $OutFile $text }
    $text
    exit $code
}

if ($Action -eq 'lib') { return }

# ── 자가시험: 표시 구성을 바꾸지 않는다 ─────────────────────────────
function Invoke-SelfTest {
    $checks = New-Object System.Collections.ArrayList
    $add = { param($n, [bool]$ok, $d) [void]$checks.Add([pscustomobject]@{ name = $n; pass = $ok; detail = $d }) }
    $dir = Join-Path $WorkDir ('selftest_' + (Get-Date).ToString('yyyyMMdd_HHmmss'))
    [void][IO.Directory]::CreateDirectory($dir)
    $c = Get-Config
    $before = @($c.topology)
    & $add 'query_paths_nonzero' ($c.np -ge 1) ("paths=$($c.np) modes=$($c.nm)")
    $mons = @(Get-Monitors $c.topology)
    & $add 'target_monitor_present' (@($mons | Where-Object { $_.name -ceq $TOPO_TARGET }).Count -eq 1) (@($mons | ForEach-Object { "$($_.name)[$($_.l),$($_.t),$($_.r),$($_.b)]" }) -join ' ')
    $primary = @($mons | Where-Object { $_.l -eq 0 -and $_.t -eq 0 } | ForEach-Object { $_.name })
    & $add 'target_is_not_primary' ($primary -notcontains $TOPO_TARGET) ("primary=$($primary -join ',')")
    & $add 'no_save_to_database' ((($FLAGS_APPLY -bor $FLAGS_VALIDATE -bor $FLAGS_DATABASE) -band 512) -eq 0) ('apply=0x{0:X} validate=0x{1:X} db=0x{2:X}' -f $FLAGS_APPLY, $FLAGS_VALIDATE, $FLAGS_DATABASE)
    # 제거 구성·검증
    $rm = New-RemovalConfig $c $TOPO_TARGET
    $left = @([PdCcd]::Normalize($rm.Paths, $rm.NumPaths, $rm.Modes, $rm.NumModes))
    & $add 'removal_config_compacted' (($rm.NumPaths -eq ($c.np - $rm.Removed)) -and ($rm.NumModes -lt $c.nm) -and (@($left | Where-Object { $_ -like "$TOPO_TARGET|*" }).Count -eq 0)) ("removed=$($rm.Removed) paths=$($rm.NumPaths) modes=$($rm.NumModes) of $($c.nm)")
    $rcCur = [PdCcd]::Set($c.paths, $c.np, $c.modes, $c.nm, $FLAGS_VALIDATE)
    $rcRm = [PdCcd]::Set($rm.Paths, $rm.NumPaths, $rm.Modes, $rm.NumModes, $FLAGS_VALIDATE)
    & $add 'validate_only_rc0' (($rcCur -eq 0) -and ($rcRm -eq 0)) ("current=$rcCur remove_display1=$rcRm")
    # 대상 고정
    foreach ($d in @('DISPLAY2', 'DISPLAY3', 'DISPLAY9', 'display1')) {
        $refused = $false; try { [void](New-RemovalConfig $c $d) } catch { $refused = $true }
        & $add "refuse_target_$d" $refused '고정 대상 외 제거 구성 거부'
    }
    # 겹침 판정(가짜 창)
    $tg = [pscustomobject]@{ l = 2560; t = 236; r = 4480; b = 1436 }
    $mk = { param($id, $vis, $ico, $l, $t, $r, $b, $nl, $nt, $nr, $nb) [pscustomobject]@{ hwnd = $id; pid = 41312; process = $PdProcessNames[0]; class = 'x'; title = 't'; visible = $vis; iconic = $ico; zoomed = $false; cloaked = 0
            rect = [pscustomobject]@{ l = $l; t = $t; r = $r; b = $b }; normal = [pscustomobject]@{ l = $nl; t = $nt; r = $nr; b = $nb } } }
    $off = & $mk 1 $true $false -2138 5 7 1303 -2138 5 7 1303
    $on = & $mk 2 $true $false 3000 300 3500 800 3000 300 3500 800
    $hid = & $mk 3 $false $false 3000 300 3500 800 3000 300 3500 800
    $ico = & $mk 4 $true $true -32000 -32000 -31840 -31972 3000 300 3500 800
    & $add 'pd_window_elsewhere_ok' ((Test-UserPdWindows @($off) $tg @(41312)).ok) '다른 모니터의 창은 통과'
    & $add 'pd_window_on_target_refused' (-not (Test-UserPdWindows @($off, $on) $tg @(41312)).ok) '가시 창 겹침 거부'
    & $add 'pd_hidden_window_excluded' ((Test-UserPdWindows @($off, $hid) $tg @(41312)).ok) '숨은 창 제외'
    & $add 'pd_iconic_restore_rect_refused' (-not (Test-UserPdWindows @($ico) $tg @(41312)).ok) '최소화 창은 복원 위치로 판단'
    $zmNear = & $mk 5 $true $false -8 -8 2568 1400 0 0 0 0; $zmNear.zoomed = $true
    $zmOn = & $mk 6 $true $false 2552 228 4488 1444 0 0 0 0; $zmOn.zoomed = $true
    & $add 'pd_maximized_overhang_not_on_target' ((Test-UserPdWindows @($zmNear) $tg @(41312)).ok) '옆 모니터 최대화 창의 8px 테두리는 겹침이 아니다'
    & $add 'pd_maximized_on_target_refused' (-not (Test-UserPdWindows @($zmOn) $tg @(41312)).ok) '대상 모니터 최대화 창은 거부'
    # 실제 상태 읽기(변경 없음)
    $snap = New-Snapshot
    $sf = Join-Path $dir 'snapshot_probe.json'
    Write-Json $sf $snap
    $snapRead = Load-Snapshot $sf
    & $add 'snapshot_roundtrip' ((Same-Topology $snapRead.topology $before) -and ([Convert]::FromBase64String($snapRead.paths_b64).Length -eq ($c.np * 72)) -and (@($snapRead.windows).Count -gt 0)) ("windows=$(@($snapRead.windows).Count)")
    $pf = Test-Preflight $snapRead $UserPdPid $PdProcessNames @() '' $UserReturnedFlag
    & $add 'real_preflight_user_pd_windows_outside_target' ($pf.user_pd_windows_outside_target -eq $true) ("checked=$($pf.user_pd_window_check.checked_count) hidden_excluded=$($pf.user_pd_window_check.hidden_excluded_count) reasons=$($pf.refuse_reasons -join ',')")
    & $add 'real_preflight_ok' ($pf.ok -eq $true) ("reasons=$($pf.refuse_reasons -join ',')")
    $pfDead = Test-Preflight $snapRead 2147483000 $PdProcessNames @() '' $UserReturnedFlag
    & $add 'preflight_refuses_dead_user_pid' (@($pfDead.refuse_reasons) -contains 'user_pd_not_alive') '없는 PID 거부'
    $pfOther = Test-Preflight $snapRead $PID $PdProcessNames @() '' $UserReturnedFlag
    & $add 'preflight_refuses_non_pd_pid' (@($pfOther.refuse_reasons) -contains 'user_pd_not_alive') 'Pd 가 아닌 프로세스 PID 거부'
    Write-Json (Join-Path $dir 'preflight.json') $pf
    # 가드 호출 경로. 실제 check 는 부르지 않는다(복귀 플래그·시작 기록을 남길 수 있다)
    & $add 'guard_rc3_is_returned' ((Invoke-Guard 'cmd.exe /c exit 3').rc -eq 3) '복귀 rc 전달'
    & $add 'guard_rc0_is_away' ((Invoke-Guard 'cmd.exe /c exit 0').rc -eq 0) '부재 rc 전달'
    & $add 'guard_missing_exe_nonzero' ((Invoke-Guard 'pd_no_such_exe.exe').rc -ne 0) '기동 실패는 복귀로 다룸'
    $helpCmd = (Get-DefaultGuard) -replace ' check$', ' --help'
    & $add 'guard_default_path_reaches_script' ((Invoke-Guard $helpCmd 30000).rc -eq 0) $helpCmd
    & $add 'guard_none_skips' ((Invoke-Guard 'none').skipped -eq $true) 'DryRun 전용'

    # 감시 직접 기동 — 마감·복귀 플래그(적용 대신 SDC_VALIDATE)
    $wdFile = Join-Path $PSScriptRoot 'display_watchdog.ps1'
    $d1 = Join-Path $dir 'wd_deadline'; $d2 = Join-Path $dir 'wd_flag'
    [void][IO.Directory]::CreateDirectory($d1); [void][IO.Directory]::CreateDirectory($d2)
    $ffile = Join-Path $d2 'user_returned.flag'
    $w1 = Start-Detached $wdFile "-ParentPid $PID -SnapshotFile `"$sf`" -RunDir `"$d1`" -DeadlineSec 4 -UserReturnedFlag `"$(Join-Path $d1 'none.flag')`" -DryRun"
    $w2 = Start-Detached $wdFile "-ParentPid $PID -SnapshotFile `"$sf`" -RunDir `"$d2`" -DeadlineSec 60 -UserReturnedFlag `"$ffile`" -DryRun"
    # 세션 전 경로(DryRun) — 분리 기동·감시 무장·유지·원복 검증·감시 해제
    $sess = {
        param($id, $hold)
        $rd = Get-RunDir $id
        [void][IO.Directory]::CreateDirectory($rd)
        Write-Json (Join-Path $rd 'config.json') ([pscustomobject]@{ run_id = $id; dry_run = $true; guard = 'none'; user_returned_flag = (Join-Path $rd 'none.flag'); test_pids = @(); pd_process_names = $PdProcessNames; user_pd_pid = $UserPdPid; settle_ms = 500; deadline_sec = 60; hold_sec = $hold })
        return [pscustomobject]@{ dir = $rd; proc = (Start-Detached $PSCommandPath "-Action session -RunDir `"$rd`" -WorkDir `"$WorkDir`"") }
    }
    $s1 = & $sess ('selftest_s1_' + $PID) 2
    $untilT = (Get-Date).AddSeconds(45)
    while (-not (Test-Path (Join-Path $d2 'watchdog_armed.json')) -and (Get-Date) -lt $untilT) { Start-Sleep -Milliseconds 200 }
    Write-Text $ffile 'selftest'
    while (((-not $s1.proc.HasExited) -or (-not $w1.HasExited) -or (-not $w2.HasExited)) -and (Get-Date) -lt $untilT) { Start-Sleep -Milliseconds 300 }
    $r1 = Read-Json (Join-Path $d1 'watchdog_result.json'); $r2 = Read-Json (Join-Path $d2 'watchdog_result.json')
    & $add 'watchdog_deadline_triggers' ($null -ne $r1 -and $r1.reason -eq 'deadline' -and $r1.restore.rc -eq 0) ($(if ($r1) { "reason=$($r1.reason) rc=$($r1.restore.rc)" } else { '결과 없음' }))
    & $add 'watchdog_user_flag_triggers' ($null -ne $r2 -and $r2.reason -eq 'user_returned' -and $r2.restore.rc -eq 0) ($(if ($r2) { "reason=$($r2.reason) rc=$($r2.restore.rc)" } else { '결과 없음' }))
    $sr1 = Read-Json (Join-Path $s1.dir 'result.json')
    & $add 'dry_session_completes' ($null -ne $sr1 -and $sr1.phase -eq 'done' -and $sr1.apply_rc -eq 0 -and $sr1.applied -eq $false -and $sr1.ended_by -eq 'hold_elapsed') ($(if ($sr1) { "phase=$($sr1.phase) apply_rc=$($sr1.apply_rc) ended=$($sr1.ended_by) reasons=$($sr1.refuse_reasons -join ',')" } else { '결과 없음' }))
    $sw1 = $null; $u = (Get-Date).AddSeconds(15)
    while ($null -eq $sw1 -and (Get-Date) -lt $u) { $sw1 = Read-Json (Join-Path $s1.dir 'watchdog_result.json'); if ($null -eq $sw1) { Start-Sleep -Milliseconds 300 } }
    & $add 'dry_session_disarms_watchdog' ($null -ne $sw1 -and $sw1.reason -eq 'disarmed') ($(if ($sw1) { "reason=$($sw1.reason)" } else { '결과 없음' }))
    # 세션 강제 종료 → 감시 발동(G1 의 변경 없는 판)
    $s2 = & $sess ('selftest_s2_' + $PID) 40
    $u = (Get-Date).AddSeconds(30); $st = $null
    while ((Get-Date) -lt $u) { $st = Read-Json (Join-Path $s2.dir 'state.json'); if ($null -ne $st -and $st.phase -in @('applied', 'holding')) { break }; Start-Sleep -Milliseconds 200 }
    if (-not $s2.proc.HasExited) { $s2.proc.Kill() }
    $sw2 = $null; $u = (Get-Date).AddSeconds(15)
    while ($null -eq $sw2 -and (Get-Date) -lt $u) { $sw2 = Read-Json (Join-Path $s2.dir 'watchdog_result.json'); if ($null -eq $sw2) { Start-Sleep -Milliseconds 300 } }
    & $add 'watchdog_restores_after_main_kill_dry' ($null -ne $sw2 -and $sw2.reason -eq 'parent_gone' -and $sw2.restore.rc -eq 0) ($(if ($sw2) { "reason=$($sw2.reason) rc=$($sw2.restore.rc) method=$($sw2.restore.method)" } else { "결과 없음 phase=$($st.phase)" }))
    # 저장·조회 왕복 뒤에도 표시 구성이 그대로인지
    & $add 'topology_unchanged_by_self_test' (Same-Topology (Get-Config).topology $before) '자가시험은 표시 구성을 바꾸지 않는다'

    $fail = @($checks | Where-Object { -not $_.pass })
    return [pscustomobject]@{ tool = 'display_topology.ps1 self-test'; at = (Get-Stamp); dir = $dir; total = $checks.Count; failed = $fail.Count; checks = @($checks) }
}

# restore·compare 는 복구를 막지 않으려고 제외한다. 창 보호는 Get-PdPids 가 프로세스 이름으로도 잡는다
if (@('preflight', 'apply', 'self-test') -contains $Action -and $UserPdPid -le 0) { Emit ([pscustomobject]@{ started = $false; refuse_reasons = @('user_pd_pid_required') }) 3 }
switch ($Action) {
    'snapshot' {
        $s = New-Snapshot
        if ($OutFile -ne '') { Write-Json $OutFile $s; Emit ([pscustomobject]@{ out_file = $OutFile; at = $s.at; topology = $s.topology; window_count = @($s.windows).Count }) }
        Emit $s
    }
    'validate' {
        $c = Get-Config
        $rcCur = [PdCcd]::Set($c.paths, $c.np, $c.modes, $c.nm, $FLAGS_VALIDATE)
        $rm = New-RemovalConfig $c $TOPO_TARGET
        $rcRm = [PdCcd]::Set($rm.Paths, $rm.NumPaths, $rm.Modes, $rm.NumModes, $FLAGS_VALIDATE)
        Emit ([pscustomobject]@{ action = 'validate SDC_VALIDATE only'; validate_current_rc = $rcCur; validate_remove_rc = $rcRm; target = $TOPO_TARGET; forbidden_not_attempted = $TOPO_FORBIDDEN; validate_flags = ('0x{0:X}' -f $FLAGS_VALIDATE); topology = $c.topology }) $(if ($rcCur -eq 0 -and $rcRm -eq 0) { 0 } else { 1 })
    }
    'preflight' {
        $s = New-Snapshot
        $f = Join-Path $env:TEMP "pd_topo_preflight_$PID.json"
        Write-Json $f $s; $s = Load-Snapshot $f; Remove-Item -LiteralPath $f -Force
        $g = if ($GuardCommand -eq '') { Get-DefaultGuard } else { $GuardCommand }
        $pf = Test-Preflight $s $UserPdPid $PdProcessNames (Get-TestPidList $TestPids) $g $UserReturnedFlag
        Emit $pf $(if ($pf.ok) { 0 } else { 3 })
    }
    'apply' {
        $g = if ($GuardCommand -eq '') { Get-DefaultGuard } else { $GuardCommand }
        if ($g -eq 'none' -and -not $DryRun) { Emit ([pscustomobject]@{ started = $false; refuse_reasons = @('guard_none_requires_dry_run') }) 3 }
        if ($DeadlineSec -lt 30 -or $DeadlineSec -gt 300) { Emit ([pscustomobject]@{ started = $false; refuse_reasons = @('deadline_out_of_range_30_300') }) 3 }
        $id = if ($RunId -ne '') { $RunId } else { 'topo_' + (Get-Date).ToString('yyyyMMdd_HHmmss') }
        $rd = Get-RunDir $id
        if (Test-Path -LiteralPath $rd) { Emit ([pscustomobject]@{ started = $false; refuse_reasons = @('run_dir_exists'); run_dir = $rd }) 3 }
        [void][IO.Directory]::CreateDirectory($rd)
        Write-Json (Join-Path $rd 'config.json') ([pscustomobject]@{ run_id = $id; dry_run = [bool]$DryRun; guard = $g; user_returned_flag = $UserReturnedFlag; test_pids = @(Get-TestPidList $TestPids); pd_process_names = $PdProcessNames; user_pd_pid = $UserPdPid; settle_ms = $SettleMs; deadline_sec = $DeadlineSec; hold_sec = $HoldSec })
        $p = Start-Detached $PSCommandPath "-Action session -RunDir `"$rd`" -WorkDir `"$WorkDir`""
        Emit ([pscustomobject]@{ started = $true; run_id = $id; run_dir = $rd; session_pid = $p.Id; dry_run = [bool]$DryRun; poll = "-Action status -RunId $id" })
    }
    'session' { Invoke-Session $RunDir }
    'status' {
        if ($RunId -eq '') { throw '-RunId 가 필요하다' }
        $rd = Get-RunDir $RunId
        $st = Read-Json (Join-Path $rd 'state.json')
        $alive = $false
        if ($null -ne $st) { $alive = $null -ne (Get-Process -Id ([int]$st.session_pid) -ErrorAction SilentlyContinue) }
        Emit ([pscustomobject]@{ run_id = $RunId; state = $st; session_alive = $alive; result = (Read-Json (Join-Path $rd 'result.json')); watchdog = (Read-Json (Join-Path $rd 'watchdog_result.json')); current_topology = (Get-Config).topology })
    }
    'request-restore' {
        if ($RunId -eq '') { throw '-RunId 가 필요하다' }
        Write-Text (Join-Path (Get-RunDir $RunId) 'restore_request') (Get-Stamp)
        Emit ([pscustomobject]@{ requested = $true; run_id = $RunId })
    }
    'restore' {
        $sf = Resolve-Snapshot
        $snap = Load-Snapshot $sf
        $rs = Restore-Topology $snap ([bool]$DryRun)
        Start-Sleep -Milliseconds $SettleMs
        $testList = @(Get-TestPidList $TestPids)
        $cmp = Compare-AndRepairWindows $snap @(Get-PdPids $UserPdPid $PdProcessNames $testList) $testList (-not $DryRun) $SettleMs
        Emit ([pscustomobject]@{ snapshot = $sf; restore = $rs; topology_equal_after_restore = $rs.equal; windows = $cmp }) $(if ($rs.equal -and $cmp.user_pd_windows_unmoved) { 0 } else { 5 })
    }
    'compare' {
        $sf = Resolve-Snapshot
        $snap = Load-Snapshot $sf
        $eq = Same-Topology (Get-Config).topology @($snap.topology)
        $testList = @(Get-TestPidList $TestPids)
        $cmp = Compare-AndRepairWindows $snap @(Get-PdPids $UserPdPid $PdProcessNames $testList) $testList $false 0
        Emit ([pscustomobject]@{ snapshot = $sf; topology_equal = $eq; windows = $cmp }) $(if ($eq) { 0 } else { 5 })
    }
    'self-test' {
        $r = Invoke-SelfTest
        Emit $r $(if ($r.failed -eq 0) { 0 } else { 1 })
    }
}

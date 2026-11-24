#!/usr/bin/env python3
# 창 메뉴 회귀 측정의 승인 필요 단계 — 팝업 열기와 비클라이언트 우클릭 메시지 전달(별도 OS 창이 뜰 수 있어 사용자 부재 승인 뒤에만 retest_title_window_menu.py 가 부른다)
import subprocess
import time

HTCAPTION, WM_NCHITTEST, WM_NCRBUTTONUP = 2, 0x0084, 0x00A5
PS_MSG = r'''
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class W {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder b, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
"@
$h = [IntPtr](Get-Process -Id %(pid)d).MainWindowHandle
$r = [W]::SendMessage($h, %(msg)d, [IntPtr]%(w)d, [IntPtr]%(l)d)
Start-Sleep -Milliseconds %(wait)d
$found = New-Object System.Collections.ArrayList
[void][W]::EnumWindows({ param($x, $y); $o = 0; [void][W]::GetWindowThreadProcessId($x, [ref]$o)
  if ($o -eq %(pid)d -and [W]::IsWindowVisible($x)) { $b = New-Object Text.StringBuilder 64; [void][W]::GetClassName($x, $b, 64); [void]$found.Add($b.ToString()) }; $true }, [IntPtr]::Zero)
Write-Output ("result=" + [int64]$r + " classes=" + ($found -join ","))
'''


def send(t, msg, w, x, y, wait=300):
    t.guard.check()
    l = ((y & 0xFFFF) << 16) | (x & 0xFFFF)
    script = PS_MSG % {"pid": t.pid, "msg": msg, "w": w, "l": l, "wait": wait}
    return subprocess.run(["powershell.exe", "-NoProfile", "-Command", script], capture_output=True, text=True, timeout=60).stdout.strip()


def stage_popup(t):
    """승인 필요: 팝업은 별도 OS 창이다."""
    a = t.open_doc()
    t.call("winmenu.noteTab", doc=a)
    for name, host in (("tab", "shell.tabStrip"), ("main", "SystemMenu")):
        t.call("ui.contextMenu", id=host, open=True)
        time.sleep(0.4)
        read = t.call("ui.contextMenu", id=host)
        t.check("popup." + name + ".open", bool(read and read.get("isOpen")), [(i.get("id"), i.get("enabled")) for i in (read or {}).get("items", []) if i.get("id")])
        t.call("ui.contextMenu", id=host, open=False)
        time.sleep(0.3)
    return {}


def stage_nc(t):
    """승인 필요: 비클라이언트 우클릭 메시지 전달(실제 포인터 입력 아님). OS 메뉴(#32768)가 없어야 하고 Pd 메뉴가 열려야 한다."""
    main = t.call("window.snapshot")["main"]
    x, y = main["x"] + main["pxW"] // 2, main["y"] + 16
    hit = send(t, WM_NCHITTEST, 0, x, y, wait=0)
    t.check("nc.hit.caption", hit.startswith("result=%d " % HTCAPTION), hit)
    sent = send(t, WM_NCRBUTTONUP, HTCAPTION, x, y, wait=500)
    t.check("nc.noNativeMenu", "#32768" not in sent, sent)
    read = t.call("ui.contextMenu", id="SystemMenu")
    t.check("nc.pdMenuOpen", bool(read and read.get("isOpen")), read and read.get("isOpen"))
    t.call("ui.contextMenu", id="SystemMenu", open=False)
    return {}

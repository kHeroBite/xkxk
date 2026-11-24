#!/usr/bin/env python3
# AC-026 앱 아이콘 — 정식 기동된 자기 인스턴스의 window.icon 과 작업 표시줄 띠 화면을 남긴다(입력·클릭 없음)
"""
사용
  app_icon_taskbar_capture.py --info <격리 data-dir/remote-control.json> --owner <PD_SLOT_OWNER> \
      --away-state <pc_away.json> --out-dir <고정 결과 폴더> --monitor <DISPLAYn> --onscreen-approved '<누가·언제>'
  app_icon_taskbar_capture.py --self-test      # 앱·화면 없이 판정 함수만 시험

앱을 띄우거나 끝내지 않는다. 앱은 run_desktop_bg.sh 로만 `--multi-instance on --data-dir <절대경로 격리>`(attach 가 인자·layout-file 확인)·화면 안(승인)에 띄우되 --offscreen-guard 를 붙이지 않는다(붙이면 작업 표시줄 단추가 없다).
찍기 전 전제: 자기 기동 기록·명령줄 일치, 다른 xkxk.frameworks.ui.*(및 LEGACY 옛 이름 Pd.Poc.*) 0개, pc_away_monitor away, 유휴 300초 이상(찍기 직전 다시 확인).
화면 캡처는 작업 표시줄 띠 영역만이다. 고정된 Pd 단추는 옛 빌드를 띄울 수 있어 누르지 않는다.
결과는 고정 이름(taskbar.png·main_render.png·app_icon.json)에 덮어쓰고, 이번 시작보다 오래된 파일은 증거로 쓰지 않는다.

한계: Alt+Tab 은 시스템 조합 입력이 필요해 찍지 않는다(미확인으로 적는다). 메인 제목줄은 앱 원격 렌더 캡처다.
rc 0=원격 값 PASS+띠 캡처(판독 필요) 1=FAIL 2=준비 실패 3=전제 미충족 4=띠 캡처 없음 5=away 아님·유휴 부족·가드 불가(중단)
"""
import argparse
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)

import drag_shadow_option_input as own  # noqa: E402  자기 인스턴스 확인(OwnRig.attach)만 쓴다
import mdi_drag_app as app  # noqa: E402
import pc_away_monitor  # noqa: E402
import retest_real_input as real  # noqa: E402
import user_away_guard as away  # noqa: E402

MIN_IDLE_S = 300

BAND_PS = r"""
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PdBand {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[void][PdBand]::SetProcessDPIAware()
$h = [PdBand]::FindWindow('Shell_TrayWnd', $null)
if ($h -eq [IntPtr]::Zero) { 'NO_TASKBAR'; exit }
$r = New-Object PdBand+R
[void][PdBand]::GetWindowRect($h, [ref]$r)
$w = $r.Rt - $r.L; $hh = $r.B - $r.T
if ($w -lt 20 -or $hh -lt 20 -or [Math]::Min($w, $hh) -gt 200) { 'BAD_BAND'; exit }
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save('__OUT__', [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
'BAND {0},{1},{2},{3}' -f $r.L, $r.T, $r.Rt, $r.B
"""


def judge(icon):
    """window.icon 응답의 (합격, 사유 목록)."""
    icon = icon or {}
    bad = [key for key in ("ok", "embedded") if icon.get(key) is not True]
    for kind in ("main", "sdi"):
        rows = [w for w in icon.get("windows", []) if w.get("kind") == kind]
        if not rows:
            bad.append("%s 창 없음" % kind)
        for row in rows:
            bad += ["%s.%s" % (kind, key) for key in ("explicit", "big", "small") if not row.get(key)]
    return not bad, bad


def require_away(state):
    if pc_away_monitor.check(argparse.Namespace(state=state, max_age=60)) != 0:
        raise away.UserReturned("pc_away_monitor 가 away 가 아니다")
    idle = away.read_probe()["idle_ms"] / 1000.0
    if idle < MIN_IDLE_S:
        raise away.UserReturned("유휴 %d초 — %d초 미만이라 찍지 않는다" % (idle, MIN_IDLE_S))
    return int(idle)


def fresh(path, started):
    return path if os.path.exists(path) and os.path.getmtime(path) >= started else None


def measure(args, res):
    out = os.path.abspath(args.out_dir)
    os.makedirs(out, exist_ok=True)
    band, render = os.path.join(out, "taskbar.png"), os.path.join(out, "main_render.png")
    started = time.time()
    res["idle_s"] = require_away(args.away_state)
    rig = own.OwnRig(args, out)
    res["window_rect"], res["pid"] = rig.attach(args.info), rig.pid
    rig.need("docs.open", kind="grid", sdi="true")
    time.sleep(2)
    res["window_icon"] = rig.need("window.icon")
    res["value_pass"], res["value_missing"] = judge(res["window_icon"])
    rig.r.call("screenshot", window=0, path=app.win(render))
    rig.only_own_pd()
    res["idle_s_before_capture"] = require_away(args.away_state)
    res["band"] = real.ps_encoded(BAND_PS.replace("__OUT__", app.win(band).replace("'", "''")))
    res["taskbar_capture"] = fresh(band, started) if "BAND " in res["band"] else None
    res["main_title_render"] = fresh(render, started)
    res["alt_tab"] = "미확인(시스템 조합 입력이 필요해 찍지 않음)"
    if not res["value_pass"]:
        return 1, "값 확인 FAIL"
    if not res["taskbar_capture"]:
        return 4, "값 확인 PASS / 작업 표시줄 화면 미확인"
    return 0, "값 확인 PASS / 작업 표시줄 캡처 판독 필요 / Alt+Tab 미확인"


def self_test():
    row = {"explicit": True, "big": True, "small": True}
    good = {"ok": True, "embedded": True, "windows": [dict(row, kind="main"), dict(row, kind="sdi")]}
    cases = [
        judge(good)[0],
        not judge(dict(good, embedded=False))[0],
        not judge(dict(good, windows=[dict(row, kind="main")]))[0],
        not judge(dict(good, windows=[dict(row, kind="main"), dict(row, kind="sdi", small=False)]))[0],
        not judge(dict(good, windows=[dict(row, kind="main", explicit=False), dict(row, kind="sdi")]))[0],
        not judge(None)[0],
        fresh(os.path.abspath(__file__), time.time() + 60) is None,
        fresh(os.path.abspath(__file__), 0) is not None,
    ]
    print("self-test %s %d/%d" % ("PASS" if all(cases) else "FAIL", sum(cases), len(cases)))
    return 0 if all(cases) else 1


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for name in ("--info", "--owner", "--away-state", "--out-dir", "--monitor", "--onscreen-approved"):
        p.add_argument(name)
    p.add_argument("--self-test", action="store_true")
    args = p.parse_args()
    if args.self_test:
        return self_test()
    if not all((args.info, args.owner, args.away_state, args.out_dir, args.monitor, args.onscreen_approved)):
        print("거부: --info·--owner·--away-state·--out-dir·--monitor·--onscreen-approved 가 모두 필요하다")
        return 3
    args.user_pid = None
    res, code = {"ac": "AC-026", "approved": args.onscreen_approved}, 2
    try:
        code, res["result"] = measure(args, res)
    except (away.UserReturned, away.GuardUnavailable) as e:
        res["stopped"], code = repr(e), 5
    except own.Precondition as e:
        res["refused"], code = str(e), 3
    except (OSError, ValueError, KeyError, RuntimeError, SystemExit, subprocess.SubprocessError) as e:
        res["error"], code = repr(e), 2
    res["rc"] = code
    os.makedirs(args.out_dir, exist_ok=True)
    with open(os.path.join(args.out_dir, "app_icon.json"), "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print(json.dumps({"rc": code, "result": res.get("result"), "stop": res.get("stopped") or res.get("refused") or res.get("error")}, ensure_ascii=False))
    return code


if __name__ == "__main__":
    sys.exit(main())

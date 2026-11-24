// 데스크톱 호스트 시작 옵션 적용 — 제목줄 확장·트레이 저장값 억제와 진단 전용 창 옵션(보이는 프레임 맞춤·화면 밖 가드·주 모니터 대체·창 크롬 강제)
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.desktop;

/// <summary>호스트 인자를 창·셸 정적 옵션에 반영한다(Program.cs 에서 분리 — ADR-003 시작 옵션 책임). Avalonia 초기화 전에 한 번 부른다.</summary>
internal static class LaunchOptions
{
    public static void Apply(HostArgs host, bool background)
    {
        // Q14: 기본 on(제목줄 확장 한 줄 — lead 02:42, i0215 --main-titlebar on 캡처 확인). --main-titlebar off 로만 끈다.
        MainWindowChrome.Enabled = !string.Equals(host.Get("--main-titlebar"), "off", StringComparison.OrdinalIgnoreCase);
        ShellPlatform.SuppressSavedTray = background; // 비전경 시험 기동은 저장된 트레이 켬을 무시(사용자 알림 영역 노출 방지)
#if PD_DIAGNOSTICS
        ApplyDiagnostics(host);
#endif
    }

#if PD_DIAGNOSTICS
    /// <summary>진단 --main-size WxH(논리px): 기동 시 메인 창 클라이언트 크기. null = 기본 1400x900. Program.ConfigureMainWindow 가 표시 전에 적용한다(Q11 R2 화면 밖 캡처용).</summary>
    public static Avalonia.Size? MainSize { get; private set; }

    private static void ApplyDiagnostics(HostArgs host)
    {
        if (host.Get("--main-size") is { Length: > 0 } ms)
        {
            var p = ms.Split('x', 'X');
            MainSize = p.Length == 2 && double.TryParse(p[0], System.Globalization.CultureInfo.InvariantCulture, out var mw) && double.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var mh) && mw > 0 && mh > 0 ? new Avalonia.Size(mw, mh) : null;
            Program.Log(MainSize is { } s ? $"main-size {s.Width}x{s.Height}dip" : $"main-size invalid '{ms}' (WxH)");
        }

        // Q6 ⑤⑥ 보이는 프레임 맞춤 — 기준 재확인 전까지 기본 꺼짐, --fit-visible on 으로만 켠다(lead 03:4x)
        DesktopWindowHost.FitVisibleFrameEnabled = host.IsOn("--fit-visible");
        xkxk.frameworks.ui.shell.Docking.ChromePlatform.SetForcedStyleArg(host.Get("--chrome-style")); // 창 크롬 강제 스타일 mac|win|mars(검수용, 저장 안 함)
        DesktopWindowHost.OffscreenGuard = host.IsOn("--offscreen-guard"); // 실제 모니터로 들어오는 이동·크기 변경 차단(로그만 남김)
        ShellPlatform.SuppressSavedTray |= DesktopWindowHost.OffscreenGuard; // --offscreen-guard on 도 트레이 저장값 무시
        // 시험 창 화면 노출 방지(lead 04:1x): 윈도우 메뉴 ④⑤⑥ 의 기준 사각형을 사용자가 보지 않는 영역으로 바꾼다
        if (host.Get("--primary-override") is { Length: > 0 } po)
        {
            var p = po.Split(',', StringSplitOptions.TrimEntries);
            if (p.Length == 4 && int.TryParse(p[0], out var px) && int.TryParse(p[1], out var py) && int.TryParse(p[2], out var pw) && int.TryParse(p[3], out var ph) && pw > 0 && ph > 0)
            {
                DesktopWindowHost.PrimaryOverride = new Avalonia.PixelRect(px, py, pw, ph);
                Program.Log($"primary-override {px},{py},{pw}x{ph}");
            }
            else
            {
                Program.Log($"primary-override invalid '{po}' (x,y,w,h)");
            }
        }
    }
#endif
}

// 데스크톱 전용 플랫폼 기능 — 트레이 상주·최소화 시 트레이(MDI-21) 연결과 주 창 복원. 트레이 메뉴는 App.axaml + TrayMenuModel, 화면 캡처 방지(MDI-26)는 App.WindowHost
using Pd.Poc.Shell;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Dock.Avalonia.Controls;
using Avalonia.Platform;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Poc.Shell.Platform;

public static class ShellPlatform
{
    /// <summary>독립 창(SDI)을 OS 창으로 띄울 수 있는 플랫폼인가 — 웹(브라우저)·Android 는 아니다. '항상 메인 창 위' 같은 데스크톱 전용 설정 칸의 표시 여부에 쓴다.</summary>
    public static bool IsSdiWindowSupported { get; } = !OperatingSystem.IsBrowser() && !OperatingSystem.IsAndroid();

    /// <summary>호스트가 비전경 시험 기동(--bg-pos·--offscreen-guard on)일 때 켠다 — 저장된 트레이 켬을 이번 실행에서 무시한다.</summary>
    public static bool SuppressSavedTray { get; set; }

    /// <summary>App.axaml 에 선언된 트레이 아이콘. 데스크톱 외(Android·웹)는 플랫폼 구현이 없어 무동작이다(Avalonia PlatformManager.CreateTrayIcon=null).</summary>
    public static TrayIcon? Tray { get; private set; }

    private static WindowIcon? s_appIcon;

    /// <summary>앱 아이콘 하나 — 트레이와 데스크톱 호스트 AppWindowIcon(작업 표시줄에 뜨는 창)이 같이 쓴다.</summary>
    public static WindowIcon AppIcon => s_appIcon ??= LoadAppIcon();

    /// <summary>진입 어셈블리에 앱 아이콘 자원이 내장돼 있는가(false 면 AppIcon 은 단색 대체본).</summary>
    public static bool HasEmbeddedIcon => EmbeddedIconUri() is not null;

#if PD_DIAGNOSTICS
    /// <summary>진단 window.icon 이 읽는 OS 창 아이콘 상태 — 데스크톱 호스트 AppWindowIcon 이 채운다(null = 판정 수단 없음).</summary>
    public static Func<Window, WindowIconState?>? WindowIconProbe { get; set; }
#endif

    /// <summary>트레이 메뉴 모델(진단 원격 tray.* 도 같은 명령을 부른다).</summary>
    public static TrayMenuModel? Menu { get; private set; }

    /// <summary>메인 창에 트레이를 연결한다(데스크톱 수명에서만 불린다). 표시 규칙은 TrayMenuModel.Visible.</summary>
    public static void Attach(Application app, Window window, MainViewModel vm)
    {
        Menu = new TrayMenuModel(window, vm);
        app.DataContext = Menu; // App.axaml 트레이 바인딩의 원본
        Tray = TrayIcon.GetIcons(app)?.FirstOrDefault();
        if (Tray is not null)
        {
            Tray.Icon = AppIcon; // 진입 어셈블리(데스크톱 헤드) 앱 아이콘이라 AXAML 경로로 지정할 수 없다
        }

        window.PropertyChanged += (_, e) =>
        {
            // 트레이 아이콘이 실제로 보일 때만 최소화를 트레이로 숨긴다(아이콘 없이 숨기면 창을 되찾을 길이 없다).
            if (e.Property == Window.WindowStateProperty && Menu.Visible && window.WindowState == WindowState.Minimized)
            {
                HideMain(window, hideSdi: false); // 메인 최소화에 SDI 는 반응하지 않는다(요구 2)
            }
        };
    }

    /// <summary>
    /// 주 창 복원(트레이 '보이기'·두 번째 실행 알림 공용): 숨김이면 Show, 최소화면 Normal, activate 면 활성화.
    /// 두 번째 실행 경로(Program.ActivateMain)가 숨김 창을 Show 하지 않아 트레이에 숨은 창이 그대로 남던 결함을 함께 고친다.
    /// </summary>
    public static void RestoreMain(Window window, bool activate)
    {
        if (!window.IsVisible)
        {
            window.Show();
            RestoreOwned(window);
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (activate)
        {
            window.Activate();
        }
    }

    private static Window[] s_hiddenOwned = [];
    private static Window? s_hiddenActive;

    /// <summary>
    /// 주 창을 숨긴다(트레이 숨기기·트레이로 최소화 공용). 숨긴 창이 보이기에서 돌아오도록(D-C3-8) 숨기기 직전에 보이던 창 목록과 활성 창을 기억한다. 위치·크기는 같은 창 객체라 그대로다.
    /// SDI 는 owner 가 없어 OS 가 함께 숨기지 않는다. hideSdi=true(트레이 '숨기기')면 직접 숨기고, false(메인 최소화→트레이)면 SDI 는 그대로 둔다(메인 최소화에 SDI 무반응).
    /// 메인이 소유한 창(시스템 창 등)은 OS 가 함께 숨기므로 두 경로 모두 기억한다.
    /// </summary>
    public static void HideMain(Window window, bool hideSdi = true)
    {
        // SDI 는 OwnedWindows 에 없으므로 앱 수명의 창 목록에서 메인을 뺀 HostWindow 를 모은다
        var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows;
        var sdi = hideSdi
            ? (windows ?? (System.Collections.Generic.IReadOnlyList<Window>)Array.Empty<Window>())
                .Where(w => !ReferenceEquals(w, window) && w is HostWindow && w.IsVisible)
                .ToArray()
            : Array.Empty<Window>();
        var hidden = sdi.Concat(window.OwnedWindows.Where(w => w.IsVisible)).Distinct().ToArray();
        if (hidden.Length > 0) // 이미 숨긴 상태에서 다시 불려도(최소화→트레이 뒤 숨기기, 숨기기 두 번) 기억한 목록을 빈 목록으로 덮지 않는다
        {
            s_hiddenOwned = hidden;
            s_hiddenActive = hidden.FirstOrDefault(w => w.IsActive);
        }

        foreach (var w in sdi)
        {
            w.Hide();
        }

        window.Hide();
    }

    /// <summary>HideMain 이 기억한 SDI 를 숨김 전 순서(소유 목록 순)대로 다시 보이고, 숨기기 전 활성이던 SDI 를 마지막에 올린다.</summary>
    private static void RestoreOwned(Window main)
    {
        var list = s_hiddenOwned;
        var active = s_hiddenActive;
        s_hiddenOwned = [];
        s_hiddenActive = null;
        foreach (var w in list.Where(w => !w.IsVisible))
        {
            w.Show(); // owner 를 다시 걸지 않는다(SDI 는 소유자 없는 독립 창)
        }

        if (active is { IsVisible: true } && !Pd.Poc.Shell.Platform.SystemDialogHost.BackgroundMode)
        {
            active.Activate();
        }
    }

    /// <summary>트레이·창 공용 앱 아이콘. 데스크톱 csproj 의 ApplicationIcon 을 Avalonia 가 진입 어셈블리에
    /// !__AvaloniaDefaultWindowIcon 으로 내장하므로 그것을 읽는다. 자원이 없으면 기존 단색 BMP 로 대체한다.</summary>
    private static WindowIcon LoadAppIcon()
    {
        if (EmbeddedIconUri() is { } uri)
        {
            using var asset = AssetLoader.Open(uri);
            var copy = new MemoryStream();
            asset.CopyTo(copy);
            copy.Position = 0;
            return new WindowIcon(copy);
        }

        return new WindowIcon(new MemoryStream(CreateIconBmp()));
    }

    private static Uri? EmbeddedIconUri() =>
        Assembly.GetEntryAssembly()?.GetName().Name is { } name
        && new Uri($"avares://{name}/!__AvaloniaDefaultWindowIcon") is var uri
        && AssetLoader.Exists(uri) ? uri : null;

    /// <summary>16x16 단색(파랑 바탕 + 흰 테두리) 32bpp BMP — 앱 아이콘 자원이 없을 때의 트레이 아이콘 대체본.</summary>
    private static byte[] CreateIconBmp()
    {
        const int size = 16;
        const int pixelBytes = size * size * 4;
        var data = new byte[54 + pixelBytes];
        void Put32(int offset, int value) => BitConverter.GetBytes(value).CopyTo(data, offset);
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        Put32(2, data.Length);
        Put32(10, 54);
        Put32(14, 40);
        Put32(18, size);
        Put32(22, size);
        data[26] = 1;
        data[28] = 32;
        Put32(34, pixelBytes);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                var i = 54 + ((y * size) + x) * 4;
                data[i] = edge ? (byte)255 : (byte)180;     // B
                data[i + 1] = edge ? (byte)255 : (byte)110; // G
                data[i + 2] = edge ? (byte)255 : (byte)30;  // R
                data[i + 3] = 255;
            }
        }

        return data;
    }
}

#if PD_DIAGNOSTICS
/// <summary>OS 창 하나의 아이콘 핸들 유무(WM_GETICON·GCLP_HICON)와 작업 표시줄 관련 확장 스타일.</summary>
public sealed record WindowIconState(bool Big, bool Small, bool Small2, bool ClassBig, bool ClassSmall, bool AppWindow, bool ToolWindow);
#endif

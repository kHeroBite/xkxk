// Android 메인 액티비티 — Avalonia 뷰를 담고, 폴드 접힘/펼침·회전 때 재생성 없이 다시 배치하며, 캡처 방지 설정을 FLAG_SECURE 로 반영
using System.ComponentModel;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.android;

// ConfigurationChanges: 폴드5 접힘↔펼침(ScreenSize·SmallestScreenSize·ScreenLayout)·회전·다크 모드(UiMode)·밀도 변경을 앱이 직접 처리
// → 문서·설정 상태가 재생성으로 사라지지 않는다(Avalonia 가 새 크기로 다시 배치).
[Activity(
    Label = "별별UI",
    Theme = "@style/PdTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private MainViewModel? _hooked;
    private AndroidSafeArea? _safeArea;
    private bool _backDownConsumed;
    private StartupTouchGate? _touchGate;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        StartupMarks.Mark("activity_create_begin");
#if PD_DIAGNOSTICS
        // 진단 인자(예: --loc static): Android 에는 명령줄이 없어 인텐트 문자열 추가값 pdargs 로 받는다. 앱 초기화(base.OnCreate) 전에 넣어야 셸이 읽는다.
        // 사용: adb shell am force-stop xkxk.frameworks.ui 후 am start -n <패키지>/<MainActivity> --es pdargs "--loc static"
        // 파일(files/pdargs.txt, AndroidApp — App 초기화 전)로 이미 받았으면 그쪽을 우선한다(lead 02:3x). 인텐트는 App 초기화 뒤라
        // 자동 저장 억제·--fresh(ApplyStartupArgs 재판정)에만 효과가 있고 --loc static·--notify-panel 에는 늦다.
        if (!AndroidApp.ArgsFromFile && Intent?.GetStringExtra("pdargs") is { Length: > 0 } pdargs)
        {
            // 대입 대신 오버로드(B03) — 결과는 MainView 의 재판정과 같다
            App.ApplyStartupArgs(pdargs.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
        }
#endif
        UpdateShortSide(Resources?.Configuration); // D-C3-14: 기기 분류를 첫 레이아웃 크기가 아니라 기기 고정값으로(앱 초기화 전)
        xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostTraits = AndroidDeviceInfo.Collect(this);
        base.OnCreate(savedInstanceState);
        BackRequested += OnHostBackRequested; // base.OnCreate 가 뷰를 만든 뒤라 Avalonia 처리(TopLevel.BackRequested)보다 뒤에 불린다
        ConfigureMotion();
#if PD_DIAGNOSTICS
        // 원격 window.captureProtect.get 이 실제 창 플래그를 읽게 한다(설정값과 실제 적용 대조) — 진단 빌드 전용
        xkxk.frameworks.ui.shell.Diagnostics.Remote.RemoteCommands.CaptureProtectProbe = () => IsSecure(this);
#endif
        HookCaptureProtection();
        // C5: 화면 준비 전 터치를 받아 뒀다가 준비된 뒤 보낸다. 준비 시점을 시스템 로그 'Fully drawn' 으로도 남긴다.
        _touchGate = new StartupTouchGate(this, e => base.DispatchTouchEvent(e),
            () => (Window?.DecorView?.Width ?? 0, Window?.DecorView?.Height ?? 0), ReportFullyDrawn);
        _touchGate.Start(Content as Avalonia.Controls.Control);
        StartupMarks.Mark("activity_create_end");
    }

    public override bool DispatchTouchEvent(MotionEvent? e) =>
        (e is not null && _touchGate?.Intercept(e) == true) || base.DispatchTouchEvent(e);

    /// <summary>
    /// 메뉴·Flyout·서랍·시트가 열려 있으면 BACK 키는 그것만 닫는다. 눌림을 썼으면 뗌도 써서 뒤로 가기로 이어지지 않게 한다.
    /// 뷰로 가면 BACK 이 Esc 로 바뀌어(Avalonia 키 표) 초점 위치에 따라 닿는 곳이 달라지므로 뷰보다 먼저 받는다. 열린 것이 없을 때만 뷰로 넘긴다.
    /// Android 16 + targetSdk 36 은 Down 이 뷰까지 오고 Up 이 뷰와 무관하게 콜백을 부른다(AOSP ViewRootImpl.doOnBackKeyEvent). 키에서도 닫으면 BACK 1회가 두 번 처리돼
    /// 콜백 때 닫을 것이 없어 앱을 벗어나므로, 이때는 키를 삼키고 OnHostBackRequested 한 곳에서만 닫는다. 열린 것이 없으면 콜백이 미처리로 끝나 기존처럼 앱을 벗어난다.
    /// 출처 https://developer.android.com/about/versions/16/behavior-changes-16
    /// </summary>
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e?.KeyCode == Keycode.Back)
        {
            if (BackGoesToCallback)
            {
                return true;
            }

            if (e.Action == KeyEventActions.Down)
            {
                if (e.RepeatCount == 0)
                {
                    _backDownConsumed = DismissTop("key") || base.DispatchKeyEvent(e); // 뷰가 Esc 로 쓴 경우(Esc 창닫기)도 뗌이 뒤로 가기로 이어지지 않게 한다
                    return _backDownConsumed;
                }

                if (_backDownConsumed)
                {
                    return true;
                }
            }
            else if (e.Action == KeyEventActions.Up && _backDownConsumed)
            {
                _backDownConsumed = false;
                return true;
            }
        }

        return base.DispatchKeyEvent(e);
    }

    /// <summary>Android 16 + targetSdk 36 은 뒤로 가기 콜백이 기본으로 켜진다(매니페스트에 끄는 설정 없음).</summary>
    private bool BackGoesToCallback => OperatingSystem.IsAndroidVersionAtLeast(36) && ApplicationInfo is { } info && (int)info.TargetSdkVersion >= 36;

    /// <summary>제스처·콜백 경로의 뒤로 가기. Avalonia 쪽(시스템 Flyout·서랍)이 이미 처리했으면 그대로 두고, 아니면 메뉴까지 닫는다. 닫은 것이 없으면 기본 동작으로 넘어간다.</summary>
    private void OnHostBackRequested(object? sender, AndroidBackRequestedEventArgs e)
    {
        if (e.Handled)
        {
            Console.WriteLine("[host] back via=callback handled_by=avalonia");
            return;
        }

        e.Handled = DismissTop("callback");
    }

    private bool DismissTop(string via)
    {
        var view = Content as Avalonia.Visual
            ?? (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime)?.MainView;
        var top = view is null ? null : Avalonia.Controls.TopLevel.GetTopLevel(view);
        var dismissed = top is not null && xkxk.frameworks.ui.shell.Platform.BackDismissal.TryDismissTop(top);
        Console.WriteLine($"[host] back via={via} top={top is not null} dismissed={dismissed}");
        return dismissed;
    }

    /// <summary>폴드 접힘·펼침 등으로 가장 작은 화면 폭이 바뀌면 기기 짧은 변 값을 다시 넣는다(재생성 없이 처리하므로 여기서 받는다).</summary>
    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        UpdateShortSide(newConfig);
        xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostTraits = AndroidDeviceInfo.Collect(this); // T7: 접힘·펼침 때 기기 특성 재주입
        base.OnConfigurationChanged(newConfig);
    }

    private static void UpdateShortSide(Android.Content.Res.Configuration? config)
    {
        xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostShortSideDp = config is { SmallestScreenWidthDp: > 0 } ? config.SmallestScreenWidthDp : null;
        xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostWidthDp = config is { ScreenWidthDp: > 0 } ? config.ScreenWidthDp : null; // D-C3-19: 첫 형 판정 폭
    }

    /// <summary>
    /// 개발자 옵션 '애니메이터 지속 시간 배율'(Settings.Global.ANIMATOR_DURATION_SCALE)이 0 이면 애니메이션 줄이기로 본다.
    /// 호출 때마다 다시 읽어 실행 중 설정 변경을 반영한다. 읽기 실패·미설정(기본 1) 이면 false.
    /// 출처 https://developer.android.com/reference/android/provider/Settings.Global#ANIMATOR_DURATION_SCALE
    /// </summary>
    private void ConfigureMotion()
    {
        xkxk.frameworks.ui.Motion.MotionPreference.Provider = () =>
            Android.Provider.Settings.Global.GetFloat(ContentResolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#if PD_DIAGNOSTICS
        // 시험 강제: pdargs 안의 --reduced-motion[ on|off] (OS 설정은 바꾸지 않는다)
        if (Intent?.GetStringExtra("pdargs") is { Length: > 0 } pa)
        {
            var t = pa.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var i = System.Array.FindIndex(t, x => x.StartsWith("--reduced-motion", System.StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                var v = t[i].Contains('=') ? t[i][(t[i].IndexOf('=') + 1)..] : i + 1 < t.Length && !t[i + 1].StartsWith("--") ? t[i + 1] : "on";
                xkxk.frameworks.ui.Motion.MotionPreference.ForcedReduced = !string.Equals(v, "off", System.StringComparison.OrdinalIgnoreCase);
            }
        }
#endif
    }

    protected override void OnResume()
    {
        base.OnResume();
        HookCaptureProtection(); // 뷰모델이 OnCreate 뒤에 만들어진 경우 대비(재호출은 무해)
        if (_safeArea is null)
        {
            AndroidSafeArea.Attach(Window, a => _safeArea = a);
        }
    }

    // 레이아웃 자동 저장 즉시 반영(odev-2 App.FlushLayoutAutoSave — 동기, 변경 없으면 즉시 반환, 예외 없음).
    // Android 는 백그라운드에서 프로세스가 회수될 수 있어 일시정지·정지 두 시점 모두에서 부른다.
    protected override void OnPause()
    {
        base.OnPause();
        App.FlushLayoutAutoSave();
    }

    protected override void OnStop()
    {
        _touchGate?.DiscardPending();
        base.OnStop();
        App.FlushLayoutAutoSave();
    }

    /// <summary>
    /// 윈도우 메뉴 ⑬ 화면 캡처 방지(MainViewModel.WindowMenu.CaptureProtection)를 Android FLAG_SECURE 로 반영한다.
    /// 켜면 스크린샷·화면 녹화·최근 앱 미리보기가 검게 나온다. 데스크톱은 ShellPlatform(SetWindowDisplayAffinity)이 맡는다.
    /// </summary>
    private void HookCaptureProtection()
    {
        if (XkxkUi.Current.MainViewModel is not { } vm || ReferenceEquals(vm, _hooked))
        {
            return;
        }

        if (_hooked is not null)
        {
            _hooked.WindowMenu.PropertyChanged -= OnVmChanged;
        }

        _hooked = vm;
        vm.WindowMenu.PropertyChanged += OnVmChanged;
        ApplySecure(vm.WindowMenu.CaptureProtection);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowMenuViewModel.CaptureProtection) && sender is WindowMenuViewModel vm)
        {
            ApplySecure(vm.CaptureProtection);
        }
    }

    private void ApplySecure(bool on) => RunOnUiThread(() =>
    {
        if (on) { Window?.AddFlags(WindowManagerFlags.Secure); }
        else { Window?.ClearFlags(WindowManagerFlags.Secure); }
        Console.WriteLine($"[host] capture-protect FLAG_SECURE={(on ? "on" : "off")}");
    });

#if PD_DIAGNOSTICS
    /// <summary>현재 FLAG_SECURE 상태(원격 시험 판정용 — 실제 창 플래그를 읽는다).</summary>
    public static bool IsSecure(Activity a) =>
        a.Window?.Attributes is { } attrs && (attrs.Flags & WindowManagerFlags.Secure) != 0;
#endif
}

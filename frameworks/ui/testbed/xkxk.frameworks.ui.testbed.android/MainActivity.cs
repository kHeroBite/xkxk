// 시험대 Android 메인 액티비티 — 호스트 기반 액티비티에 Activity 특성(이름·테마·런처·구성 변경 처리)만 단다
using Android.App;
using Android.Content.PM;
using xkxk.frameworks.ui.hosting.android;

namespace xkxk.frameworks.ui.testbed.android;

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
public sealed class MainActivity : XkxkMainActivity
{
}

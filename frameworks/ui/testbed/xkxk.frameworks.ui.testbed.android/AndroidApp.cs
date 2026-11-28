// 시험대 Android 진입(Application) — 호스트 기반 클래스에 시험대 모듈·임베드 한글 글꼴을 옵션으로 넘긴다
using Android.App;
using Android.Runtime;
using xkxk.frameworks.ui.hosting.android;
using xkxk.frameworks.ui.testbed;

namespace xkxk.frameworks.ui.testbed.android;

/// <summary>시험대 Application. 앱 이름 특성은 이 실행 프로젝트가 단다(UADR-0018). 정체성(패키지·어셈블리 이름)은 xkxk.frameworks.ui.testbed 계열이다(B11-3).</summary>
[Application(Label = "별별UI")]
public sealed class AndroidApp : XkxkAndroidApplication<xkxk.frameworks.ui.views.PdApp>
{
    // 임베드 한글 폰트(fonts 폴더 Assets/Fonts 링크, 패밀리명 "Noto Sans KR") — 실행 어셈블리 이름 기준 URI
    private const string KoreanFont = "avares://xkxk.frameworks.ui.testbed.android/Assets/Fonts#Noto Sans KR";

    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AndroidHostOptions CreateHostOptions() => new()
    {
        ConfigureUi = b => b.AddModule<TestbedModule>(),
        EmbeddedFontUri = KoreanFont,
        ApplicationId = "xkxk.frameworks.ui.testbed",
        // 시험대 데이터 폴더(옛 xkxk/frameworks/ui 와 분리, UADR-0018)
        DataFolderSegments = ["xkxk", "frameworks", "ui", "testbed"],
    };
}

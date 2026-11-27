// 시험대 모듈 — 데모 창·명령·벤치·강타입 표본을 프레임워크 공개 형식만으로 등록한다
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.testbed.Samples;
using xkxk.frameworks.ui.views.Windows;

namespace xkxk.frameworks.ui.testbed;

/// <summary>
/// 별별UI 시험대 모듈이다. 호스트가 <c>AddModule&lt;TestbedModule&gt;()</c> 로 등록한다.
/// </summary>
public sealed class TestbedModule : UiModule
{
    /// <inheritdoc />
    public override string Id => TestbedIds.ModuleId;

    /// <inheritdoc />
    public override void Configure(IUiModuleContext context)
    {
        TestbedWindows.Register(context);
        TestbedCommands.Register(context);
        TestbedBench.Register(context);

        // 강타입 창 등록 표본(뷰모델·화면·인자 3형식). 원격 docs.open 은 param 을 문자열로만 넘기므로 인자 형식은 string 이다.
        context.Windows.Register<TypedSampleViewModel, TypedSampleView, string>(
            TestbedIds.TypedSample,
            o => o.TitleKey = TestbedIds.TypedSampleTitleKey);
    }
}

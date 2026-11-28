// 시험대 브라우저(WASM) 실행 진입점 — 호스트 본문은 라이브러리 BrowserHost 에 있고, 시험대 모듈(TestbedModule)만 등록해 넘긴다
using System.Runtime.Versioning;
using xkxk.frameworks.ui.hosting.browser;
using xkxk.frameworks.ui.testbed;

[assembly: SupportedOSPlatform("browser")]

namespace xkxk.frameworks.ui.testbed.browser;

internal static class Program
{
    private static Task Main(string[] args) =>
        BrowserHost.RunAsync(args, new BrowserHostOptions { ConfigureUi = b => b.AddModule<TestbedModule>() }); // 시험대 모듈(B10): 데모 창·명령·벤치는 셸이 아니라 시험대가 등록한다
}

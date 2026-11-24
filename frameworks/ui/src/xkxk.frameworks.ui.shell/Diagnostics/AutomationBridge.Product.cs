// 제품 빌드(PD_DIAGNOSTICS 없음)용 자동화 브리지 빈 껍데기 — 셸이 부르는 시그니처만 남기고 인자 해석·명령 실행 본문은 컴파일하지 않는다
#if !PD_DIAGNOSTICS
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>제품 빌드는 실행 인자 자동화를 하지 않는다. 모든 판정은 false·빈 집합이다.</summary>
public static class AutomationBridge
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    /// <summary>첫 렌더 뒤 셸이 1회 부른다. 제품 빌드는 실제 채택 렌더링 백엔드를 앱 로그에 남기기만 한다.</summary>
    public static Task RunAsync(IShellCommands shell, TopLevel top, IReadOnlyList<string> args)
    {
        Rendering.RenderInfo.LogOnce();
        return Task.CompletedTask;
    }

    public static bool HasScenarioArgs(IReadOnlyList<string> args) => false;

    public static bool IsTransientSettingArg(string name) => false;

    public static IReadOnlySet<string> TransientSettingArgs(IReadOnlyList<string> args) => Empty;
}
#endif

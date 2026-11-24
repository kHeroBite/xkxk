// 도움말 카탈로그의 requires 조건(요건 6) — 이름 → 지금 참인가. 권한은 확장 지점만 두고 PoC 에는 권한 체계가 없어 기본 허용이다
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Tour;

public static class TourConditions
{
    private const string PermissionPrefix = "permission:";

    // 이름을 더하면 카탈로그에서 바로 쓸 수 있다. 검사기가 이 표의 이름과 카탈로그를 대조한다
    private static readonly Dictionary<string, Func<bool>> Named = new(StringComparer.Ordinal)
    {
        ["bench"] = () => ViewRegistry.Current.BenchAvailable,
        ["logTool"] = () => App.MainViewModel?.WindowMenu.IsLogToolAvailable == true,
        ["gpuRestart"] = () => App.MainViewModel?.SystemMenu.SysGpuRestartPending == true,
    };

    /// <summary>권한 판정 연결 지점. 권한 체계가 들어오면 여기에 넣는다. 없으면 허용한다.</summary>
    public static Func<string, bool>? PermissionCheck { get; set; }

    /// <summary>조건이 없으면 참. 모르는 이름은 거짓이라 그 단계는 건너뛴다(검사기가 빌드 전에 잡는다).</summary>
    public static bool IsMet(string? condition)
    {
        if (string.IsNullOrEmpty(condition))
        {
            return true;
        }

        if (condition.StartsWith(PermissionPrefix, StringComparison.Ordinal))
        {
            return PermissionCheck?.Invoke(condition[PermissionPrefix.Length..]) ?? true;
        }

        return Named.TryGetValue(condition, out var check) && check();
    }
}

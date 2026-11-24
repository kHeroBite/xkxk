// 배포 채널(dev|staging|qa|prod)을 어셈블리 메타데이터에서 읽고 디버그모드 기본값을 정하는 공용 정보(UI-17)
using System.Linq;
using System.Reflection;

namespace Pd.Client.Data;

/// <summary>
/// 빌드 속성 PdChannel(Directory.Build.props, 기본 dev)이 AssemblyMetadata("PdChannel") 로 들어온 값.
/// 진단 빌드(PD_DIAGNOSTICS)와는 별개다 — 채널은 로그 상세도·디버그 표시 기본값만 정한다.
/// </summary>
public static class AppChannel
{
    /// <summary>dev | staging | qa | prod. 메타데이터가 없거나(예: 트리밍으로 빠짐) 알 수 없는 값이면 prod 로 본다(디버그 기본 꺼짐 — 안전한 쪽).</summary>
    public static string Current { get; } = Read();

    public static bool IsProd => Current == "prod";

    /// <summary>디버그모드 기본값: prod 가 아니면 켬(개발·스테이징·QA), prod 면 끔.</summary>
    public static bool DebugDefault => !IsProd;

    private static string Read()
    {
        var value = typeof(AppChannel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "PdChannel")?.Value?.Trim().ToLowerInvariant();
        return value is "dev" or "staging" or "qa" or "prod" ? value : "prod";
    }
}

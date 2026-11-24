// 진단 depth.probe 대리자 — 화면 층이 등록하는 창 RenderTransform 시점 표본 호스트
namespace Pd.Poc.Shell.Theming;

/// <summary>창 RenderTransform 시점 표본 대리자(진단 depth.probe). 화면 층이 등록하고 kind=open|page 에 대해 {"samples":[{"angleY":n}],"restored":bool} JSON 문자열을 돌려준다. 진단 빌드가 아니어도 화면 층이 등록할 수 있게 형식은 항상 존재한다.</summary>
public static class DepthProbeHost
{
    public static System.Func<string, System.Threading.Tasks.Task<string>>? Probe { get; set; }

    /// <summary>진단 motion.probe 대리자(REQ-112). 창 움직임 재생기가 등록하고 (effect, placement, hold) 에 대해 효과별 표본·restored JSON 문자열을 돌려준다(hold = 진행 지점 고정·release).</summary>
    public static System.Func<string?, string?, string?, System.Threading.Tasks.Task<string>>? MotionProbe { get; set; }
}

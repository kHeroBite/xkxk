// Android 기동 구간 표시 — 프로세스 시작 기준 경과 시간을 logcat 한 줄로 남겨 첫 프레임까지 어디에 시간을 쓰는지 가른다
using Android.OS;

namespace Pd.Poc.Droid;

/// <summary>
/// 형식은 <c>[startup] 구간 +경과ms</c> 이다. 기동 때 몇 줄만 쓰므로 제품 빌드에도 둔다(Release 제품판을 재야 하기 때문이다).
/// 기준점은 프로세스 시작이고 logcat Displayed 는 활동 시작 요청이 기준이라, 두 값을 직접 빼지 않는다.
/// </summary>
internal static class StartupMarks
{
    /// <summary>프로세스 시작 뒤 지난 시간(ms). Process.StartElapsedRealtime 은 API 24 부터라 앱 최소 버전과 같다.</summary>
    public static long ElapsedMs => SystemClock.ElapsedRealtime() - Android.OS.Process.StartElapsedRealtime;

    public static void Mark(string phase) => Console.WriteLine($"[startup] {phase} +{ElapsedMs}ms");
}

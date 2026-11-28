// Android 호스트 시작 정적 상태(내부) — 기동 인자와 파일 인자 사용 여부를 Application 과 Activity 가 공유한다(제네릭 닫힌 형식마다 갈라지지 않게 비제네릭으로 둔다)
namespace xkxk.frameworks.ui.hosting.android;

/// <summary>
/// <see cref="XkxkAndroidApplication{TApp}"/> 이 채우고 <see cref="XkxkMainActivity"/> 가 읽는 시작 상태다.
/// 제네릭 형식 안의 정적 멤버는 닫힌 형식마다 따로 생기고 비제네릭 액티비티가 참조할 수 없어 별도 형식으로 둔다.
/// </summary>
internal static class AndroidHostStartup
{
    /// <summary>셸에 넘길 기동 인자(파일 인자 반영·--gpu 제거 뒤). App 에 직접 대입하지 않고 빌더 옵션 Args 로 넘긴다(B03).</summary>
    internal static IReadOnlyList<string> StartupArgs { get; set; } = Array.Empty<string>();

#if PD_DIAGNOSTICS
    /// <summary>진단 인자를 앱 전용 파일에서 읽었는가(MainActivity 인텐트 pdargs 보다 우선 — 둘 다 오면 파일, lead 02:3x).</summary>
    internal static bool ArgsFromFile { get; set; }
#endif
}

// 앱 식별자(AppUserModelID) 지정 — 빌드 판(실행 파일 경로)이 달라도 작업표시줄에서 Pd 버튼 하나로 묶이게 한다(Windows 전용)
using System.Runtime.InteropServices;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 명시 AUMID 가 없으면 Windows 가 휴리스틱(실행 파일 기반)으로 내부 식별자를 정해 conf 마다 다른 경로의 exe 가 별개 버튼이 될 수 있다.
/// Microsoft 문서(https://learn.microsoft.com/en-us/windows/win32/shell/appids): SetCurrentProcessExplicitAppUserModelID 는
/// UI 를 표시하기 전 시작 루틴에서 불러야 한다. 형식은 CompanyName.ProductName(공백 없음·128자 이하). 값은 제품명 확정(ADR-023) 시 바꾼다.
/// </summary>
internal static class AppIdentity
{
    /// <summary>Program.Main 맨 앞(Avalonia 창 생성 전)에서 호출. Windows 외에는 아무것도 하지 않는다. 결과 HRESULT 를 호스트 로그에 남긴다.</summary>
    public static void Apply(string appId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        int hr;
        try
        {
            hr = SetCurrentProcessExplicitAppUserModelID(appId);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            HostLog.Write($"aumid={appId} failed: {ex.GetType().Name}"); // HostLog 가 '[host] ' 접두를 붙인다
            return; // 식별자 지정 실패는 기동을 막지 않는다
        }

        HostLog.Write($"aumid={appId} hr=0x{hr:X}"); // 기동 로그 한 줄: '[host] aumid=Projecd.Pd hr=0x0'(AC-13)
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

// 알림 화면 상태 기록(진단 빌드 전용) — 알림 화면이 쓰고 셸의 진단이 읽는다
#if PD_DIAGNOSTICS
namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>panel.joinProbe·perf notify_view 가 읽는 알림 화면 상태.</summary>
public static class NotificationPanelProbe
{
    /// <summary>Dock 알림 패널 안에 지금 붙어 있는 알림 화면의 영역 보고 방식(new). 붙은 화면이 없으면 null.</summary>
    public static string? LayoutMode { get; set; }

    /// <summary>지금까지 만들어진 알림 화면 수(트리 부착 여부와 무관).</summary>
    public static int CreatedCount { get; set; }

    /// <summary>지금 Dock 알림 패널에 붙어 있는 알림 화면 수(정상 1, 알림 패널 끔·틀만이면 0).</summary>
    public static int AttachedCount { get; set; }
}
#endif

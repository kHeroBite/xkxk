// 앱 모달 범위의 해제 누락 안전망·앱 로그·상태 변경 알림 — 모든 범위가 끝났는데 남은 플랫폼 차단을 풀고 그 사실을 앱 로그에 남긴다
using System;
using System.Linq;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.shell.Platform;

public static partial class ModalScope
{
    /// <summary>모든 범위가 끝났는데 남은 플랫폼 차단을 푼다(해제 누락 안전망 — 남으면 그 창이 영구 비활성). 경고 로그로 남긴다.</summary>
    private static void SweepLeaks()
    {
        if (PlatformCounts.Count == 0)
        {
            return;
        }

        var leaked = PlatformCounts.Keys.ToArray();
        PlatformCounts.Clear();
        Log($"[경고] 해제 누락 {leaked.Length}건 정리: {string.Join(", ", leaked.Select(t => (t as Window)?.Title ?? t.GetType().Name))}");
        foreach (var top in leaked)
        {
            if (top is Window window)
            {
                window.Closed -= OnBlockedClosed;
            }

            CallPlatform(top, false);
        }
    }

    /// <summary>앱 로그(데스크톱 호스트가 콘솔을 로그 파일로 받는다)에 남긴다. Debug.WriteLine 은 Release 빌드에서 빠져 해제 누락을 추적할 수 없었다.</summary>
    private static void Log(string message) => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [modal] {message}");

    private static void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log($"[경고] Changed 처리기 오류: {ex.Message}");
        }
    }
}

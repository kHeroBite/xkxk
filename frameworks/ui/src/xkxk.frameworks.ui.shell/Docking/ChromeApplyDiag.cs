// 크롬 스타일 적용 진단(진단 빌드 전용) — 전환 때 열린 SDI 틀의 실제 적용값과 그림자 리소스를 한 줄 로그로 남긴다(D-C3-9)
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.data.Logging;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// WindowChromeStyle.ApplyShadow 끝에서 부른다. 읽기만 하며 동작을 바꾸지 않는다.
/// 형식: forced=… effective=… sdi=N | [제목 frameThickness=… framePadding=… contentCorner=…]… | res thickness=… padding=… mdiShadow=n겹 sdiShadow=켜짐판정 (DWM 그림자 정책 값은 코드에 없어 기록하지 않음)
/// </summary>
internal static class ChromeApplyDiag
{
    internal static void Log()
    {
        try
        {
            var app = Application.Current;
            var main = (app?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var sdis = (app?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.Where(w => !ReferenceEquals(w, main) && w.IsVisible).ToArray() ?? [];
            var parts = sdis.Select(w =>
            {
                var b = w.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Classes.Contains("pdSdiFrame"));
                return b is null ? $"[{w.Title} noframe]" : $"[{w.Title} frameThickness={b.BorderThickness} framePadding={b.Padding}]";
            });
            string R(string key) => app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var v) ? v?.ToString() ?? "null" : "none";
            var shadow = app is not null && app.TryGetResource("PdMdiShadow", app.ActualThemeVariant, out var s) && s is BoxShadows bs ? bs.Count : -1;
            // log.tail(앱 로그 패널)은 PdLog 가 아니라 MainViewModel.LogLines 를 읽는다 — 시험이 같은 줄을 두 곳에서 볼 수 있게 함께 남긴다(진단 빌드 한정)
            var text = $"forced={ChromePlatform.ForcedStyle?.ToString() ?? "-"} effective={WindowChromeStyle.EffectiveStyle} sdi={sdis.Length} {string.Join(' ', parts)} | res thickness={R("PdSdiFrameThickness")} padding={R("PdChromeFramePadding")} contentCorner={R("PdChromeContentCorner")} mdiShadow={shadow}겹";
            PdLog.Info("chrome-apply", text);
            App.MainViewModel?.AddLog("[chrome-apply] " + text);
        }
        catch (Exception ex)
        {
            PdLog.Info("chrome-apply", "진단 실패 " + ex.Message);
        }
    }
}
#endif

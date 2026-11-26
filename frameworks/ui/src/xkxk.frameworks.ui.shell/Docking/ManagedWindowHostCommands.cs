// 브라우저(웹 관리형 창) 윈도우 메뉴 호스트 — ⑦ 독립 창 위치 초기화만 직접 실행(공식 SdiResetLayout), ④⑤⑥·⑬ 은 브라우저에서 불가, ⑧⑨⑩ 은 셸 기본 경로(odev-2 계약 IWindowHostCommands)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Linq;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 웹(DockSettings.UseManagedWindows=true) 호스트 구현. 관리형 창은 창 레이어 좌표를 쓰므로 ⑦ 의 계단식 원점은 레이어 기준(40,40).
/// ⑧⑨⑩ 은 관리형 창 특유 처리가 없어 CanExecute=false 로 두고 셸 기본 경로(문서별 SetState·CloseDocument)에 맡긴다.
/// </summary>
public sealed class ManagedWindowHostCommands : IWindowHostCommands
{
    /// <summary>웹에서 실행할 수 없는 명령의 사유(메뉴 비활성 툴팁용). 실행 가능하거나 셸이 처리하면 null.</summary>
    public static string? UnavailableReason(WindowHostCommand command) => command switch
    {
        WindowHostCommand.ResetMainWindow => Loc.Tr("chrome.host.mainWindowNa"),
        WindowHostCommand.FitSingleMonitor or WindowHostCommand.FitMultiMonitor or WindowHostCommand.MaximizeOnSelectedMonitor => Loc.Tr("chrome.host.monitorNa"),
        _ => null,
    };

    /// <summary>캡처 방지 불가 사유(⑬ 툴팁용). 현재 언어로 매번 새로 만든다(Q17 — 상수에서 속성으로 바뀜).</summary>
    public static string CaptureProtectionUnavailable => Loc.Tr("chrome.host.captureNa");

    public bool CanExecute(WindowHostCommand command) => command == WindowHostCommand.ResetSdiPositions;

    public bool Execute(WindowHostCommand command)
    {
        if (command == WindowHostCommand.ResetSdiPositions && XkxkUi.Current.Shell is WindowCommands shell)
        {
            // 공용 공식(SdiResetLayout): 레이어 (40,40) → 같은 좌상단이면 +30 → 레이어 밖이면 가운데, 크기 불변, 위치 고정 창 제외
            shell.SdiResetPositions(LayerArea);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 관리형 창 레이어의 실제 배치 영역 = ManagedWindowLayer 안 MdiLayoutPanel 크기(Dock 이 클램프에 쓰는 finalSize).
    /// 최소화 창 줄이 없으므로(U7 — MagnetMdiLayoutManager 가 최소화 창을 화면 밖에 둔다) 패널 높이를 그대로 쓴다. 패널을 못 찾으면 앱 화면(TopLevel) 전체, 화면도 없으면 빈 사각형.
    /// </summary>
    /// <summary>레이아웃 복원(D-5)용: 저장된 관리형 창 사각형을 ⑦ 과 같은 규칙(SdiResetLayout.FitInLayer)으로 현재 레이어에 맞춘다. 레이어·크기를 모르면 그대로.</summary>
    internal static Rect FitToLayer(Rect saved)
    {
        var layer = LayerArea();
        if (layer.Width <= 0 || layer.Height <= 0 || !(saved.Width > 0) || !(saved.Height > 0))
        {
            return saved;
        }

        return SdiResetLayout.FitInLayer(new Point(saved.X - layer.X, saved.Y - layer.Y), saved.Size, layer.Size)
            .Translate(new Vector(layer.X, layer.Y));
    }

    private static Rect LayerArea()
    {
        var view = Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: { } v } ? v : null;
        if (view is null || TopLevel.GetTopLevel(view) is not { } top)
        {
            return default;
        }

        var layer = top.GetVisualDescendants().OfType<ManagedWindowLayer>().FirstOrDefault(l => l.IsVisible);
        var panel = layer?.GetVisualDescendants().OfType<MdiLayoutPanel>().FirstOrDefault();
        if (panel is null || panel.Bounds.Width <= 0 || panel.Bounds.Height <= 0)
        {
            return new Rect(top.ClientSize);
        }

        return new Rect(0, 0, panel.Bounds.Width, panel.Bounds.Height); // U7: 최소화 줄이 없어 패널 전체(MagnetMdiLayoutManager 가 최소화 창을 화면 밖에 둔다)
    }

    public bool SetCaptureProtection(bool enabled) => false;

    public bool? GetCaptureProtection() => null;

    public bool MoveMainToDefault() => false; // 웹 메인 화면은 브라우저 창이다(W-01)

    public bool MoveSdiToDefault(IDockWindow window) => XkxkUi.Current.Shell is WindowCommands shell && shell.SdiPlacement.MoveOne(LayerArea, window);
}

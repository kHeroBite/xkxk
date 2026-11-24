// 제목줄 캡션 3버튼(최소·최대·닫기) 실클릭 시간 계측 — 버튼을 놓는 순간 시작해 클릭 처리 뒤 끝나는 PerfProbe 작업으로 남긴다
using xkxk.frameworks.ui.shell;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Diagnostics;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// ChromeButtons 가 창마다 하나 들고 Attach/Detach 한다. 처리기는 기능 버튼을 품은 가장 가까운 Grid(제목줄 행)에 단다 —
/// 캡션 3버튼은 기능 버튼 줄(PdCaptionStrip)과 다른 칸(Grid#PdCaptionOps, 크롬 스타일별 왼쪽/오른쪽)에 있으므로 줄이 아니라 행 단위로 받아야 한다.
/// MDI(Dock 템플릿 PART_*)와 SDI(Btn*) 이름을 모두 안다.
/// </summary>
internal sealed class CaptionClickTiming
{
    private Panel? _row;

    public void Attach(Control buttons)
    {
        var row = buttons.FindAncestorOfType<Grid>();
        if (ReferenceEquals(row, _row))
        {
            return;
        }

        Detach();
        _row = row;
        _row?.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void Detach()
    {
        _row?.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _row = null;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var name = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true)?.Name switch
        {
            "PART_MinimizeButton" or "BtnMin" => "chrome.min",
            "PART_MaximizeRestoreButton" or "BtnMax" => "chrome.max",
            "PART_CloseButton" or "BtnClose" => "chrome.close",
            _ => null,
        };
        if (name is not null)
        {
            var op = PerfProbe.Current.TimeOperation(name);
            Dispatcher.UIThread.Post(op.Dispose); // 버튼 Click 처리(Dock 내부) 뒤에 종료
        }
    }
}

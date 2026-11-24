// 위치 고정(잠금)된 MDI 문서 창의 제목줄 끌기 이동을 막는 가드 — 창 부모(MdiLayoutPanel)에 터널 처리기를 걸어 창 자신의 끌기보다 먼저 받는다
using xkxk.frameworks.ui.shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

/// <summary>ChromeButtons 가 창마다 하나 들고 Hook(mdi) 로 붙인다. 잠금 여부·활성화는 생성자에서 받은 함수로 읽는다(제목줄 버튼 상태와 같은 정본).</summary>
internal sealed class MdiLockDragGuard
{
    private readonly Func<bool> _isLocked;
    private readonly Action _activate;
    private MdiDocumentWindow? _mdi;
    private InputElement? _mdiParent;

    public MdiLockDragGuard(Func<bool> isLocked, Action activate)
    {
        _isLocked = isLocked;
        _activate = activate;
    }

    /// <summary>MDI 창의 부모에 터널 처리기를 건다(null 이면 뗀다). 같은 창·부모면 아무 일도 하지 않는다.</summary>
    public void Hook(MdiDocumentWindow? mdi)
    {
        var parent = mdi?.GetVisualParent() as InputElement;
        if (ReferenceEquals(mdi, _mdi) && ReferenceEquals(parent, _mdiParent))
        {
            return;
        }

        _mdiParent?.RemoveHandler(InputElement.PointerPressedEvent, OnMdiPressed);
        (_mdi, _mdiParent) = (mdi, parent);
        _mdiParent?.AddHandler(InputElement.PointerPressedEvent, OnMdiPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>위치 고정이면 제목줄(버튼 제외) 왼쪽 누름을 소비해 끌기 이동을 막는다. 최소·최대·닫기 등 버튼은 통과.</summary>
    private void OnMdiPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isLocked() || _mdi is null || !e.GetCurrentPoint(_mdi).Properties.IsLeftButtonPressed)
        {
            return;
        }

        for (var v = e.Source as Visual; v is not null && !ReferenceEquals(v, _mdi); v = v.GetVisualParent())
        {
            if (v is Button button)
            {
                // 옵션(IDE 조사 결론용): 위치 고정 중 최소·최대도 막기. 닫기·크롬 버튼은 항상 통과.
                if (WindowCommands.LockBlocksMinMax && button.Name is "PART_MinimizeButton" or "PART_MaximizeRestoreButton")
                {
                    e.Handled = true;
                }

                return;
            }

            if (v is Control { Name: "PART_Header" })
            {
                e.Handled = true;
                _activate(); // 끌기만 막고 클릭 활성화는 유지
                return;
            }
        }
    }
}

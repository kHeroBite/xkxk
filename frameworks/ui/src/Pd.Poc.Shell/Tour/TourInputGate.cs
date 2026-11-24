// 도움말 투어 입력 차단(요건 6) — 창 내용 루트를 비활성으로 격리하고 키를 터널에서 먼저 받는다. Esc 는 어떤 처리기보다 먼저 투어를 끝낸다
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Pd.Poc.Shell.Tour;

/// <summary>
/// 창(TopLevel) 하나의 입력 격리 상태. 오버레이만으로는 UIA Invoke 가 아래 컨트롤에 닿으므로 내용 루트의 IsEnabled 를 끈다
/// (비활성 요소는 포인터 히트테스트·키보드 포커스·UIA Invoke 에서 모두 빠진다). 해제는 시작 전 값·포커스를 그대로 되돌린다.
/// </summary>
public sealed class TourInputGate
{
    private readonly TopLevel _top;
    private Control? _root;
    private bool _rootWasEnabled;
    private IInputElement? _focused;

    public TourInputGate(TopLevel top) => _top = top;

    /// <summary>
    /// TopLevel 클래스 처리기는 등록 순서대로 불린다. 설정·창 단축키 처리기(App 초기화에서 등록)보다 먼저 받아야 Ctrl+0·Ctrl+F4 등이 투어 중 새지 않으므로
    /// 셸 어셈블리 적재 시점(모듈 초기화)에 등록한다. 등록 자체는 플랫폼에 의존하지 않는다.
    /// </summary>
#pragma warning disable CA2255 // 앱 내부 어셈블리이고 등록 순서를 보장할 다른 지점이 없다
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void InstallClassHandlers()
    {
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel);
        InputElement.KeyUpEvent.AddClassHandler<TopLevel>(OnKeyUp, RoutingStrategies.Tunnel);
    }

    /// <summary>창 내용 루트(오버레이 층 아래 전부를 담는 요소). 격리 대상이다.</summary>
    public static Control? ContentRootOf(TopLevel top) =>
        top.GetVisualDescendants().OfType<VisualLayerManager>().FirstOrDefault()?.Child as Control;

    /// <summary>열린 메뉴·플라이아웃을 닫고 내용 루트를 비활성으로 만든다. 루트를 못 찾으면 false.</summary>
    public bool Capture()
    {
        if (ContentRootOf(_top) is not { } root)
        {
            return false;
        }

        CloseTransientUi(_top);
        _root = root;
        _rootWasEnabled = root.IsEnabled;
        _focused = _top.FocusManager?.GetFocusedElement();
        root.IsEnabled = false;
        return true;
    }

    /// <summary>시작 전 IsEnabled·포커스를 되돌린다. 포커스가 없었으면 없는 상태로 돌린다.</summary>
    public void Release()
    {
        if (_root is null)
        {
            return;
        }

        _root.IsEnabled = _rootWasEnabled;
        _root = null;
        if (_focused is not null)
        {
            _focused.Focus();
        }
        else
        {
            _top.FocusManager?.Focus(null);
        }

        _focused = null;
    }

    /// <summary>열린 팝업·메뉴·플라이아웃을 닫는다. 오버레이와 같은 층이라 남으면 투어 위에 뜬다.</summary>
    private static void CloseTransientUi(TopLevel top)
    {
        foreach (var visual in top.GetVisualDescendants())
        {
            switch (visual)
            {
                case Popup { IsOpen: true } popup:
                    popup.IsOpen = false;
                    break;
                case Menu { IsOpen: true } menu:
                    menu.Close();
                    break;
                case Button { Flyout.IsOpen: true } button:
                    button.Flyout.Hide();
                    break;
            }

            if (visual is Control control)
            {
                if (control.ContextMenu is { IsOpen: true } contextMenu)
                {
                    contextMenu.Close();
                }

                if (control.ContextFlyout is { IsOpen: true } contextFlyout)
                {
                    contextFlyout.Hide();
                }

                if (FlyoutBase.GetAttachedFlyout(control) is { IsOpen: true } attached)
                {
                    attached.Hide();
                }
            }
        }
    }

    private static void OnKeyDown(TopLevel top, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (TourController.SessionOf(top) is not { } session)
        {
            if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None && top is not PopupRoot && TourController.StartFrom(top))
            {
                e.Handled = true; // 그 창의 투어를 연다(모르는 창이면 그대로 통과)
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                session.End();
                e.Handled = true;
                return;
            case Key.Enter or Key.Space when e.KeyModifiers == KeyModifiers.None && session.OverlayHasFocus:
                return; // 초점 버튼이 직접 처리한다
            case Key.Tab when (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None:
                session.MoveFocus((e.KeyModifiers & KeyModifiers.Shift) != 0);
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.None:
                session.Previous();
                break;
            case Key.Right when e.KeyModifiers == KeyModifiers.None:
                session.Next();
                break;
            case Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End when e.KeyModifiers == KeyModifiers.None:
                session.ScrollLayer(e.Key); // 여기서 막으면 ScrollViewer 까지 가지 못하므로 직접 넘긴다
                break;
        }

        e.Handled = true; // 그 밖의 키(창 메뉴·단축키·접근 키 포함)는 본창으로 보내지 않는다
    }

    private static void OnKeyUp(TopLevel top, KeyEventArgs e)
    {
        if (!e.Handled && TourController.SessionOf(top) is { } session
            && !(e.Key is Key.Enter or Key.Space && e.KeyModifiers == KeyModifiers.None && session.OverlayHasFocus))
        {
            e.Handled = true; // Alt 떼기로 메뉴가 열리는 접근 키 동작까지 막는다
        }
    }
}

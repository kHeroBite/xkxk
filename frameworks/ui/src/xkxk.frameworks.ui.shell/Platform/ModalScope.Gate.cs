// 앱 모달 입력 관문 — 모달 범위가 열린 동안 모달 밖 포인터·휠·키·글자 입력을 원시 입력 단계(라우팅 전)와 TopLevel 터널 단계 두 곳에서 막고, 막힌 창을 누르면 모달을 앞으로 가져온다
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Platform;

public static partial class ModalScope
{
    private static bool s_gateInstalled;

    /// <summary>
    /// 앱 시작 때 1회 — 입력 관문 등록. 반드시 SettingsInput.Install 보다 먼저 불러야(App.cs) 같은 터널 단계의 Ctrl+휠·Ctrl+0 처리기보다 먼저 돈다.
    /// 클래스 처리기는 등록 순서대로 불리고, 인스턴스 처리기(KeyBinding·FittedFlyout Esc 등)보다 먼저다.
    /// </summary>
    public static void InstallGate()
    {
        if (s_gateInstalled)
        {
            return;
        }

        s_gateInstalled = true;
        EnsureHooks();
        InstallRawGate(); // 1차는 막힌 창·메인 뷰 히트 테스트 차단(ModalScope.RawGate.cs, 범위 시작 때 적용). 아래 터널 처리기는 2차(키·원격 합성 입력·허용 판정)
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(OnPointerPressed, RoutingStrategies.Tunnel);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, e) => GatePointer(e), RoutingStrategies.Tunnel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, e) => GatePointer(e), RoutingStrategies.Tunnel);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, e) => GateKey(e), RoutingStrategies.Tunnel);
        InputElement.KeyUpEvent.AddClassHandler<TopLevel>((_, e) => GateKey(e), RoutingStrategies.Tunnel);
        InputElement.TextInputEvent.AddClassHandler<TopLevel>((_, e) => GateRouted(e), RoutingStrategies.Tunnel);
        // 제스처(탭·두 번 탭·길게 누름)는 누름·뗌 경로 완료 뒤 따로 올라오므로 원천 요소의 클래스 처리기에서 먼저 막는다
        InputElement.TappedEvent.AddClassHandler<InputElement>((_, e) => GateRouted(e), RoutingStrategies.Bubble);
        InputElement.DoubleTappedEvent.AddClassHandler<InputElement>((_, e) => GateRouted(e), RoutingStrategies.Bubble);
        InputElement.HoldingEvent.AddClassHandler<InputElement>((_, e) => GateRouted(e), RoutingStrategies.Bubble);
    }

    /// <summary>
    /// 이 시각 요소가 맨 위 모달 범위 안인가(입력 허용). 모달이 없으면 true.
    /// 허용: 모달 창·Flyout 내용 하위, 그 안에서 연 팝업(논리 부모 사슬로 이어짐), 모달 창이 소유한 창(테마 편집기 등)과 그 하위.
    /// </summary>
    public static bool IsAllowed(Visual? source)
    {
        if (Stack.Count == 0)
        {
            return true;
        }

        if (source is null)
        {
            return false;
        }

        var scope = Stack[^1];
        foreach (var v in source.GetSelfAndVisualAncestors())
        {
            if (ReferenceEquals(v, scope.Modal) || (v is TopLevel top && top is not PopupRoot && IsTopLevelWithin(scope, top)))
            {
                return true;
            }
        }

        // 팝업(콤보 목록·문맥 메뉴)은 시각 트리가 끊기므로 논리 부모(Popup → 그 팝업을 연 컨트롤) 사슬로 잇는다
        if (source is ILogical logical)
        {
            foreach (var l in logical.GetSelfAndLogicalAncestors())
            {
                if (ReferenceEquals(l, scope.Modal) || (l is TopLevel top && top is not PopupRoot && IsTopLevelWithin(scope, top)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>맨 위 모달을 앞으로 가져온다(막힌 창 누름·트레이 실행). 데스크톱 모달 창만 해당. 비전경 시험 모드면 활성화하지 않는다. 가져왔으면 true.</summary>
    public static bool ActivateModal()
    {
        if (Stack.Count == 0 || Stack[^1].Modal is not Window window || !window.IsVisible)
        {
            return false;
        }

        if (!SystemDialogHost.BackgroundMode)
        {
            window.Activate();
        }

        return true;
    }

    /// <summary>top 이 범위 모달 자신이거나 그 소유 사슬 아래 창인가. 팝업 루트는 시각 트리만으로 소속을 알 수 없어 false(IsAllowed 가 논리 사슬로 가른다).</summary>
    private static bool IsTopLevelWithin(Scope scope, TopLevel top)
    {
        var modalTop = scope.Modal as TopLevel ?? TopLevel.GetTopLevel(scope.Modal);
        if (modalTop is null)
        {
            return false;
        }

        if (ReferenceEquals(top, modalTop))
        {
            return scope.Modal is TopLevel; // 단일 뷰는 같은 TopLevel 이어도 통째 허용이 아니다(내용 단위)
        }

        for (var owner = (top as Window)?.Owner; owner is not null; owner = (owner as Window)?.Owner)
        {
            if (ReferenceEquals(owner, modalTop))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnPointerPressed(TopLevel sender, PointerPressedEventArgs e)
    {
        if (GatePointer(e))
        {
            ActivateModal(); // OS 관례 — 막힌 창을 누르면 모달이 앞으로 온다
        }
    }

    /// <summary>모달 밖 포인터 입력이면 Handled 로 막는다. 막았으면 true.</summary>
    private static bool GatePointer(RoutedEventArgs e) => GateRouted(e);

    /// <summary>Esc 는 모달을 닫는 경로(FittedFlyout·창 닫기)라 통과시킨다. 나머지 모달 밖 키는 막는다.</summary>
    private static void GateKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            return;
        }

        GateRouted(e);
    }

    private static bool GateRouted(RoutedEventArgs e)
    {
        if (Stack.Count == 0 || e.Handled || IsAllowed(e.Source as Visual))
        {
            return false;
        }

        e.Handled = true;
        return true;
    }
}

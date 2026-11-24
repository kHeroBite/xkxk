// 윈도우 메뉴 ② Esc 창닫기 입력 처리 — 메인 창·SDI 창(별도 TopLevel) 공통으로 Esc 를 받아, 입력 컨트롤이 쓰지 않은 Esc 만 활성 문서 창 닫기로 넘긴다
using xkxk.frameworks.ui.shell;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

public static class EscCloseInput
{
    private static MainViewModel? _vm;

    /// <summary>앱 시작 시 1회. 모든 TopLevel 의 KeyDown 을 버블 단계에서 받는다 — 입력란·콤보·메뉴가 먼저 처리(Handled)한 Esc 는 오지 않는다.</summary>
    public static void Install(MainViewModel vm)
    {
        if (_vm is not null)
        {
            _vm = vm;
            return;
        }

        _vm = vm;
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Bubble);
        xkxk.frameworks.ui.views.Input.WindowKeyGestures.Install(vm); // U9 창 관리 단축키·제목줄 끌어 복원·로고 더블클릭(메인·SDI 공통 입력 계층)
    }

    private static void OnKeyDown(TopLevel top, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || _vm is not { WindowMenu.EscCloseEnabled: true } vm)
        {
            return;
        }

        // 입력 컨트롤 안에서 누른 Esc 는 가로채지 않는다(선택 해제·편집 취소 등 컨트롤 고유 동작 보호).
        var source = e.Source as Visual;
        if (IsInEditor(top.FocusManager?.GetFocusedElement() as Visual) || IsInEditor(source))
        {
            return;
        }

        // 문서 안에서 눌렀으면 그 문서(SDI 창 포함), 아니면 활성 문서.
        var doc = source?.GetSelfAndVisualAncestors().OfType<ZoomDecorator>().Select(z => z.Tag).OfType<PocDocument>().FirstOrDefault();
        if (vm.TryEscClose(doc))
        {
            e.Handled = true;
        }
    }

    private static bool IsInEditor(Visual? v) => v is not null && v.GetSelfAndVisualAncestors().Any(a => a is TextBox
        or ComboBox { IsDropDownOpen: true } or NumericUpDown or AutoCompleteBox or MenuItem or Slider);
}

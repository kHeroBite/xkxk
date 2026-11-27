// 강타입 표본 창의 코드 전용 화면(axaml 없음)
using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.testbed.Samples;

/// <summary><see cref="TypedSampleViewModel"/> 의 문구를 보여 주는 코드 전용 화면이다.</summary>
public sealed class TypedSampleView : UserControl
{
    /// <summary>화면을 만든다. 문구는 DataContext 의 뷰모델에서 읽는다.</summary>
    public TypedSampleView()
    {
        var text = new TextBlock();
        AutomationProperties.SetAutomationId(text, "testbed.typed.message");
        text.Text = "Typed sample";
        Content = text;

        // 뷰모델이 연결되면 문구를 따라가고, 열림 초기화 뒤 속성 알림으로 갱신한다.
        // 뷰모델이 바뀌거나 화면이 트리에서 떨어지면 구독을 해제한다.
        PropertyChangedEventHandler onChanged = (_, _) =>
        {
            if (_vm is not null)
            {
                text.Text = _vm.Message;
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.PropertyChanged -= onChanged;
            }

            _vm = DataContext as TypedSampleViewModel;
            if (_vm is null)
            {
                return;
            }

            text.Text = _vm.Message;
            _vm.PropertyChanged += onChanged;
        };
        DetachedFromLogicalTree += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.PropertyChanged -= onChanged;
                _vm = null;
            }
        };
    }

    private TypedSampleViewModel? _vm;
}

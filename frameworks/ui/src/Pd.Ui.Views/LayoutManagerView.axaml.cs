// 레이아웃 저장·관리 패널 코드비하인드 — 화면 로드만 담당(동작은 LayoutManagerViewModel)
using Pd.Poc.Shell;
using Avalonia.Controls;

namespace Pd.Ui.Views;

public partial class LayoutManagerView : UserControl
{
    public LayoutManagerView()
    {
        InitializeComponent();
    }
}

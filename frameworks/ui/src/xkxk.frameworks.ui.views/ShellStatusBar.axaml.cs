// 셸 상태바 코드비하인드 — 화면 로드와 알림 배지 뷰모델 연결만 담당(동작은 MainViewModel·NotifyBadgeViewModel)
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.ViewModels;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.views;

public partial class ShellStatusBar : UserControl
{
    public ShellStatusBar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            // 배지 버튼만 별도 뷰모델을 쓴다(나머지 상태바 항목은 MainViewModel 상속)
            if (DataContext is MainViewModel main && this.FindControl<Button>("NotifyBadge") is { DataContext: not NotifyBadgeViewModel } badge)
            {
                badge.DataContext = new NotifyBadgeViewModel(main.Factory, main.Commands, main.Compact);
            }
        };
    }
}

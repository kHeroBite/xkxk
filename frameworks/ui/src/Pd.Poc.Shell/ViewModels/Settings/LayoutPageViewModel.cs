// 설정 창 '레이아웃' 페이지 뷰모델(요건 7) — 현재 화면 수동 저장·화면 백업/복원 관리(템플릿·백업 포함) 진입. 저장되는 설정값은 없어 되돌릴 값도 없다
using CommunityToolkit.Mvvm.Input;

namespace Pd.Poc.Shell.ViewModels.Settings;

public sealed class LayoutPageViewModel : SettingsPageViewModel
{
    public LayoutPageViewModel(MainViewModel main)
        : base(main)
    {
    }

    /// <summary>현재 화면 수동 저장(메인 창의 레이아웃 패널을 저장 화면으로 연다).</summary>
    public RelayCommand SaveCurrentCommand => Main.WindowMenu.SaveCurrentLayoutCommand;

    /// <summary>화면 백업/복원 관리(메인 창의 레이아웃 패널을 목록 화면으로 연다).</summary>
    public RelayCommand ManageCommand => Main.WindowMenu.ManageLayoutsCommand;

    public override void ResetToDefaults()
    {
    }
}

// 하단 로그 도구 패널 모델 — 메인 뷰모델(Shell)을 들고 있으며 화면은 App.axaml DataTemplate
using Dock.Model.Mvvm.Controls;

namespace Pd.Poc.Shell.ViewModels;

/// <summary>하단 로그 도구 패널. 화면은 App.axaml DataTemplate.</summary>
public sealed class LogTool : Tool
{
    public LogTool(MainViewModel shell) => Shell = shell;

    public MainViewModel Shell { get; }
}

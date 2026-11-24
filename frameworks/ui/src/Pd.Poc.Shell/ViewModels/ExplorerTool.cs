// 좌측 탐색기 도구 패널 모델 — 메인 뷰모델(Shell)을 들고 있으며 화면은 App.axaml DataTemplate. 항목 별 단추(즐겨찾기 넣기·빼기) 명령 포함
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Pd.Client.Data.Navigation;

namespace Pd.Poc.Shell.ViewModels;

/// <summary>좌측 탐색기 도구 패널. 화면은 App.axaml DataTemplate.</summary>
public sealed class ExplorerTool : Tool
{
    public ExplorerTool(MainViewModel shell)
    {
        Shell = shell;
        // 별 단추(22 §4 넣기·빼기 경로): 인자는 PoC 문서 종류 이름(Grid·Gantt·Form) — 그 종류의 대표 화면 ID 를 즐겨찾기에 넣거나 뺀다
        ToggleFavoriteCommand = new AsyncRelayCommand<string>(kind => shell.FavoriteBar.ToggleScreenAsync(MenuIconCatalog.ScreenIdOfPocKind(kind)));
    }

    public MainViewModel Shell { get; }

    /// <summary>탐색기 항목 별 단추 명령(CommandParameter = 문서 종류 이름).</summary>
    public AsyncRelayCommand<string> ToggleFavoriteCommand { get; }
}

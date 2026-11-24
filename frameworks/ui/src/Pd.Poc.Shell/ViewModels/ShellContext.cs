// 메인 뷰모델의 하위 뷰모델(단일 문서 모드·부가 기능·설정 패널·시스템 메뉴·윈도우 메뉴)이 함께 쓰는 셸 의존 묶음
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels;

/// <summary>하위 뷰모델에 생성자로 넘기는 공유 의존. 메인 뷰모델이 만든 셸 객체를 그대로 가리킨다(새 상태를 만들지 않는다).</summary>
public sealed class ShellContext
{
    public ShellContext(MainViewModel main) => Main = main;

    /// <summary>메인 뷰모델(활성 문서·이름 변경 상태·속성 변경 알림).</summary>
    public MainViewModel Main { get; }

    public WindowCommands Commands => Main.Commands;

    public PocDockFactory Factory => Main.Factory;

    public ExplorerTool Explorer => Main.Explorer;

    /// <summary>UI 설정 서비스(UiSettings.Current).</summary>
    public UiSettings Settings => Main.Settings;

    public void AddLog(string line) => Main.AddLog(line);
}

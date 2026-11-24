// 셸이 화면 층을 참조하지 않고 화면을 만들고 여는 계약 — 구현은 xkxk.frameworks.ui.views 가 기동 때 등록한다
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell;

/// <summary>셸(컨트롤 층)이 화면 형식을 모른 채 쓰는 화면 연결점.</summary>
public interface IViewRegistry
{
    /// <summary>메인 화면을 만든다.</summary>
    Control CreateMainView(MainViewModel vm);

    /// <summary>문서 종류별 뷰 생성기 기본값.</summary>
    IDocumentViewFactory DocumentViews { get; }

    /// <summary>시스템 메뉴 창 제공자.</summary>
    ISystemViews SystemViews { get; }

    /// <summary>화면 층의 전역 입력 처리를 설치한다(Esc 창 닫기).</summary>
    void InstallInput(MainViewModel vm);

    /// <summary>알림 패널 DataTemplate 을 앱 수준에 1회 등록한다.</summary>
    void EnsureNotificationTemplate();

    /// <summary>테마 편집기 창을 연다.</summary>
    void OpenThemeEditor();

    /// <summary>비활성 창 크롬 스타일을 새로 만든다.</summary>
    IStyle CreateInactiveChromeStyle();

    /// <summary>Window 없는 플랫폼용 PinnedDock 테마 사본을 만든다.</summary>
    IResourceProvider CreatePinnedDockNoWindowTheme();

    /// <summary>패널 내용의 화면 값(목록 선택·스크롤)을 지금 기억하고, 트리를 옮긴 뒤 되돌리는 동작을 돌려준다. 돌려받은 동작은 배치가 끝날 때까지 나눠 다시 맞춘다.</summary>
    Action CapturePanelViewState(IReadOnlyList<Visual> roots);

    /// <summary>제목줄 버튼 묶음의 형식(진단 스타일 선택자용).</summary>
    Type ChromeButtonsType { get; }

    /// <summary>화면 층이 가진 진단 명령을 등록한다. 제품 빌드에는 명령이 없다.</summary>
    void RegisterRemote(RemoteCommandRouter router);

    /// <summary>벤치 문서가 포함된 빌드인가.</summary>
    bool BenchAvailable { get; }

    /// <summary>벤치 문서를 연다. 벤치 미포함 빌드에서는 아무것도 하지 않는다.</summary>
    void OpenBench(string target);
}

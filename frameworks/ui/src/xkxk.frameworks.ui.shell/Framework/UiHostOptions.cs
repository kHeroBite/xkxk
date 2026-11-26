// 호스트가 기동 전에 주는 값 묶음 — 빌더 옵션 Host 로 받아 UseXkxkUi 가 App 에 1회 연결한다(B03, UADR-0010)
using System;
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Layout;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 호스트(데스크톱·웹·Android)가 기동 전에 주는 값이다. <see cref="XkxkUiBuilderOptions.Host"/> 로 받는다.
/// </summary>
/// <remarks>
/// 모든 속성의 기본값은 null 이며, null 은 "호스트가 주지 않음"을 뜻한다. 그때는 각 속성 설명에 적힌 기본 동작을 쓴다.
/// 확장 규칙(UADR-0009): 새 호스트 값은 기본값이 null 인 속성을 추가하는 방식으로만 넓힌다. 기존 속성의 의미는 바꾸지 않는다.
/// 연결은 UseXkxkUi 때 1회이며, 그 뒤에 바꾼 값은 반영하지 않는다.
/// </remarks>
public sealed class UiHostOptions
{
    /// <summary>빈 묶음(모든 속성 null)을 만든다.</summary>
    public UiHostOptions()
    {
    }

    /// <summary>레이아웃 저장소(데스크톱 파일 / 브라우저 localStorage). null 이면 메모리 저장소(종료 시 소멸)를 쓴다.</summary>
    public ILayoutStore? LayoutStore { get; set; }

    /// <summary>UI 설정(테마·글자 크기·폰트) 저장소(데스크톱 파일 / 브라우저 localStorage). null 이면 UI 설정을 저장하지 않는다.</summary>
    public ILayoutStore? SettingsStore { get; set; }

    /// <summary>개인 레이아웃 저장소(UI-11, 데스크톱·Android 폴더 / 브라우저 localStorage). null 이면 메모리(종료 시 소멸)를 쓴다.</summary>
    public IKeyValueStore? LayoutLibraryStore { get; set; }

    /// <summary>데스크톱 메인 창 생성 직후 호출(화면 밖 배치·위치 정상화 등 호스트 처리). null 이면 호출하지 않는다.</summary>
    public Action<Window>? ConfigureMainWindow { get; set; }

    /// <summary>창의 보이는 프레임(물리 px). 데스크톱 호스트가 준다. null 이면 창 틀 크기로 본다(알림 패널 비켜 놓기용).</summary>
    public Func<Window, PixelRect?>? VisibleFrameOf { get; set; }

    /// <summary>데스크톱 SDI(플로팅) 호스트 창 생성기. 같은 UI 스레드에서 생성된다. null 이면 Dock 기본 HostWindow 를 쓴다.</summary>
    public Func<IHostWindow>? CreateHostWindow { get; set; }

    /// <summary>윈도우 메뉴 ④~⑩·⑬ 실행 호스트(데스크톱 / 브라우저 관리형 창). null 이면 셸 기본 경로만 쓴다.</summary>
    public IWindowHostCommands? WindowHost { get; set; }

    /// <summary>
    /// 모니터 정보 공급자(데스크톱 호스트가 준다). 기본값은 null 이며, null 이면 모니터 정보가 없다(모니터 메뉴는 빈 목록, HasMonitors=false — 웹·Android).
    /// UseXkxkUi 때 비 null 이면 <see cref="IUiAppContext.Monitors"/> 에 1회 연결한다. null 이면 이미 연결된 공급자를 바꾸지 않는다(B05).
    /// </summary>
    public IMonitorProvider? MonitorProvider { get; set; }
}

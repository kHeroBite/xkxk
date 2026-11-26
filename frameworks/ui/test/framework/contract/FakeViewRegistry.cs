// 계약 시험 C26 용 IViewRegistry 손 대역 — 13멤버를 직접 구현(리플렉션·DispatchProxy 없음, 멤버가 늘면 컴파일이 깨져 동결 검출기 역할)
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>
/// 등록·조회 위임만 확인하는 대역이다. 화면을 만들지 않으므로 모든 멤버는 <see cref="NotSupportedException"/> 을 던진다.
/// 인스턴스 동일성(ReferenceEquals)만 시험에 쓴다.
/// </summary>
internal sealed class FakeViewRegistry : IViewRegistry
{
    private const string Message = "FakeViewRegistry 는 등록 위임 확인용 대역이라 이 멤버를 지원하지 않는다";

    public IDocumentViewFactory DocumentViews => throw new NotSupportedException(Message);

    public ISystemViews SystemViews => throw new NotSupportedException(Message);

    public Type ChromeButtonsType => throw new NotSupportedException(Message);

    public bool BenchAvailable => throw new NotSupportedException(Message);

    public Control CreateMainView(MainViewModel vm) => throw new NotSupportedException(Message);

    public void InstallInput(MainViewModel vm) => throw new NotSupportedException(Message);

    public void EnsureNotificationTemplate() => throw new NotSupportedException(Message);

    public void OpenThemeEditor() => throw new NotSupportedException(Message);

    public IStyle CreateInactiveChromeStyle() => throw new NotSupportedException(Message);

    public IResourceProvider CreatePinnedDockNoWindowTheme() => throw new NotSupportedException(Message);

    public Action CapturePanelViewState(IReadOnlyList<Visual> roots) => throw new NotSupportedException(Message);

    public void RegisterRemote(RemoteCommandRouter router) => throw new NotSupportedException(Message);

    public void OpenBench(string target) => throw new NotSupportedException(Message);
}

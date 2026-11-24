// Dock 문서 뷰 재활용 템플릿에 창 사이 이동 전 레이아웃 비우기를 더한 것(MDI 창 본문·탭 KeepAlive 공용)
using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Controls.Templates;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// ControlRecyclingDataTemplate 와 같고 Build 직전에 CrossRootLayoutFlush.BeforeFetch 만 부른다.
/// 기반의 Build 는 virtual 이 아니므로 인터페이스를 다시 구현한다. ContentPresenter.CreateChild 는 IRecyclingDataTemplate·IDataTemplate 인터페이스로 Build 를 부르고,
/// Dock DeferredContentControl·DeferredContentPresenter 는 ContentPresenter 의 Content·ContentTemplate 만 설정하므로 이 Build 로 온다(Avalonia 12.1.4·Dock 12.1.0.12 디컴파일 확인).
/// </summary>
public sealed class RootSafeRecyclingDataTemplate : ControlRecyclingDataTemplate, IRecyclingDataTemplate, IDataTemplate, ITemplate<object?, Control?>
{
    public new Control? Build(object? param) => Build(param, null);

    public new Control? Build(object? data, Control? existing)
    {
        if (Parent is { } presenter)
        {
            CrossRootLayoutFlush.BeforeFetch(data, presenter);
        }

        return base.Build(data, existing);
    }
}

// 화면 층 등록 지점 — 앱 컨텍스트(XkxkUi.Current)에 위임하며 등록 전 사용은 조용히 무동작하지 않고 예외로 드러낸다
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell;

/// <summary>기동 때 화면 층이 넣는 <see cref="IViewRegistry"/> 보관소. B04 부터 <see cref="XkxkUi.Current"/> 의 Views·RegisterViews 위임 접근자다. B16 에 Obsolete 예고, B21 에 제거한다.</summary>
public static class ViewRegistry
{
    /// <summary>등록된 화면 층. 미등록이면 InvalidOperationException, null 대입은 ArgumentNullException.</summary>
    public static IViewRegistry Current
    {
        get => XkxkUi.Current.Views;
        set => XkxkUi.Current.RegisterViews(value);
    }
}

// 도움말 투어 호출 카드 코드비하인드(요건 6) — 내용 표시와 버튼 알림만 맡고 진행 판단은 상위 층이 한다
using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.Text;

namespace xkxk.frameworks.ui.Controls;

public partial class TourCard : UserControl
{
    private const string UiKey = "sysview.tour.ui.";

    public TourCard() => InitializeComponent();

    public event Action? PreviousRequested;

    public event Action? NextRequested;

    public event Action? SkipRequested;

    public event Action? CloseRequested;

    /// <summary>내용을 바꾼다. 문구 사전은 언어 변경이 투어 중에 일어나지 않으므로 표시할 때마다 읽는다.</summary>
    public void Show(string title, string body, int index, int total, bool canGoBack)
    {
        TitleText.Text = title;
        BodyText.Text = body;
        CounterText.Text = string.Format(UiText.Tr(UiKey + "counter"), index, total);
        BtnPrev.Content = UiText.Tr(UiKey + "prev");
        BtnNext.Content = UiText.Tr(UiKey + "next");
        BtnSkip.Content = UiText.Tr(UiKey + "skip");
        BtnPrev.IsEnabled = canGoBack;
        AutomationProperties.SetName(BtnClose, UiText.Tr(UiKey + "close"));
        ToolTip.SetTip(BtnClose, UiText.Tr(UiKey + "close"));
    }

    /// <summary>기본 초점(다음).</summary>
    public void FocusPrimary() => BtnNext.Focus(NavigationMethod.Tab);

    /// <summary>Tab 순회 대상 버튼. 비활성은 호출하는 쪽이 건너뛴다.</summary>
    internal IEnumerable<Button> FocusOrder => new[] { BtnNext, BtnPrev, BtnSkip, BtnClose };

    private void OnPrevious(object? sender, RoutedEventArgs e) => PreviousRequested?.Invoke();

    private void OnNext(object? sender, RoutedEventArgs e) => NextRequested?.Invoke();

    private void OnSkip(object? sender, RoutedEventArgs e) => SkipRequested?.Invoke();

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}

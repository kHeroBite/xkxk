// 도움말 투어 마지막 레이어 코드비하인드(요건 6) — 상위 층이 준 단축키·기능 행을 보여 주고 이전·닫기를 알린다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.Text;

namespace xkxk.frameworks.ui.Controls;

/// <summary>단축키 한 줄. Note 가 있으면 경고색 보조 문구로 표시한다(웹 예약키 등).</summary>
public sealed record ShortcutRowItem(string Id, string Keys, string Text, string Note)
{
    public bool HasNote => Note.Length > 0;

    public string AutomationId => "tour.key." + Id;

    public string NoteAutomationId => "tour.keyNote." + Id;
}

/// <summary>기능 설명 한 줄.</summary>
public sealed record FeatureRowItem(string Id, string Title, string Body)
{
    public string AutomationId => "tour.feature." + Id;
}

public partial class ShortcutLayer : UserControl
{
    private const string UiKey = "sysview.tour.ui.";

    public ShortcutLayer() => InitializeComponent();

    public event Action? PreviousRequested;

    public event Action? CloseRequested;

    /// <summary>내용을 채운다. 단축키가 없는 플랫폼(스마트폰)은 단축키 제목도 숨긴다.</summary>
    public void Show(IReadOnlyList<ShortcutRowItem> keys, IReadOnlyList<FeatureRowItem> features, string contactTitle, string contactBody, int index, int total, bool canGoBack)
    {
        HeaderText.Text = UiText.Tr(UiKey + "layerTitle");
        KeysHeader.Text = UiText.Tr(UiKey + "keysTitle");
        KeysHeader.IsVisible = keys.Count > 0;
        KeyList.ItemsSource = keys;
        FeaturesHeader.Text = UiText.Tr(UiKey + "featuresTitle");
        FeatureList.ItemsSource = features;
        ContactHeader.Text = contactTitle;
        ContactBody.Text = contactBody;
        CounterText.Text = string.Format(UiText.Tr(UiKey + "counter"), index, total);
        BtnPrev.Content = UiText.Tr(UiKey + "prev");
        BtnPrev.IsEnabled = canGoBack;
        BtnClose.Content = UiText.Tr(UiKey + "done");
        AutomationProperties.SetName(BtnX, UiText.Tr(UiKey + "close"));
        ToolTip.SetTip(BtnX, UiText.Tr(UiKey + "close"));
        LayerScroll.Offset = default; // 다시 들어와도 첫 줄부터 보여 준다
    }

    /// <summary>키보드 스크롤. 투어 입력 차단이 키를 먼저 받으므로 ScrollViewer 대신 상위 층이 여기로 넘긴다.</summary>
    public void ScrollByKey(Key key)
    {
        switch (key)
        {
            case Key.Up: LayerScroll.LineUp(); break;
            case Key.Down: LayerScroll.LineDown(); break;
            case Key.PageUp: LayerScroll.PageUp(); break;
            case Key.PageDown: LayerScroll.PageDown(); break;
            case Key.Home: LayerScroll.ScrollToHome(); break;
            case Key.End: LayerScroll.ScrollToEnd(); break;
        }
    }

    /// <summary>기본 초점(닫기).</summary>
    public void FocusPrimary() => BtnClose.Focus(NavigationMethod.Tab);

    /// <summary>Tab 순회 대상 버튼.</summary>
    internal IEnumerable<Button> FocusOrder => new[] { BtnClose, BtnPrev, BtnX }.AsEnumerable();

    private void OnPrevious(object? sender, RoutedEventArgs e) => PreviousRequested?.Invoke();

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}

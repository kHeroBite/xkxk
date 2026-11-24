// 진단 컨트롤 갤러리 화면 — 체크·라디오·토글·스위치·슬라이더·진행 막대·콤보·목록 선택·강조 버튼·하이퍼링크·포커스 테두리를 고정 상태로 배치(코드 생성, 진단 빌드 전용)
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace xkxk.frameworks.ui.views.Diagnostics.Gallery;

/// <summary>
/// Fluent 강조색(SystemAccentColor·Light1·Dark1)과 포커스 테두리 브러시를 쓰는 컨트롤을 '켜진 상태'로 한 화면에 고정해 둔다.
/// 애니메이션이 있는 상태(불확정 진행 막대·링)는 화소 비교를 흔들어서 넣지 않는다. 비활성(disabled) 줄은 SystemControlDisabledAccentBrush 확인용.
/// 각 컨트롤 Name = AutomationId(gallery.*). 상태 전환은 원격 gallery.set 으로만 한다(실입력 없음).
/// </summary>
public sealed class GalleryView : UserControl
{
    /// <summary>가장 최근에 만들어진 갤러리 화면(원격 명령용).</summary>
    internal static GalleryView? Latest { get; private set; }

    private readonly CheckBox _check = new() { Name = "gallery.check", Content = "CheckBox (checked)", IsChecked = true };
    private readonly CheckBox _checkOff = new() { Name = "gallery.checkOff", Content = "CheckBox (unchecked)", IsChecked = false };
    private readonly CheckBox _checkMixed = new() { Name = "gallery.checkMixed", Content = "CheckBox (indeterminate)", IsThreeState = true, IsChecked = null };
    private readonly RadioButton _radioA = new() { Name = "gallery.radioA", Content = "Radio A (checked)", GroupName = "pdGallery", IsChecked = true };
    private readonly RadioButton _radioB = new() { Name = "gallery.radioB", Content = "Radio B", GroupName = "pdGallery" };
    private readonly ToggleSwitch _switchOn = new() { Name = "gallery.switchOn", Content = "ToggleSwitch", IsChecked = true };
    private readonly ToggleSwitch _switchOff = new() { Name = "gallery.switchOff", Content = "ToggleSwitch (off)", IsChecked = false };
    private readonly ToggleButton _toggle = new() { Name = "gallery.toggle", Content = "ToggleButton (checked)", IsChecked = true };
    private readonly Button _accent = new() { Name = "gallery.accent", Content = "Accent button" };
    private readonly HyperlinkButton _link = new() { Name = "gallery.link", Content = "HyperlinkButton" };
    private readonly Slider _slider = new() { Name = "gallery.slider", Minimum = 0, Maximum = 100, Value = 60, Width = 220 };
    private readonly ProgressBar _progress = new() { Name = "gallery.progress", Minimum = 0, Maximum = 100, Value = 60, Width = 220 };
    private readonly ComboBox _combo = new() { Name = "gallery.combo", Width = 220, ItemsSource = new[] { "Alpha", "Beta", "Gamma" }, SelectedIndex = 1 };
    private readonly ListBox _list = new() { Name = "gallery.list", Width = 220, ItemsSource = new[] { "Item 1", "Item 2 (selected)", "Item 3" }, SelectedIndex = 1 };
    private readonly TextBox _text = new() { Name = "gallery.text", Width = 220, Text = "TextBox (focus target)" };
    private readonly StackPanel _disabledRow = new() { Orientation = Orientation.Horizontal, Spacing = 12, IsEnabled = false };

    public GalleryView()
    {
        _accent.Classes.Add("accent");
        _disabledRow.Children.Add(new CheckBox { Name = "gallery.dCheck", Content = "disabled check", IsChecked = true });
        _disabledRow.Children.Add(new ToggleSwitch { Name = "gallery.dSwitch", IsChecked = true });
        _disabledRow.Children.Add(new Slider { Name = "gallery.dSlider", Minimum = 0, Maximum = 100, Value = 40, Width = 160 });

        var grid = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("Auto,32,Auto"), // 가운데 32px 간격 열
        };
        AddColumn(grid, 0, Header("Selection"), _check, _checkOff, _checkMixed, _radioA, _radioB, _switchOn, _switchOff, _toggle);
        AddColumn(grid, 2, Header("Value · input"), _accent, _link, _slider, _progress, _combo, _list, _text, Header("Disabled"), _disabledRow);

        Content = new ScrollViewer
        {
            Name = "gallery.scroll",
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = grid,
        };
        Latest = this;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (ReferenceEquals(Latest, this))
        {
            Latest = null;
        }
    }

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) };

    private static void AddColumn(Grid grid, int column, params Control[] items)
    {
        var stack = new StackPanel { Spacing = 10 };
        foreach (var item in items)
        {
            stack.Children.Add(item);
        }

        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
    }

    /// <summary>원격 상태 전환. 모르는 값이면 false.</summary>
    internal bool Apply(string state)
    {
        switch (state.Trim().ToLowerInvariant())
        {
            case "combo-open": _combo.IsDropDownOpen = true; break;
            case "combo-close": _combo.IsDropDownOpen = false; break;
            case "focus-text": _text.Focus(NavigationMethod.Tab); break;   // Tab 방식이어야 포커스 테두리(FocusAdorner)가 보인다
            case "focus-check": _check.Focus(NavigationMethod.Tab); break;
            // 12.1.4 IFocusManager 에는 ClearFocus 가 없다(odev-4b 컴파일 확인). 문서: Focus(null) 은 포커스를 지운다(권장은 아니나 진단 상태 전환에는 충분)
            case "focus-none": TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null!, NavigationMethod.Unspecified, KeyModifiers.None); break;
            case "disabled-on": _disabledRow.IsEnabled = false; break;
            case "disabled-off": _disabledRow.IsEnabled = true; break;
            default: return false;
        }

        return true;
    }

    /// <summary>화소 비교 전 적용 확인용 상태.</summary>
    internal JsonObject State() => new()
    {
        ["check"] = _check.IsChecked,
        ["checkMixed"] = _checkMixed.IsChecked is null ? "null" : _checkMixed.IsChecked.ToString(),
        ["radioA"] = _radioA.IsChecked,
        ["switchOn"] = _switchOn.IsChecked,
        ["toggle"] = _toggle.IsChecked,
        ["slider"] = _slider.Value,
        ["progress"] = _progress.Value,
        ["comboOpen"] = _combo.IsDropDownOpen,
        ["comboSelected"] = _combo.SelectedIndex,
        ["listSelected"] = _list.SelectedIndex,
        ["focus"] = (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control)?.Name,
        ["disabledRowEnabled"] = _disabledRow.IsEnabled,
        ["bounds"] = new JsonObject { ["w"] = Bounds.Width, ["h"] = Bounds.Height },
    };
}
#endif

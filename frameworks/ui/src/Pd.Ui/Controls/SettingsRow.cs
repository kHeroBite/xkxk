// 설정 창의 한 행(요건 7) — 라벨 + 입력 컨트롤. 폭이 모자라면 입력이 다음 줄로 내려가고, 바깥 SettingsShell 의 검색어에 맞지 않으면 스스로 숨는다
using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Pd.Ui.Controls;

/// <summary>
/// 자식으로 입력 컨트롤을 두는 행. SettingId 는 설정 인벤토리의 항목 Id 이고 AutomationId settings.row.{id} 가 된다.
/// 라벨은 Label(문자열)로 받는다. 이 컨트롤은 사전·설정을 모른다.
/// </summary>
public class SettingsRow : WrapPanel
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> SettingIdProperty =
        AvaloniaProperty.Register<SettingsRow, string?>(nameof(SettingId));

    private const double LabelWidth = 200;

    private readonly TextBlock _label;
    private SettingsShell? _shell;

    public SettingsRow()
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0, 0, 0, 10);
        _label = new TextBlock
        {
            Width = LabelWidth,
            Margin = new Thickness(0, 0, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Children.Add(_label);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? SettingId
    {
        get => GetValue(SettingIdProperty);
        set => SetValue(SettingIdProperty, value);
    }

    /// <summary>검색어가 이름에 들어 있는가(앞뒤 공백 무시, 대소문자 무시). 비어 있으면 항상 참.</summary>
    public static bool Matches(string? label, string? search) =>
        string.IsNullOrWhiteSpace(search)
        || (label?.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase) ?? false);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _shell = this.FindAncestorOfType<SettingsShell>();
        if (_shell is not null)
        {
            _shell.SearchTextChanged += ApplySearch;
        }

        ApplySearch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_shell is not null)
        {
            _shell.SearchTextChanged -= ApplySearch;
            _shell = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
        {
            _label.Text = Label;
            ApplySearch();
        }
        else if (change.Property == SettingIdProperty)
        {
            AutomationProperties.SetAutomationId(this, "settings.row." + SettingId);
        }
    }

    protected override void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        foreach (var item in e.NewItems ?? Array.Empty<object>())
        {
            if (item is Control editor && !ReferenceEquals(editor, _label))
            {
                AutomationProperties.SetLabeledBy(editor, _label); // 입력 컨트롤의 접근성 이름 = 행 라벨
            }
        }
    }

    private void ApplySearch()
    {
        if (_shell is not null)
        {
            SetCurrentValue(IsVisibleProperty, Matches(Label, _shell.SearchText));
        }
    }
}

// 스코프별 진행 표시 오버레이 — 창 내용 위 중앙에 작은 간트 스피너를 띄운다. 입력을 막지 않고 창마다 독립으로 켜진다
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Pd.Ui.Text;

namespace Pd.Ui.Controls;

/// <summary>
/// 창 내용과 같은 칸의 마지막 자식으로 둔다. 상위 층이 <see cref="SetScopeActive"/> 로 스코프 상태를 알리면 그 스코프의 오버레이가 켜지고 꺼진다.
/// 스코프는 <see cref="ScopeProperty"/>(상속되는 첨부 속성)로 정한다. 스코프가 켜진 뒤 붙은 오버레이도 바로 켜진다. 모든 호출은 UI 스레드에서 한다.
/// </summary>
public sealed class BusyOverlay : Decorator
{
    /// <summary>스피너 크기(dip).</summary>
    public const double SpinnerSize = 28;

    /// <summary>스피너 불투명도.</summary>
    public const double SpinnerOpacity = 0.45;

    /// <summary>오버레이가 속한 스코프 키(main 또는 문서 Id). 부모에 달면 자식 오버레이가 물려받는다.</summary>
    public static readonly AttachedProperty<string?> ScopeProperty =
        AvaloniaProperty.RegisterAttached<BusyOverlay, Control, string?>("Scope", inherits: true);

    private static readonly Dictionary<string, List<BusyOverlay>> Attached = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> ActiveScopes = new(StringComparer.Ordinal); // 스코프 → 표시 지연(ms)

    private string? _registeredScope;

    public BusyOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    public string? Scope
    {
        get => GetValue(ScopeProperty);
        set => SetValue(ScopeProperty, value);
    }

    /// <summary>안의 스피너. 처음 켜지기 전에는 null 이다.</summary>
    public GanttSpinner? Spinner => Child as GanttSpinner;

    public static string? GetScope(Control control) => control.GetValue(ScopeProperty);

    public static void SetScope(Control control, string? scope) => control.SetValue(ScopeProperty, scope);

    /// <summary>
    /// 스코프의 표시 상태를 바꾼다. 끝난 스코프는 false 로 알려 상태가 쌓이지 않게 한다.
    /// showDelayMs 를 주면 지연을 렌더 스레드에서 세므로, 이 호출 뒤 커밋이 한 번 일어나면 UI 스레드가 막혀도 지연 뒤에 나타난다. 그 전에 false 를 알리면 보이지 않는다.
    /// </summary>
    public static void SetScopeActive(string scope, bool active, int showDelayMs = 0)
    {
        if (active)
        {
            ActiveScopes[scope] = showDelayMs;
        }
        else
        {
            ActiveScopes.Remove(scope);
        }

        if (Attached.TryGetValue(scope, out var overlays))
        {
            foreach (var overlay in overlays)
            {
                overlay.Sync();
            }
        }
    }

    /// <summary>스코프가 켜져 있는가.</summary>
    public static bool IsScopeActive(string scope) => ActiveScopes.ContainsKey(scope);

    /// <summary>시각 트리에 붙어 있는 그 스코프의 오버레이(복사본). 진단용.</summary>
    public static IReadOnlyList<BusyOverlay> OverlaysFor(string scope) =>
        Attached.TryGetValue(scope, out var overlays) ? overlays.ToArray() : Array.Empty<BusyOverlay>();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Register();
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unregister();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScopeProperty && VisualRoot is not null)
        {
            Unregister();
            Register();
            Sync();
        }
    }

    private void Register()
    {
        _registeredScope = Scope;
        if (_registeredScope is null)
        {
            return;
        }

        if (!Attached.TryGetValue(_registeredScope, out var overlays))
        {
            overlays = new List<BusyOverlay>();
            Attached[_registeredScope] = overlays;
        }

        overlays.Add(this);
    }

    private void Unregister()
    {
        if (_registeredScope is not null && Attached.TryGetValue(_registeredScope, out var overlays))
        {
            overlays.Remove(this);
            if (overlays.Count == 0)
            {
                Attached.Remove(_registeredScope);
            }
        }

        _registeredScope = null;
    }

    private void Sync()
    {
        int showDelayMs = 0;
        bool active = _registeredScope is not null && ActiveScopes.TryGetValue(_registeredScope, out showDelayMs);
        if (active && Child is null)
        {
            Child = new GanttSpinner
            {
                Width = SpinnerSize,
                Height = SpinnerSize,
                Opacity = SpinnerOpacity,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        if (Child is { } spinner)
        {
            if (active)
            {
                AutomationProperties.SetName(spinner, UiText.Tr("busy.name")); // 켤 때마다 읽어 현재 언어를 따른다
                ((GanttSpinner)spinner).ShowDelayMs = showDelayMs;
            }

            spinner.IsVisible = active; // 스피너 구동을 함께 멈추려고 스피너에도 직접 준다
        }

        IsVisible = active;
    }
}

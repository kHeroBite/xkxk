// 도구 머리줄 컨트롤 위 확장 버튼 배치(요건 3) — 기본 패널의 ToolChromeControl 템플릿 뿌리에 겹침 층을 달고, 지금 잘린 끝에만 PanelCornerButton 을 둔다
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Platform;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 다시 그리는 시점은 머리줄의 Bounds·DataContext 변화(NarrowToolChrome 이 부른다)와 PanelCornerService.ButtonsInvalidated(문서 영역 분할·해제처럼 Bounds 가 그대로일 수 있는 변화)다.
/// 같은 버튼 구성이면 컨트롤을 다시 만들지 않는다(포커스 유지). 독립 창(SDI) 안 패널은 기본 패널이 아니라 버튼이 없다.
/// </summary>
internal static class PanelCornerChrome
{
    private static readonly AttachedProperty<Panel?> OverlayProperty =
        AvaloniaProperty.RegisterAttached<Control, Panel?>("Overlay", typeof(PanelCornerChrome));

    private static readonly AttachedProperty<ServiceLink?> LinkProperty =
        AvaloniaProperty.RegisterAttached<Control, ServiceLink?>("Link", typeof(PanelCornerChrome));

    /// <summary>템플릿 적용 뒤 겹침 층을 단다. 템플릿 뿌리가 Panel 이 아니면 층을 달지 않는다.</summary>
    internal static void Attach(TemplatedControl chrome)
    {
        if (chrome.GetVisualChildren().OfType<Panel>().FirstOrDefault() is not { } root)
        {
            return;
        }

        var overlay = new Panel { Name = "PART_PdCornerOverlay" };
        Grid.SetRowSpan(overlay, 8);
        Grid.SetColumnSpan(overlay, 8);
        root.Children.Add(overlay);
        chrome.SetValue(OverlayProperty, overlay);
        if (chrome.GetValue(LinkProperty) is null) // 템플릿이 다시 적용돼도 구독 연결은 머리줄당 하나다
        {
            var link = new ServiceLink(chrome);
            chrome.SetValue(LinkProperty, link);
            chrome.AttachedToVisualTree += (_, _) => Refresh(chrome);
            chrome.DetachedFromVisualTree += (_, _) => link.Track(null);
        }

        Refresh(chrome);
    }

    /// <summary>지금 상태에 맞게 버튼을 맞춘다.</summary>
    internal static void Refresh(Control chrome)
    {
        if (chrome.GetValue(OverlayProperty) is not { } overlay)
        {
            return;
        }

        var dock = chrome.DataContext as IDock;
        var factory = dock?.Factory as PocDockFactory;
        chrome.GetValue(LinkProperty)?.Track(TopLevel.GetTopLevel(chrome) is null ? null : factory?.Corners); // 트리에서 떨어진 머리줄은 구독하지 않는다(서비스가 붙들지 않게)
        var wanted = dock is not null && factory is not null
            ? factory.Corners.ButtonsForChrome(dock)
            : new List<(string PanelId, PanelCorner Corner, PanelCornerDirection Direction)>();

        var current = overlay.Children.OfType<PanelCornerButton>().Select(b => (PanelCorner)b.Tag!).ToHashSet();
        if (current.SetEquals(wanted.Select(w => w.Corner)))
        {
            return;
        }

        overlay.Children.Clear();
        foreach (var (panelId, corner, direction) in wanted)
        {
            overlay.Children.Add(CreateButton(factory!, panelId, corner, direction));
        }
    }

    /// <summary>머리줄 하나가 지금 구독 중인 서비스. 트리에서 떨어지면 해제한다.</summary>
    private sealed class ServiceLink(Control chrome)
    {
        private PanelCornerService? _service;

        internal void Track(PanelCornerService? service)
        {
            if (ReferenceEquals(_service, service))
            {
                return;
            }

            if (_service is not null)
            {
                _service.ButtonsInvalidated -= OnInvalidated;
            }

            _service = service;
            if (_service is not null)
            {
                _service.ButtonsInvalidated += OnInvalidated;
            }
        }

        private void OnInvalidated() => Refresh(chrome);
    }

    private static PanelCornerButton CreateButton(PocDockFactory factory, string panelId, PanelCorner corner, PanelCornerDirection direction)
    {
        var button = new PanelCornerButton(direction, InputFormFactor.IsTouchDevice) { Tag = corner };
        AutomationProperties.SetAutomationId(button, $"panel.corner.{panelId}.{corner}");
        if (direction == PanelCornerDirection.Up)
        {
            button.Margin = new Thickness(0, 26, 0, 0); // 머리줄 제목·📌·✕ 를 가리지 않게 머리줄 아래에 둔다
        }

        button.AttachedToVisualTree += (_, _) => xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Localization.LanguageChanged += button.RefreshText; // 언어 전환 즉시 툴팁·자동화 이름 갱신
        button.DetachedFromVisualTree += (_, _) => xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Localization.LanguageChanged -= button.RefreshText;
        button.Click += (_, e) =>
        {
            e.Handled = true;
            factory.Corners.ClaimCorner(panelId, corner);
        };
        return button;
    }
}

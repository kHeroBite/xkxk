// Tab 키 포커스 이동 범위 제한(U21, 컨트롤 계층) — 상단 메뉴 영역(IsScope 표시 요소) 안에서만 Tab 순환, 그 밖의 Tab·Shift+Tab 은 메뉴 영역으로 보낸다
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.views.Input;

/// <summary>
/// 사용자 지시(11-01 U21): Tab 키 포커스 이동은 상단 메뉴 영역에서만 가능하고 나머지 컴포넌트(문서·패널·탭·캡션 버튼)는 Tab 으로 가지 않는다.
/// 화면(MainView.axaml)이 메뉴 영역에 IsScope=True 와 KeyboardNavigation.TabNavigation=Cycle 을 단다 — 영역 안 순환은 Avalonia 기본 처리에 맡긴다.
/// 영역 밖에서 누른 Tab 은 터널 단계에서 막고 영역의 첫(Shift 면 마지막) 항목으로 포커스를 옮긴다. 영역이 없는 창(SDI 분리 창)에서는 막기만 한다.
/// 이월 3(AC-202): 메뉴 영역 마지막 항목에서 Tab 은 활성 문서 창의 크롬 단추 순환으로 이어지고 크롬 단추 끝에서 메뉴 첫 항목으로 돌아온다(SDI 는 그 창 제목줄 단추 안에서만 돈다).
/// 예외: 메뉴·콤보의 열린 목록(팝업 창), Tab 을 글자로 받는 입력란(AcceptsTab) — 입력·편집 동작은 그대로 둔다. 마우스 클릭 포커스는 건드리지 않는다.
/// </summary>
public static class TabFocusScope
{
    public static readonly AttachedProperty<bool> IsScopeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsScope", typeof(TabFocusScope));

    private static readonly List<WeakReference<Control>> s_scopes = new();
    private static bool s_installed;

    static TabFocusScope()
    {
        IsScopeProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            s_scopes.RemoveAll(w => !w.TryGetTarget(out var t) || ReferenceEquals(t, c));
            if (e.NewValue is true)
            {
                s_scopes.Add(new WeakReference<Control>(c));
                Install();
            }
        });
    }

    public static bool GetIsScope(Control control) => control.GetValue(IsScopeProperty);

    public static void SetIsScope(Control control, bool value) => control.SetValue(IsScopeProperty, value);

    /// <summary>모든 TopLevel(메인·SDI 창)의 KeyDown 을 터널 단계에서 받는다. 1회만 건다.</summary>
    private static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnKeyDown(TopLevel top, KeyEventArgs e)
    {
        // Ctrl+Tab·Ctrl+Shift+Tab(다음·이전 창)·Alt+Tab 은 창 전환 단축키라 건드리지 않는다
        if (e.Handled || e.Key != Key.Tab || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None
            || top is Avalonia.Controls.Primitives.PopupRoot)
        {
            return;
        }

        // Snap Assist 세션 중에는 Tab 을 덮개(SnapAssistLayer)가 후보 이동에 쓴다 — 가로채지 않고 지나간다(AC-015, Handled·포커스 이동 없음)
        if (xkxk.frameworks.ui.shell.Docking.SnapAssistSession.IsActive)
        {
            return;
        }

        var focused = top.FocusManager?.GetFocusedElement() as Visual;
        if (focused is TextBox { AcceptsTab: true })
        {
            return; // Tab 을 글자로 받는 입력란은 편집 동작 유지
        }

        var scope = ScopeIn(top);
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var stops = scope is null ? null : StopsOf(scope);
        if (TryCycleChrome(top, scope, stops, focused as InputElement, shift))
        {
            e.Handled = true; // 제목줄 스코프 ↔ 활성 문서 창 크롬 단추 순환(이월 3, AC-202)
            return;
        }

        if (scope is not null && focused is not null && IsInside(scope, focused))
        {
            return; // 메뉴 영역 안: KeyboardNavigation.TabNavigation=Cycle 로 영역 안에서만 돈다
        }

        e.Handled = true;
        var target = stops is null ? null : shift ? stops.LastOrDefault() : stops.FirstOrDefault();
        target?.Focus(NavigationMethod.Tab);
    }

    private static List<InputElement> StopsOf(Visual root) => root.GetVisualDescendants().OfType<InputElement>()
        .Where(x => x.Focusable && x.IsTabStop && x.IsEffectivelyVisible && x.IsEffectivelyEnabled).ToList();

    /// <summary>
    /// 크롬 단추(ChromeButtons) 순환. 포커스가 제목줄 스코프 마지막(Shift 면 첫) 항목이면 활성 문서 창의 크롬 단추로 넘어가고,
    /// 크롬 단추 안에서는 단추 사이를 돌다 끝에서 스코프 첫(마지막) 항목으로 돌아온다. SDI 창(스코프 없음)은 포커스가 있는 창 제목줄의 단추 안에서만 돈다.
    /// 활성 문서가 없거나 최소화·숨김이면 단추 구간이 비어 건너뛴다. 문서 본문·제목 탭 목록의 Tab 은 건드리지 않는다.
    /// </summary>
    private static bool TryCycleChrome(TopLevel top, Control? scope, List<InputElement>? scopeStops, InputElement? focused, bool shift)
    {
        var own = focused?.FindAncestorOfType<ChromeButtons>(true);
        var group = own ?? (scope is not null && scopeStops is { Count: > 0 } && ReferenceEquals(focused, shift ? scopeStops[0] : scopeStops[^1]) ? ActiveChrome(top) : null);
        if (group is null && scope is null && (focused is null || ReferenceEquals(focused, top)))
        {
            group = ActiveChrome(top); // SDI 창(스코프 없음)에서 포커스가 없거나 창 루트일 때 Tab 은 크롬 단추로 진입한다(Shift 면 마지막 단추, D-18)
        }

        if (group is null)
        {
            return false;
        }

        var chrome = StopsOf(group);
        if (chrome.Count == 0)
        {
            return false;
        }

        var i = focused is null ? -1 : chrome.IndexOf(focused);
        InputElement? target;
        if (i < 0) // 스코프 끝에서 진입
        {
            target = shift ? chrome[^1] : chrome[0];
        }
        else if (shift ? i > 0 : i < chrome.Count - 1)
        {
            target = chrome[i + (shift ? -1 : 1)];
        }
        else if (scopeStops is { Count: > 0 })
        {
            target = shift ? scopeStops[^1] : scopeStops[0];
        }
        else
        {
            target = shift ? chrome[^1] : chrome[0];
        }

        target?.Focus(NavigationMethod.Tab);
        return true;
    }

    /// <summary>활성 문서 창(MDI 는 :active, SDI 는 창 하나뿐)의 보이는 크롬 단추 묶음. 없으면 null.</summary>
    private static ChromeButtons? ActiveChrome(TopLevel top) => top.GetVisualDescendants().OfType<ChromeButtons>()
        .FirstOrDefault(c => c.IsEffectivelyVisible && (c.FindAncestorOfType<Dock.Avalonia.Controls.MdiDocumentWindow>() is not { } w || w.Classes.Contains(":active")));

    /// <summary>이 TopLevel 안에 있는 메뉴 영역(없으면 null — SDI 분리 창).</summary>
    private static Control? ScopeIn(TopLevel top)
    {
        foreach (var w in s_scopes)
        {
            if (w.TryGetTarget(out var c) && c.IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(c), top))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>영역 안인가 — 시각 트리 또는 논리 트리(메뉴 항목의 펼친 목록은 논리 자식)로 본다.</summary>
    private static bool IsInside(Control scope, Visual focused) =>
        ReferenceEquals(scope, focused) || scope.IsVisualAncestorOf(focused)
        || (focused is ILogical logical && scope.IsLogicalAncestorOf(logical));
}

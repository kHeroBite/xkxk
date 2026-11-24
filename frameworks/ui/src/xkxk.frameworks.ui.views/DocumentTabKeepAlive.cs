// 탭형(DOCK) 문서 본문 유지 — 비활성 탭 문서 뷰를 화면 트리에서 떼지 않고 숨겨 두어 탭 전환 때 재부착(스타일·측정 전체)을 없앤다
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Recycling;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Platform;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// Themes/DocumentDockMode.axaml 의 탭 DocumentControl 이 Enable=True 를 건다. 문서 전환 가속(UiSettings.FastDocumentSwitch)이 켜져 있으면
/// 템플릿 적용 때 본문 표시기(PART_ContentPresenter, Dock 12.1.0.12 의 DeferredContentControl)의 ContentTemplate 을 <see cref="KeepAliveTemplate"/> 로 바꾼다.
/// 원래 템플릿(ControlRecyclingDataTemplate)은 활성 문서가 바뀔 때마다 이전 문서 뷰를 떼고 새 뷰를 붙인다(디컴파일 확인).
/// 붙일 때 뷰 하위 트리 전체(1만 행 그리드 등)의 스타일 적용·측정·배치가 한 번에 돌아 탭 전환이 느렸다(웹 그리드 탭 238~382ms, otest-w-7 분석).
/// 바꾼 템플릿은 늘 같은 Panel 을 돌려주고(ContentPresenter 는 같은 자식이면 떼지 않는다 — Avalonia 12.1.4 ContentPresenter.UpdateChild 디컴파일 확인)
/// 문서마다 칸(Decorator)을 두어 IsVisible 만 바꾼다.
/// 문서 뷰 인스턴스는 계속 공용 ControlRecycling 캐시에서 얻으므로 MDI·SDI·폰 화면이 같은 뷰를 가져가면 칸이 빈다 — 다음에 이 탭이 활성이 되면 캐시에서 다시 받아 온다.
/// 끄면(설정 화면 '문서 전환 가속') 새로 만들어지는 탭 영역부터 Dock 원래 템플릿 그대로다.
/// 단계별 시간은 진단판에서 ui.tab.* 로 남는다(DocumentTabSwitchProbe).
/// </summary>
public static class DocumentTabKeepAlive
{
    /// <summary>본문 표시기 이름(Dock Fluent DocumentControl 템플릿). 바뀌면 찾지 못해 Dock 기본 동작으로 남는다.</summary>
    private const string PresenterPart = "PART_ContentPresenter";

    /// <summary>머리 먼저 그리기의 프레임 대기 상한(ms). 프레임이 오지 않아도(가려진 브라우저 탭 등) 이 시간 뒤에는 본문을 바꾼다.</summary>
    private const int HeaderFirstFallbackMs = 100;

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enable", typeof(DocumentTabKeepAlive));

    static DocumentTabKeepAlive()
    {
        EnableProperty.Changed.AddClassHandler<Control>((c, e) => OnEnableChanged(c, e.GetNewValue<bool>()));
    }

    /// <summary>
    /// 탭 머리 먼저 그리기(설정 '탭 선택 먼저 표시' = UiSettings.TabHeaderFirst, 기본 웹만 켬). 켜면 다른 문서가 보이는 중의 탭 전환에서 본문 교체(숨김 전환·첫 방문 뷰 부착)를
    /// 다음 프레임 뒤로 미뤄 누른 탭의 선택 표시가 먼저 그려진다(첫 반응 단축). 대가: 새 본문은 약 1프레임 늦게 보인다(그동안 이전 본문이 남는다).
    /// 웹은 렌더 스레드가 없어 무거운 본문 교체가 끝날 때까지 탭 선택 표시도 그려지지 않는다(final8 Fold5 웹 첫 반응 96ms, 앱 8ms). 다음 탭 전환부터 적용된다.
    /// </summary>
    private static bool HeaderFirst => UiSettings.Current.TabHeaderFirst;

    /// <summary>
    /// 탭 전환으로 보이던 문서 뷰를 숨기기 직전(인자 = 그 뷰). 창 움직임 효과(Depth/WindowTransitions)가 나가는 모습을 찍는다(21 §4 DOCK 짝 나감).
    /// 머리 먼저 그리기가 꺼져 있으면(Android·데스크톱 기본) 숨김이 활성 알림보다 먼저 올 수 있어 이 시점이 아니면 찍을 화면이 없다.
    /// </summary>
    internal static event Action<Control>? SlotHiding;

    public static bool GetEnable(Control c) => c.GetValue(EnableProperty);

    public static void SetEnable(Control c, bool value) => c.SetValue(EnableProperty, value);

    private static void OnEnableChanged(Control c, bool enabled)
    {
        if (c is TemplatedControl owner)
        {
            owner.TemplateApplied -= OnTemplateApplied;
            owner.TemplateApplied += enabled ? OnTemplateApplied : null;
        }
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not Control owner || !UiSettings.Current.FastDocumentSwitch)
        {
            return;
        }

        if (e.NameScope.Find(PresenterPart) is ContentControl presenter)
        {
            DocumentTabSwitchProbe.EnsureInstalled(); // 진단판만 탭 누름 시각 처리기를 건다
            // 템플릿이 건 값(Template 우선순위)은 스타일로 덮을 수 없어 지역 값으로 바꾼다(NarrowToolChrome 과 같은 이유)
            presenter.ContentTemplate = new KeepAliveTemplate(owner, presenter);
        }
    }

    /// <summary>칸 묶음. 측정·배치는 Panel 그대로이며 진단판에서 표시 전환 뒤 첫 측정·배치 시간만 잰다.</summary>
    private sealed class KeepAlivePanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize) => DocumentTabSwitchProbe.Measure(availableSize, base.MeasureOverride);

        protected override Size ArrangeOverride(Size finalSize) => DocumentTabSwitchProbe.Arrange(finalSize, base.ArrangeOverride);
    }

    /// <summary>탭 영역 하나(DocumentControl 하나)의 문서 칸 묶음. 활성 문서 칸만 보이고 나머지는 숨긴 채 화면 트리에 남는다.</summary>
    private sealed class KeepAliveTemplate : IRecyclingDataTemplate
    {
        private readonly Control _owner;
        private readonly RootSafeRecyclingDataTemplate _inner;
        private readonly KeepAlivePanel _panel = new();
        private readonly Dictionary<IDockable, Decorator> _slots = new();
        private INotifyCollectionChanged? _watched;
        private IDockable? _active;
        private bool _showPending;
        private IDisposable? _fallback;
        private double _switchStart;
        private double _builtAt;

        public KeepAliveTemplate(Control owner, Control presenter)
        {
            _owner = owner;
            _inner = new RootSafeRecyclingDataTemplate { Parent = presenter }; // 원래 템플릿과 같은 캐시(부모의 상속 ControlRecycling). 다른 창에서 꺼내 올 때 옛 창 레이아웃을 먼저 비운다(A10)
            owner.AttachedToVisualTree += (_, _) => Watch();
            owner.DetachedFromVisualTree += (_, _) => Unwatch(); // 탭 영역이 사라지면 문서 목록 구독을 끊어 이 묶음이 남지 않게 한다
        }

        /// <summary>원래 템플릿(ControlRecyclingDataTemplate.Match)과 같이 모든 값을 받는다.</summary>
        public bool Match(object? data) => true;

        public Control? Build(object? param) => Build(param, null);

        public Control? Build(object? data, Control? existing)
        {
            if (data is not IDockable dockable)
            {
                // 문서가 아닌 값·활성 문서 없음은 원래 템플릿과 같게 처리한다(칸 묶음은 떨어지고, 다음 문서가 오면 다시 붙는다)
                _showPending = false;
                return _inner.Build(data, null);
            }

            _switchStart = DocumentTabSwitchProbe.BeginBuild();
            _active = dockable;
            Prune();
            if (!_slots.TryGetValue(dockable, out var slot))
            {
                // 칸이 자기 DataContext 를 가져야 숨은 문서 뷰가 표시기의 DataContext(지금 활성 문서)를 물려받지 않는다
                slot = new Decorator { DataContext = dockable, IsVisible = false };
                slot.Classes.Add(xkxk.frameworks.ui.shell.Docking.TabSlotMarker.SlotClass); // 셸의 트리 훑기가 숨은 칸 하위를 건너뛴다(MdiActiveSync)
                _slots[dockable] = slot;
                _panel.Children.Add(slot);
            }

            Watch();
            if (ShouldYield(slot))
            {
                // 머리 먼저 그리기: 이번 프레임은 탭 선택 표시만 그리고, 본문 교체는 그 프레임 뒤에 한다(이전 본문은 그동안 그대로)
                RequestShowAfterFrame();
                _builtAt = DocumentTabSwitchProbe.Now();
                DocumentTabSwitchProbe.End("build", _switchStart);
                return _panel;
            }

            _showPending = false;
            var shown = ShowSlot(slot);
            DocumentTabSwitchProbe.End("build", _switchStart);
            return shown ? _panel : null;
        }

        /// <summary>다른 칸이 보이는 중이고 목표 칸이 아직 숨어 있을 때만 미룬다(처음 표시·같은 문서 재적용은 바로 한다).</summary>
        private bool ShouldYield(Decorator target) =>
            HeaderFirst && !target.IsVisible && TopLevel.GetTopLevel(_panel) is not null
            && _panel.Children.Any(c => c.IsVisible && !ReferenceEquals(c, target));

        private void RequestShowAfterFrame()
        {
            if (_showPending)
            {
                return; // 연타: 대기 중인 교체가 그때의 활성 문서(_active)를 보인다
            }

            _showPending = true;
            // 웹 렌더는 rAF 틱 안에서 돈다(추정). rAF 콜백에서 다시 미루면 그 틱의 그리기가 끝난 뒤에 돈다 — BusyHooks.YieldForRenderAsync 와 같은 방식
            TopLevel.GetTopLevel(_panel)?.RequestAnimationFrame(_ => Dispatcher.UIThread.Post(ShowPending, UiDeferred.Priority));
            _fallback = DispatcherTimer.RunOnce(ShowPending, TimeSpan.FromMilliseconds(HeaderFirstFallbackMs)); // 프레임이 오지 않는 경우의 안전장치(먼저 도는 쪽만 처리)
        }

        private void ShowPending()
        {
            _fallback?.Dispose(); // 다음 전환의 대기를 옛 안전장치가 앞당기지 않게 한다
            _fallback = null;
            if (!_showPending)
            {
                return;
            }

            _showPending = false;
            DocumentTabSwitchProbe.End("wait", _builtAt);
            if (_active is not null && _slots.TryGetValue(_active, out var slot) && !ShowSlot(slot))
            {
                Show(null); // 뷰를 받지 못했다(원래는 빈 본문) — 이전 본문을 남기지 않는다
            }
        }

        /// <summary>칸에 뷰가 없으면 캐시에서 받아 붙이고 그 칸만 보인다. 뷰를 받지 못하면 false.</summary>
        private bool ShowSlot(Decorator slot)
        {
            if (slot.Child is null)
            {
                // 처음 보거나 MDI·SDI·폰 화면이 가져간 뒤다. 공용 캐시에서 받아 온다(옛 부모에서 떼어 준다)
                var fetchStart = DocumentTabSwitchProbe.Now();
                var view = _inner.Build(slot.DataContext, null);
                if (view is null)
                {
                    return false;
                }

                slot.Child = view;
                DocumentTabSwitchProbe.End("fetch", fetchStart);
            }

            var showStart = DocumentTabSwitchProbe.Now();
            Show(slot);
            DocumentTabSwitchProbe.End("show", showStart);
            DocumentTabSwitchProbe.Shown(_panel, _switchStart);
            return true;
        }

        private void Show(Decorator? active)
        {
            var hadFocus = false;
            foreach (var child in _panel.Children)
            {
                var visible = ReferenceEquals(child, active);
                if (!visible && child.IsVisible && active is not null && child is Decorator { Child: { } leaving })
                {
                    SlotHiding?.Invoke(leaving); // 숨기기 전 — 아직 보이는 모습
                }

                if (!visible && child.IsVisible && child.IsKeyboardFocusWithin)
                {
                    hadFocus = true;
                }

                child.IsVisible = visible;
            }

            if (hadFocus)
            {
                // 예전에는 뷰가 떨어지며 초점이 풀렸다. 숨은 문서가 키 입력을 받지 않도록 같은 결과를 만든다
                TopLevel.GetTopLevel(_panel)?.FocusManager?.Focus(null);
            }
        }

        /// <summary>이 탭 영역에서 빠진 문서(닫힘·다른 영역·분리 창으로 이동)의 칸을 지운다. 뷰는 떼기만 하고 캐시에 남는다(원래 동작과 같다).</summary>
        private void Prune()
        {
            var visible = (_owner.DataContext as IDock)?.VisibleDockables;
            foreach (var pair in _slots.ToArray())
            {
                if (ReferenceEquals(pair.Key, _active) || (visible?.Contains(pair.Key) ?? true))
                {
                    continue;
                }

                pair.Value.Child = null;
                _panel.Children.Remove(pair.Value);
                _slots.Remove(pair.Key);
            }
        }

        private void Watch()
        {
            var list = (_owner.DataContext as IDock)?.VisibleDockables as INotifyCollectionChanged;
            if (ReferenceEquals(list, _watched))
            {
                return;
            }

            Unwatch();
            _watched = list;
            if (list is not null)
            {
                list.CollectionChanged += OnDockablesChanged;
            }
        }

        private void Unwatch()
        {
            if (_watched is not null)
            {
                _watched.CollectionChanged -= OnDockablesChanged;
                _watched = null;
            }
        }

        private void OnDockablesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Prune();
    }
}

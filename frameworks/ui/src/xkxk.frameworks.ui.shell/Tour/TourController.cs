// 도움말 투어 진행(요건 6) — 창(TopLevel)마다 세션 1개. 단계 이동·대상 없는 단계 건너뛰기·마지막 단축키 레이어·종료 시 상태 복원을 맡는다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Tour;

/// <summary>
/// 시작: <see cref="StartFrom"/>(누른 버튼·창 기준) 또는 <see cref="StartMain"/>(메인 창). 같은 창에 세션이 있으면 새로 시작하지 않는다.
/// 서로 다른 창의 세션은 독립이라 동시에 다른 단계에 있을 수 있다. 모든 호출은 UI 스레드에서 한다.
/// </summary>
public sealed class TourController
{
    private static readonly Dictionary<TopLevel, TourController> Sessions = new();

    private readonly TopLevel _top;
    private readonly Visual _root;
    private readonly IReadOnlyList<TourStep> _steps;
    private readonly TourInputGate _gate;
    private readonly TourOverlay _overlay = new();
    private readonly PocDocument? _activeBefore;
    private readonly TourSkipLog _skips;
    private readonly TourModes _mode;
    private readonly TourPlatforms _platform;
    private int _position = -1; // _steps 위치. _steps.Count 면 마지막 단축키 레이어
    private bool _ended;

    private TourController(TopLevel top, Visual root, TourScope scope, TourModes mode, TourPlatforms platform)
    {
        var plan = TourCatalog.Plan(scope, mode, platform);
        _skips = new TourSkipLog(scope);
        _mode = mode;
        _platform = platform;
        foreach (var (step, reason) in plan.Excluded)
        {
            _skips.Note(step.Id, reason);
        }

        _top = top;
        _root = root;
        Scope = scope;
        _steps = plan.Steps;
        _gate = new TourInputGate(top);
        _activeBefore = XkxkUi.Current.MainViewModel?.ActiveDocument;
    }

    /// <summary>이 세션에서 건너뛴 단계와 사유(진단용).</summary>
    public IReadOnlyList<(string StepId, string Reason)> Skipped => _skips.Items;

    /// <summary>이 세션의 창 종류.</summary>
    public TourScope Scope { get; }

    /// <summary>현재 단계 id. 마지막 단축키 레이어면 null.</summary>
    public string? CurrentStepId => OnShortcutLayer ? null : _steps[_position].Id;

    /// <summary>마지막 단축키·기능 레이어를 보여 주는 중인가.</summary>
    public bool OnShortcutLayer => _position >= _steps.Count;

    /// <summary>화면에 표시한 현재 순번(1부터).</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>화면에 표시한 전체 수(보이는 단계 + 마지막 레이어).</summary>
    public int Count { get; private set; }

    /// <summary>움직임 줄이기로 전환 없이 표시 중인가.</summary>
    public bool IsMotionReduced => _overlay.IsMotionReduced;

    /// <summary>초점이 투어 버튼 위에 있는가.</summary>
    public bool OverlayHasFocus => _top.FocusManager?.GetFocusedElement() is Visual v
        && (ReferenceEquals(v, _overlay) || v.GetVisualAncestors().Contains(_overlay));

    /// <summary>진행 중인 모든 세션(원격 시험·진단용).</summary>
    public static IReadOnlyCollection<TourController> ActiveSessions => Sessions.Values;

    /// <summary>이 창의 진행 중 세션. 없으면 null.</summary>
    public static TourController? SessionOf(TopLevel top) => Sessions.GetValueOrDefault(top);

    /// <summary>메인 창 투어(시스템 메뉴 도움말·메인 창 도움말).</summary>
    public static bool StartMain() =>
        Application.Current?.ApplicationLifetime switch
        {
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } w } => StartFrom(w),
            Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime { MainView: { } v } => StartFrom(v),
            _ => false,
        };

    /// <summary>
    /// 눌린 컨트롤·창이 속한 창의 투어를 시작한다. MDI 문서 창 안이면 그 문서 창, SDI 창이면 그 창, 설정 창이면 설정, 나머지는 메인(스마트폰은 NAV).
    /// 이미 진행 중이거나 설명할 내용이 없는 창이면 false.
    /// </summary>
    public static bool StartFrom(Visual origin)
    {
        if (TopLevel.GetTopLevel(origin) is not { } top || Sessions.ContainsKey(top))
        {
            return false;
        }

        var (scope, root) = TourScopeResolver.Resolve(top, origin);
        if (scope is null)
        {
            return false;
        }

        return new TourController(top, root, scope.Value, TourScopeResolver.CurrentMode(), TourScopeResolver.CurrentPlatform()).Begin();
    }

    /// <summary>다음 단계(보이는 대상만). 마지막 단계 다음은 단축키 레이어, 레이어에서는 그대로다.</summary>
    public void Next() => MoveTo(NextVisible(_position, 1));

    /// <summary>이전 단계(보이는 대상만). 첫 단계에서는 그대로다.</summary>
    public void Previous() => MoveTo(NextVisible(_position, -1));

    /// <summary>남은 단계를 건너뛰고 마지막 단축키 레이어로 간다.</summary>
    public void Skip() => MoveTo(_steps.Count);

    /// <summary>Tab 초점을 투어 버튼 안에서 돌린다.</summary>
    public void MoveFocus(bool back) => _overlay.MoveFocus(back);

    /// <summary>마지막 레이어에서만 키로 스크롤한다. 카드 단계에서는 아무 일도 하지 않는다.</summary>
    public void ScrollLayer(Key key)
    {
        if (OnShortcutLayer)
        {
            _overlay.ScrollLayer(key);
        }
    }

    /// <summary>투어를 끝내고 시작 전 상태로 되돌린다. 여러 번 불러도 한 번만 처리한다.</summary>
    public void End()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        Sessions.Remove(_top);
        _top.Closed -= OnTopClosed;
        _overlay.SizeChanged -= OnOverlaySizeChanged;
        _overlay.PreviousRequested -= Previous;
        _overlay.NextRequested -= Next;
        _overlay.SkipRequested -= Skip;
        _overlay.CloseRequested -= End;
        _overlay.Detach();
        RestoreState();
    }

    private bool Begin()
    {
        if (OverlayLayer.GetOverlayLayer(_top) is not { } layer || !_gate.Capture())
        {
            return false;
        }

        Sessions[_top] = this;
        _top.Closed += OnTopClosed;
        _overlay.PreviousRequested += Previous;
        _overlay.NextRequested += Next;
        _overlay.SkipRequested += Skip;
        _overlay.CloseRequested += End;
        _overlay.AttachTo(layer);
        layer.UpdateLayout(); // 대상 좌표 변환에 오버레이 크기가 필요하다
        _overlay.SizeChanged += OnOverlaySizeChanged;
        MoveTo(NextVisible(-1, 1));
        Dispatcher.UIThread.Post(() => { if (!_ended) { _overlay.FocusPrimary(); } }, DispatcherPriority.Loaded);
        return true;
    }

    /// <summary>시작 전 값으로 복원: 입력 격리·포커스(게이트)와 활성 문서.</summary>
    private void RestoreState()
    {
        _gate.Release();
        if (_activeBefore is not null && XkxkUi.Current.Shell is { } shell && !ReferenceEquals(XkxkUi.Current.MainViewModel?.ActiveDocument, _activeBefore)
            && shell.Documents.Contains(_activeBefore))
        {
            shell.Activate(_activeBefore);
        }
    }

    private void OnTopClosed(object? sender, EventArgs e) => End();

    private void OnOverlaySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (OnShortcutLayer || _ended)
        {
            return;
        }

        // 창 크기가 바뀌면 대상도 옮겨 간다. 숨었으면 다음 보이는 단계로 넘어간다.
        if (TourTargets.Find(_root, _steps[_position].TargetId, _overlay) is { } target && TourTargets.BoundsIn(target, _overlay) is { } rect)
        {
            _overlay.MoveSpotlight(rect);
        }
        else
        {
            Next();
        }
    }

    /// <summary>위치에서 방향으로 가며 대상이 보이는 첫 단계. 앞으로 다 없으면 단축키 레이어, 뒤로 다 없으면 현재 위치.</summary>
    private int NextVisible(int from, int step)
    {
        for (var i = from + step; i >= 0 && i < _steps.Count; i += step)
        {
            if (IsAvailable(_steps[i]))
            {
                return i;
            }
        }

        return step > 0 ? _steps.Count : from;
    }

    private bool IsAvailable(TourStep step)
    {
        if (TourTargets.Find(_root, step.TargetId, _overlay) is not null)
        {
            return true;
        }

        _skips.Note(step.Id, TourTargets.MissingReason(_root, step.TargetId));
        return false;
    }

    private void MoveTo(int position)
    {
        if (position == _position && position < _steps.Count && position >= 0)
        {
            return;
        }

        _position = position;
        var visibleSteps = _steps.Where(IsAvailable).ToList();
        var total = visibleSteps.Count + 1; // +1 = 마지막 단축키·기능 레이어
        Count = total;
        if (OnShortcutLayer)
        {
            CurrentIndex = total;
            ShowShortcutLayer(total, visibleSteps.Count > 0);
            return;
        }

        var current = _steps[_position];
        var target = TourTargets.Find(_root, current.TargetId, _overlay);
        var bounds = target is null ? null : TourTargets.BoundsIn(target, _overlay);
        if (bounds is null)
        {
            Next(); // 판정과 표시 사이에 사라진 대상
            return;
        }

        CurrentIndex = visibleSteps.IndexOf(current) + 1;
        _overlay.ShowStep(bounds.Value, TourCatalog.Tr(current.TitleKey), BodyOf(current, target), CurrentIndex, total, NextVisible(_position, -1) != _position);
    }

    /// <summary>문서 본문 단계는 지금 보이는 문서의 화면별 설명(그리드·간트)을 앞에 붙인다. 설명이 없는 종류는 공통 문구만 쓴다.</summary>
    private static string BodyOf(TourStep step, Control? target)
    {
        var body = TourCatalog.Tr(step.BodyKey);
        return step.TargetId == TourCatalog.ContentAnchor
            && target is { Tag: PocDocument { Kind: DocumentKind.Grid or DocumentKind.Gantt } document }
            && XkxkUi.Current.Shell is WindowCommands shell
            ? shell.ChromeHelpText(document) + "\n\n" + body
            : body;
    }

    private void ShowShortcutLayer(int total, bool canGoBack) =>
        _overlay.ShowShortcuts(TourLayerContent.Keys(_platform, OperatingSystem.IsMacOS()), TourLayerContent.Features(_mode, _platform),
            Loc.Tr("sysview.help.contact.title"), Loc.Tr("sysview.help.contact.body"), total, total, canGoBack);
}

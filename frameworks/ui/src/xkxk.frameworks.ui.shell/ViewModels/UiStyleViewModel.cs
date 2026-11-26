// 화면 방식(PC형·태블릿형·모바일형) 선택·적용 상태와 200ms 디바운스 전환 요청을 맡는 컨트롤 레이어 뷰모델
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>
/// 화면 방식 컨트롤. 규칙 판정은 <see cref="UiStyleRules"/> 에 위임하고, 여기서는 입력(크기·설정) 모으기, 디바운스,
/// 전환 지연, 전환 알림만 한다. 애니메이션 자체는 UI 레이어(UiStyleTransition)가 StyleChanging/StyleChanged/TransitionOut 으로 붙는다.
/// 바인딩 경로는 MainViewModel.Compact.Style.X.
/// </summary>
public sealed partial class UiStyleViewModel : ObservableObject
{
    /// <summary>마지막 크기 변경 뒤 이 시간 동안 변화가 없을 때 자동 판정한다(끌기 도중 깜빡임 방지).</summary>
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);

    private readonly ShellContext _ctx;
    private readonly DispatcherTimer _timer;
    private UiStyle _current = UiStyle.Auto; // Auto = 아직 적용 전
    private UiStyle _beforeMobile = UiStyle.Auto;
    private DeviceClass _device = DeviceClass.Pc;
    private double _width;
    private double _height;
    private bool _busy;

    public UiStyleViewModel(ShellContext ctx)
    {
        _ctx = ctx;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = DebounceDelay }; // D-C3-19: Background 는 Android 진단판에서 tick 이 수십 초 밀렸다(c3dev_c 21초·f 69초)
        _timer.Tick += async (_, _) => { _timer.Stop(); await EvaluateAsync("auto"); };
        SelectCommand = new RelayCommand<string?>(p =>
        {
            if (string.Equals(p, "AutoToggle", StringComparison.OrdinalIgnoreCase)) { ToggleAuto(); }
            else if (Enum.TryParse<UiStyle>(p, true, out var s)) { Select(s); }
        });
        XkxkUi.Current.Localization.LanguageChanged += RaiseSelectionProps; // 툴팁 문구 갱신
        ctx.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UiSettings.UiStyle) or nameof(UiSettings.UiStyleOverride) or nameof(UiSettings.CompactWindowKeepsTablet))
            {
                RaiseSelectionProps();
                _ = EvaluateAsync("manual"); // 수동 선택은 디바운스 없이 즉시(자동이면 직전 형 기준 재판정)
            }
        };
    }

    /// <summary>전환 직전 알림(from,to). UI 레이어가 페이드아웃 준비에 쓴다.</summary>
    public event Action<UiStyle, UiStyle>? StyleChanging;

    /// <summary>전환 적용 직후 알림(from,to). UI 레이어가 페이드인에 쓴다. 최초 적용은 알리지 않는다.</summary>
    public event Action<UiStyle, UiStyle>? StyleChanged;

    /// <summary>UI 레이어가 채우면 화면 교체 전에 기다린다(페이드아웃). 줄이기 설정이면 null 로 두거나 즉시 완료.</summary>
    public Func<Task>? TransitionOut { get; set; }

    /// <summary>끌기·모달·IME 조합 중이면 true 를 돌려주는 UI 쪽 판정. true 면 끝날 때까지 전환을 미룬다.</summary>
    public Func<bool>? IsInteractionBusy { get; set; }

    /// <summary>기기 분류 공용 진입점 — 호스트 기기 정보(HostTraits)가 있으면 그것으로, 없으면 기존 (터치, 짧은 변) 경로로 판정한다. shortSideFallback 은 SmallestWidthDp 가 비었을 때 채우는 값.</summary>
    internal static DeviceClass ClassifyHost(double shortSideFallback)
    {
        if (xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostTraits is { } traits)
        {
            return UiStyleRules.Classify(traits.SmallestWidthDp is null ? traits with { SmallestWidthDp = shortSideFallback } : traits);
        }

        return UiStyleRules.Classify(xkxk.frameworks.ui.shell.Platform.InputFormFactor.IsTouchDevice, shortSideFallback);
    }

    /// <summary>현재 설정의 compactWindowKeepsTablet 값(진단 조회용).</summary>
    public bool CompactKeepsTablet => _ctx.Settings.CompactWindowKeepsTablet;

    public DeviceClass Device => _device;

    public double ViewWidth => _width;

    /// <summary>실제 적용 중인 형(Pc·Tablet·Mobile). 최초 판정 전은 Auto.</summary>
    public UiStyle Current => _current;

    /// <summary>선택값(시험 강제값 우선).</summary>
    public UiStyle Setting => _ctx.Settings.UiStyleOverride ?? _ctx.Settings.UiStyle;

    public IReadOnlyList<UiStyle> Allowed => UiStyleRules.Allowed(_device);

    /// <summary>선택 UI(메뉴 라디오·제목줄 스위치) 표시 여부. 폰 기기는 숨김.</summary>
    public bool SelectorVisible => UiStyleRules.SelectorVisible(_device);

    /// <summary>마지막 전환 기록(진단용).</summary>
    public (UiStyle From, UiStyle To, string Reason, bool Deferred)? LastTransition { get; private set; }

    public RelayCommand<string?> SelectCommand { get; }

    /// <summary>뷰 크기 변경 시 호출(MainView.SizeChanged → Compact.UpdateWidth). 최초 1회는 즉시 적용, 이후는 200ms 디바운스.</summary>
    public void UpdateSize(double width, double height)
    {
        if (width <= 0)
        {
            return;
        }

        var first = _width <= 0;
        // D-C3-19: 첫 판정은 호스트 창 폭(Android ScreenWidthDp) 우선 — OnLoaded 의 과도기 폭(600 미만)으로 모바일형이 고정되지 않게. 이후는 뷰 폭
        _width = first && xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostWidthDp is { } hostWidth ? hostWidth : width;
        _height = height > 0 ? height : width;
        OnPropertyChanged(nameof(ViewWidth));
        if (first)
        {
            _ = EvaluateAsync("init");
            return;
        }

        _timer.Stop();
        _timer.Start(); // 마지막 변경 뒤 200ms 조용하면 판정
    }

    /// <summary>전환 중 대기 요청을 취소한다(창 닫기·앱 종료).</summary>
    public void Cancel() => _timer.Stop();

    private async Task EvaluateAsync(string reason)
    {
        if (_width <= 0)
        {
            return;
        }

        // D-C3-14: 호스트가 기기 짧은 변을 알려 주면(Android) 첫 레이아웃의 과도기 뷰 높이 대신 그 값으로 기기를 정한다
        _device = ClassifyHost(xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostShortSideDp ?? Math.Min(_width, _height));
        var target = UiStyleRules.Resolve(_device, Setting, _width, _current, _ctx.Settings.CompactWindowKeepsTablet);
        RaiseSelectionProps();
        if (target == _current)
        {
            return;
        }

        if (_busy || (reason != "init" && IsInteractionBusy?.Invoke() == true))
        {
            LastTransition = (_current, target, reason, true);
            _ctx.AddLog($"ui.style deferred {_current}->{target} reason={reason}");
            _timer.Stop();
            _timer.Start(); // 끝날 때까지 200ms 간격으로 다시 확인(마지막 요청 1개만 대기)
            return;
        }

        await ApplyAsync(target, reason);
    }

    private async Task ApplyAsync(UiStyle target, string reason)
    {
        _busy = true;
        try
        {
            var from = _current;
            var initial = from == UiStyle.Auto;
            if (!initial)
            {
                StyleChanging?.Invoke(from, target);
                if (TransitionOut is { } waitOut)
                {
                    await waitOut();
                }
            }

            _current = target;
            if (!initial || target == UiStyle.Tablet) // 기본 트리가 MDI 라 첫 적용은 태블릿형만 문서 탭 배치가 필요하다
            {
                ApplyPreset(from, target);
            }

            if (target == UiStyle.Mobile && !initial && from != UiStyle.Mobile)
            {
                _beforeMobile = from;
            }

            LastTransition = (from, target, reason, false);
            _ctx.AddLog($"ui.style {from}->{target} reason={reason}");
            Raise();
            if (!initial)
            {
                StyleChanged?.Invoke(from, target);
            }
        }
        finally
        {
            _busy = false;
        }

        // 전환 중 들어온 요청(설정·크기 변화)이 있으면 한 번 더 판정(합치기).
        var next = UiStyleRules.Resolve(_device, Setting, _width, _current, _ctx.Settings.CompactWindowKeepsTablet);
        if (next != _current)
        {
            _timer.Stop();
            _timer.Start();
        }
    }

    /// <summary>레이아웃 모델은 모바일형에서도 그대로 남는다. 모바일형으로 갈 땐 프리셋을 쓰지 않고, 직전과 같은 형으로 돌아오면 원래 배치를 그대로 쓴다.</summary>
    private void ApplyPreset(UiStyle from, UiStyle to)
    {
        if (to == UiStyle.Mobile || (from == UiStyle.Mobile && to == _beforeMobile))
        {
            return;
        }

        _ctx.Commands.ApplyPreset(to == UiStyle.Tablet ? LayoutPreset.Tabbed : LayoutPreset.MdiCascade);
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Current));
        foreach (var name in new[] { nameof(SwitchPc), nameof(SwitchTablet), nameof(SwitchMobile), nameof(AutoPc), nameof(AutoTablet), nameof(AutoMobile) })
        {
            OnPropertyChanged(name);
        }
    }
}

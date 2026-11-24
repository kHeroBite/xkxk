// 화면 방식 선택 UI(보기 메뉴 라디오·활성·사유 툴팁, 제목줄 스위치) 상태와 선택 저장·변경 알림 — UiStyleViewModel 의 partial
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

public sealed partial class UiStyleViewModel
{
    // 메뉴 라디오(선택값) — 폰 기기에서는 쓰이지 않는다.
    public bool IsAuto { get => Setting == UiStyle.Auto; set { if (value) { Select(UiStyle.Auto); } } }

    public bool IsPc { get => Setting == UiStyle.Pc; set { if (value) { Select(UiStyle.Pc); } } }

    public bool IsTablet { get => Setting == UiStyle.Tablet; set { if (value) { Select(UiStyle.Tablet); } } }

    public bool IsMobile { get => Setting == UiStyle.Mobile; set { if (value) { Select(UiStyle.Mobile); } } }

    public bool IsAutoEnabled => Allowed.Contains(UiStyle.Auto);

    public bool IsPcEnabled => Allowed.Contains(UiStyle.Pc);

    public bool IsTabletEnabled => Allowed.Contains(UiStyle.Tablet);

    public bool IsMobileEnabled => Allowed.Contains(UiStyle.Mobile);

    // 비활성일 때만 사유 툴팁(활성이면 null). 폰 기기는 메뉴를 숨기지 않고 비활성 + 사유로 보인다.
    public string? AutoTip => IsAutoEnabled ? null : DisabledTip;

    public string? PcTip => IsPcEnabled ? null : DisabledTip;

    public string? TabletTip => IsTabletEnabled ? null : DisabledTip;

    public string? MobileTip => IsMobileEnabled ? null : DisabledTip;

    private static string DisabledTip => Localization.Loc.Tr("menu.view.uiStyle.disabled.phone");

    // 제목줄 스위치 — 선택 표시는 설정이 아니라 실제 적용 형을 따른다. 자동이면 [자동] 칸만 켜지고 판정 칸은 AutoXxx(옅은 표시)로 나뉜다.
    /// <summary>자동 칸 켜짐(보기 메뉴 '자동'과 같은 상태 — 저장값 Setting==Auto 하나만 쓴다).</summary>
    public bool SwitchAuto => Setting == UiStyle.Auto;

    public bool SwitchPc => !SwitchAuto && _current == UiStyle.Pc;

    public bool SwitchTablet => !SwitchAuto && _current == UiStyle.Tablet;

    public bool SwitchMobile => !SwitchAuto && _current == UiStyle.Mobile;

    public bool AutoPc => SwitchAuto && _current == UiStyle.Pc;

    public bool AutoTablet => SwitchAuto && _current == UiStyle.Tablet;

    public bool AutoMobile => SwitchAuto && _current == UiStyle.Mobile;

    /// <summary>[자동] 칸: 꺼져 있으면 자동을 켜고, 켜져 있으면 지금 판정된 형을 수동 선택으로 남긴다.</summary>
    private void ToggleAuto()
    {
        if (SwitchAuto)
        {
            Select(_current == UiStyle.Auto ? UiStyle.Pc : _current); // 아직 판정 전이면 PC형
        }
        else
        {
            Select(UiStyle.Auto);
        }
    }

    /// <summary>선택 저장(이번 실행의 시험 강제값은 풀어 사용자 선택이 보이게 한다). 허용 밖 값은 무시.</summary>
    public void Select(UiStyle style)
    {
        if (!Allowed.Contains(style))
        {
            RaiseSelectionProps();
            return;
        }

        _ctx.Settings.UiStyleOverride = null;
        _ctx.Settings.UiStyle = style;
        RaiseSelectionProps(); // 같은 값 재선택에도 표시를 다시 맞춘다
    }

    // 선택값이 바뀔 때 다시 읽게 할 속성 이름(순서 불변)
    private static readonly string[] SelectionProps =
    {
        nameof(SwitchAuto), nameof(SwitchPc), nameof(SwitchTablet), nameof(SwitchMobile),
        nameof(AutoPc), nameof(AutoTablet), nameof(AutoMobile), nameof(Device), nameof(Setting), nameof(SelectorVisible),
        nameof(IsAuto), nameof(IsPc), nameof(IsTablet), nameof(IsMobile),
        nameof(IsAutoEnabled), nameof(IsPcEnabled), nameof(IsTabletEnabled), nameof(IsMobileEnabled),
        nameof(AutoTip), nameof(PcTip), nameof(TabletTip), nameof(MobileTip),
    };

    private void RaiseSelectionProps()
    {
        foreach (var name in SelectionProps)
        {
            OnPropertyChanged(name);
        }
    }
}

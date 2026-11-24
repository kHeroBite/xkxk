// 패널 기능 켬/끔 상태를 보관하고 형 변경 시 패널 탭 영역의 CanPin 반영·끔 전환 시 핀 해제를 수행하는 게이트
using System;
using Pd.Poc.Shell.Theming;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 패널 기능 게이트. 모드(설정)와 적용 형(UiStyleViewModel.Current)로 <see cref="PanelFeatureRules.IsEnabled"/> 를 계산해 둔다.
/// 끔이면 ① PanelQuery.CanDrop 이 이 값을 읽어 패널 쪽 놓기를 거부하고 ② 패널 탭 영역 CanPin=false 로 📌 를 숨기며
/// ③ 핀 상태 패널은 핀을 푼다. 켬으로 돌아와도 자동으로 다시 핀하지 않는다. 설정 저장·UI 는 이 클래스 밖이다.
/// </summary>
public sealed class PanelFeatureGate
{
    private readonly PocDockFactory _f;
    private PanelFeatureMode _mode = PanelFeatureMode.Auto;
    private UiStyle _applied = UiStyle.Auto;

    internal PanelFeatureGate(PocDockFactory factory) => _f = factory;

    /// <summary>켬/끔이 바뀐 뒤(새 값).</summary>
    public event Action<bool>? EnabledChanged;

    public PanelFeatureMode Mode => _mode;

    public UiStyle Applied => _applied;

    /// <summary>지금 패널 기능을 쓸 수 있는지.</summary>
    public bool Enabled => PanelFeatureRules.IsEnabled(_mode, _applied);

    /// <summary>핀 요청 거절 사유 코드: 패널 기능 끔. 핀 요청 처리는 이 판정을 다른 거절 사유(접힘 등)보다 먼저 본다.</summary>
    public const string PinOffReason = "feature-off";

    /// <summary>이 핀 요청을 패널 기능 끔 때문에 거절해야 하는지(끔이면 핀이 없다). 핀 풀기(pinned=false)는 언제나 허용한다.</summary>
    public bool RefusesPin(bool pinned) => pinned && !Enabled;

    /// <summary>설정 모드를 바꾼다(설정 콤보·복원 경로가 부른다).</summary>
    public void SetMode(PanelFeatureMode mode) => Update(mode, _applied);

    /// <summary>적용 형이 바뀌었음을 알린다(UiStyleViewModel.StyleChanged 연결은 Attach 가 한다).</summary>
    public void SetApplied(UiStyle applied) => Update(_mode, applied);

    /// <summary>적용 형 변경 이벤트를 구독하고 지금 적용 형을 반영한다. 모드는 호출자가 SetMode 로 넣는다.</summary>
    public void Attach(UiStyleViewModel style)
    {
        style.StyleChanged -= OnStyleChanged;
        style.StyleChanged += OnStyleChanged;
        SetApplied(style.Current);
    }

    /// <summary>현재 상태를 패널 탭 영역에 다시 반영한다(레이아웃 생성·복원 뒤 새 탭 영역을 위해 부른다).</summary>
    public void Reapply()
    {
        var enabled = Enabled;
        foreach (var id in _f.PanelIds)
        {
            if (_f.TabsOf(id) is { } tabs)
            {
                tabs.CanPin = enabled;
            }

            if (!enabled && _f.IsPanelPinned(id))
            {
                _f.SetPanelPinned(id, false); // 복원된 핀도 끔 규칙이 이긴다
            }
        }
    }

    private void OnStyleChanged(UiStyle from, UiStyle to) => SetApplied(to);

    private void Update(PanelFeatureMode mode, UiStyle applied)
    {
        var before = Enabled;
        _mode = mode;
        _applied = applied;
        Reapply();
        var after = Enabled;
        if (before != after)
        {
            EnabledChanged?.Invoke(after);
        }
    }
}

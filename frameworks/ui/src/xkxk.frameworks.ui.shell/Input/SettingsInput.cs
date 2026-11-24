// UI 설정 입력 처리(컨트롤 계층) — 문서 창 위에서만 순수 Ctrl+휠(글자 크기 또는 창 배율)·Ctrl+0(초기화)·두 손가락 핀치 처리, 메인 창 크롬은 고정
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Input;

/// <summary>
/// UiSettings(설정 모델)의 전역 입력 처리기와 시작 순서. 글자 크기·창 배율 값 변경은 UiSettings 에 위임한다(옛 Theming/UiSettingsInput + UiSettings 의 Visual·Input 부분, D-LAYER).
/// </summary>
public static class SettingsInput
{
    /// <summary>창 확대 모드 처리기(셸이 설정): (이벤트 원천 Visual, +1/-1/0) → 처리했으면 true.</summary>
    public static Func<Visual?, int, bool>? WindowZoomHandler { get; set; }

    /// <summary>
    /// 앱 시작 시 1회: 기본값 반영 → Ctrl+휠/Ctrl+0/Ctrl±키 전역 처리 등록 → 저장값 비동기 복원.
    /// 등록 순서는 분리 전 UiSettings.Install 과 같다(창 크롬 → 언어 → 핀치 → 색 테마 감시 → Apply → 휠 → 키 → 복원).
    /// </summary>
    public static void Install(UiSettings settings, Application app, ILayoutStore? store)
    {
        settings.InstallBegin(app, store); // 저장소·창 크롬 기본값(UiWindowSettings)·표시 언어(UiLanguageSettings)
        if (app is not null)
        {
            InstallPinch();                // 두 손가락 핀치 확대·축소(odev-7c PinchZoom)
        }

        settings.InstallThemesAndApply();  // 색 테마 감시 → 전체 적용
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(OnWheel, RoutingStrategies.Tunnel);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel);
        settings.StartLoad();              // 끝나면 Loaded 완료
    }

    /// <summary>시각 요소가 문서 래퍼(ZoomDecorator) 안에 있는가 — 메인 창 크롬이면 false(크기 조절 대상 아님).</summary>
    public static bool IsInDocument(Visual? v) =>
        v is not null && v.GetSelfAndVisualAncestors().OfType<IDocumentZoomHost>().Any();

    /// <summary>
    /// delta=+1/-1/0(초기화). 원천이 문서 창 밖이면 false(무시). 창 배율 모드는 WindowZoomHandler 에 위임.
    /// 원격 제어·자동화(input.ctrlWheel 등)도 이 입구를 써서 실제 Ctrl+휠/Ctrl+0 과 같은 판정·처리 경로를 탄다.
    /// </summary>
    public static bool Step(Visual? source, int delta)
    {
        if (!IsInDocument(source))
        {
            return false;
        }

        var settings = UiSettings.Current;
        if (settings.WheelMode == CtrlWheelMode.WindowZoom)
        {
            return WindowZoomHandler?.Invoke(source, delta) == true;
        }

        if (delta > 0) { settings.Increase(); } else if (delta < 0) { settings.Decrease(); } else { settings.ResetFontSize(); }
        return true;
    }

    /// <summary>마지막으로 Ctrl+휠이 처리된 문서 창 안 요소(U1 — Ctrl+0 이 "휠로 배율을 바꾼 그 창"을 찾는 데 쓴다). 약한 참조라 창이 닫히면 사라진다.</summary>
    private static WeakReference<Visual>? s_lastWheel;

    // 순수 Ctrl+휠, macOS 는 Cmd+휠(Shift·Alt 조합 제외 — Alt·Alt+Shift 는 xkxk.frameworks.ui DenseWheel). 포인터가 문서 창(래퍼) 위일 때만 처리·Handled.
    // 메인 창 크롬(메뉴·탭·패널·상태바) 위에서는 무시하고 일반 스크롤로 통과시킨다(사용자 지시 21:40 "메인창은 조절불가").
    private static void OnWheel(TopLevel sender, PointerWheelEventArgs e)
    {
        if (IsCommandModifier(sender, e.KeyModifiers) && e.Delta.Y != 0 && Step(e.Source as Visual, e.Delta.Y > 0 ? 1 : -1))
        {
            s_lastWheel = new WeakReference<Visual>((Visual)e.Source!);
            e.Handled = true;
        }
    }

    // Ctrl+0(macOS Cmd+0) = 초기화(① 문서 기본 글자 크기 / ② 그 창 100%). 크게/작게 키는 제거됨(사용자 지시).
    // U1: 문서가 포커스를 잡지 않는 경우(그리드·간트 등 비포커스 뷰를 휠로만 조절) 포커스 기준으로는 대상이 없어 무시되던 결함 → 대상 해석을 ResetTarget 에 둔다.
    // 터널 단계라 텍스트 입력 중에도 수정키 조합만 가로채고, 수정키 없는 '0' 입력은 그대로 입력란에 간다.
    private static void OnKeyDown(TopLevel sender, KeyEventArgs e)
    {
        if ((e.Key is Key.D0 or Key.NumPad0) && IsCommandModifier(sender, e.KeyModifiers) && Step(ResetTarget(sender, e.Source as Visual), 0))
        {
            e.Handled = true;
        }
    }

    /// <summary>메뉴 '보기 ▸ 문서 글자 기본 크기(Ctrl+0)' 용 — 키 입력과 같은 대상 해석·초기화(글자 크기 모드 = 기본 글자, 창 배율 모드 = 그 창 100%).</summary>
    public static bool ResetFromMenu(TopLevel? top) => top is not null && Step(ResetTarget(top, null), 0);

    /// <summary>Ctrl 단독, 또는 플랫폼 명령 수정키 단독(macOS = Cmd). 다른 수정키가 섞이면 false.</summary>
    internal static bool IsCommandModifier(TopLevel top, KeyModifiers modifiers) =>
        modifiers == KeyModifiers.Control // top 은 호출 형태 통일용 — 12.1.4 는 TopLevel.PlatformSettings 가 비공개라 Application 쪽 설정을 읽는다
        || (Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers is { } cmd && cmd != KeyModifiers.None && modifiers == cmd);

    /// <summary>
    /// Ctrl+0 대상 문서 창 안 요소: ① 포커스(키 원천)가 문서 안이면 그것 ② 같은 창에서 마지막으로 Ctrl+휠한 문서 ③ 이 창의 문서가 하나뿐(SDI 창)이면 그 문서
    /// ④ 활성 문서 래퍼. 메인 창 크롬은 대상이 아니므로 배율이 바뀌지 않는다(크롬 고정).
    /// </summary>
    private static Visual? ResetTarget(TopLevel top, Visual? source)
    {
        if (IsInDocument(source))
        {
            return source;
        }

        if (s_lastWheel?.TryGetTarget(out var last) == true && ReferenceEquals(TopLevel.GetTopLevel(last), top) && IsInDocument(last))
        {
            return last;
        }

        var docs = TabSlotMarker.Descendants(top).Where(z => z is IDocumentZoomHost && z.IsEffectivelyVisible).Take(2).ToList(); // 숨은 탭 문서 하위는 건너뛴다(TabSlotMarker)
        if (docs.Count == 1)
        {
            return docs[0];
        }

        var active = App.Shell?.ActiveDocument;
        return active is null ? null : TabSlotMarker.Descendants(top).FirstOrDefault(z => z is IDocumentZoomHost h && ReferenceEquals(h.Tag, active));
    }

    /// <summary>
    /// Install 때 1회: 두 손가락 핀치(터치·펜, odev-7c Input/PinchZoom)를 Ctrl+휠과 같은 단계 처리에 잇는다.
    /// 문서 창 판정·글자/창 배율 모드·범위·저장을 Ctrl+휠과 공유한다. 핀치는 ±1 만 보내고 초기화(0)는 쓰지 않는다.
    /// </summary>
    private static void InstallPinch()
    {
        PinchZoom.Install();
        PinchZoom.StepHandler = Step;
    }
}

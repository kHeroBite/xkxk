// 도구 머리줄(Dock ToolChromeControl) 판정 — 측면 패널이면 pdSidePanel(▾ 숨김, 모든 플랫폼), 태블릿·모바일 폼팩터에서 폭이 좁으면 pdNarrow 와 여백 축소 리소스를 단다
using Pd.Poc.Shell;
using Pd.Poc.Shell.Platform;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Pd.Ui.Views;

/// <summary>
/// Themes/NarrowToolChrome.axaml 의 ToolChromeControl 스타일이 Enable=True 를 건다. 켜지면 그 컨트롤의 Bounds 폭을 보고 처리한다.
/// 폭 &lt; <see cref="NarrowWidth"/> 이면:
///  · 클래스 pdNarrow → 스타일이 최대화 버튼을 폭 0 으로 접는다. 📌(자동 숨김)는 측면 패널이 아닐 때만 접는다(측면 패널은 ▾ 가 없어 📌 가 유일한 진입로).
///  · 이 컨트롤의 Resources 에 Dock 머리줄 여백 키를 작게 덮는다(템플릿이 DynamicResource 로 참조 — 컨트롤 자신의 리소스가 먼저 조회된다).
///    값 근거: Dock Fluent 12.1.0.12 기본 HeaderMargin(8,0,8,0)·TitleMargin(0,4,8,4)·ButtonMargin(2,0,0,0)(디컴파일 확인). 114dp 탐색기에서 제목 칸 약 50 → 68dp.
/// ✕ 는 항상 남긴다. 좁은 폭 처리는 태블릿·모바일 폼팩터만 하고(D-UX1 — PC 웹은 데스크톱 앱과 같게), 측면 패널 클래스 pdSidePanel(▾ 숨김)은 모든 플랫폼에서 단다(<see cref="Update"/>).
/// </summary>
public static class NarrowToolChrome
{
    /// <summary>이 폭(dp) 미만이면 좁은 머리줄로 처리한다. Fold5 펼침 탐색기 114dp 실측 기준 초기값.</summary>
    public const double NarrowWidth = 160;

    // 좁음/넓음 전환 계측 이름(perf.ops op=ui.narrowtool.apply. 로 조회)
    private const string OpNarrow = "ui.narrowtool.apply.narrow";
    private const string OpWide = "ui.narrowtool.apply.wide";

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enable", typeof(NarrowToolChrome));

    // 좁은 머리줄 처리는 태블릿·모바일 폼팩터만 한다. 기준은 플랫폼이 아니라 폼팩터다(REQ-25·D-UX1 — PC 웹은 데스크톱 앱과 같게).
    // 호스트가 기동 전에 정한 값이라 실행 중 바뀌지 않지만, 정적 초기화 순서에 기대지 않도록 읽을 때마다 판정한다.
    private static bool Applies => InputFormFactor.IsTouchDevice;

    // 좁은 머리줄 여백 3키를 머리줄마다 따로 만든 사전 1개로 묶어 MergedDictionaries Add/Remove 1회로 붙이고 뗀다(키별 쓰기는 알림 3회).
    // ResourceDictionary 는 소유자를 하나만 가질 수 있어(공유하면 'already has a parent') 컨트롤마다 새로 만들고, 뗄 때 쓰도록 컨트롤에 약하게 매달아 둔다(트리에서 떨어져 수거되면 함께 사라진다).
    private static readonly ConditionalWeakTable<Control, ResourceDictionary> Attached = new();

    private static ResourceDictionary CreateNarrowMargins() => new()
    {
        ["DockToolChromeHeaderMargin"] = new Thickness(4, 0, 2, 0),
        ["DockToolChromeTitleMargin"] = new Thickness(0, 4, 2, 4),
        ["DockChromeButtonMargin"] = new Thickness(0),
    };

    static NarrowToolChrome()
    {
        EnableProperty.Changed.AddClassHandler<Control>((c, e) => OnEnableChanged(c, e.GetNewValue<bool>()));
    }

    public static bool GetEnable(Control c) => c.GetValue(EnableProperty);

    public static void SetEnable(Control c, bool value) => c.SetValue(EnableProperty, value);

    private static void OnEnableChanged(Control c, bool enabled)
    {
        c.PropertyChanged -= OnPropertyChanged;
        if (c is TemplatedControl chrome)
        {
            chrome.TemplateApplied -= OnTemplateApplied;
            chrome.TemplateApplied += enabled ? OnTemplateApplied : null;
        }

        if (enabled)
        {
            c.PropertyChanged += OnPropertyChanged;
            Update(c);
        }
        else
        {
            c.Classes.Set("pdSidePanel", false);
            c.Classes.Set("pdTouchPanelMenu", false);
            Apply(c, false);
        }
    }

    /// <summary>
    /// 빈 패널 제목 칸에 Dock 템플릿의 FallbackValue=TITLE 이 새지 않게 같은 경로 바인딩으로 바꾼다(D-TITLE-LEAK, 디컴파일 ToolChromeControl PART_Title).
    /// 템플릿 바인딩은 LocalValue 라 스타일로 덮을 수 없다(위 axaml 주석과 같은 이유). 빈 패널은 빈 제목이다.
    /// </summary>
    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        e.NameScope.Find<TextBlock>("PART_Title")?.Bind(TextBlock.TextProperty, new Binding("ActiveDockable.Title") { FallbackValue = "", TargetNullValue = "" });
        if (sender is TemplatedControl chrome)
        {
            PanelCornerChrome.Attach(chrome); // 패널 끝 확장 버튼 층(요건 3)
            PanelPositionMenu.AttachToChrome(chrome, e.NameScope); // 제목 줄 우클릭 '패널 위치'
        }
    }

    private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if ((e.Property == Visual.BoundsProperty || e.Property == StyledElement.DataContextProperty) && sender is Control c)
        {
            Update(c);
            PanelCornerChrome.Refresh(c);
        }
    }

    /// <summary>
    /// 측면 패널 판정(모든 플랫폼): 머리줄 DataContext(도구 dock)가 탭 영역(PanelDocumentDock)을 품으면 pdSidePanel.
    /// 스타일이 ▾ 를 숨긴다 — 메뉴 대상이 탭 영역이라 '떠 있게·닫기'가 도구 의도(CanFloat/CanClose=false)와 어긋난다(lead 결정 (나)).
    /// 좁은 폭 판정은 태블릿·모바일 폼팩터만. 폭 0(아직 배치 전)은 판정하지 않는다 — 첫 배치 때 Bounds 변경으로 다시 들어온다.
    /// </summary>
    private static void Update(Control c)
    {
        var sidePanel = c.DataContext is Dock.Model.Core.IDock { VisibleDockables: { } v } && v.Any(d => d is Pd.Poc.Shell.Docking.PanelDocumentDock);
        c.Classes.Set("pdSidePanel", sidePanel);
        c.Classes.Set("pdTouchPanelMenu", sidePanel && Applies); // 터치 기기는 ▾ 가 '위치' 메뉴를 연다(PanelPositionMenu)
        PanelPositionMenu.SyncTouchButton(c);
        var w = c.Bounds.Width;
        if (Applies && w > 0)
        {
            Apply(c, w < NarrowWidth);
        }
    }

    /// <summary>상태가 바뀔 때만 클래스·리소스를 바꾼다(같은 상태면 리소스 알림 없음).</summary>
    private static void Apply(Control c, bool narrow)
    {
        if (c.Classes.Contains("pdNarrow") == narrow)
        {
            return;
        }

        using var op = Pd.Poc.Shell.Diagnostics.PerfProbe.Current.TimeOperation(narrow ? OpNarrow : OpWide); // 진단판만 기록, 제품판은 NoopScope
        c.Classes.Set("pdNarrow", narrow);
        if (narrow)
        {
            if (!Attached.TryGetValue(c, out _))
            {
                var dict = CreateNarrowMargins();
                Attached.Add(c, dict);
                c.Resources.MergedDictionaries.Add(dict);
            }
        }
        else if (Attached.TryGetValue(c, out var dict))
        {
            c.Resources.MergedDictionaries.Remove(dict);
            Attached.Remove(c);
        }
    }
}

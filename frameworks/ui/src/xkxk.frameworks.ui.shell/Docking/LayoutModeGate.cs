// 문서 dock 의 LayoutMode 가 매개변수와 같을 때만 dock 을 내보내는 게이트 변환기(D-2 예비안 B — 숨은 쪽 문서 영역을 아예 만들지 않는다)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// Themes/DocumentDockMode.axaml 의 MultiBinding 용. values[0] = 문서 dock(DataContext), values[1] = LayoutMode, 매개변수 = "Tabbed" 또는 "Mdi".
/// 같으면 dock 을, 다르면 <see cref="DocumentAreaClosed.Instance"/> 를 돌려준다 — 래퍼의 지역 DataTemplate 이 빈 Panel 로 그린다.
/// null 을 돌려주지 않는 이유(D-11, ContentPresenter.CreateChild 디컴파일): Content=null 이고 ContentTemplate 이 있으면 FindDataTemplate(null) 이
/// 조상(앱 수준) DataTemplate 까지 올라가 null 에 맞는 템플릿(예: 알림 뷰 FuncDataTemplate)으로 엉뚱한 화면을 만들었고,
/// Panel 의 위쪽 자식(MDI 래퍼)이 그 화면으로 탭 영역을 덮어 탭·집중 모드·측면 패널이 빈칸이 됐다.
/// Dock 의 DocumentLayoutModeMatchConverter 는 불일치 때 false 를 돌려줘(디컴파일 확인) 영역을 비울 수 없어 따로 둔다.
/// </summary>
public sealed class LayoutModeGate : IMultiValueConverter
{
    public static LayoutModeGate Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[1] is not DocumentLayoutMode mode || parameter is not string want)
        {
            return DocumentAreaClosed.Instance;
        }

        // values[2](있으면) = 폰 모드(MainViewModel.IsCompact). 폰 모드 동안은 문서 영역을 만들지 않는다 —
        // 폰 모드 화면이 같은 캐시에서 활성 문서 뷰를 가져가고, 태블릿으로 돌아올 때 새로 만든 문서 영역이 캐시에서 뷰를 되찾게 한다(M4).
        // 테마 전환은 이 값을 바꾸지 않으므로 문서 영역을 다시 만들지 않는다(Q4).
        if (values.Count > 2 && values[2] is true)
        {
            return DocumentAreaClosed.Instance;
        }

        return Enum.TryParse<DocumentLayoutMode>(want, ignoreCase: true, out var expected) && expected == mode && values[0] is not null
            ? values[0]
            : DocumentAreaClosed.Instance;
    }
}

/// <summary>
/// 측면 패널 탭 줄 표시(사용자 요구 10-29, 10-30 모든 폼팩터로 확대): 측면 패널 탭 영역(PanelDocumentDock)의 보이는 탭이 1개 이하이면
/// 탭 줄을 숨기고 패널 머리줄(제목·핀·닫기)만 보인다. 머리줄 제목은 활성 탭 제목이다(Themes/NarrowToolChrome.axaml). 2개 이상·중앙 문서 영역은 그대로 보인다.
/// values[0] = dock(DataContext), values[1] = VisibleDockables.Count(바뀌면 다시 계산 — 탭을 끌어 넣으면 즉시 다시 보인다).
/// </summary>
public sealed class PanelTabStripVisibility : IMultiValueConverter
{
    public static PanelTabStripVisibility Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // U15: SDI(분리 창) 안 문서 영역만 문서가 1개면 탭 줄을 숨긴다(제목줄에 이미 제목이 있다). 2개 이상 합치면 즉시 다시 보인다.
        // 메인 창 문서 칸(탭 문서 모드·분할 칸)은 문서 수와 무관하게 항상 보인다(U14 — 탭 우클릭 '분할 해제'·탭 끌어 합치기).
        if (values.Count < 2 || (values[0] is not PanelDocumentDock && !IsInFloatingWindow(values[0] as IDockable)))
        {
            return true;
        }

        // 분리 창이 칸으로 나뉘어 문서가 2개 이상이면 칸마다 문서가 1개여도 탭 줄을 보인다. 제목줄은 초점 문서 1개만 보여 주므로 탭 줄이 없으면 어느 칸이 어떤 문서인지 알 수 없다(U15).
        // 측면 패널 탭 영역은 이 규칙이 아니다 — 메인 트리 전체 문서 수를 세면 탭 1개 패널에도 탭 줄이 나타난다(실측 android_3b 캡처).
        return values[1] is not int count || count > 1 || (values[0] is IDockable dock && IsInFloatingWindow(dock) && CountDocumentsInWindow(dock) > 1);
    }

    private static int CountDocumentsInWindow(IDockable dockable)
    {
        var top = dockable;
        while (top.Owner is { } owner)
        {
            top = owner;
        }

        return CountDocuments(top);
    }

    private static int CountDocuments(IDockable dockable) => dockable switch
    {
        Dock.Model.Controls.IDocument => 1,
        IDock { VisibleDockables: { } list } => list.Sum(CountDocuments),
        _ => 0,
    };

    /// <summary>
    /// dock 이 분리 창(SDI 호스트 창) 레이아웃 안에 있는가 — 메인 루트의 Windows 레이아웃 안이거나, 소유 사슬 꼭대기 루트가 창에 붙어 있고 메인 루트(팩토리 Root)가 아니면 분리 창이다.
    /// 메인 루트를 명시적으로 뺀다: 메인 루트에도 Window 가 붙는 경우 메인 창 탭 문서 모드·분할 칸의 문서 1개 칸까지 탭 줄이 숨던 회귀가 있었다(c2dev_d 실측 u14_tab_split.png, U14 탭 메뉴·탭 끌기 불가).
    /// 소유 사슬이 끊긴(트리 밖) dock 은 분리 창으로 보지 않는다(탭 줄 표시).
    /// </summary>
    private static bool IsInFloatingWindow(IDockable? dockable)
    {
        // 1순위: 메인 루트가 가진 분리 창(Windows — 데스크톱 실 창·웹 관리형 창 공통) 레이아웃 안에 있으면 SDI 다.
        // 메인 루트 도달 검사(FindDockable(Root))만으로 판정하면 웹 관리형 SDI 창 dock 이 메인 쪽으로 판정돼 SDI 단일 문서 탭 줄이 다시 보였다(web_c2dev_h, U15 회귀 D-C2-4).
        if (dockable?.Factory is PocDockFactory { Root: { Windows: { } windows } } factory)
        {
            foreach (var window in windows)
            {
                if (window.Layout is { } layout && (ReferenceEquals(layout, dockable) || factory.FindDockable(layout, d => ReferenceEquals(d, dockable)) is not null))
                {
                    return true;
                }
            }
        }

        // 2순위(창 목록에 아직 없을 때): 소유 사슬 꼭대기가 메인이 아닌 창 루트면 SDI 다.
        while (dockable?.Owner is { } owner)
        {
            dockable = owner;
        }

        return dockable is Dock.Model.Controls.IRootDock { Window: not null } root
            && !(root.Factory is PocDockFactory f && ReferenceEquals(root, f.Root));
    }
}

/// <summary>모드가 맞지 않아 만들지 않은 문서 영역 자리 표식. 이 형식에만 맞는 지역 DataTemplate(빈 Panel)이 그린다 — 다른 데이터와 섞이지 않는다.</summary>
public sealed class DocumentAreaClosed
{
    public static DocumentAreaClosed Instance { get; } = new();

    private DocumentAreaClosed()
    {
    }
}

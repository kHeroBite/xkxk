// PoC 문서 모델 — 창 ID·열기 값·일련번호·분리 전 위치·배율·주의 환기·프로젝트(옛 PocDockFactory.cs 안의 공개 클래스를 그대로 옮김)
using System;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>PoC 문서. 화면 내용은 IDocumentViewFactory 가 WindowId 로 만든다.</summary>
public sealed class PocDocument : Document, Dock.Controls.DeferredContentControl.IDeferredContentPresentation
{
    /// <summary>
    /// Dock 의 문서 본문 표시기(DeferredContentControl — 탭 DocumentControl·MDI 창·SDI 안 탭 본문)가 이 문서를 지연 표시할지.
    /// 기본은 Dock 이 본문 교체를 Background 한 바퀴 뒤로 미루고 0.09초 투명도 효과(0.85→1)를 붙인다(Dock 12.1.0.12 디컴파일 확인).
    /// 문서 전환 가속(UiSettings.FastDocumentSwitch, 기본 켬)이면 false 를 돌려 바로 붙인다 — 뷰 생성은 여전히 DocumentViewLocator 가 스피너 뒤로 미룬다.
    /// </summary>
    bool Dock.Controls.DeferredContentControl.IDeferredContentPresentation.DeferContentPresentation =>
        !XkxkUi.Current.Settings.FastDocumentSwitch;

    /// <summary>
    /// 창 ID(IUiWindowRegistry 에 등록한 ID). 등록 안 된 창(자리표시 문서)은 열거나 저장본에서 읽은 값 그대로라 다시 저장해도 남는다.
    /// </summary>
    public string WindowId { get; init; } = "";

    /// <summary>열 때 넘긴 값(UiWindowOpenOptions.Parameter). 레이아웃 저장본에는 남지 않아 복원한 문서에서는 null 이다.</summary>
    public object? Parameter { get; init; }

    /// <summary>창별 일련번호(1부터).</summary>
    public int Seq { get; init; }

    /// <summary>SDI 분리 직전 MDI 위치(재결합 시 원위치 복귀용). 분리한 적 없으면 null.</summary>
    public DockRect? PreFloatBounds { get; set; }

    public const double MinZoom = 0.5;
    public const double MaxZoom = 2.0;

    private bool _isAttention;
    private string _project = "";
    private double _zoom = 1.0;

    /// <summary>창 내용 전체 배율(UI-06, 0.5~2.0). 글자·버튼·여백·행 높이가 함께 바뀐다(Views/ZoomDecorator).</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, Math.Clamp(Math.Round(value, 1), MinZoom, MaxZoom));
    }

    /// <summary>주의 환기(반짝임) 중 여부 — 탭 강조 표시(MDI-05).</summary>
    public bool IsAttention
    {
        get => _isAttention;
        set => SetProperty(ref _isAttention, value);
    }

    /// <summary>이 창이 보고 있는 프로젝트(창 간 문맥 연동, MDI-22).</summary>
    public string Project
    {
        get => _project;
        set => SetProperty(ref _project, value);
    }

    private string? _iconKey;
    private bool _isLoading;
    private bool _isLoadSlow;
    private Exception? _lastLoadError;

    /// <summary>화면 ID(SCR-…, 즐겨찾기 대상 ID — 09/20 §4). 메뉴로 연 창만 값이 있다.</summary>
    public string? ScreenId { get; set; }

    /// <summary>불변 4자리 메뉴 ID(09/09). 메뉴로 연 창만 값이 있다.</summary>
    public string? MenuId { get; set; }

    /// <summary>창 아이콘 키(메뉴 아이콘 카탈로그 키, 22 §2). null 이면 창 기본 아이콘(WindowDisplay.IconKeyOf).</summary>
    public string? IconKey
    {
        get => _iconKey;
        set => SetProperty(ref _iconKey, value);
    }

    /// <summary>열기 토큰(WindowLoadState 가 붙인다). 콘텐츠는 IContentLoadReporter 로 받아 첫 조회가 끝나면 완료한다.</summary>
    public ILoadToken? LoadToken { get; internal set; }

    /// <summary>콘텐츠 불러오는 중(WindowLoadState 만 바꾼다) — 창 아이콘 스피너가 따른다(23 §3).</summary>
    public bool IsLoading
    {
        get => _isLoading;
        internal set => SetProperty(ref _isLoading, value);
    }

    /// <summary>가장 오래된 진행 작업이 지연 판정 시간을 넘었다(시계 표지·'오래 걸리는 중').</summary>
    public bool IsLoadSlow
    {
        get => _isLoadSlow;
        internal set => SetProperty(ref _isLoadSlow, value);
    }

    /// <summary>마지막 불러오기 실패(오류 표지를 ui.shell.busy_icon.error_badge_seconds 동안 보인다). 새 열기에서 지워진다.</summary>
    public Exception? LastLoadError
    {
        get => _lastLoadError;
        internal set => SetProperty(ref _lastLoadError, value);
    }

    /// <summary>
    /// 사람이 읽을 이름(문서 제목, 없으면 Id) — UTODO-0015. 탭 항목·MDI 창 같은 ContentControl 의 접근성 이름은
    /// AutomationProperties.Name·자식 TextBlock 이 비면 Content.ToString() 으로 넘어간다(Avalonia 12.1.4 ContentControlAutomationPeer.GetNameCore 디컴파일 확인).
    /// 재정의 전에는 형식 이름(xkxk.frameworks.ui.shell.Docking.PocDocument)이 노출됐다.
    /// 불러오는 중에는 상태를 덧붙인다(K8·AC-020, 23 §3 '불러오는 중'·지연이면 '오래 걸리는 중'). 창 아이콘이 트리에서 빠지는 자리
    /// (제목 탭 ListItem·MDI 창)에서도 화면 읽기 사용자가 상태를 듣게 하려는 것이며, 불러오기가 끝나면 제목만 남는다.
    /// </summary>
    public override string ToString()
    {
        var name = !string.IsNullOrWhiteSpace(Title) ? Title : !string.IsNullOrWhiteSpace(Id) ? Id : base.ToString() ?? nameof(PocDocument);
        return IsLoading ? name + ", " + xkxk.frameworks.ui.shell.Localization.Loc.Tr(IsLoadSlow ? "shell.icon.loadingSlow" : "shell.icon.loading") : name;
    }
}

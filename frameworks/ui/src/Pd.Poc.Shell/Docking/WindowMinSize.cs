// 문서 창 최소 크기 계산·적용(U9 ⑩) — 크롬 수치(캡션 버튼·로고·제목·틀)로 하한을 계산해 MDI 문서(Dock 크기 제약)와 테마 바인딩용 리소스에 넣는다
using System;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// Windows 는 창 최소 추적 크기(GetSystemMetrics SM_CXMINTRACK·SM_CYMINTRACK)를 "제목줄 버튼·아이콘·제목 몇 글자가 보이는 크기"로 둔다
/// (learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getsystemmetrics). 같은 관례로 계산한다:
/// 폭 = 캡션 3버튼(폭+여백) + 캡션 묶음 여백 + 로고 칸(보일 때) + 제목 최소 폭("W…" 측정 + 끌기 칸 여백) + 틀 좌우 여백,
/// 높이 = 제목줄 높이 + 틀 위아래 여백. 상수를 박지 않고 크롬 스타일 전환(CaptionLayout.Applied)·글자 크기 변경 때 다시 계산한다.
/// 결과: 앱 리소스 PdWindowMinWidth·PdWindowMinHeight(테마의 MinWidth·MinHeight 바인딩용) + 열린 문서의 IDockable.MinWidth·MinHeight(MDI 끌기 크기 제약).
/// </summary>
public static class WindowMinSize
{
    public const string WidthKey = "PdWindowMinWidth";
    public const string HeightKey = "PdWindowMinHeight";

    /// <summary>MDI 머리줄 끌기 손잡이 폭·제목 칸 왼쪽 여백의 단일 정의. Install 이 같은 이름의 앱 리소스로 내보내 테마(MdiDocumentWindowTheme)가 읽게 한다.</summary>
    public const string DragHandleKey = "PdMdiDragHandleWidth";
    public const string TitleInsetKey = "PdMdiTitleInset";
    public const double DragHandleWidth = 4;
    public const double TitleInsetLeft = 2;

    /// <summary>제목 최소 표시 문자열(첫 글자 + 줄임표) — 제목이 완전히 사라지지 않게 하는 하한.</summary>
    private const string TitleProbe = "W…";

    private static bool s_installed;

    public static Size Current { get; private set; }

    /// <summary>MDI 창 크기를 창 최소 크기 이상으로 올린다(위치는 그대로). 최소 크기를 아직 모르면(Current 0) 그대로 돌려준다. 재결합·복원·계단식이 좁은 창을 만들지 않게 한다(D-26).</summary>
    public static Dock.Model.Core.DockRect AtLeast(Dock.Model.Core.DockRect b)
    {
        var m = Current;
        return m.Width <= 0 || m.Height <= 0 || (b.Width >= m.Width && b.Height >= m.Height)
            ? b
            : new Dock.Model.Core.DockRect(b.X, b.Y, Math.Max(b.Width, m.Width), Math.Max(b.Height, m.Height));
    }

    /// <summary>앱 시작 시 1회(WindowKeyGestures.Install 에서). 셸이 아직 없으면 리소스만 계산한다.</summary>
    public static void Install()
    {
        if (s_installed || Application.Current is not { } app)
        {
            return;
        }

        s_installed = true;
        app.Resources[DragHandleKey] = DragHandleWidth;
        app.Resources[TitleInsetKey] = new Thickness(TitleInsetLeft, 0, 0, 0);
        CaptionLayout.Applied += Recompute;
        UiSettings.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiSettings.BaseFontSize))
            {
                Recompute();
            }
        };
        if (App.Shell is WindowCommands shell)
        {
            shell.OpenDocuments.CollectionChanged += OnDocumentsChanged;
        }

        Recompute();
    }

    /// <summary>현재 크롬 수치로 하한을 다시 계산해 리소스·열린 문서에 반영한다.</summary>
    public static void Recompute()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var capMargin = Res(app, "PdCapMargin", default(Thickness));
        var opsMargin = Res(app, "PdCaptionOpsMargin", default(Thickness));
        var frame = Res(app, "PdChromeFramePadding", default(Thickness));
        var caption = 3 * (Res(app, "PdCapWidth", 0.0) + capMargin.Left + capMargin.Right) + opsMargin.Left + opsMargin.Right;
        var logo = Res(app, "PdLogoVisible", true) ? Res(app, "PdLogoWidth", 0.0) : 0;
        var title = Math.Ceiling(MeasureTitle() + 8); // 8 = SdiTitleBar DragArea Padding(4,0) 좌우 합. 올림 = PdTitleMinReserve 와 같은 값
        // MDI 머리줄 고정 칸(끌기 손잡이·제목 칸 왼쪽 여백) — 정의는 아래 상수 하나, 테마는 같은 이름의 리소스로 읽는다(코드·테마 값 일치)
        var mdiFixed = Res(app, DragHandleKey, DragHandleWidth) + Res(app, TitleInsetKey, new Thickness(TitleInsetLeft, 0, 0, 0)).Left;
        var width = Math.Ceiling(caption + logo + title + mdiFixed + frame.Left + frame.Right);
        // 제목 최소 칸(MDI 머리줄 PdTitleMinReserve)도 같은 값으로 함께 건다 — 글자 크기 변경 때도 하한과 예약이 벌어지지 않게(CaptionLayout 은 크롬 스타일 적용 때만 계산)
        WindowChromeStyle.Set(app, "PdTitleMinReserve", new Thickness(title, 0, 0, 0));
        var height = Math.Ceiling(Res(app, "PdMdiTitleHeight", 0.0) + frame.Top + frame.Bottom);
        Current = new Size(width, height);
        // SDI 호스트 창 테마(SdiHostWindowTheme MinWidth/MinHeight)가 DynamicResource 로 읽는 앱 최상위 리소스 — ChromeResources 기본 0 을 덮는다
        app.Resources[WidthKey] = width;
        app.Resources[HeightKey] = height;
        var terms = string.Create(CultureInfo.InvariantCulture,
            $"min={width}x{height} mdiFixed={mdiFixed} caption=3x({Res(app, "PdCapWidth", 0.0)}+{capMargin.Left + capMargin.Right})+{opsMargin.Left + opsMargin.Right}={caption} logo={logo}(visible={Res(app, "PdLogoVisible", true)}) title={title:0.#}(probe+8) frameLR={frame.Left + frame.Right} titleH={Res(app, "PdMdiTitleHeight", 0.0)} frameTB={frame.Top + frame.Bottom}");
        Pd.Client.Data.Logging.PdLog.Info("window", "최소 창 크기 계산 " + terms); // U9 ⑩ 각 항 기록(다음 실측에서 하한 산식 대조)
        Diagnostics.PerfProbe.Current.SetText("window_min_size", terms); // 진단판 원격 perf.get 으로 읽기
        if (App.Shell is { } shell)
        {
            foreach (var doc in shell.Documents)
            {
                Apply(doc);
            }
        }
    }

    private static void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null)
        {
            return;
        }

        foreach (var item in e.NewItems)
        {
            if (item is PocDocument doc)
            {
                Apply(doc);
            }
        }
    }

    private static void Apply(PocDocument doc)
    {
        doc.MinWidth = Current.Width;
        doc.MinHeight = Current.Height;
    }

    private static double MeasureTitle()
    {
        var size = UiSettings.Current.BaseFontSize > 0 ? UiSettings.Current.BaseFontSize : 12;
        return new FormattedText(TitleProbe, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, size, null).Width;
    }

    private static T Res<T>(Application app, string key, T fallback) =>
        app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is T t ? t : fallback;
}

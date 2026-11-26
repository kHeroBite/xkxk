// 문서 창 크롬 스타일(자동·Windows 11·macOS·Mars 클래식)과 창 그림자 — 스타일 차이를 앱 리소스 별칭·수치로 바꿔 템플릿 1개로 그린다(UI-10, 옛 WindowCommands.ChromeStyle)
using System;
using Avalonia;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>정적 상태만 가진다(K4 — 옛 WindowCommands static 필드를 그대로 옮김). 외부 호출부는 W5 까지 WindowCommands 의 전달자를 거친다.</summary>
[WindowCommandsPart]
internal static partial class WindowChromeStyle
{
    private static bool s_shadowEnabled = true;
    private static ChromeStyleKind s_style = ChromeStyleKind.Auto;
    private static bool s_hooked;

    /// <summary>창 그림자 켜기/끄기(PERF-UI 비교·설정). MarsClassic 은 원래 그림자가 없다.</summary>
    public static bool ShadowEnabled
    {
        get => s_shadowEnabled;
        set { s_shadowEnabled = value; ApplyShadow(); }
    }

    // ── 입체감 조절값(Mars 비교 튜닝용, 테마와 무관한 수치 — 색은 테마 토큰) ──
    private static double s_shadowSpread = 1.0;
    private static double s_shadowOpacity = 1.0;
    private static double s_highlight;

    /// <summary>그림자 크기 배율(오프셋·블러에 곱함, 0.25~3). 1 = 기본 2겹 값.</summary>
    public static double ShadowSpread
    {
        get => s_shadowSpread;
        set { s_shadowSpread = Math.Clamp(value, 0.25, 3); ApplyShadow(); }
    }

    /// <summary>그림자 농도 배율(알파에 곱함, 0~3). 1 = 기본.</summary>
    public static double ShadowOpacity
    {
        get => s_shadowOpacity;
        set { s_shadowOpacity = Math.Clamp(value, 0, 3); ApplyShadow(); }
    }

    // ── 비활성 창 제목줄 흐림 방식(Q5): false = 컨테이너 Opacity(기본, 오프스크린 층), true = 반투명 색 브러시(층 없음) ──
    internal static bool s_inactiveDimBrush; // WindowChromeDiag 가 바꾼다
    internal static double? s_inactiveOpacityOverride; // 진단 전용 덮어쓰기(WindowChromeDiag), null = 스타일 기본값
    private static readonly System.Collections.Generic.Dictionary<string, SolidColorBrush> s_dimBrushes = new(StringComparer.Ordinal);

    private static bool s_accentTitle = true; // team-lead 23:5x: Windows 스타일도 진한 액자 띠가 기본

    /// <summary>
    /// Windows 스타일 제목줄·띠를 강조 제목줄 색(PdBrushMdiTitleBar — Mars 팔레트면 #40568D, 기본 true)으로 칠한다(진한 액자 띠, team-lead 23:5x 결정).
    /// false 면 Win11 기본처럼 표면색 제목줄(비교용).
    /// </summary>
    public static bool AccentTitleBar
    {
        get => s_accentTitle;
        set { s_accentTitle = value; ApplyShadow(); }
    }

    /// <summary>창 안쪽 위 하이라이트 선 두께(px, 0~2, 색 = 토큰 PdBrushWindowHighlight). 0 = 없음(기본). Windows 스타일에만 적용.</summary>
    public static double HighlightThickness
    {
        get => s_highlight;
        set { s_highlight = Math.Clamp(value, 0, 2); ApplyShadow(); }
    }

    /// <summary>
    /// 크롬 스타일이 실제로 바뀐 뒤 1회 알린다(값이 같으면 알리지 않는다). 자동화·원격이 직접 바꿔도
    /// UiSettings(설정 콤보·저장값)가 구독해 따라오게 하기 위함. 구독자가 같은 값을 다시 넣어도 여기서 걸러져 재귀하지 않는다.
    /// </summary>
    public static event Action<ChromeStyleKind>? ChromeStyleChanged;

    /// <summary>크롬 스타일. 바꾸면 모든 창(MDI·SDI·웹 관리형)에 즉시 반영된다. 같은 값이면 아무 일도 하지 않는다.</summary>
    /// <summary>실제로 그리는 스타일(강제 지정 → 설정값, Auto 면 실행 OS — ChromePlatform.Resolve).</summary>
    public static ChromeStyleKind EffectiveStyle => ChromePlatform.Resolve(s_style);

    public static ChromeStyleKind ChromeStyle
    {
        get => s_style;
        set
        {
            if (s_style == value)
            {
                return;
            }

            s_style = value;
            ApplyShadow();
            ChromeStyleChanged?.Invoke(value);
        }
    }

    /// <summary>호환용(설정·자동화·원격 기존 연결): true = MarsClassic, false = Windows.</summary>
    public static bool MarsFrame
    {
        get => EffectiveStyle == ChromeStyleKind.MarsClassic;
        set => ChromeStyle = value ? ChromeStyleKind.MarsClassic : ChromeStyleKind.Windows;
    }

    /// <summary>
    /// 크롬 리소스를 다시 만든다(이름은 호환용). 테마 변경(UiSettings.Changed) 때 자동 재적용.
    /// 수치: Windows 11 공식 값(모서리 8px·외곽선 1px·제목줄 32px, odev-1 05_창크롬_OS디자인_기준.md)과 공식 값이 없는 PD 설계값(그림자·버튼 폭·hover 색).
    /// </summary>
    public static void ApplyShadow()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        if (!s_hooked)
        {
            s_hooked = true;
            XkxkUi.Current.Settings.Changed += OnSettingsChanged; // 입력이 같으면 생략(WindowChromeStyle.Design.cs, K13)
        }

        var eff = EffectiveStyle; // 강제 지정 → 설정값(Auto 면 실행 OS) — ChromePlatform
        var mars = eff == ChromeStyleKind.MarsClassic;
        var mac = eff == ChromeStyleKind.MacOS;
        ApplySizes(app, mars, mac);
        var inactive = s_inactiveOpacityOverride ?? (mars ? 0.7 : 0.75); // 비활성은 회색 띠(#808080)로 이미 구분되므로 글자·아이콘은 약하게만 흐림(PD 설계값)
        // 흐림 방식: Opacity 면 컨테이너 투명도, 브러시면 투명도 1(층 없음) + 아래 Inactive 별칭이 같은 알파의 색으로 흐린다.
        Set(app, "PdChromeInactiveOpacity", s_inactiveDimBrush ? 1.0 : inactive);

        // 색 별칭(브러시 인스턴스는 ThemeApplier 가 색만 바꿔 유지하므로 별칭은 스타일이 바뀔 때만 다시 걸면 된다)
        var accent = mars || (s_accentTitle && !mac); // 강조 제목줄(남색 띠) — Mars 클래식은 항상, Windows 는 스위치, macOS 는 표면색
        ApplyTitleColors(app, mars, accent, inactive);
        // 캡션 3버튼 배치·모양 — 상위 별칭(PdChromeGlyph·Hover·Pressed)을 건 뒤에 부른다(앞에 두면 강조 띠 전환 때 한 박자 늦게 따라왔다)
        CaptionLayout.Apply(app, eff, s_inactiveDimBrush ? 1.0 : inactive);
        ApplyWindowShadow(app, mars);
        s_appliedInputs = Inputs(app); // 설정 변경 알림 때 비교할 입력 서명(WindowChromeStyle.Design.cs)
        Applied?.Invoke(); // W1: SDI 그림자(SdiShadowStack.SyncAll)가 이동 없이 즉시 따라오게 한다
#if PD_DIAGNOSTICS
        ChromeApplyDiag.Log();
#endif
    }

    /// <summary>ApplyShadow 가 크롬 리소스를 다시 건 뒤 매번 알린다(UI 스레드). 구독: Desktop SdiShadowStack.</summary>
    public static event Action? Applied;

    /// <summary>크기: 모서리·띠·제목줄·버튼·아이콘(ApplyShadow 에서 분리 — 메서드 60줄 상한).</summary>
    private static void ApplySizes(Application app, bool mars, bool mac)
    {
        Set(app, "PdChromeCorner", new CornerRadius(mars ? 0 : mac ? 10 : 8));     // Win11 공식: 창 8px, 최대화 0(템플릿 :maximized). macOS 10 은 PD 설계값(공식 수치 없음)
        Set(app, "PdChromeInnerCorner", new CornerRadius(mars ? 0 : mac ? 9 : 7)); // 내용 잘라냄 = 바깥 모서리 - 외곽선 1px
        // C3 본문 반경: Windows 0 — 곡선은 잘라내는 쪽 1곳(MDI PdClip 7·SDI DWM 8)만 둔다(본문 반경이 어긋나면 틈으로 틀 색이 초승달처럼 비쳤다). macOS 9 그대로
        Set(app, "PdChromeContentCorner", new CornerRadius(0, 0, mac ? 9 : 0, mac ? 9 : 0));
        Set(app, "PdSdiFrameThickness", new Thickness(mars ? 1 : 0));    // SDI(OS 창): Windows 는 OS 가 외곽선·그림자를 그린다
        // 액자 띠: Mars 클래식만(입체감의 핵심 — MDI 배경 → 진한 띠 → 흰 본문, 띠 3 + 제목줄 23). C3: Windows 는 Win11 처럼 띠 0(본문이 1px 외곽선에 닿음), macOS 도 0
        Set(app, "PdChromeFramePadding", mars ? new Thickness(2, 3, 2, 3) : default);
        Set(app, "PdMdiTitleHeight", mars ? 23.0 : mac ? 28.0 : 32.0); // macOS 28 은 PD 설계값(공식 수치 없음)
        Set(app, "PdCaptionButtonWidth", mars ? 18.0 : 36.0);   // 최소·최대·닫기(U11: 46 → 36, PD 설계값 — 05_창크롬 문서 §2.6)
        // 기능 버튼 7개. 그림 사이 빈 공간 1/2(사용자 지시 2026-11-05, 실측 18 → 9, 폭 28 → 19, 클래식 16 → 13). 터치 기기는 눌림 영역을 지키려고 줄이지 않는다(공통원칙 §4.2)
        var touch = xkxk.frameworks.ui.shell.Platform.InputFormFactor.IsTouchDevice;
        Set(app, "PdChromeButtonWidth", touch ? (mars ? 16.0 : 28.0) : (mars ? 13.0 : 19.0));
        Set(app, "PdChromeStateMargin", mars ? new Thickness(0, 2) : new Thickness(1, 4)); // 호버·켜짐 채움이 줄 전체를 칠하지 않고, 이웃 채움과 붙지 않게 하는 여백
        Set(app, "PdChromeButtonHeight", mars ? 22.0 : 32.0);
        Set(app, "PdChromeButtonCorner", new CornerRadius(mars ? 2 : 4));
        Set(app, "PdChromeIconSize", mars ? 11.0 : 12.0);
        Set(app, "PdCaptionGlyphSize", mars ? 9.0 : 10.0);       // Win11 실측 캡처(notepad x4): 글리프 약 10px·버튼 간격 약 46px
        Set(app, "PdLogoWidth", mars ? 18.0 : 48.0);             // Win11 공식: 아이콘 16px 을 왼쪽 16px 에, 제목은 아이콘에서 16px
        Set(app, "PdLogoIconSize", mars ? 11.0 : 16.0);
    }

    /// <summary>제목줄·외곽선·hover 색 별칭과 비활성 별칭(ApplyShadow 에서 분리 — 메서드 60줄 상한).</summary>
    private static void ApplyTitleColors(Application app, bool mars, bool accent, double inactive)
    {
        Alias(app, "PdChromeTitleActive", accent ? "PdBrushMdiTitleBar" : "PdBrushSurface");
        Alias(app, "PdChromeTitleInactive", accent ? "PdBrushMdiTitleBarInactive" : "PdBrushBackground");
        Alias(app, "PdChromeTitleText", accent ? "PdBrushMdiTitleText" : "PdBrushText");
        Alias(app, "PdChromeGlyph", mars ? "PdBrushChromeGlyph" : accent ? "PdBrushMdiTitleText" : "PdBrushText");
        Alias(app, "PdChromeBorderActive", mars ? "PdBrushWindowBorderActive" : "PdBrushWindowHairlineActive");
        Alias(app, "PdChromeBorderInactive", mars ? "PdBrushWindowBorderInactive" : "PdBrushWindowHairlineInactive");
        // hover·누름: 진한 띠 제목줄(강조색)에서는 흰 반투명, 표면색 제목줄에서는 검정 반투명 — 검정 10% 는 남색 위에서 보이지 않았다(RETEST 3차)
        Alias(app, "PdChromeHover", accent ? "PdBrushMdiTitleButtonHover" : "PdBrushCaptionHover");
        Alias(app, "PdChromePressed", accent ? "PdBrushMdiTitleButtonChecked" : "PdBrushCaptionPressed");
        ApplyInactiveAliases(app, mars, accent, inactive);
    }

    /// <summary>창 그림자 2겹과 위 하이라이트 선(ApplyShadow 에서 분리 — 메서드 60줄 상한).</summary>
    private static void ApplyWindowShadow(Application app, bool mars)
    {
        SetMany(app, ShadowPair(app, mars)); // 두 키를 한 번에(앱 전체 리소스 알림 1회)
        Set(app, "PdChromeHighlightThickness", new Thickness(0, mars ? 0 : s_highlight, 0, 0));
    }

    /// <summary>문서 창 그림자 2키(활성·비활성) 값. 디자인 테마 그림자가 있으면 그 값(WindowChromeStyle.Design.cs).</summary>
    private static (string Key, object Value)[] ShadowPair(Application app, bool mars)
    {
        // 그림자: 가까운 그림자 + 넓게 퍼진 그림자 2겹(PD 설계값 — 공식 픽셀 값 없음). 활성 깊게·비활성 얕게.
        // 진하기는 테마 토큰 PdBrushWindowShadow 의 알파(밝은 테마 0x70 기준)에 비례 — 어두운 테마는 더 진하다.
        var c = app.TryGetResource("PdBrushWindowShadow", app.ActualThemeVariant, out var res) && res is ISolidColorBrush b
            ? b.Color
            : Color.FromArgb(0x70, 0, 0, 0);
        var on = s_shadowEnabled && !mars;
        // 띠의 명암 대비를 해치지 않게 약하게(team-lead 23:5x — 입체감은 띠 계단이 주, 그림자는 PD 강화분)
        // 비활성 = 1겹(활성 가까운 겹과 같은 값) — 20창 이동 시 그림자 비용 절감(lead 00:4x 채택). 활성 전환 때 넓은 겹만 생기고 사라져 모양이 튀지 않는다.
        // 비활성 그림자 공식 수치: 출처 없음(05_창크롬_OS디자인_기준.md 17행 "확인 불가") — PD 설계값.
        var active = on ? Two(c, (0, 1, 3, 0x20), (0, 4, 12, 0x30)) : default;
        // D-C2-44: 비활성도 2겹(활성 가까운 겹 + 활성의 절반 진하기·작은 넓은 겹) — 한 겹은 창 밖에서 거의 보이지 않았다(otest-2: 어두운 화소 0). Win11 비활성 메모장은 활성의 약 절반이다.
        var inactive = on ? Two(c, (0, 1, 3, 0x20), (0, 3, 8, 0x18)) : default;
        WithDesignShadow(on, ref active, ref inactive); // 디자인 테마 그림자(덮어쓰기 왕복 없이 한 번에, K13)
        return new (string, object)[] { ("PdMdiShadow", active), ("PdMdiShadowInactive", inactive) };
    }

    private static Avalonia.Styling.IStyle? s_inactiveStyles; // Themes/ChromeInactive.axaml(1회 생성 후 넣고 뺀다)
    private static bool s_inactiveStylesAttached;

    /// <summary>
    /// 비활성 흐림 전역 스타일(ChromeInactive.axaml)은 브러시 방식일 때만 앱 스타일 맨 끝에 붙인다(M1).
    /// Opacity 방식(기본·제품 빌드 유일)에서는 별칭이 원래 브러시라 겉모습은 같고, 창마다 :not(:active) 선택자 평가 비용만 들기 때문이다.
    /// 맨 끝에 붙여야 MdiChrome.axaml 의 pdChrome 전역 스타일보다 뒤가 되어 이긴다.
    /// </summary>
    private static void SyncInactiveStyles(Application app)
    {
        if (s_inactiveDimBrush == s_inactiveStylesAttached)
        {
            return;
        }

        s_inactiveStyles ??= XkxkUi.Current.Views.CreateInactiveChromeStyle();
        if (s_inactiveDimBrush)
        {
            app.Styles.Add(s_inactiveStyles);
        }
        else
        {
            app.Styles.Remove(s_inactiveStyles);
        }

        s_inactiveStylesAttached = s_inactiveDimBrush;
    }

    private static BoxShadows Two(Color c, (double X, double Y, double Blur, int A) near, (double X, double Y, double Blur, int A) far) =>
        new(Make(c, near), new[] { Make(c, far) });

    private static BoxShadows One(Color c, (double X, double Y, double Blur, int A) s) => new(Make(c, s));

    private static BoxShadow Make(Color c, (double X, double Y, double Blur, int A) s) => new()
    {
        OffsetX = s.X * s_shadowSpread,
        OffsetY = s.Y * s_shadowSpread,
        Blur = s.Blur * s_shadowSpread,
        Color = Color.FromArgb((byte)Math.Min(255, s.A * c.A / 0x70 * s_shadowOpacity), c.R, c.G, c.B),
    };

    /// <summary>
    /// factor 1 이면 원본 브러시를 그대로 별칭으로 건다. 1 미만이면 별칭마다 브러시 인스턴스 1개를 캐시해 Color(원본 × 알파)만 바꾼다 —
    /// 인스턴스가 같아 Set 이 다시 쓰지 않으므로 테마·설정 변경 때 리소스 알림이 나지 않는다(Q5 캐시 조건).
    /// </summary>
    private static void DimAlias(Application app, string alias, string source, double factor)
    {
        if (factor >= 1)
        {
            Alias(app, alias, source);
            return;
        }

        if (app.TryGetResource(source, app.ActualThemeVariant, out var res) && res is ISolidColorBrush src)
        {
            DimColor(app, alias, src.Color, factor);
        }
    }

    /// <summary>고정 색(예: 닫기 hover 흰 글리프)의 흐림 별칭. factor 1 이면 그 색 그대로(캐시 브러시 1개, 같은 색이면 다시 쓰지 않음).</summary>
    private static void DimColor(Application app, string alias, Color c, double factor)
    {
        var color = Color.FromArgb((byte)Math.Round(c.A * Math.Min(factor, 1)), c.R, c.G, c.B);
        if (!s_dimBrushes.TryGetValue(alias, out var brush))
        {
            brush = new SolidColorBrush(color);
            s_dimBrushes[alias] = brush;
        }

        brush.Color = color;
        Set(app, alias, brush);
    }

    internal static void Alias(Application app, string alias, string source) // CaptionLayout 도 쓴다
    {
        if (app.TryGetResource(source, app.ActualThemeVariant, out var brush) && brush is not null)
        {
            Set(app, alias, brush);
        }
    }

    internal static void Set(Application app, string key, object value) // CaptionLayout 도 쓴다
    {
        if (!app.Resources.TryGetValue(key, out var old) || !Equals(old, value))
        {
            app.Resources[key] = value;
        }
    }
}

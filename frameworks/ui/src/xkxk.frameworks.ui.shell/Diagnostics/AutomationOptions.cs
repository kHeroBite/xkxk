// 실행 인자 자동화(AutomationBridge)가 해석한 결과를 담는 옵션 객체 — 플랫폼 중립
#if PD_DIAGNOSTICS // 진단 빌드 전용. 제품 빌드에는 자동화 인자 해석이 없다(AutomationBridge.Product.cs)
using System.Collections.Generic;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>자동화 인자. 플랫폼 중립(호스트가 문자열 배열로 넘긴다 — 브라우저는 URL 쿼리를 --인자 배열로 변환).</summary>
public sealed class AutomationOptions
{
    public List<(DocumentKind Kind, int Count)> Open { get; } = new();
    public string? Layout { get; set; }
    public int Sdi { get; set; }
    public (double Dx, double Dy)? MoveAll { get; set; }
    public bool SaveLayout { get; set; }
    public bool LoadLayout { get; set; }
    public string? Screenshot { get; set; }
    public string? PerfOut { get; set; }
    public double? ExitAfter { get; set; }
    public (int X, int Y)? BgPos { get; set; }
    public bool Dedupe { get; set; } // 종류별 고정 제목 + allowDuplicate=false(Mars Fn중복창확인)
    public List<string> Cmds { get; } = new(); // --cmd 목록(쉼표 구분, 순서대로)
    public List<string> Unknown { get; } = new(); // 해석 못한 인자(무시)
    public double? FontSize { get; set; } // --font-size: 기준 글자 크기(UiSettings.BaseFontSize)
    public string? Font { get; set; } // --font: 폰트 이름(UiSettings.FontName)
    public string? Theme { get; set; } // --theme: 테마 Id(ThemeCatalog.Select — pd-light·pd-dark·pd-contrast·pd-ocean·pd-ember·pd-forest)
    public bool? Shadow { get; set; } // --shadow on|off — WindowCommands.ShadowEnabled(UI-10)
    public string? Frame { get; set; } // --frame mars|pd — WindowCommands.MarsFrame(UI-10)
    public bool? FramePump { get; set; } // --frame-pump on|off(기본: --perf-out 있으면 on) — PerfProbe.KeepRendering
    public double? Zoom { get; set; } // --zoom: 활성 문서 창 배율 %(50~200, 셸 SetDocumentZoom)
    public string? ChromeButtons { get; set; } // --chrome-buttons full|min|transparent — WindowCommands.SetChromeButtonsMode(odev-7c, 성능 분리)
    public string? ChromeInactiveDim { get; set; } // --chrome-inactive-dim opacity|brush — SetInactiveDimMode(odev-7c, Q5)
    public string? ChromeInactiveOpacity { get; set; } // --chrome-inactive-opacity 0~1 — SetInactiveOpacity(odev-7c, Q5)
    public bool? FluentOverride { get; set; } // --fluent-override on|off — ThemeApplier.SetDiagnosticOverrides(odev-7c, Q4 분리)
    public bool? ThemeNewTokens { get; set; } // --theme-new-tokens on|off — 같은 함수(23:10 이후 추가 토큰 8개)
    public int SampleLoadDelayMs { get; set; } // --sample-load-delay-ms: 표본 조회 비동기 지연(AC-018a)
    public int SampleUiBlockMs { get; set; } // --sample-ui-block-ms: 표본 조회 중 UI 스레드 점유(AC-018c)
    public string? FluentPalette { get; set; } // --fluent-palette on|off|always(원문) — ThemeApplier.SetFluentPaletteMode(odev-7c, M3 팔레트 몫 분리·M4 대조)
}
#endif

// 도움말 투어 단계 데이터(요건 6) — Avalonia 비의존 레코드. 대상 AutomationId·문구 사전 키·적용 창·UI 방식·플랫폼을 담는다
using System;

namespace Pd.Poc.Shell.Tour;

/// <summary>투어가 설명하는 창 종류. 창마다 자기 단계 집합을 쓴다.</summary>
public enum TourScope
{
    MainWindow,
    MdiDocument,
    SdiWindow,
    Settings,
    Nav,
}

/// <summary>단계가 적용되는 UI 방식(MDI=PC형, DOCK=태블릿형, NAV=모바일형).</summary>
[Flags]
public enum TourModes
{
    Mdi = 1,
    Dock = 2,
    Nav = 4,
    Wide = Mdi | Dock,
    All = Mdi | Dock | Nav,
}

/// <summary>단계·단축키가 적용되는 플랫폼.</summary>
[Flags]
public enum TourPlatforms
{
    Desktop = 1,
    Web = 2,
    Android = 4,
    All = Desktop | Web | Android,
}

/// <summary>투어 한 단계. 대상이 지금 화면에 없거나 숨겨져 있으면 이 단계는 건너뛴다.</summary>
/// <param name="Id">단계 식별자(원격 시험·로그용).</param>
/// <param name="TargetId">대상 AutomationId. '@' 로 시작하면 TourTargets 가 해석하는 앵커다.</param>
/// <param name="TitleKey">제목 사전 키.</param>
/// <param name="BodyKey">본문 사전 키.</param>
/// <param name="Scope">적용 창.</param>
/// <param name="Modes">적용 UI 방식.</param>
/// <param name="Platforms">적용 플랫폼.</param>
/// <param name="Requires">추가 조건 이름(TourConditions). 없으면 항상 적용.</param>
public sealed record TourStep(
    string Id,
    string TargetId,
    string TitleKey,
    string BodyKey,
    TourScope Scope,
    TourModes Modes = TourModes.All,
    TourPlatforms Platforms = TourPlatforms.All,
    string? Requires = null);

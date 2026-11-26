// 레지스트리 등록 주체 상수와 프레임워크 예약 접두어 — 소유(P2)·예약(T-RESERVED) 판정의 기준(UADR-0013)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 아이콘·명령 레지스트리의 등록 주체(Owner) 이름과 프레임워크 예약 접두어를 모은다.
/// </summary>
/// <remarks>
/// <para>등록 주체는 <see cref="UiIconDefinition.Owner"/> 등에 남는 문자열이며 Ordinal(대소문자 구분)로 비교한다.
/// 앱이 실행 중에 직접 등록하면 <see cref="App"/>, 모듈 컨텍스트로 등록하면 그 모듈 ID 가 주체가 된다.</para>
/// <para>예약 접두어(<see cref="ReservedPrefix"/>): 이 접두어로 시작하는 주체는 프레임워크 주체다.
/// 프레임워크 아닌 주체는 아직 없는 <c>xkxk.</c> 명령 ID 를 새로 만들 수 없고(XKUI0308),
/// <c>xkxk.</c> 아이콘 공급자 Id 를 등록할 수 없다(XKUI0206). 아이콘 키는 예약하지 않는다 —
/// 명시 등록이 공급자 키를 가리는 것은 정상 재정의이고, 프레임워크는 아이콘을 명시 등록하지 않고 공급자로만 더하기 때문이다.
/// 모듈 ID 의 <c>xkxk.</c> 접두어도 문서상 프레임워크 예약이지만 지금은 진단으로 검사하지 않는다.</para>
/// <para>확장 규칙: 상수는 추가만 한다(기존 값 변경·제거 금지).</para>
/// </remarks>
public static class UiRegistryOwners
{
    /// <summary>셸 기본 명령의 등록 주체.</summary>
    public const string Shell = "xkxk.shell";

    /// <summary>프레임워크 생성 아이콘 공급자의 Id 이자 등록 주체.</summary>
    public const string Generated = "xkxk.generated";

    /// <summary>앱이 실행 중 레지스트리(<see cref="IUiAppContext"/> 의 레지스트리)로 직접 등록할 때의 주체.</summary>
    public const string App = "app";

    /// <summary>프레임워크 예약 접두어(Ordinal, 대소문자 구분).</summary>
    public const string ReservedPrefix = "xkxk.";

    /// <summary>주체가 프레임워크 주체(예약 접두어로 시작)인지 판정한다.</summary>
    /// <param name="owner">등록 주체. null 이면 false.</param>
    internal static bool IsFramework(string? owner)
    {
        return owner is not null && owner.StartsWith(ReservedPrefix, StringComparison.Ordinal);
    }

    /// <summary>ID 가 예약 접두어로 시작하는지 판정한다(명령 ID·공급자 Id 용).</summary>
    /// <param name="id">검사할 ID. null 이면 false.</param>
    internal static bool IsReserved(string? id)
    {
        return id is not null && id.StartsWith(ReservedPrefix, StringComparison.Ordinal);
    }
}

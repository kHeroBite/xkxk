// 아이콘 등록 옵션 — IUiIconRegistry.Add 의 configure 대리자가 채운다(UADR-0009 원칙 2, UADR-0013)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary><see cref="IUiIconRegistry.Add(string, System.Action{UiIconOptions})"/> 에 쓰는 아이콘 등록 옵션.</summary>
/// <remarks>
/// <para><see cref="PathData"/> 와 <see cref="ResourceKey"/> 는 정확히 하나만 채워야 한다.
/// 둘 다 비었거나 둘 다 채우면 XKUI0203 진단을 남기고 등록하지 않는다.</para>
/// <para>확장 규칙: 새 입력은 기본값이 이전 동작과 같은 속성 추가로만 넓힌다(Add 서명은 바꾸지 않는다).</para>
/// </remarks>
public sealed class UiIconOptions
{
    /// <summary>빈 옵션을 만든다.</summary>
    public UiIconOptions()
    {
    }

    /// <summary>
    /// SVG path 문법 그림 데이터. 좌표는 24 격자 기준이다(메뉴 아이콘·창 아이콘의 현재 그리기 기준).
    /// </summary>
    public string? PathData { get; set; }

    /// <summary>Avalonia 리소스 키. 테마별 값을 둘 수 있으며, 해석 시 리소스에서 Geometry 를 찾는다.</summary>
    public string? ResourceKey { get; set; }

    /// <summary>개발자용 설명(선택).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// 같은 키의 명시 등록을 교체할지 여부. 기본 false.
    /// </summary>
    /// <remarks>
    /// <para>false 이고 같은 키 명시 등록이 이미 있으면 XKUI0202 진단을 남기고 첫 등록을 유지한다.</para>
    /// <para>true 이면 기존 명시 등록을 교체하고 <see cref="UiRegistryChangeKind.Replaced"/> 를 알린다.
    /// 기존 명시 등록이 없으면 새로 등록하고 <see cref="UiRegistryChangeKind.Added"/> 를 알린다(등록 또는 교체, P1).</para>
    /// <para>교체는 등록 주체와 무관하게 허용되며, 교체 뒤 등록 주체와 제거 권한은 교체한 주체로 옮겨 간다(P2).</para>
    /// </remarks>
    public bool Replace { get; set; }
}

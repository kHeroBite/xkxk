// 레지스트리 변경 알림 인자 — 변경 종류와 대상 키·ID(공개 생성자 없음, UADR-0013)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>아이콘·명령 레지스트리의 <c>Changed</c> 이벤트 인자.</summary>
/// <remarks>
/// <para>프레임워크만 만든다(공개 생성자 없음). 이벤트는 레지스트리 잠금 밖에서 발생하며,
/// 구독자 예외는 삼키지 않고 호출자에게 전파된다(<see cref="UiDiagnostics.Reported"/> 와 같다).</para>
/// <para>확장 규칙: 새 정보는 속성 추가로만 넓힌다.</para>
/// </remarks>
public sealed class UiRegistryChangedEventArgs : EventArgs
{
    internal UiRegistryChangedEventArgs(UiRegistryChangeKind kind, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Kind = kind;
        Id = id;
    }

    /// <summary>변경 종류. 모르는 값이면 전체가 바뀐 것으로 보고 다시 읽는다.</summary>
    public UiRegistryChangeKind Kind { get; }

    /// <summary>바뀐 아이콘 키·명령 ID, 또는 공급자 변경이면 공급자 Id.</summary>
    public string Id { get; }
}

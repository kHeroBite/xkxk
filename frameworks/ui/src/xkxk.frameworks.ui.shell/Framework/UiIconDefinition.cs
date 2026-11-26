// 아이콘 정의 불변 값 — 키·원천 종류·그림 데이터·등록 주체(공개 생성자 없음, 팩터리로만 만든다, UADR-0013)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>아이콘 1개의 정의(불변).</summary>
/// <remarks>
/// <para>공개 생성자가 없다. <see cref="FromPathData(string, string)"/> 또는 <see cref="FromResource(string, string)"/> 로 만든다.
/// 팩터리로 만든 값의 <see cref="Owner"/> 는 빈 문자열이고, 레지스트리가 돌려주는 값은 등록 주체를 채운 사본이다.</para>
/// <para><see cref="Kind"/> 가 <see cref="UiIconKind.PathData"/> 면 <see cref="PathData"/> 만,
/// <see cref="UiIconKind.Resource"/> 면 <see cref="ResourceKey"/> 만 값이 있다.</para>
/// <para>확장 규칙: 새 원천은 <see cref="UiIconKind"/> 값과 새 팩터리 추가로만 넓힌다(기존 팩터리 서명 불변).</para>
/// </remarks>
public sealed class UiIconDefinition
{
    private UiIconDefinition(string key, UiIconKind kind, string? pathData, string? resourceKey, string? description, string owner)
    {
        Key = key;
        Kind = kind;
        PathData = pathData;
        ResourceKey = resourceKey;
        Description = description;
        Owner = owner;
    }

    /// <summary>SVG path 문법 그림 데이터로 아이콘 정의를 만든다.</summary>
    /// <param name="key">아이콘 키.</param>
    /// <param name="pathData">SVG path 문법 문자열(24 격자 좌표).</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 또는 <paramref name="pathData"/> 가 null 일 때.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> 또는 <paramref name="pathData"/> 가 비었거나 공백뿐일 때.</exception>
    public static UiIconDefinition FromPathData(string key, string pathData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathData);
        return new UiIconDefinition(key, UiIconKind.PathData, pathData, null, null, string.Empty);
    }

    /// <summary>Avalonia 리소스 키로 아이콘 정의를 만든다.</summary>
    /// <param name="key">아이콘 키.</param>
    /// <param name="resourceKey">Geometry 를 담은 리소스 키(테마별 값 가능).</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 또는 <paramref name="resourceKey"/> 가 null 일 때.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> 또는 <paramref name="resourceKey"/> 가 비었거나 공백뿐일 때.</exception>
    public static UiIconDefinition FromResource(string key, string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return new UiIconDefinition(key, UiIconKind.Resource, null, resourceKey, null, string.Empty);
    }

    /// <summary>아이콘 키.</summary>
    public string Key { get; }

    /// <summary>그림 원천 종류. 모르는 값이면 그리지 못하는 아이콘으로 보고 폴백한다.</summary>
    public UiIconKind Kind { get; }

    /// <summary>SVG path 문법 그림 데이터. <see cref="Kind"/> 가 <see cref="UiIconKind.PathData"/> 가 아니면 null.</summary>
    public string? PathData { get; }

    /// <summary>리소스 키. <see cref="Kind"/> 가 <see cref="UiIconKind.Resource"/> 가 아니면 null.</summary>
    public string? ResourceKey { get; }

    /// <summary>개발자용 설명. 없으면 null.</summary>
    public string? Description { get; }

    /// <summary>
    /// 등록 주체(<see cref="UiRegistryOwners"/> 상수·모듈 ID·공급자 Id). 팩터리로 막 만든 값은 빈 문자열이다.
    /// </summary>
    public string Owner { get; }

    /// <summary>등록 주체를 바꾼 사본을 만든다(레지스트리 전용).</summary>
    /// <param name="owner">등록 주체. null 이면 <see cref="ArgumentNullException"/>.</param>
    internal UiIconDefinition WithOwner(string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return string.Equals(owner, Owner, StringComparison.Ordinal)
            ? this
            : new UiIconDefinition(Key, Kind, PathData, ResourceKey, Description, owner);
    }

    /// <summary>설명을 바꾼 사본을 만든다(레지스트리 전용).</summary>
    /// <param name="description">설명. null 허용.</param>
    internal UiIconDefinition WithDescription(string? description)
    {
        return string.Equals(description, Description, StringComparison.Ordinal)
            ? this
            : new UiIconDefinition(Key, Kind, PathData, ResourceKey, description, Owner);
    }
}

// 아이콘 키를 그릴 Geometry 로 바꾸는 화면 층 도우미 — 레지스트리 해석(PathData 파싱 캐시·리소스 조회) 뒤 모르는 키는 리소스로 폴백(UADR-0013)
using System;
using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// 아이콘 레지스트리(<see cref="IUiAppContext.Icons"/>)의 정의를 Avalonia <see cref="Geometry"/> 로 해석한다.
/// </summary>
/// <remarks>
/// 프레임워크 화면 층 전용이다. 공개 범위는 B16 에서 다시 정한다(Framework/ 밖이라 PublicAPI 분석기 대상이 아니다).
/// PathData 정의는 (키, 경로) 단위로 파싱 결과를 캐시하고, 파싱에 실패하면 키당 1회 XKUI0204 를 남기고 null 을 돌려준다.
/// Resource 정의는 host 에서 테마별 리소스를 찾는다. 레지스트리가 모르는 키는 호환을 위해 host 리소스로 떨어진다(진단 없음).
/// </remarks>
public static class UiIconGeometry
{
    private static readonly ConcurrentDictionary<(string Key, string Path), Geometry?> PathCache = new();

    /// <summary>아이콘 키의 Geometry 를 찾는다. 키가 비었거나 찾지 못하면 null.</summary>
    /// <param name="host">리소스를 찾을 대상(컨트롤·Application). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="theme">리소스 조회 테마. null 이면 host 기본.</param>
    /// <param name="key">아이콘 키.</param>
    public static Geometry? Find(IResourceHost host, ThemeVariant? theme, string? key)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!UiAppContext.Instance.Icons.TryGet(key, out var icon))
        {
            return FromResource(host, theme, key); // 레지스트리 밖 키 — 기존처럼 리소스만 본다.
        }

        switch (icon.Kind)
        {
            case UiIconKind.PathData when icon.PathData is { } path:
                return FromPath(key, path);
            case UiIconKind.Resource when icon.ResourceKey is { } resourceKey:
                return FromResource(host, theme, resourceKey);
            default:
                // 모르는 종류(이후 판에서 추가될 수 있음)는 그리지 못하는 아이콘으로 보고 리소스로 폴백한다.
                return FromResource(host, theme, key);
        }
    }

    private static Geometry? FromResource(IResourceHost host, ThemeVariant? theme, string resourceKey) =>
        host.TryFindResource(resourceKey, theme, out var value) ? value as Geometry : null;

    private static Geometry? FromPath(string key, string path) =>
        PathCache.GetOrAdd((key, path), static k =>
        {
            try
            {
                return StreamGeometry.Parse(k.Path);
            }
            catch (Exception ex)
            {
                UiAppContext.Instance.IconStore.ReportRenderFailure(k.Key, $"PathData 파싱 실패 {ex.GetType().Name}: {ex.Message}"); // i18n-ok 개발자 진단
                return null;
            }
        });
}

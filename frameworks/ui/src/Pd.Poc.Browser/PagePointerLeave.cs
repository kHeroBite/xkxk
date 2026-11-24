// 웹 페이지 떠남 알림 — Avalonia.Browser 는 pointermove·down·up·cancel 만 구독해 포인터가 페이지를 떠나도 앱이 모르므로, 문서 뿌리의 pointerleave 를 근접 표시와 추적 스피너에 전한다
using System;
using System.Runtime.InteropServices.JavaScript;
using Pd.Ui.Controls;

namespace Pd.Poc.Browser;

internal static partial class PagePointerLeave
{
    [JSImport("globalThis.document.documentElement.addEventListener")]
    private static partial void AddEventListener(string type, [JSMarshalAs<JSType.Function>] Action handler);

    /// <summary>터치 판정 브라우저에도 마우스가 붙을 수 있어 폼팩터와 무관하게 건다. 터치 떠남은 근접 표시·스피너가 이미 꺼져 있어 바뀌는 것이 없다.</summary>
    internal static void Install() => AddEventListener("pointerleave", OnPointerLeave);

    private static void OnPointerLeave()
    {
        PanelCornerProximity.NotifyPointerLeftHost();
        PointerFollower.NotifyPointerLeftHost();
    }
}

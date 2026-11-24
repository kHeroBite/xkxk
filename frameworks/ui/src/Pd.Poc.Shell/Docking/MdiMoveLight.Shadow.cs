// 끌기 중 그림자 모드(ui.mdi.move_light.shadow hide|keep, 기본 hide — R7·D-589·01a §2·§3) — MdiMoveLight 의 partial(원본 줄 수 상한으로 나눔)
using System;

namespace Pd.Poc.Shell.Docking;

/// <summary>끌기 중 그림자: Hide = 끄는 창·겹친 정지 창의 그림자를 끈다(빠름, 기본), Keep = 정지 그림자를 그대로 둔다.</summary>
public enum MoveShadowMode
{
    Hide,
    Keep,
}

public static partial class MdiMoveLight
{
    /// <summary>기본값(숨김) — 사용자 지시 2026-11-22 '기본값은 그림자숨기기'.</summary>
    public const MoveShadowMode DefaultShadow = MoveShadowMode.Hide;

    private static MoveShadowMode _shadow = DefaultShadow;

    /// <summary>끌기 중 그림자 모드. 바뀌면 Changed 로 알린다(Views 의 MdiMoveLightener·MdiStaticLight 가 Hide 일 때만 그림자 끄기 클래스를 단다).</summary>
    public static MoveShadowMode Shadow
    {
        get => _shadow;
        set
        {
            var v = Enum.IsDefined(value) ? value : DefaultShadow;
            if (_shadow == v) { return; }
            _shadow = v;
            Changed?.Invoke();
        }
    }

    /// <summary>끄는 동안 그림자를 숨기는가(Hide).</summary>
    public static bool HideShadowWhileMoving => _shadow == MoveShadowMode.Hide;

    /// <summary>설정 키 값(hide·keep).</summary>
    public static string ShadowKey(MoveShadowMode mode) => mode == MoveShadowMode.Keep ? "keep" : "hide";

    /// <summary>설정 키 값 → 모드. null·모르는 값은 기본(hide).</summary>
    public static MoveShadowMode ParseShadow(string? text) =>
        string.Equals(text?.Trim(), "keep", StringComparison.OrdinalIgnoreCase) ? MoveShadowMode.Keep : DefaultShadow;
}

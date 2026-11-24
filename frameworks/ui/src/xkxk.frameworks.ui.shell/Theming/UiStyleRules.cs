// 화면 방식(PC형·태블릿형·모바일형) 기기별 규칙 — UI 비의존 순수 데이터/판정 레이어
using System.Collections.Generic;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>화면 방식 선택값. Auto 는 기기 종류에 따라 해석한다(PC=PC형, 태블릿=태블릿형).</summary>
public enum UiStyle
{
    Auto,

    /// <summary>PC형 = MDI.</summary>
    Pc,

    /// <summary>태블릿형 = DOCK.</summary>
    Tablet,

    /// <summary>모바일형 = NAV(하단 탭 + 삼선).</summary>
    Mobile,
}

/// <summary>접속 기기 종류.</summary>
public enum DeviceClass
{
    Pc,
    Tablet,
    Phone,
}

/// <summary>기기별 허용 형·기본형·자동 전환 판정. Avalonia 의존 없음.</summary>
public static class UiStyleRules
{
    /// <summary>이 폭(dp) 미만이면 모바일형(기존 결정 8). 기기 판정의 짧은 변 기준도 같다.</summary>
    public const double PhoneWidth = 600;

    /// <summary>PC 자동에서 이 폭(dp) 이상이면 PC형(Android 창 크기 등급 expanded).</summary>
    public const double PcWidth = 840;

    /// <summary>경계를 벗어나 위로 복귀할 때 더하는 폭(dp) — 경계 왕복 떨림 방지.</summary>
    public const double Hysteresis = 32;

    private static readonly UiStyle[] AllChoices = [UiStyle.Auto, UiStyle.Pc, UiStyle.Tablet, UiStyle.Mobile];
    private static readonly UiStyle[] PhoneChoices = [UiStyle.Mobile];

    /// <summary>기기 판별: 터치 아님=PC, 터치이고 짧은 변 600 미만=폰, 그 밖=태블릿.</summary>
    public static DeviceClass Classify(bool touch, double shortSide)
        => Classify(new DeviceTraits(PlatformKind.Unknown, HasTouch: touch, SmallestWidthDp: shortSide));

    /// <summary>
    /// 기기 정보 기반 분류(정본 08 §1-3-1 판정표 14행). 순수 함수. 정보가 없으면(null) 기존 크기 규칙으로 폴백한다.
    /// 행 13 iOS idiom → 행 7 데스크톱 앱(PC 유지) → 행 10 ChromeOS(PC 클래스, 정밀 포인터 없이 터치만이면 터치 경로)
    /// → 행 8·11 웹(정밀 포인터+주 hover 이면 PC, 주 포인터가 coarse 이면 터치 우선) → 터치 없음=PC → 행 1~6·12 짧은 변 600 기준 폰/태블릿.
    /// 행 9(DeX)·14(leanback)는 입력 정보가 없어 크기 폴백, 행 3·4 는 SmallestWidthDp 가 기기 기준이라 창 폭·자세로 분류가 흔들리지 않는다.
    /// </summary>
    public static DeviceClass Classify(DeviceTraits traits)
    {
        // 행 13: iOS userInterfaceIdiom
        if (traits.Idiom == DeviceIdiom.Phone)
        {
            return DeviceClass.Phone;
        }

        if (traits.Idiom == DeviceIdiom.Pad)
        {
            return DeviceClass.Tablet;
        }

        // 행 7: Windows·macOS·Linux 앱은 PC(터치 디지타이저는 판정에 쓰지 않는다)
        if (traits.Platform is PlatformKind.Windows or PlatformKind.MacOS or PlatformKind.Linux)
        {
            return DeviceClass.Pc;
        }

        var touch = traits.HasTouch ?? traits.Platform is PlatformKind.Android or PlatformKind.IOS;

        // 행 10: PC 클래스(ChromeOS). 정밀 포인터가 없다고 확인되고 터치가 있을 때만 터치 경로로 보낸다(정보 없으면 PC 우선)
        if (traits.IsPcClass == true && !(traits.PrecisePointer == false && touch))
        {
            return DeviceClass.Pc;
        }

        // 행 8·11·12: 웹. 정밀 포인터(any-pointer: fine)가 있어도 주 입력이 hover 불가(coarse)이면 모바일 기기로 보아 터치 우선
        if (traits.Platform == PlatformKind.Browser && traits.PrecisePointer == true)
        {
            if (traits.HoverCapable == false)
            {
                touch = true;
            }
            else
            {
                return DeviceClass.Pc;
            }
        }

        if (!touch)
        {
            return DeviceClass.Pc;
        }

        var shortSide = traits.SmallestWidthDp ?? 0;
        return shortSide > 0 && shortSide < PhoneWidth ? DeviceClass.Phone : DeviceClass.Tablet;
    }

    /// <summary>기기에서 고를 수 있는 선택값 집합(폰은 모바일형 하나 — 선택 UI 숨김).</summary>
    public static IReadOnlyList<UiStyle> Allowed(DeviceClass device) => device == DeviceClass.Phone ? PhoneChoices : AllChoices;

    /// <summary>기기의 기본형(자동의 1순위 해석).</summary>
    public static UiStyle DefaultOf(DeviceClass device)
        => device switch { DeviceClass.Pc => UiStyle.Pc, DeviceClass.Tablet => UiStyle.Tablet, _ => UiStyle.Mobile };

    /// <summary>선택 UI 를 보일지(폰 기기는 숨김).</summary>
    public static bool SelectorVisible(DeviceClass device) => device != DeviceClass.Phone;

    /// <summary>
    /// 적용 형 판정. 순서: 폰=모바일형 강제 → 수동 선택 유지 → 자동이면 폭 판정(히스테리시스).
    /// previous 는 직전 적용 형(없거나 Auto 면 기본 경계 사용).
    /// </summary>
    public static UiStyle Resolve(DeviceClass device, UiStyle setting, double width, UiStyle previous, bool compactWindowKeepsTablet = false)
    {
        if (device == DeviceClass.Phone)
        {
            return UiStyle.Mobile;
        }

        if (setting != UiStyle.Auto)
        {
            return setting;
        }

        if (width <= 0)
        {
            return DefaultOf(device);
        }

        if (device == DeviceClass.Pc && width >= (previous is UiStyle.Pc or UiStyle.Auto ? PcWidth : PcWidth + Hysteresis))
        {
            return UiStyle.Pc;
        }

        // 옵션 compactWindowKeepsTablet(정본 08 §1-3-1): 태블릿 기기는 창 폭이 좁아도 태블릿형 유지(모바일형 전환만 막는다)
        if (compactWindowKeepsTablet && device == DeviceClass.Tablet)
        {
            return UiStyle.Tablet;
        }

        return width < (previous == UiStyle.Mobile ? PhoneWidth + Hysteresis : PhoneWidth) ? UiStyle.Mobile : UiStyle.Tablet;
    }
}

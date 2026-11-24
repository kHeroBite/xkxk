// 폼팩터 판정(REQ-25·D-UX1) — 화면·동작 분기를 플랫폼(IsBrowser·IsAndroid)이 아니라 폼팩터(터치가 주 입력인 장치인가)로 하도록 한 곳에 모은다
using Pd.Poc.Shell;
using System;
using System.Collections.Generic;

namespace Pd.Poc.Shell.Platform;

/// <summary>
/// 데스크톱 폼팩터(Windows·macOS 앱, PC 브라우저)와 태블릿·모바일 폼팩터(Android·iOS 앱, 같은 기기의 모바일 브라우저)를 가른다.
/// 같은 기기의 앱과 웹은 같은 답을 내야 한다(plan/05_아키텍처/08_지원_폼팩터_용어.md §7). 이 판정은 입력 장치 질의라 플랫폼 분기가 남는 유일한 곳이다.
/// ① 앱: Android·iOS 는 터치 장치, 그 밖(Windows·macOS)은 데스크톱이다.
/// ② 웹: 브라우저 호스트가 기동 전에 <see cref="HostTouch"/> 를 채운다(주 포인터 미디어 쿼리 '(pointer: coarse)', 시험 인자 --form-factor 우선).
/// 폰모드(단일 문서)로 갈지는 이 값이 아니라 폭(600)과 화면 모드 설정으로 정한다(MainViewModel.Compact).
/// </summary>
public static class InputFormFactor
{
    /// <summary>시험 인자 이름. 값은 touch|desktop. 브라우저 호스트가 소비한다(셸 자동화 인자로 넘기지 않는다).</summary>
    public const string Arg = "--form-factor";

    /// <summary>호스트가 알려 준 터치 장치 여부. null 이면 앱 플랫폼으로 판정한다.</summary>
    public static bool? HostTouch { get; set; }

    /// <summary>
    /// 호스트가 알려 준 기기 짧은 변(dp). Android 는 Configuration.SmallestScreenWidthDp. 기기 분류(폰/태블릿)가 첫 레이아웃의 과도기 뷰 높이에 흔들리지 않게 하려는 값이다(D-C3-14).
    /// null 이면(데스크톱·웹) 기존처럼 뷰 크기로 판별한다. 형 전환(폭 규칙)에는 쓰지 않는다 — 분할 화면에서는 실제 폭이 이보다 작을 수 있다.
    /// </summary>
    public static double? HostShortSideDp { get; set; }

    /// <summary>
    /// 호스트가 알려 준 현재 앱 창 폭(dp). Android 는 Configuration.ScreenWidthDp(분할 화면이면 그 창 폭). 형 판정의 첫 폭(init)에만 쓴다 —
    /// 첫 레이아웃의 과도기 뷰 폭(600 미만)으로 모바일형이 고정되지 않게 한다(D-C3-19). null 이면(데스크톱·웹) 뷰 폭.
    /// </summary>
    public static double? HostWidthDp { get; set; }

    /// <summary>
    /// 호스트가 알려 준 기기 정보 전체(Android·브라우저가 기동 전·구성 변경 때 채운다). null 이면 HostTouch·HostShortSideDp 로 기존 방식 판정한다.
    /// 판정은 UiStyleRules.Classify(DeviceTraits) 가 한다.
    /// </summary>
    public static DeviceTraits? HostTraits { get; set; }

    /// <summary>태블릿·모바일 폼팩터(터치가 주 입력)인지. 데스크톱 폼팩터면 false.</summary>
    public static bool IsTouchDevice => HostTouch ?? (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());

    /// <summary>
    /// 앱(데스크톱·Android) 시험 인자 --form-factor touch|desktop(또는 --form-factor=값)을 소비해 강제값으로 넣고 나머지 인자를 돌려준다.
    /// 우선순위 ① 강제값 ② 플랫폼(lead D-UX1). 웹은 브라우저 호스트가 먼저 소비하므로 여기 오지 않는다.
    /// </summary>
    public static IReadOnlyList<string> TakeArg(IReadOnlyList<string> args)
    {
        var rest = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string? value = null;
            if (a.StartsWith(Arg + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = a[(Arg.Length + 1)..];
            }
            else if (string.Equals(a, Arg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                value = args[++i];
            }
            else
            {
                rest.Add(a);
                continue;
            }

            if (Parse(value) is { } forced)
            {
                HostTouch = forced;
            }
        }

        return rest;
    }

    /// <summary>--form-factor 값 해석: "touch" → true, "desktop" → false, 그 밖 → null(판정에 맡김).</summary>
    public static bool? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "touch" or "tablet" or "mobile" => true,
        "desktop" or "pc" => false,
        _ => null,
    };
}

// Android Activity 에서 기기 특성(DeviceTraits)을 수집해 형 판정(InputFormFactor.HostTraits)에 넘기는 수집기
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Input;
using Android.Runtime;
using Android.Views;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.android;

internal static class AndroidDeviceInfo
{
    // 문자열 기능명(.NET 바인딩 상수 유무와 무관하게 사용). 출처 https://developer.android.com/reference/android/content/pm/PackageManager
    private const string FeaturePc = "android.hardware.type.pc";            // FEATURE_PC (API 29)
    private const string FeatureArc = "org.chromium.arc";                   // ChromeOS Android 런타임 기능
    private const string FeatureArcDeviceManagement = "org.chromium.arc.device_management";
    private const string FeatureHinge = "android.hardware.sensor.hinge_angle"; // FEATURE_SENSOR_HINGE_ANGLE (API 30)

    /// <summary>현재 구성(접힘·펼침·창 크기)을 반영한 DeviceTraits 를 만든다. 호출 때마다 다시 읽는다.</summary>
    public static DeviceTraits Collect(Activity activity)
    {
        var pm = activity.PackageManager;
        var cfg = activity.Resources?.Configuration;

        bool? hasTouch = pm is null ? null : pm.HasSystemFeature(PackageManager.FeatureTouchscreen);
        bool? isPcClass = pm is null ? null : (pm.HasSystemFeature(FeaturePc) || pm.HasSystemFeature(FeatureArc) || pm.HasSystemFeature(FeatureArcDeviceManagement));
        // 힌지 센서는 폴더블 후보를 알리는 참고값이다(있으면 true, 없으면 알 수 없음으로 null).
        bool? foldable = pm is not null && OperatingSystem.IsAndroidVersionAtLeast(30) && pm.HasSystemFeature(FeatureHinge) ? true : null;
        bool? multiWindow = OperatingSystem.IsAndroidVersionAtLeast(24) ? activity.IsInMultiWindowMode : null;

        return new DeviceTraits(
            PlatformKind.Android,
            HasTouch: hasTouch,
            PrecisePointer: DetectPrecisePointer(activity, hasTouch),
            SmallestWidthDp: DisplaySmallestWidthDp(activity) ?? (cfg is { SmallestScreenWidthDp: > 0 } ? cfg.SmallestScreenWidthDp : null),
            WindowWidthDp: cfg is { ScreenWidthDp: > 0 } ? cfg.ScreenWidthDp : null,
            IsPcClass: isPcClass,
            IsFoldableCapable: foldable,
            IsMultiWindow: multiWindow);
    }

    /// <summary>
    /// 디스플레이(창이 아님) 기준 짧은 변 dp. 멀티윈도우·freeform 창 크기에 흔들리지 않는다(기기 분류용).
    /// API 30+ 는 WindowManager.MaximumWindowMetrics.Bounds, 그 미만은 Display.GetRealMetrics. 실패하면 null(호출부가 Configuration 으로 폴백).
    /// 출처(미확인, 웹 조회 미수행) https://developer.android.com/reference/android/view/WindowManager#getMaximumWindowMetrics()
    /// </summary>
    private static int? DisplaySmallestWidthDp(Activity activity)
    {
        try
        {
            var density = activity.Resources?.DisplayMetrics?.Density ?? 0f;
            if (density <= 0f || activity.GetSystemService(Context.WindowService) is not { } svc)
            {
                return null;
            }

            using var wm = svc.JavaCast<IWindowManager>();
            int shortPx;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var b = wm.MaximumWindowMetrics.Bounds;
                shortPx = Math.Min(b.Width(), b.Height());
            }
            else
            {
                var dm = new Android.Util.DisplayMetrics();
#pragma warning disable CA1422 // API 30 미만 경로 전용
                wm.DefaultDisplay?.GetRealMetrics(dm);
#pragma warning restore CA1422
                shortPx = Math.Min(dm.WidthPixels, dm.HeightPixels);
            }

            return shortPx > 0 ? (int)Math.Round(shortPx / density) : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[host] display smallest-width probe failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// 연결된 실제 입력 장치에 마우스 소스(SOURCE_MOUSE)가 있으면 true. 장치 목록을 읽을 수 있고 터치 화면이 있는데 마우스가 없으면 false.
    /// 그 밖에는 null(미확인). 장치 연결·분리는 다음 수집 때 반영된다(실시간 감시 없음).
    /// 출처 https://developer.android.com/reference/android/view/InputDevice (SOURCE_MOUSE),
    ///      https://developer.android.com/reference/android/hardware/input/InputManager (getInputDeviceIds)
    /// </summary>
    private static bool? DetectPrecisePointer(Activity activity, bool? hasTouch)
    {
        try
        {
            if (activity.GetSystemService(Context.InputService) is not InputManager im || im.GetInputDeviceIds() is not { } ids)
            {
                return null;
            }

            foreach (var id in ids)
            {
                var dev = InputDevice.GetDevice(id);
                if (dev is not null && !dev.IsVirtual && dev.SupportsSource(InputSourceType.Mouse))
                {
                    return true;
                }
            }

            return hasTouch == true ? false : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[host] precise-pointer probe failed: {ex.GetType().Name}");
            return null;
        }
    }
}

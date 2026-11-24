// 기동 터치 게이트의 좌표 판정 — 창 좌표 두 점이 같은 입력 처리 컨트롤을 누르는지 지금 화면에서 hit test 로 확인하고, 다르면 손가락 쪽(B)으로 옮겨 보낼지 정한다
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input; // TopLevel.InputHitTest 확장 메서드(기존 HitTarget 과 같음)
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.android;

/// <summary>
/// 원칙: 누른 컨트롤이 아닌 곳을 누르지 않는다. 좌표가 조금 어긋나도 같은 컨트롤 안이면 같은 동작이므로 보낸다.
/// 비교 단위는 hit 결과에서 시각 트리를 올라가며 처음 만나는 입력 처리 컨트롤(<see cref="OwnerTypes"/>)이다. 하나라도 못 찾으면 다르다고 본다(보수).
/// 좌표는 Android 창 좌표(px)다. content 뷰 위치와 RenderScaling 으로 Avalonia 좌표로 바꾼다.
/// </summary>
internal sealed class StartupHitTest
{
    private const float SamePointPx = 1f; // 이 이하 차이는 hit test 없이 같은 곳으로 본다

    // is 판정이라 파생형(CheckBox·RadioButton·ToggleSwitch·ComboBoxItem 등)도 잡힌다. 겹쳐 넣어도 결과는 같다. Button·ToggleButton 은 Android.Widget 과 이름이 겹쳐 전체 이름으로 쓴다.
    private static readonly Type[] OwnerTypes =
    {
        typeof(Avalonia.Controls.Button), typeof(Avalonia.Controls.Primitives.ToggleButton), typeof(RepeatButton), typeof(SplitButton),
        typeof(MenuItem), typeof(ListBoxItem), typeof(TabItem), typeof(TreeViewItem),
        typeof(TextBox), typeof(ComboBox), typeof(Slider),
    };

    private readonly Android.App.Activity? _activity;

    public StartupHitTest(Android.App.Activity? activity) => _activity = activity;

    public TopLevel? Top { get; set; }

    /// <summary>탭 위치의 입력 대상(기록 전용). 입력 처리에 영향을 주지 않게 실패는 로그 값으로만 남긴다.</summary>
    public string Describe(float x, float y)
    {
        try
        {
            var hit = Hit(x, y);
            return hit is Control c ? Name(c) : hit?.GetType().Name ?? "none";
        }
        catch (Exception ex)
        {
            return "error:" + ex.GetType().Name;
        }
    }

    /// <summary>
    /// 재전송·직접 전달 판정. A=이벤트 창 좌표(앱이 그대로 누를 곳), B=화면 좌표 - 지금 창 위치(정착한 화면에서 손가락 아래). 누름(Down)·뗌(마지막) 두 쌍을 본다.
    /// A 와 B 가 누름·뗌 모두 같은 입력 처리 컨트롤이면 그대로 보낸다(null, retarget=false).
    /// 다르면 B 로 옮겨 보낸다(null, retarget=true). 단 B 누름·뗌이 같은 입력 처리 컨트롤이고 창 위치가 안정일 때만이다. 아니면 버릴 이유를 돌려준다.
    /// 화면이 없어 판정할 수 없으면 fallback(예전 기준 이유)을 돌려준다. marks 는 coord 줄 뒤에 붙일 값이다.
    /// 근거: Fold5 펼침 final7 원자료에서 A 는 라이트/다크·파일 메뉴·로고, B 는 19건 모두 설정이었다(원래 결함 '설정 탭이 파일 메뉴를 엶' 도 A 로 누른 것).
    /// </summary>
    public string? Judge((float X, float Y) aDown, (float X, float Y) bDown, (float X, float Y) aLast, (float X, float Y) bLast, bool originStable, string fallback, out string marks, out bool retarget)
    {
        marks = "";
        retarget = false;
        if (Close(aDown, bDown) && Close(aLast, bLast) && Close(aDown, aLast))
        {
            return null; // 같은 곳이라 hit test 없이 같은 컨트롤로 본다
        }

        if (Top is null)
        {
            return fallback;
        }

        var a = Owner(aDown.X, aDown.Y, out var na);
        var b = Owner(bDown.X, bDown.Y, out var nb);
        var aEnd = Close(aLast, aDown) ? a : Owner(aLast.X, aLast.Y, out _);
        var bEnd = Close(bLast, bDown) ? b : Owner(bLast.X, bLast.Y, out _);
        marks = $" target_a={na} target_b={nb}";
        if (a is not null && ReferenceEquals(a, b) && ReferenceEquals(a, aEnd))
        {
            return null;
        }

        if (b is null)
        {
            return "no_target_b"; // 손가락 아래에 입력 컨트롤이 없으면 보내지 않는다(보수)
        }

        if (!ReferenceEquals(b, bEnd))
        {
            return "target_differs";
        }

        if (!originStable)
        {
            return "origin_unstable";
        }

        retarget = true;
        marks += " decision=retarget_b";
        return null;
    }

    private static bool Close((float X, float Y) p, (float X, float Y) q) =>
        Math.Abs(p.X - q.X) <= SamePointPx && Math.Abs(p.Y - q.Y) <= SamePointPx;

    private Control? Owner(float x, float y, out string name)
    {
        try
        {
            var hit = Hit(x, y);
            for (var v = hit as Visual; v is not null; v = v.GetVisualParent())
            {
                if (v is Control c && IsOwner(c))
                {
                    name = Name(c);
                    return c;
                }
            }

            name = "none(" + (hit is Control h ? Name(h) : hit?.GetType().Name ?? "-") + ")";
            return null;
        }
        catch (Exception ex)
        {
            name = "error:" + ex.GetType().Name;
            return null;
        }
    }

    private IInputElement? Hit(float x, float y)
    {
        var origin = new int[2];
        _activity?.FindViewById(Android.Resource.Id.Content)?.GetLocationInWindow(origin);
        var scale = Top!.RenderScaling;
        return Top.InputHitTest(new Avalonia.Point((x - origin[0]) / scale, (y - origin[1]) / scale));
    }

    private static bool IsOwner(Control c) => Array.Exists(OwnerTypes, t => t.IsInstanceOfType(c));

    private static string Name(Control c) => $"{c.GetType().Name}/{c.Name ?? "-"}";
}

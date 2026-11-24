// MdiMoveLightener 의 끌기 중 그림자 부분(ui.mdi.move_light.shadow — hide 일 때만 .pdMoveShadowOff)과 템플릿 요소 찾기
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Pd.Poc.Shell.Docking;

namespace Pd.Ui.Views;

public static partial class MdiMoveLightener
{
    public const string ShadowOffClass = "pdMoveShadowOff";

    private static MoveShadowMode s_lastShadow = MdiMoveLight.Shadow;

    /// <summary>
    /// .pdMoving·.pdMovingSibling 을 걸거나 뗀 직후에 부른다. 둘 중 하나가 걸려 있고 설정이 hide 일 때만 그림자 끄기 클래스가 남는다
    /// (정지 창이 끄는 창으로 바뀌는 순서에서도 끄는 창의 클래스를 잃지 않는다).
    /// </summary>
    internal static void SyncShadowOff(MdiDocumentWindow w)
    {
        if (MdiMoveLight.HideShadowWhileMoving && (w.Classes.Contains(MovingClass) || w.Classes.Contains(MdiStaticLight.SiblingClass)))
        {
            if (!w.Classes.Contains(ShadowOffClass)) { w.Classes.Add(ShadowOffClass); }
        }
        else
        {
            w.Classes.Remove(ShadowOffClass);
        }
    }

    private static bool TakeShadowChange()
    {
        var changed = s_lastShadow != MdiMoveLight.Shadow;
        s_lastShadow = MdiMoveLight.Shadow;
        return changed;
    }

    private static Visual? FindNamed(MdiDocumentWindow w, string name)
    {
        foreach (var v in w.GetVisualDescendants())
        {
            if (v is Control c && c.Name == name)
            {
                return c;
            }
        }

        return null;
    }
}

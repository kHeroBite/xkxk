// MDI 창이 끌려 움직이는 동안 그 창 내용 영역을 입력 히트 테스트에서 빼고, 가벼운 크롬 원복과 같은 시점에 되돌리는 적용부(옵션 MdiMoveLight.HitTestOff, 01a K3)
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 근거(r8 웹 진단판 CPU 프로파일): 포인터를 잡은 끌기 중에도 이동마다 입력 히트 테스트가 시각 트리를 훑어 3회 합 21.1ms 를 썼다.
/// 끄는 창의 내용 영역(템플릿 부품 PART_ContentBorder — 제목줄·크기 조절 손잡이는 밖)을 IsHitTestVisible=false 로 두면 그 하위 트리를 훑지 않는다.
/// 화면 차이는 없고 끄는 동안 그 창 내용 위 호버만 나타나지 않는다. 시작은 MdiStaticLight.ScheduleMoveStarted, 원복은 MdiStaticLight.Release(MdiMoveLightener.End — QuietMs 원복)·ReleaseAll 이 부른다.
/// 원래 꺼져 있던 요소는 건드리지 않고, 이 클래스가 끈 요소만 되돌린다. 옵션을 끄면 걸려 있던 것을 바로 되돌린다. UI 스레드 전용. 규칙 정본 plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md(K3).
/// </summary>
internal static class MdiMoveHitTest
{
    /// <summary>템플릿(Themes/MdiDocumentWindowTheme.axaml)의 내용 영역 이름.</summary>
    private const string ContentPartName = "PART_ContentBorder";

    // 이 클래스가 히트 테스트를 끈 창과 그 요소(원복 때 이 요소만 되돌린다)
    private static readonly Dictionary<MdiDocumentWindow, Control> s_off = new();

    static MdiMoveHitTest()
    {
        MdiMoveLight.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            if (!MdiMoveLight.HitTestOff) { ReleaseAll(); }
        });
    }

    /// <summary>moving 이 움직이기 시작했다 — 옵션이 켜져 있으면 내용 영역 히트 테스트를 끈다.</summary>
    public static void Begin(MdiDocumentWindow moving)
    {
        if (!MdiMoveLight.HitTestOff || s_off.ContainsKey(moving) || FindContent(moving) is not { IsHitTestVisible: true } content)
        {
            return;
        }

        content.SetCurrentValue(InputElement.IsHitTestVisibleProperty, false);
        s_off[moving] = content;
        Note(1);
    }

    /// <summary>창 하나를 되돌린다(이 클래스가 끈 것만).</summary>
    public static void Release(MdiDocumentWindow w)
    {
        if (s_off.Remove(w, out var content))
        {
            content.SetCurrentValue(InputElement.IsHitTestVisibleProperty, true);
            Note(0);
        }
    }

    /// <summary>이동이 모두 끝났거나 옵션이 바뀌었다 — 끈 것을 모두 되돌린다.</summary>
    public static void ReleaseAll()
    {
        if (s_off.Count == 0)
        {
            return;
        }

        foreach (var content in s_off.Values)
        {
            content.SetCurrentValue(InputElement.IsHitTestVisibleProperty, true);
        }

        s_off.Clear();
        Note(0);
    }

    private static Control? FindContent(MdiDocumentWindow w)
    {
        foreach (var v in w.GetVisualDescendants())
        {
            if (v is Control { Name: ContentPartName } c)
            {
                return c;
            }
        }

        return null;
    }

    private static void Note(int applied)
    {
#if PD_DIAGNOSTICS
        MdiMoveLight.NoteHitTest(applied, s_off.Count);
#endif
    }
}

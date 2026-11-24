// 활성화 출처 입력 표식 — 제목 탭 키 선택·DOCK 문서 탭(title_tab)·문서 본문(body_click) 입력을 컨트롤 계층(WindowCommands)에 알린다. 제목 탭 클릭은 명령이 출처를 직접 넘긴다. 전환 효과 판정은 ui.motion.switch_sources(21 설계 §3)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

public partial class MainView
{
    private static bool s_originClassHandlers;

    /// <summary>입력 순간 출처 표식. 600ms 안의 다음 활성 변경이 가져간다(판정·소비는 WindowCommands). 화면은 출처만 넘긴다.</summary>
    internal static void MarkOrigin(ActivationOrigin origin) => (App.Shell as WindowCommands)?.MarkActivationOrigin(origin);

    /// <summary>생성자에서 1회. 제목 탭 줄 키 처리기는 인스턴스마다, 문서 탭·본문 클래스 처리기는 앱에 1회만 건다.</summary>
    private void InstallOriginMarks()
    {
        // 제목 탭 누름은 표식하지 않는다 — 놓을 때 TabClickCommand 가 출처를 넘긴다. 누를 때 남기면 길게 누르면 만료되고 끌기·취소하면 남는다
        TaskbarTabs.AddHandler(KeyDownEvent, OnTitleTabOriginKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (s_originClassHandlers)
        {
            return;
        }

        s_originClassHandlers = true;
        // 터널은 바깥에서 안으로 내려오므로 본문(DocumentControl·MDI 창) 표식 뒤 안쪽 문서 탭 표식이 덮어쓴다 — 탭을 누르면 title_tab 이 남는다
        InputElement.PointerPressedEvent.AddClassHandler<DocumentControl>((_, e) => MarkIfPrimary(e, ActivationOrigin.BodyClick), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>((_, e) => MarkIfPrimary(e, ActivationOrigin.BodyClick), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<DocumentTabStripItem>((_, e) => MarkIfPrimary(e, ActivationOrigin.TitleTab), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // 키보드로 제목 탭 사이를 옮겨 선택해도 같은 출처다
    private void OnTitleTabOriginKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Enter or Key.Space)
        {
            MarkOrigin(ActivationOrigin.TitleTab);
        }
    }

    private static void MarkIfPrimary(PointerPressedEventArgs e, ActivationOrigin origin)
    {
        if (e.Source is Visual v && e.GetCurrentPoint(v).Properties.IsLeftButtonPressed)
        {
            MarkOrigin(origin);
        }
    }
}

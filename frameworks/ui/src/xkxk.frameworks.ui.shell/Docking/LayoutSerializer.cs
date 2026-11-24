// 셸 레이아웃(문서 목록·MDI 위치·크기·Z순서·최소/최대·플로팅 창)을 JSON 스냅샷으로 직렬화/역직렬화
using xkxk.frameworks.ui.data.Layout;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>스냅샷 ↔ JSON, 그리고 Dock 모델 → 스냅샷 캡처.</summary>
public static class LayoutSerializer
{
    public static string Serialize(LayoutSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, LayoutJsonContext.Default.LayoutSnapshot);

    /// <summary>손상·빈 JSON 이면 null.</summary>
    public static LayoutSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, LayoutJsonContext.Default.LayoutSnapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>현재 Dock 모델에서 스냅샷을 만든다. 열린 순서는 order 를 따른다.</summary>
    public static LayoutSnapshot Capture(IDocumentDock documents, IRootDock root, IEnumerable<PocDocument> order, PocDocument? active)
    {
        var snapshot = new LayoutSnapshot
        {
            Mode = documents.LayoutMode,
            ActiveId = active?.Id,
        };

        foreach (var doc in order)
        {
            var b = doc.MdiBounds;
            var item = new DocumentSnapshot
            {
                Id = doc.Id,
                Kind = doc.Kind,
                Seq = doc.Seq,
                Title = doc.Title,
                X = b.X,
                Y = b.Y,
                Width = b.Width,
                Height = b.Height,
                MdiState = doc.MdiState,
                ZIndex = doc.MdiZIndex,
                Zoom = doc.Zoom,
                IsPinned = WindowCommands.ChromeOf(doc).IsPinned,
                SyncEnabled = WindowCommands.ChromeOf(doc).SyncEnabled,
                IsLocked = WindowCommands.ChromeOf(doc).IsLocked,
                PanelId = PocDockFactory.PanelOf(doc), // 측면 패널 탭 안 문서(odev-1 Q15)
                AutoTitle = WindowCommands.IsAutoTitle(doc),
            };

            var window = FindWindowOf(root, doc);
            if (window is not null)
            {
                item.Window = new WindowSnapshot
                {
                    X = window.X,
                    Y = window.Y,
                    Width = window.Width,
                    Height = window.Height,
                    Topmost = window.Topmost,
                    State = window.WindowState,
                };
            }

            snapshot.Documents.Add(item);
        }

        LayoutGroupCodec.Capture(documents, snapshot); // MDI 탭 묶음(Version 5 Groups) — 묶음 안 문서도 Documents 에 그대로 남는다
        return snapshot;
    }

    /// <summary>문서가 들어 있는 플로팅 창. 도킹 상태면 null.</summary>
    public static IDockWindow? FindWindowOf(IRootDock root, IDockable dockable)
    {
        if (root.Windows is null)
        {
            return null;
        }

        return root.Windows.FirstOrDefault(w => w.Layout is not null && Contains(w.Layout, dockable));
    }

    /// <summary>스냅샷의 MDI 위치·크기·최소/최대·Z순서를 문서에 적용한다.</summary>
    public static void ApplyDocument(PocDocument doc, DocumentSnapshot item)
    {
        if (item.Width > 0 && item.Height > 0)
        {
            doc.MdiBounds = WindowMinSize.AtLeast(new DockRect(item.X, item.Y, item.Width, item.Height)); // 저장된 좁은 크기가 영속되지 않게(D-26)
        }

        doc.MdiState = item.MdiState;
        // MdiZIndex 는 여기서 넣지 않는다 — LoadLayoutAsync 가 모든 문서를 연 뒤 SetMdiZOrder(odev-7c API)로 한 번에 확정
        doc.Zoom = item.Zoom > 0 ? item.Zoom : 1.0;
        var chrome = WindowCommands.ChromeOf(doc);
        chrome.IsPinned = item.IsPinned;
        chrome.SyncEnabled = item.SyncEnabled; // 캐시는 '새로고침' 동작으로 바뀌어 저장하지 않음(Mars 동일)
        chrome.IsLocked = item.IsLocked;
    }

    /// <summary>플로팅 창 항상 위(핀) 설정을 모델과 OS 창에 반영.</summary>
    public static void SetTopmost(IDockWindow window, bool topmost)
    {
        window.Topmost = topmost;
        if (window.Host is HostWindow host)
        {
            host.Topmost = topmost;
        }
    }

    /// <summary>스냅샷의 플로팅 창 위치·크기·핀·상태를 모델과 실제 호스트 창에 적용한다. window 가 null 이면 무시.</summary>
    public static void ApplyWindow(IDockWindow? window, WindowSnapshot state)
    {
        if (window is null)
        {
            return;
        }

        window.X = state.X;
        window.Y = state.Y;
        window.Width = state.Width;
        window.Height = state.Height;
        window.Topmost = state.Topmost;
        if (window.Host is HostWindow host)
        {
            // 인터페이스 경유 필수(MDI-12, odev-4b): 호스트(SdiWindowHost)가 재구현해 요구 좌표를 받고 화면 밖이면 정상화한다.
            // Position 을 직접 바꾸면 플랫폼·Dock 이 먼저 보정해 원래 요구 좌표를 잃는다(diag3 실측).
            ((IHostWindow)host).SetPosition(state.X, state.Y);
            host.Width = state.Width;
            host.Height = state.Height;
            host.Topmost = state.Topmost;
        }
        else if (window.Host is ManagedHostWindow managed)
        {
            // 웹 관리형 창(D-5): 모델 X·Y·W·H 만 바꾸면 표시와 MdiBounds 가 Float 직후 기본값으로 남는다(otest-w-2 i0944 복원 FAIL).
            // 호스트는 Float(SplitToWindow→Present) 안에서 동기로 붙으므로 여기서 SetPosition·SetSize 가 내부 UpdateBounds 로 MdiBounds 를 갱신한다(Dock 12.1.0.12 디컴파일).
            // 레이어 밖 값은 ⑦ 과 같은 규칙(SdiResetLayout.FitInLayer)으로 맞춰 모델·표시·다음 저장을 일치시킨다. 레이어 크기를 아직 모르면 저장값 그대로.
            var fit = ManagedWindowHostCommands.FitToLayer(new Rect(state.X, state.Y, state.Width, state.Height));
            (window.X, window.Y, window.Width, window.Height) = (fit.X, fit.Y, fit.Width, fit.Height);
            managed.SetPosition(fit.X, fit.Y);
            managed.SetSize(fit.Width, fit.Height);
        }

        SetHostState(window, state.State);
    }

    /// <summary>플로팅 창 최소/최대/보통 상태를 모델과 호스트(OS 창·관리형 창)에 함께 반영.</summary>
    public static void SetHostState(IDockWindow window, DockWindowState state)
    {
        window.WindowState = state;
        switch (window.Host)
        {
            case HostWindow host: host.SetWindowState(state); break;
            case ManagedHostWindow managed: managed.SetWindowState(state); break;
        }
    }

    /// <summary>dock 하위(재귀)에 dockable 이 있는가.</summary>
    public static bool Contains(IDockable node, IDockable target)
    {
        if (ReferenceEquals(node, target))
        {
            return true;
        }

        if (node is IDock dock && dock.VisibleDockables is not null)
        {
            return dock.VisibleDockables.Any(child => Contains(child, target));
        }

        return false;
    }
}

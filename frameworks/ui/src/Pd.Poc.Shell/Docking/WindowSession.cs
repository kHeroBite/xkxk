// WindowCommands 와 창 관리 서비스들이 함께 쓰는 상태(팩토리·저장소·로그·MRU·최소화 순서·복원 보호)와 조회 도우미 — W3 partial 해소의 공유 컨텍스트
using Pd.Client.Data.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 옛 WindowCommands.cs 의 private 인스턴스 필드 8개와 조회 도우미(Docs·Root·FindWindow·MdiDocs)를 담는다.
/// 서비스(WindowActivation·WindowStateOps·LayoutRestorer·DocumentChromeService·MdiZOrder·DocumentZoom·SidePanelVisibility·SdiPlacement·DocumentAutoTitles)는
/// 생성자로 이것 하나를 받고, 다른 서비스와 공개 API 는 <see cref="Owner"/> 를 거쳐 부른다(예: s.Owner.Activation.Float(doc)).
/// </summary>
[WindowCommandsPart] // FindWindow·MdiDocs 는 옛 WindowCommands 메서드다(D-12 via= 이름 유지)
internal sealed class WindowSession
{
    internal WindowSession(WindowCommands owner, PocDockFactory factory, ILayoutStore store, Action<string> log)
    {
        Owner = owner;
        Factory = factory;
        Store = store;
        Log = log;
    }

    /// <summary>파사드. 공개 API(OpenDocuments·ActiveDocument·OpenDocument·CloseDocument·CloseAll·ActiveChanged)와 서비스 속성을 가진다.</summary>
    internal WindowCommands Owner { get; }

    internal PocDockFactory Factory { get; }

    internal ILayoutStore Store { get; }

    internal Action<string> Log { get; }

    internal Dictionary<DocumentKind, int> Seq { get; } = new();

    /// <summary>최근 활성 순서(앞=가장 최근).</summary>
    internal List<PocDocument> Mru { get; } = new();

    /// <summary>최소화한 순서(뒤=가장 최근).</summary>
    internal List<PocDocument> Minimized { get; } = new();

    /// <summary>레이아웃 복원 직후 지켜야 할 활성 문서.</summary>
    internal PocDocument? RestoreTarget { get; set; }

    /// <summary>보호 만료 시각(Environment.TickCount64).</summary>
    internal long RestoreUntil { get; set; }

    internal IDocumentDock Docs => Factory.Documents ?? throw new InvalidOperationException("레이아웃 미초기화");

    internal IRootDock Root => Factory.Root ?? throw new InvalidOperationException("레이아웃 미초기화");

    internal IDockWindow? FindWindow(PocDocument doc) => LayoutSerializer.FindWindowOf(Root, doc);

    internal IEnumerable<PocDocument> MdiDocs() => Owner.OpenDocuments.Where(d => FindWindow(d) is null && PocDockFactory.PanelOf(d) is null);
}

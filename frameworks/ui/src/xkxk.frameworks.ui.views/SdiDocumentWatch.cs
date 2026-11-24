// SDI 제목줄 문서 추적 보조 — 보여 주던 문서가 제거되면 제목줄이 다시 보게 하고, 패널 탭만 남은 창의 제목 대상을 찾는다
using System;
using Avalonia.Threading;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views;

/// <summary>초점이 패널 탭에 있으면 문서를 닫아도 초점 변경이 오지 않아, 팩토리의 제거 알림으로 제목줄이 문서를 놓게 한다.</summary>
internal sealed class SdiDocumentWatch
{
    private readonly Func<PocDocument?> _shown;
    private readonly Action _refresh;
    private PocDockFactory? _factory;

    internal SdiDocumentWatch(Func<PocDocument?> shown, Action refresh)
    {
        _shown = shown;
        _refresh = refresh;
    }

    /// <summary>구독 대상을 바꾼다. null 이면 해제만 한다.</summary>
    internal void Watch(PocDockFactory? factory)
    {
        if (_factory is not null)
        {
            _factory.DockableRemoved -= OnRemoved;
        }

        _factory = factory;
        if (_factory is not null)
        {
            _factory.DockableRemoved += OnRemoved;
        }
    }

    /// <summary>활성 쪽을 따라 내려간 첫 비-dock 항목(패널 탭).</summary>
    internal static IDockable? ActiveLeaf(IDockable? dockable) => dockable is IDock dock ? ActiveLeaf(dock.ActiveDockable) : dockable;

    private void OnRemoved(object? sender, DockableRemovedEventArgs e)
    {
        if (ReferenceEquals(e.Dockable, _shown()))
        {
            Dispatcher.UIThread.Post(_refresh); // 제거 뒤 활성 항목이 정해진 다음에 본다
        }
    }
}

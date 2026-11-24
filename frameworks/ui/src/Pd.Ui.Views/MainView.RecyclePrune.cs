// 셸 메인 화면의 닫힌 문서 뷰 정리 부분 — 문서가 닫히면(레이아웃 불러오기로 빠지는 경우 포함) Dock 문서 뷰 캐시(ControlRecycling)에서 그 항목을 빼고 남은 뷰를 떼어 낸다
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Threading;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

public partial class MainView
{
    // 지금 열린 문서 목록의 사본(Clear 는 Reset 이라 빠진 항목을 알려 주지 않으므로 직전 목록과 비교한다)
    private readonly HashSet<PocDocument> _knownDocuments = new();
    private MainViewModel? _watchedVm;
    private bool _prunePosted;

    /// <summary>
    /// Dock 의 문서 뷰 캐시(DockHost 의 ControlRecycling, 키 = 문서 객체)는 DockControl 이 다시 초기화될 때만 정리된다.
    /// 그래서 닫힌 문서(layout.load 로 바뀐 옛 문서 포함)의 뷰가 캐시에 남고, 그 뷰를 통해 떨어진 옛 MDI 영역·창·문서가 함께 남았다
    /// (A11 힙 덤프: live MdiDocumentControl 2·MdiDocumentWindow 40·PocDocument 41, 캐시 항목 47, gcroot 34경로 모두 이 캐시를 거침).
    /// 열린 문서 목록에서 빠진 문서만 정리하므로 SDI 분리·재결합(목록에서 빠지지 않음)과 다시 열기(새 문서 객체 → 새 뷰)는 그대로다.
    /// </summary>
    private void WatchClosedDocuments(MainViewModel? vm)
    {
        if (ReferenceEquals(vm, _watchedVm))
        {
            return;
        }

        if (_watchedVm is { } old)
        {
            old.OpenDocuments.CollectionChanged -= OnOpenDocumentsChanged;
        }

        _watchedVm = vm;
        _knownDocuments.Clear();
        if (vm is null)
        {
            return;
        }

        vm.OpenDocuments.CollectionChanged += OnOpenDocumentsChanged;
        _knownDocuments.UnionWith(vm.OpenDocuments);
    }

    private void OnOpenDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Dock 의 닫기 처리(창 제거·뷰 떼기)가 끝난 뒤 한 번에 정리한다. 한 틱에 여러 문서가 닫혀도 1회
        if (_prunePosted)
        {
            return;
        }

        _prunePosted = true;
        Dispatcher.UIThread.Post(PruneClosedDocumentViews, DispatcherPriority.Background);
    }

    private void PruneClosedDocumentViews()
    {
        _prunePosted = false;
        if (_watchedVm is not { } vm)
        {
            return;
        }

        var open = new HashSet<PocDocument>(vm.OpenDocuments);
        var closed = _knownDocuments.Where(d => !open.Contains(d)).ToList();
        _knownDocuments.Clear();
        _knownDocuments.UnionWith(open);
        if (closed.Count == 0 || ControlRecyclingDataTemplate.GetControlRecycling(DockHost) is not ControlRecycling cache)
        {
            return;
        }

        foreach (var doc in closed)
        {
            if (cache.TryGetValue(doc, out var view) && view is Panel host && TopLevel.GetTopLevel(host) is null)
            {
                DocumentViewLocator.TryDetach(host); // 화면 밖(떨어진 옛 트리)에 남은 뷰만 뗀다. 화면에 붙어 있으면 Dock 이 정리하게 둔다
            }

            cache.Remove(doc);
        }
    }
}

// 문서 하나의 창 문맥(IUiWindowContext 구현) — 제목 바꾸기·닫기·바쁨 표시와, 열린 문서 목록 구독으로 닫힘을 1회 알린다(UADR-0017 D3)
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// <see cref="PocDocument"/> 하나에 대한 <see cref="IUiWindowContext"/> 다. 문서마다 1개를 만들어 다시 쓴다(<see cref="For"/>).
/// </summary>
/// <remarks>
/// 닫기 경로는 한 곳이 아니다(Dock 의 닫힘 알림 처리기, <c>CloseAll</c> 의 목록 비우기, 레이아웃 복원의 남은 문서 제거).
/// 셋 다 열린 문서 목록(<see cref="WindowCommands.OpenDocuments"/>)에서 문서를 빼므로, 이 문맥은 그 목록의
/// <see cref="INotifyCollectionChanged.CollectionChanged"/>(Remove·Reset·Replace)를 구독한다. 변경 뒤 자기 문서가 목록에 없으면
/// Dispatcher 한 바퀴 뒤 다시 확인하고(빼었다가 다시 넣는 이동은 닫힘이 아니다), 그때도 없으면 <see cref="Closed"/> 를 1회 내고 구독을 푼다.
/// 한계 2가지: 앱 종료 때는 목록에서 빠지지 않아 닫힘을 알리지 않는다. 내용을 만들기 전에 닫힌 문서는 문맥 자체가 없다.
/// 모든 멤버는 UI 스레드에서 부른다.
/// </remarks>
internal sealed class DocumentWindowContext : IUiWindowContext
{
    private const string DefaultBusyReason = "window"; // i18n-ok 진단용 사유

    // 문서 객체가 사라지면 함께 사라진다 — 같은 문서는 같은 문맥을 돌려준다
    private static readonly ConditionalWeakTable<PocDocument, DocumentWindowContext> s_contexts = new();

    private readonly PocDocument _doc;
    private readonly IUiAppContext _app;
    private readonly ObservableCollection<PocDocument>? _list;
    private bool _closed;
    private bool _checkPosted;

    private DocumentWindowContext(string windowId, PocDocument doc, IUiAppContext app)
    {
        WindowId = windowId;
        _doc = doc;
        _app = app;
        _list = (app.Shell as WindowCommands)?.OpenDocuments;
        if (_list is not null)
        {
            _list.CollectionChanged += OnDocumentsChanged;
        }
    }

    /// <summary>
    /// 문서의 창 문맥을 얻는다. 이미 있고 아직 열려 있으면 그 인스턴스를, 없거나 닫혔으면 새로 만든다.
    /// </summary>
    /// <param name="windowId">등록된 창 ID(별칭으로 열었어도 ID). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="doc">대상 문서. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="app">앱 문맥(셸 명령·열린 문서 목록을 얻는다). null 이면 <see cref="ArgumentNullException"/>.</param>
    internal static DocumentWindowContext For(string windowId, PocDocument doc, IUiAppContext app)
    {
        ArgumentNullException.ThrowIfNull(windowId);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(app);
        if (s_contexts.TryGetValue(doc, out var existing) && !existing._closed)
        {
            return existing;
        }

        var created = new DocumentWindowContext(windowId, doc, app);
        s_contexts.AddOrUpdate(doc, created);
        return created;
    }

    /// <inheritdoc/>
    public string WindowId { get; }

    /// <inheritdoc/>
    public string DocumentId => _doc.Id ?? "";

    /// <inheritdoc/>
    public string Title => _doc.Title ?? "";

    /// <inheritdoc/>
    public object? Parameter => _doc.Parameter;

    /// <inheritdoc/>
    public bool IsOpen => !_closed;

    /// <inheritdoc/>
    public event EventHandler? Closed;

    /// <inheritdoc/>
    /// <remarks>셸의 이름 바꾸기(<see cref="IShellCommands.Rename"/>)와 같다. 자동 제목 표시를 풀어 언어가 바뀌어도 제목이 남는다.</remarks>
    public bool SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (_closed)
        {
            return false;
        }

        if (_app.Shell is { } shell)
        {
            shell.Rename(_doc, title);
        }
        else
        {
            DocumentAutoTitles.SetAutoTitle(_doc, false);
            _doc.Title = title;
        }

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>기존 닫기 경로(<see cref="IShellCommands.CloseDocument"/>)를 부른다. <see cref="Closed"/> 는 목록에서 빠진 것을 확인한 뒤 난다.</remarks>
    public bool Close()
    {
        if (_closed || _app.Shell is not { } shell || (_list is not null && !_list.Contains(_doc)))
        {
            return false;
        }

        shell.CloseDocument(_doc);
        return true;
    }

    /// <inheritdoc/>
    public IDisposable BeginBusy() => BeginBusy(DefaultBusyReason);

    /// <inheritdoc/>
    /// <remarks>문서 범위(스코프 = 문서 ID)의 <see cref="BusyService.Begin(string, string)"/> 에 위임한다.</remarks>
    public IDisposable BeginBusy(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (_closed)
        {
            return NoopDisposable.Instance;
        }

        return BusyHooks.Service.Begin(DocumentId, reason);
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_closed || _checkPosted || e.Action is not (NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace))
        {
            return;
        }

        if (_list is null || _list.Contains(_doc))
        {
            return;
        }

        // 빼었다가 바로 다시 넣는 경우(이동)를 닫힘으로 보지 않도록 한 바퀴 뒤 다시 본다
        _checkPosted = true;
        Dispatcher.UIThread.Post(ConfirmClosed, DispatcherPriority.Background);
    }

    private void ConfirmClosed()
    {
        _checkPosted = false;
        if (_closed || _list is null || _list.Contains(_doc))
        {
            return;
        }

        _closed = true;
        _list.CollectionChanged -= OnDocumentsChanged;
        var handler = Closed;
        Closed = null; // 1회만 — 뒤늦은 구독자·중복 알림을 남기지 않는다
        handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>닫힌 창의 바쁨 표시 요청에 돌려주는 빈 값.</summary>
    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}

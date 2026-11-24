// 레이아웃 자동 저장(디바운스) — 창 배치가 바뀌면 마지막 변경 1초 뒤 자동 저장 슬롯에 쓴다. 비정상 종료에도 마지막 배치를 남기고, 일시정지·페이지 숨김 때는 즉시 저장(Flush)한다
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 규칙(team-lead 승인): ① 변경 신호마다 1초 타이머를 다시 건다 → 끌기 중에는 저장하지 않고 끝난 뒤 1회.
/// ② 변경이 없으면(dirty=false) 어떤 경로든 즉시 돌아간다 → pagehide·visibilitychange, OnPause·OnStop 이 겹쳐도 실제 저장은 1회.
/// ③ 저장은 직렬화한다(진행 중에 새 변경이 오면 끝난 뒤 최신 상태로 1회 더).
/// ④ 스냅샷 읽기는 UI 스레드, JSON 변환·쓰기는 스레드 풀(데스크톱·Android). 브라우저 WASM 은 단일 스레드라 같은 스레드에서 Background 우선순위로 한다.
/// 저장 여부는 enabled()(MainViewModel.ShouldAutoSave — --fresh·시나리오 인자·사용자 설정 '윈도우 자동저장' 끔이면 false)를 매번 본다.
/// </summary>
public sealed class LayoutAutoSaver
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    private readonly WindowCommands _commands;
    private readonly IFactory _factory;
    private readonly ILayoutStore _store;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _log;
    private readonly DispatcherTimer _timer;
    private bool _started;
    private bool _dirty;
    private bool _saving;

    public LayoutAutoSaver(WindowCommands commands, IFactory factory, ILayoutStore store, Func<bool> enabled, Action<string> log)
    {
        _commands = commands;
        _factory = factory;
        _store = store;
        _enabled = enabled;
        _log = log;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _ = SaveAsync();
        };
    }

    /// <summary>기동 복원 판단이 끝난 뒤 1회 호출. 그 전의 빈 화면이 저장본을 덮지 않게 이때부터 변경을 받는다.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _commands.OpenDocuments.CollectionChanged += OnDocumentsChanged;
        foreach (var doc in _commands.OpenDocuments)
        {
            doc.PropertyChanged += OnDocumentChanged;
        }

        _commands.ActiveChanged += _ => MarkDirty();
        _factory.DockableMoved += (_, _) => MarkDirty();
        _factory.DockableDocked += (_, _) => MarkDirty();
        _factory.DockableUndocked += (_, _) => MarkDirty();
        _factory.WindowOpened += (_, _) => MarkDirty();
        _factory.WindowClosed += (_, _) => MarkDirty();
        _factory.WindowMoveDragEnd += (_, _) => MarkDirty();
        if (_factory is PocDockFactory poc)
        {
            poc.PanelEdgeChanged += _ => MarkDirty(); // 패널 위치 이동은 Dock 이벤트를 내지 않는다 — 웹은 종료 저장이 없어 새로고침 때 잃었다(D-NOTIPOS-SAVE)
        }
    }

    /// <summary>종료 직전: 디바운스를 멈추고 진행 중 저장이 끝나기를 최대 max 만큼 기다린다(종료 저장과 같은 파일에 겹쳐 쓰지 않게).</summary>
    public async Task StopAsync(TimeSpan max)
    {
        _started = false;
        _timer.Stop();
        var deadline = DateTime.UtcNow + max;
        while (_saving && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    /// <summary>변경 알림(호스트가 SDI 창 크기 조절 끝 등 셸이 모르는 변경을 알릴 때도 쓴다).</summary>
    public void MarkDirty()
    {
        if (!_started)
        {
            return;
        }

        _dirty = true;
        _timer.Stop();
        _timer.Start(); // 디바운스: 마지막 변경 1초 뒤
    }

    /// <summary>
    /// 즉시 저장(동기) — 웹 pagehide·visibilitychange, Android OnPause·OnStop. 변경이 없거나 꺼져 있으면 바로 돌아간다.
    /// 브라우저 저장소는 동기라 바로 끝나고, 파일 저장소는 스레드 풀에서 최대 2초 기다린다(UI 컨텍스트 교착 방지).
    /// </summary>
    public void Flush()
    {
        _timer.Stop();
        if (!_dirty || !_enabled())
        {
            return;
        }

        try
        {
            var json = LayoutSerializer.Serialize(_commands.CaptureLayout());
            _dirty = false;
            if (OperatingSystem.IsBrowser())
            {
                _ = _store.SaveAsync(json); // localStorage 쓰기는 동기 — 반환 시점에 끝나 있다
            }
            else if (!Task.Run(() => _store.SaveAsync(json)).Wait(TimeSpan.FromSeconds(2)))
            {
                _log(Loc.Tr("log.layout.flushSlow"));
            }
        }
        catch (Exception ex)
        {
            _dirty = true;
            _log(Loc.Tr("log.layout.flushFailed", ex.Message));
        }
    }

    private async Task SaveAsync()
    {
        if (!_dirty || !_enabled())
        {
            return;
        }

        if (_saving)
        {
            return; // 끝난 뒤 dirty 를 보고 다시 저장한다
        }

        _saving = true;
        try
        {
            while (_dirty && _enabled())
            {
                var t0 = Stopwatch.GetTimestamp();
                _dirty = false;
                var snapshot = _commands.CaptureLayout(); // 모델 읽기는 UI 스레드에서(짧다)
                var json = OperatingSystem.IsBrowser()
                    ? LayoutSerializer.Serialize(snapshot)
                    : await Task.Run(() => LayoutSerializer.Serialize(snapshot));
                var syncMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                await (OperatingSystem.IsBrowser() ? _store.SaveAsync(json) : Task.Run(() => _store.SaveAsync(json)));
                var totalMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                PerfProbe.Current.RecordOperation("ui.layout.autosave", syncMs, totalMs); // 진단 빌드만 기록
            }
        }
        catch (Exception ex)
        {
            _dirty = true; // 다음 변경·Flush·종료 저장 때 다시 시도
            _log(Loc.Tr("log.layout.autoSaveFailed", ex.Message));
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems ?? Array.Empty<object>())
        {
            if (item is PocDocument d) { d.PropertyChanged -= OnDocumentChanged; }
        }

        foreach (var item in e.NewItems ?? Array.Empty<object>())
        {
            if (item is PocDocument d) { d.PropertyChanged += OnDocumentChanged; }
        }

        MarkDirty();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PocDocument.MdiBounds):
            case nameof(PocDocument.MdiState):
            case nameof(PocDocument.Zoom):
            case nameof(PocDocument.Title):
                MarkDirty();
                break;
        }
    }
}

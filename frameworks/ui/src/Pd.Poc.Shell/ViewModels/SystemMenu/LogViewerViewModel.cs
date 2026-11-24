// 로그 보기 뷰모델(UI-17 로그 ▶) — 메모리 링 버퍼(PdLog.Memory)의 시스템·사용자 로그를 표시 레벨·범주로 걸러 보여 주고, 새 로그를 실시간으로 덧붙인다
using Pd.Client.Data.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pd.Poc.Shell.ViewModels.SystemMenu;

/// <summary>
/// 표시 필터는 이 창에서만 쓰는 보기 조건이다. 실제 기록 최소 레벨(PdLog.MinLevel)은 시스템 메뉴의 '로그 레벨'이 바꾼다.
/// MemoryLogSink.Added 는 쓰기 스레드에서 오므로 UI 스레드로 넘겨 처리한다. 창이 닫히면 Dispose 로 구독을 푼다.
/// </summary>
public sealed partial class LogViewerViewModel : ObservableObject, IDisposable
{
    /// <summary>범주 필터 선택지(전체·시스템·사용자).</summary>
    public static IReadOnlyList<LocalizedOption> CategoryOptions { get; } = LocalizedOption.List(
        "sysview.log.cat.all", "sysview.log.cat.system", "sysview.log.cat.user");

    /// <summary>표시 레벨 선택지(이 레벨 이상만 보인다).</summary>
    public static IReadOnlyList<PdLogLevel> LevelOptions { get; } = Enum.GetValues<PdLogLevel>();

    private readonly MemoryLogSink _sink;
    private LocalizedOption _category = CategoryOptions[0];
    private PdLogLevel _level = PdLogLevel.Trace;
    private bool _disposed;

    public LogViewerViewModel(MemoryLogSink? sink = null)
    {
        _sink = sink ?? PdLog.Memory;
        ClearCommand = new RelayCommand(() =>
        {
            _sink.Clear();
            Lines.Clear();
            OnPropertyChanged(nameof(Count));
        });
        _sink.Added += OnAdded;
        Reload();
    }

    public ObservableCollection<string> Lines { get; } = new();

    public IReadOnlyList<LocalizedOption> Categories => CategoryOptions;

    public IReadOnlyList<PdLogLevel> Levels => LevelOptions;

    public LocalizedOption Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, value ?? CategoryOptions[0]))
            {
                Reload();
            }
        }
    }

    public PdLogLevel Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value))
            {
                Reload();
            }
        }
    }

    /// <summary>지금 보이는 줄 수(시험 판정용).</summary>
    public int Count => Lines.Count;

    /// <summary>현재 기록 최소 레벨(PdLog.MinLevel) — 표시 필터와 다름을 알려 준다.</summary>
    public string RecordingLevel => PdLog.MinLevel.ToString();

    public RelayCommand ClearCommand { get; }

    /// <summary>보이는 줄 전체를 내보내기용 문자열로 만든다(줄바꿈 구분).</summary>
    public string ExportText()
    {
        var sb = new StringBuilder();
        foreach (var line in Lines)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _sink.Added -= OnAdded;
        }
    }

    private bool Accept(in PdLogEntry e)
    {
        if (e.Level < _level)
        {
            return false;
        }

        return _category.Key switch
        {
            "sysview.log.cat.system" => e.Category == PdLogCategory.System,
            "sysview.log.cat.user" => e.Category == PdLogCategory.User,
            _ => true,
        };
    }

    private void Reload()
    {
        Lines.Clear();
        foreach (var e in _sink.Snapshot().Where(x => Accept(x)))
        {
            Lines.Add(e.ToString());
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(RecordingLevel));
    }

    private void OnAdded(PdLogEntry e) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || !Accept(e))
        {
            return;
        }

        Lines.Add(e.ToString());
        while (Lines.Count > _sink.Capacity)
        {
            Lines.RemoveAt(0); // 링 버퍼 용량을 넘으면 오래된 줄부터 버린다
        }

        OnPropertyChanged(nameof(Count));
    });
}

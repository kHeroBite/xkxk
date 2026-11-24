// 최근 로그를 메모리 링 버퍼에 보관하는 싱크 — 모든 플랫폼의 '로그 보기' 창이 읽는다(UI-17)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.data.Logging;

/// <summary>최근 Capacity 개만 보관하는 스레드 안전 링 버퍼. 가득 차면 가장 오래된 항목을 버린다.</summary>
public sealed class MemoryLogSink : ILogSink
{
    private readonly PdLogEntry[] _buffer;
    private readonly object _gate = new();
    private int _start;
    private int _count;

    public MemoryLogSink(int capacity = 2000)
    {
        Capacity = Math.Max(1, capacity);
        _buffer = new PdLogEntry[Capacity];
    }

    public int Capacity { get; }

    /// <summary>항목이 추가될 때 — 로그를 쓴 스레드에서 발생한다. UI 는 Dispatcher 로 넘겨 받는다.</summary>
    public event Action<PdLogEntry>? Added;

    public void Write(in PdLogEntry entry)
    {
        lock (_gate)
        {
            _buffer[(_start + _count) % Capacity] = entry;
            if (_count < Capacity)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % Capacity;
            }
        }

        try
        {
            Added?.Invoke(entry);
        }
        catch (Exception)
        {
            // 구독자 오류가 로그 쓰기를 깨지 않게 한다
        }
    }

    /// <summary>지금 보관 중인 항목(오래된 것부터) 사본.</summary>
    public IReadOnlyList<PdLogEntry> Snapshot()
    {
        lock (_gate)
        {
            var list = new PdLogEntry[_count];
            for (var i = 0; i < _count; i++)
            {
                list[i] = _buffer[(_start + i) % Capacity];
            }

            return list;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _start = 0;
            _count = 0;
        }
    }
}

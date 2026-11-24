// 진행 상태 서비스 — 스코프(main·문서 Id)별 참조 카운트, 표시 지연·최소 표시 시간을 다루는 상태기계. Avalonia 비의존이며 시계를 주입해 시험한다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pd.Poc.Shell.Busy;

/// <summary>시각과 1회 예약 공급자. 실제 구현은 BusyHooks 의 디스패처 시계, 시험은 가짜 시계를 쓴다.</summary>
public interface IBusyClock
{
    long NowMs { get; }

    /// <summary>delayMs 뒤 callback 을 UI 스레드에서 1회 부른다. 반환값을 Dispose 하면 취소된다.</summary>
    IDisposable Schedule(int delayMs, Action callback);
}

/// <summary>스코프 1개의 현재 상태(진단용).</summary>
public readonly record struct BusyScopeInfo(string Scope, int Count, bool Visible, string LastReason);

/// <summary>
/// Begin 이 토큰을 돌려주고 토큰 Dispose 가 End 다. 스코프끼리는 서로 영향을 주지 않는다.
/// ShowDelayMs 안에 끝나면 표시하지 않고, 표시를 시작하면 MinVisibleMs 동안 유지한다(깜박임 방지).
/// 작업이 없으면 예약이 하나도 남지 않는다. 모든 호출은 UI 스레드에서 한다.
/// </summary>
public sealed class BusyService
{
    /// <summary>설정 공급자가 없을 때 쓰는 기본값(설정 기본값과 같다). 실제 적용값은 CurrentShowDelayMs·CurrentMinVisibleMs 다.</summary>
    public const int ShowDelayMs = 150;
    public const int MinVisibleMs = 150;

    private static Func<int>? s_showDelayProvider;
    private static Func<int>? s_minVisibleProvider;

    /// <summary>지금 적용되는 표시 지연(ms). 설정 공급자가 있으면 호출 때마다 읽어 설정 변경이 즉시 반영된다.</summary>
    public static int CurrentShowDelayMs => Math.Max(0, s_showDelayProvider?.Invoke() ?? ShowDelayMs);

    /// <summary>지금 적용되는 최소 유지 시간(ms). 설정 공급자가 있으면 호출 때마다 읽는다.</summary>
    public static int CurrentMinVisibleMs => Math.Max(0, s_minVisibleProvider?.Invoke() ?? MinVisibleMs);

    /// <summary>바쁨 표시 시간 설정 공급자를 연결한다(UiSettings 의 BusyShowDelayMs·BusyMinVisibleMs). null 이면 기본 상수로 돌아간다.</summary>
    public static void ConfigureTiming(Func<int>? showDelayMs, Func<int>? minVisibleMs)
    {
        s_showDelayProvider = showDelayMs;
        s_minVisibleProvider = minVisibleMs;
    }

    private sealed class ScopeState
    {
        public int Count;
        public bool Visible;
        public long VisibleSinceMs;
        public IDisposable? Pending; // 표시 예약 또는 숨김 예약
        public bool Armed; // 렌더 스레드 지연 표시를 걸어 둔 상태
        public long ArmedAtMs;
        public string LastReason = "";
    }

    private readonly IBusyClock _clock;
    private readonly Dictionary<string, ScopeState> _scopes = new(StringComparer.Ordinal);

    public BusyService(IBusyClock clock) => _clock = clock;

    /// <summary>스코프의 표시 상태가 바뀔 때마다 (스코프, 표시 여부)로 발생한다.</summary>
    public event Action<string, bool>? VisibilityChanged;

    /// <summary>preArm 시작(true)과, 표시 전에 끝나 취소됨 또는 표시 종료(false). 커서 등 UI 스레드 표시는 VisibilityChanged 를 따른다.</summary>
    public event Action<string, bool>? Armed;

    /// <summary>스코프에 작업 1건을 시작한다. 반환 토큰을 Dispose 하면 끝난다.</summary>
    public BusyScope Begin(string scope, string reason) => Begin(scope, reason, preArm: false);

    /// <summary>
    /// preArm=true 는 UI 스레드를 막을 작업용이다. 막혀 있으면 표시 예약이 돌지 못하므로 시작할 때 Armed(스코프, true)를 먼저 알린다.
    /// 받는 쪽은 렌더 스레드에서 ShowDelayMs 뒤 나타나도록 오버레이를 걸어 둔다. 그 안에 끝나면 Armed(스코프, false)로 취소되어 보이지 않는다.
    /// </summary>
    public BusyScope Begin(string scope, string reason, bool preArm)
    {
        if (!_scopes.TryGetValue(scope, out var state))
        {
            state = new ScopeState();
            _scopes[scope] = state;
        }

        state.LastReason = reason;
        state.Count++;
        if (state.Count == 1)
        {
            state.Pending?.Dispose(); // 표시 중이었다면 숨김 예약을 취소하고 계속 보인다
            state.Pending = state.Visible ? null : _clock.Schedule(CurrentShowDelayMs, () => Show(scope, state));
        }

        if (preArm && !state.Visible && !state.Armed)
        {
            state.Armed = true;
            state.ArmedAtMs = _clock.NowMs;
            Armed?.Invoke(scope, true);
        }

        return new BusyScope(this, scope, reason);
    }

    /// <summary>작업을 감싸 실행한다. main 스코프 전용이다 — 문서 단위 작업은 Docking.WindowLoadState.RunAsync 만 쓴다(23 §2.1).</summary>
    public async Task RunAsync(string scope, string reason, Func<Task> work)
    {
        using (Begin(scope, reason))
        {
            await work();
        }
    }

    /// <summary>
    /// 스코프를 통째로 비운다(문서 닫힘 해제). 예약을 지우고, 보이던 중이면 VisibilityChanged(false), 걸어 두었으면 Armed(false)를 낸다.
    /// 그 뒤 남은 토큰의 Dispose 는 아무 일도 하지 않는다(스코프가 없다). 지울 것이 있었으면 true.
    /// </summary>
    public bool Clear(string scope)
    {
        if (!_scopes.Remove(scope, out var state))
        {
            return false;
        }

        state.Pending?.Dispose();
        state.Pending = null;
        state.Count = 0;
        if (state.Visible)
        {
            state.Visible = false;
            VisibilityChanged?.Invoke(scope, false);
        }

        if (state.Armed)
        {
            state.Armed = false;
            Armed?.Invoke(scope, false);
        }

        return true;
    }

    /// <summary>스코프의 진행 중인 작업 수.</summary>
    public int CountOf(string scope) => _scopes.TryGetValue(scope, out var state) ? state.Count : 0;

    /// <summary>스코프가 지금 표시 중인가.</summary>
    public bool IsVisible(string scope) => _scopes.TryGetValue(scope, out var state) && state.Visible;

    /// <summary>살아 있는 스코프 전부의 상태(복사본).</summary>
    public IReadOnlyList<BusyScopeInfo> Snapshot() =>
        _scopes.Select(p => new BusyScopeInfo(p.Key, p.Value.Count, p.Value.Visible, p.Value.LastReason)).ToArray();

    /// <summary>예약이 남아 있는 스코프 수. 작업이 없으면 0 이어야 한다(진단 카운터).</summary>
    public int PendingTimerCount => _scopes.Values.Count(s => s.Pending is not null);

    internal void End(string scope)
    {
        if (!_scopes.TryGetValue(scope, out var state) || state.Count == 0)
        {
            return;
        }

        state.Count--;
        if (state.Count > 0)
        {
            return;
        }

        state.Pending?.Dispose();
        state.Pending = null;
        if (!state.Visible && state.Armed && _clock.NowMs - state.ArmedAtMs >= CurrentShowDelayMs)
        {
            Show(scope, state); // UI 스레드가 막혀 표시 예약이 못 돌았지만 렌더 스레드에서는 이미 보였다
        }

        if (!state.Visible)
        {
            _scopes.Remove(scope); // 표시 전에 끝남 — 스피너를 보이지 않는다
            if (state.Armed)
            {
                Armed?.Invoke(scope, false);
            }

            return;
        }

        var remaining = state.VisibleSinceMs + CurrentMinVisibleMs - _clock.NowMs;
        if (remaining <= 0)
        {
            Hide(scope, state);
        }
        else
        {
            state.Pending = _clock.Schedule((int)remaining, () => Hide(scope, state));
        }
    }

    private void Show(string scope, ScopeState state)
    {
        state.Pending?.Dispose();
        state.Pending = null;
        state.Visible = true;
        state.VisibleSinceMs = state.Armed ? state.ArmedAtMs + CurrentShowDelayMs : _clock.NowMs; // 걸어 둔 표시는 렌더 스레드가 나타낸 시각부터 센다
        VisibilityChanged?.Invoke(scope, true);
    }

    private void Hide(string scope, ScopeState state)
    {
        state.Pending?.Dispose();
        state.Pending = null;
        state.Visible = false;
        _scopes.Remove(scope);
        VisibilityChanged?.Invoke(scope, false);
        if (state.Armed)
        {
            Armed?.Invoke(scope, false);
        }
    }
}

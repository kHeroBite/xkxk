// 창 콘텐츠 불러오는 중 상태 — 문서 단위 유일 진입점(2단계 시작·토큰 1회 감소·UI 스레드 넘김·지연 판정·닫힘 해제). 정본 plan/09_화면설계/23_창_불러오는중_표시_설계.md §2
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Busy;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>상태 1건(진단용). Key 는 문서 Id 또는 결합 전 요청 키(req: 접두).</summary>
public readonly record struct LoadStateInfo(string Key, string? DocumentId, int Count, bool Slow, string Reasons);

/// <summary>
/// 창 아이콘의 불러오는 중 표시는 이것만 따른다(BusyService.IsVisible 무관). BusyService·BusyHooks 는 오버레이·커서 표시 정책 부품으로 남고,
/// 문서 스코프는 이 클래스만 건다(display=true 일 때). 시작·결합·조회는 UI 스레드에서 하고, 토큰 끝내기는 어느 스레드든 UI 스레드로 넘겨 처리한다.
/// </summary>
public sealed partial class WindowLoadState
{
    /// <summary>지연 판정 기본값(초) — ui.shell.busy_icon.slow_seconds 기본값과 같다.</summary>
    public const int DefaultSlowSeconds = 10;

    private const string RequestPrefix = "req:";
    private static Func<int>? s_slowSecondsProvider;
    private static WindowLoadState? s_default;

    private enum EndKind { Complete, Fail, Cancel }

    private sealed class State(string key, PocDocument? doc)
    {
        public string Key = key;
        public PocDocument? Doc = doc;
        public readonly List<Token> Live = new();
        public bool Released; // 닫힘 해제됨 — 늦게 끝난 작업은 무시한다
        public bool OpenClaimed; // 콘텐츠가 보고 계약으로 열기 토큰을 받아 갔다(뷰 부착이 완료가 아니다)
        public bool Slow;
        public IDisposable? SlowTimer;
    }

    private sealed class Token(WindowLoadState owner, State state, string reason, long startMs, bool isOpen) : ILoadToken
    {
        private int _ended;
        internal State State = state;
        internal readonly long StartMs = startMs;
        internal readonly bool IsOpen = isOpen;
        internal BusyScope? Busy; // 작업 단위 오버레이·커서(열기는 BusyHooks.TrackOpen 이 따로 맡는다)

        public string Reason { get; } = reason;
        public bool IsEnded => Volatile.Read(ref _ended) != 0;
        public void Complete() => End(EndKind.Complete, null);
        public void Fail(Exception? error = null) => End(EndKind.Fail, error);
        public void Cancel() => End(EndKind.Cancel, null);
        public void Dispose() => End(EndKind.Cancel, null);
        internal void Invalidate() => Interlocked.Exchange(ref _ended, 1);

        private void End(EndKind kind, Exception? error)
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0) // 어느 스레드에서 몇 번 불러도 처음 한 번만
            {
                owner.Finish(this, kind, error);
            }
        }
    }

    private readonly IBusyClock _clock;
    private readonly Func<bool> _checkAccess;
    private readonly Action<Action> _post;
    private readonly bool _display;
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly Token _ended;

    /// <param name="clock">지연 판정 시계(시험은 가짜 시계).</param>
    /// <param name="checkAccess">지금 UI 스레드인가.</param>
    /// <param name="post">UI 스레드로 넘기기.</param>
    /// <param name="display">BusyHooks 오버레이·커서 표시 정책을 함께 부를지(시험 인스턴스는 false).</param>
    public WindowLoadState(IBusyClock clock, Func<bool> checkAccess, Action<Action> post, bool display)
    {
        _clock = clock;
        _checkAccess = checkAccess;
        _post = post;
        _display = display;
        _ended = new Token(this, new State("", null), "ended", 0, isOpen: true);
        _ended.Invalidate();
    }

    /// <summary>앱 공용 인스턴스(디스패처 시계·UI 스레드 넘김·표시 정책 연결).</summary>
    public static WindowLoadState Default => s_default ??= new WindowLoadState(new DispatcherBusyClock(), Dispatcher.UIThread.CheckAccess, a => Dispatcher.UIThread.Post(a), display: true);

    /// <summary>지금 적용되는 지연 판정 시간(초).</summary>
    public static int CurrentSlowSeconds => Math.Max(1, s_slowSecondsProvider?.Invoke() ?? DefaultSlowSeconds);

    /// <summary>지연 판정 설정 공급자(ui.shell.busy_icon.slow_seconds)를 잇는다. null 이면 기본값.</summary>
    public static void ConfigureTiming(Func<int>? slowSeconds) => s_slowSecondsProvider = slowSeconds;

    /// <summary>상태가 바뀔 때(문서 또는 결합 전이면 null, 상태 키). UI 스레드에서 발생한다.</summary>
    public event Action<PocDocument?, string>? Changed;

    /// <summary>1단계: 창 열기 요청 순간(문서 생성 전) 시작한다. 문서가 생기면 Bind, 생기지 않으면 토큰을 Fail 로 끝낸다.</summary>
    public ILoadToken BeginRequest(string requestKey, string reason)
    {
        var st = GetOrAdd(RequestPrefix + requestKey, null);
        var token = Add(st, reason, isOpen: true);
        Update(st);
        return token;
    }

    /// <summary>2단계: 요청 키의 작업을 문서에 묶는다. 열기 토큰은 doc.LoadToken 으로 콘텐츠에 넘어가고 오버레이 표시를 시작한다.</summary>
    public void Bind(string requestKey, PocDocument doc)
    {
        if (!_states.Remove(RequestPrefix + requestKey, out var req))
        {
            return; // 이미 끝났거나 모르는 요청
        }

        var st = _states.TryGetValue(doc.Id, out var existing) ? existing : req;
        if (!ReferenceEquals(st, req))
        {
            foreach (var t in req.Live)
            {
                t.State = st;
                st.Live.Add(t);
            }

            req.SlowTimer?.Dispose();
        }

        st.Key = doc.Id;
        st.Doc = doc;
        _states[doc.Id] = st;
        if (st.Live.FirstOrDefault(t => t.IsOpen) is { } open)
        {
            StartOpen(doc, open);
        }

        Update(st);
    }

    /// <summary>요청 키 없이 문서가 바로 생긴 열기(레이아웃 복원·지연 생성)를 시작한다.</summary>
    public ILoadToken BeginOpen(PocDocument doc)
    {
        var st = GetOrAdd(doc.Id, doc);
        var token = Add(st, "open", isOpen: true);
        StartOpen(doc, token);
        Update(st);
        return token;
    }

    /// <summary>이미 있는 문서의 작업(검색·새로고침 등)을 시작한다. 겹치면 수를 합산한다.</summary>
    public ILoadToken Begin(PocDocument doc, string reason)
    {
        var st = GetOrAdd(doc.Id, doc);
        var token = Add(st, reason, isOpen: false);
        if (_display)
        {
            token.Busy = BusyHooks.Service.Begin(doc.Id, reason);
        }

        Update(st);
        return token;
    }

    /// <summary>닫힘 해제: 토큰을 모두 무효로 하고 수를 0 으로 두며 통지한다. 오버레이·커서·한 프레임 뒤 예약도 정리한다.</summary>
    public void Release(PocDocument doc)
    {
        if (!_checkAccess())
        {
            _post(() => Release(doc));
            return;
        }

        if (_states.Remove(doc.Id, out var st))
        {
            st.Released = true;
            foreach (var t in st.Live)
            {
                t.Invalidate();
                t.Busy = null; // 스코프는 아래 ReleaseScope 가 통째로 비운다
            }

            st.Live.Clear();
            st.SlowTimer?.Dispose();
            st.SlowTimer = null;
            st.Slow = false;
        }

        doc.LoadToken = null;
        doc.IsLoading = false;
        doc.IsLoadSlow = false;
        if (_display)
        {
            BusyHooks.ReleaseScope(doc.Id);
        }

        Changed?.Invoke(doc, doc.Id);
    }

    private State GetOrAdd(string key, PocDocument? doc)
    {
        if (!_states.TryGetValue(key, out var st))
        {
            st = new State(key, doc);
            _states[key] = st;
        }

        return st;
    }

    private Token Add(State st, string reason, bool isOpen)
    {
        var token = new Token(this, st, reason, _clock.NowMs, isOpen);
        st.Live.Add(token);
        return token;
    }

    private void StartOpen(PocDocument doc, Token open)
    {
        doc.LoadToken = open;
        doc.LastLoadError = null;
        if (_display)
        {
            BusyHooks.TrackOpen(doc.Id, BusyHooks.Service); // 기존 첫 열림 오버레이·커서(뷰 부착 때 끝남)
        }
    }

    private void Finish(Token token, EndKind kind, Exception? error)
    {
        if (!_checkAccess())
        {
            _post(() => Finish(token, kind, error)); // 상태 변경은 UI 스레드에서만
            return;
        }

        var st = token.State;
        if (st.Released || !st.Live.Remove(token))
        {
            return; // 닫힌 뒤 늦게 끝남
        }

        token.Busy?.Dispose();
        token.Busy = null;
        if (st.Doc is { } doc)
        {
            if (token.IsOpen && _display)
            {
                BusyHooks.EndOpenDisplay(doc.Id);
            }

            if (kind == EndKind.Fail)
            {
                doc.LastLoadError = error ?? new InvalidOperationException(token.Reason);
            }
        }

        if (st.Live.Count == 0)
        {
            _states.Remove(st.Key);
        }

        Update(st);
    }

    private void Update(State st)
    {
        st.SlowTimer?.Dispose();
        st.SlowTimer = null;
        st.Slow = false;
        if (st.Live.Count > 0 && !st.Released)
        {
            var limit = CurrentSlowSeconds * 1000L;
            var age = _clock.NowMs - st.Live.Min(t => t.StartMs);
            st.Slow = age > limit;
            if (!st.Slow)
            {
                st.SlowTimer = _clock.Schedule((int)(limit - age + 1), () => { st.SlowTimer = null; Update(st); });
            }
        }

        if (st.Doc is { } doc)
        {
            doc.IsLoading = st.Live.Count > 0 && !st.Released;
            doc.IsLoadSlow = st.Slow;
        }

        Changed?.Invoke(st.Doc, st.Key);
    }
}

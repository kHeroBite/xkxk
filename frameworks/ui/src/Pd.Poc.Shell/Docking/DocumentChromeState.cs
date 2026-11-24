// 문서 1개의 제목줄(크롬) 상태 — 항상 위·위치 고정·동기화·중복 허용·캐시 적중 값과 변경 알림(옛 WindowCommands.Chrome.cs 안의 공개 클래스를 그대로 옮김)
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pd.Poc.Shell.Docking;

/// <summary>문서 1개의 제목줄 상태. 제목줄 버튼(Views/ChromeButtons)이 구독해 글리프·색·툴팁을 갱신한다.</summary>
public sealed class DocumentChromeState : INotifyPropertyChanged
{
    private bool _isPinned;
    private bool _syncEnabled = true;
    private bool _allowDuplicate;
    private bool _isCached;
    private int _cacheHits;
    private int _cacheTotal;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>항상 위(Mars 📌). SDI 는 OS Topmost, MDI 는 다른 MDI 창보다 항상 앞 + 적갈색 제목줄. PD 는 저장한다(Mars 는 미저장 — 결정 대기).</summary>
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }

    private bool _isLocked;

    /// <summary>위치 고정(사용자 22:22 — 항상 위와 별개): 제목줄 끌기 이동·테두리 크기 조절 잠금. 최소·최대·닫기는 허용.</summary>
    public bool IsLocked { get => _isLocked; set => Set(ref _isLocked, value); }

    /// <summary>동기화(Mars 🔗/💥): 창 간 프로젝트 동기화 수신 여부. 꺼지면 이 창은 프로젝트 전환을 따라가지 않는다.</summary>
    public bool SyncEnabled { get => _syncEnabled; set => Set(ref _syncEnabled, value); }

    /// <summary>중복 허용(Mars 🔓/🔒): 같은 화면 여러 개 열기. 종류 단위로 공유된다.</summary>
    public bool AllowDuplicate { get => _allowDuplicate; set => Set(ref _allowDuplicate, value); }

    /// <summary>호환용(구 토글). 지금은 캐시 적중이 1건 이상인지(HitRatio &gt; 0)를 나타낸다. Mars 는 저장하지 않는다.</summary>
    public bool IsCached { get => _isCached; set => Set(ref _isCached, value); }

    /// <summary>이 창 쿼리의 캐시 적중 수(PoC 모의값 — 서버 없음).</summary>
    public int CacheHits { get => _cacheHits; set => Set(ref _cacheHits, value); }

    /// <summary>이 창 쿼리 전체 수(PoC 모의값). 데이터 로드 시작 때 다시 센다(Mars 와 같음).</summary>
    public int CacheTotal { get => _cacheTotal; set => Set(ref _cacheTotal, value); }

    public double CacheRatio => CacheTotal == 0 ? 0 : (double)CacheHits / CacheTotal;

    private bool _isMinimized;

    /// <summary>최소화 상태(U7): 상단 창 제목 탭이 활성/비활성/최소화를 색으로 구분하는 데 쓴다. MDI 는 MdiState, SDI 는 셸 최소화 목록으로 DocumentChromeService 가 맞춘다.</summary>
    public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

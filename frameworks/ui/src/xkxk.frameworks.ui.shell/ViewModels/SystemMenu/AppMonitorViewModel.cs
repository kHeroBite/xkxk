// 앱 자원 모니터 뷰모델(UI-17 모니터링 ▶) — 자기 프로세스의 메모리·GC·스레드·가동 시간을 1초마다 표본으로 읽는다(서버·DB 모니터링은 pd-admin 몫)
using System;
using System.Diagnostics;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels.SystemMenu;

/// <summary>
/// 창이 열려 있는 동안만 1초 타이머가 돈다(Dispose 로 멈춘다). 애니메이션 프레임 콜백은 쓰지 않는다 — 웹 유휴 rAF 0 조건(Q9 ⑤)을 지키기 위해서다.
/// 웹(WASM)은 작업 집합·프로세스 스레드 수를 얻을 수 없어 '해당 없음'으로 표시한다.
/// </summary>
public sealed partial class AppMonitorViewModel : ObservableObject, IDisposable
{
    private static readonly DateTimeOffset StartedAt = OperatingSystem.IsBrowser() ? DateTimeOffset.Now : SafeStart();

    private readonly DispatcherTimer _timer;
    private string _managedMb = "";
    private string _workingSetMb = "";
    private string _gc = "";
    private string _threads = "";
    private string _uptime = "";

    public AppMonitorViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
        Sample();
        _timer.Start();
    }

    /// <summary>지금까지 읽은 표본 수(시험 판정용 — 1초마다 1 증가).</summary>
    public int SampleCount { get; private set; }

    public string ManagedMb { get => _managedMb; private set => SetProperty(ref _managedMb, value); }

    public string WorkingSetMb { get => _workingSetMb; private set => SetProperty(ref _workingSetMb, value); }

    public string Gc { get => _gc; private set => SetProperty(ref _gc, value); }

    public string Threads { get => _threads; private set => SetProperty(ref _threads, value); }

    public string Uptime { get => _uptime; private set => SetProperty(ref _uptime, value); }

    public string Renderer => SystemInfo.Renderer ?? Loc.Tr("sysview.unknown");

    public void Dispose() => _timer.Stop();

    private void Sample()
    {
        SampleCount++;
        ManagedMb = Mb(GC.GetTotalMemory(false));
        WorkingSetMb = OperatingSystem.IsBrowser() ? Loc.Tr("sysview.notApplicable") : Mb(Environment.WorkingSet);
        Gc = $"{GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}";
        Threads = OperatingSystem.IsBrowser()
            ? Loc.Tr("sysview.notApplicable")
            : $"{ProcessThreads()} ({Loc.Tr("sysview.monitor.pool")} {ThreadPool.ThreadCount})";
        var up = DateTimeOffset.Now - StartedAt;
        Uptime = $"{(int)up.TotalHours:00}:{up.Minutes:00}:{up.Seconds:00}";
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0") + " MB";

    private static int ProcessThreads()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            return p.Threads.Count;
        }
        catch (Exception)
        {
            return -1; // 권한·플랫폼 제약은 -1 로 표시한다
        }
    }

    /// <summary>프로세스 시작 시각. 웹은 이 함수를 부르지 않는다(브라우저에서 Process 를 쓸 수 없다). 웹 가동 시간은 이 형식이 처음 쓰인 때부터다.</summary>
    private static DateTimeOffset SafeStart()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            return new DateTimeOffset(p.StartTime);
        }
        catch (Exception)
        {
            return DateTimeOffset.Now;
        }
    }
}

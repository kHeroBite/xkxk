// 메인 뷰모델의 하위 뷰모델 — 창 반짝임(MDI-05)·프로젝트 문맥 동기화(MDI-22)·트레이(MDI-21) 옵션·벤치 문서 열기
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels;

/// <summary>창 반짝임·프로젝트 문맥·트레이·벤치. 바인딩 경로는 MainViewModel.Extras.X.</summary>
public sealed class ShellExtrasViewModel : ObservableObject
{
    private const int FlashToggles = 6; // 3회 깜빡임
    private readonly ShellContext _ctx;
    private string _currentProject = "";

    public ShellExtrasViewModel(ShellContext ctx)
    {
        _ctx = ctx;
        OpenBenchCommand = new RelayCommand<string>(t => ViewRegistry.Current.OpenBench(t ?? "grid"));
        FlashCommand = new RelayCommand<PocDocument>(d => { if ((d ?? ctx.Main.ActiveDocument) is { } doc) { Flash(doc); } });
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Theming.UiSettings.TrayEnabled))
            {
                OnPropertyChanged(nameof(TrayEnabled)); // 메뉴 체크·트레이 표시(TrayMenuModel) 갱신 — 설정 복원·다른 경로 변경 포함
            }
        };
        ctx.Commands.OpenDocuments.CollectionChanged += (_, e) =>
        {
            // 새로 연 창도 현재 프로젝트 문맥을 이어받는다.
            foreach (var item in e.NewItems ?? Array.Empty<object>())
            {
                if (item is PocDocument doc)
                {
                    doc.Project = _currentProject;
                }
            }
        };
    }

    private UiSettings Settings => _ctx.Settings;

    /// <summary>PoC 샘플 프로젝트 목록(창 간 문맥 연동 시연용).</summary>
    public IReadOnlyList<string> Projects { get; } = new[] { "PRJ-001 사내 PMS", "PRJ-002 고객 포털", "PRJ-003 모바일 앱" }; // i18n-ok 샘플 데이터(프로젝트 이름)

    /// <summary>현재 선택 프로젝트. 바꾸면 열린 모든 창에 전파된다(Mars Fn프로젝트동기화).</summary>
    public string CurrentProject
    {
        get => _currentProject;
        set
        {
            if (SetProperty(ref _currentProject, value ?? ""))
            {
                SyncProject(_currentProject);
            }
        }
    }

    /// <summary>프로젝트 변경 알림 — 문서 뷰가 구독해 자기 데이터를 다시 불러오는 지점(이벤트 버스 PoC).</summary>
    public event Action<string>? ProjectChanged;

    /// <summary>트레이 아이콘 상주·최소화 시 트레이로(데스크톱 전용, 기본 끔). settings.json 에 저장된다(UiSettings.TrayEnabled).
    /// 비전경 시험 기동의 강제 끔은 표시 쪽(Pd.Poc.Shell.Platform.TrayMenuModel.Visible)이 맡아 이 저장값은 바꾸지 않는다.</summary>
    public bool TrayEnabled
    {
        get => Settings.TrayEnabled;
        set => Settings.TrayEnabled = value;
    }

    /// <summary>트레이 메뉴 항목 표시 여부 — 데스크톱 수명(IClassicDesktopStyleApplicationLifetime)에서만. Android·웹은 OS 트레이가 없어 숨긴다.</summary>
    public bool TrayAvailable =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;

    public RelayCommand<PocDocument> FlashCommand { get; }

    /// <summary>벤치 문서(그리드·차트 후보 비교, odev-gc Documents/BenchEntry)가 포함된 빌드인가. 제품 기본 빌드는 false → 메뉴 숨김.</summary>
    public bool BenchAvailable => ViewRegistry.Current.BenchAvailable;

    /// <summary>벤치 문서 열기(인자 grid|chart = 처음 보일 탭). 벤치 미포함 빌드에서는 아무것도 하지 않는다.</summary>
    public RelayCommand<string> OpenBenchCommand { get; }

    /// <summary>창 주의 환기: 탭을 3회 깜빡인다(Mars 포커스 반짝임 대응).</summary>
    public void Flash(PocDocument document)
    {
        var count = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            document.IsAttention = !document.IsAttention;
            if (++count >= FlashToggles)
            {
                document.IsAttention = false;
                timer.Stop();
            }
        };
        timer.Start();
    }

    private void SyncProject(string project)
    {
        var applied = 0;
        foreach (var doc in _ctx.Commands.OpenDocuments)
        {
            if (!WindowCommands.ChromeOf(doc).SyncEnabled)
            {
                continue; // 제목줄 '동기화' 해제 창은 수신하지 않음(UI-09, Mars 🔗/💥)
            }

            doc.Project = project;
            applied++;
        }

        _ctx.AddLog(Loc.Tr("log.window.projectSynced", project, applied, _ctx.Commands.OpenDocuments.Count));
        ProjectChanged?.Invoke(project);
    }
}

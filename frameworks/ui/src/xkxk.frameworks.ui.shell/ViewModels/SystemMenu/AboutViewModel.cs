// 프로그램 정보·업데이트 확인 뷰모델(UI-17) — 제품명·버전·채널·빌드·런타임·UI 라이브러리·렌더러 표시, 업데이트 서버 전에는 '미구축'과 플랫폼별 안내
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels.SystemMenu;

/// <summary>프로그램 정보와 업데이트 확인이 함께 쓴다. 언어가 바뀌면 문구 속성을 모두 다시 알린다.</summary>
public sealed partial class AboutViewModel : ObservableObject, IDisposable
{
    private string _updateStatus = "";
    private bool _disposed;

    public AboutViewModel()
    {
        CheckUpdateCommand = new RelayCommand(CheckUpdate);
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    public string Version => SystemInfo.Version;

    /// <summary>채널 표시 문구(예: '개발(dev)'). 연결 전이면 '알 수 없음'.</summary>
    public string Channel => SystemInfo.Channel is { Length: > 0 } c ? Loc.Tr("sysview.channel." + c) + $" ({c})" : Loc.Tr("sysview.unknown");

    /// <summary>채널 원문(dev·staging·qa·prod) — 시험 판정용. 연결 전이면 빈 문자열.</summary>
    public string ChannelCode => SystemInfo.Channel ?? "";

    public string BuildTime => SystemInfo.BuildTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? Loc.Tr("sysview.unknown");

    public string Platform => Loc.Tr("sysview.platform." + SystemInfo.Platform);

    public string Os => SystemInfo.OsDescription;

    public string Runtime => SystemInfo.Runtime;

    public string Libraries => $"Avalonia {SystemInfo.AvaloniaVersion} · Dock {SystemInfo.DockVersion}";

    public string Renderer => SystemInfo.Renderer ?? Loc.Tr("sysview.unknown");

    /// <summary>플랫폼별 업데이트 방식 안내(데스크톱 자동 업데이트 예정, 웹 새로고침, Android 스토어).</summary>
    public string UpdateHowTo => Loc.Tr("sysview.update.howto." + SystemInfo.Platform);

    /// <summary>업데이트 확인 결과. 확인 전에는 빈 문자열이다.</summary>
    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public RelayCommand CheckUpdateCommand { get; }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Loc.Instance.LanguageChanged -= OnLanguageChanged;
        }
    }

    /// <summary>업데이트 서버(pd-server)가 아직 없으므로 '미구축'을 알린다. 서버 연동 뒤 채널별 최신 버전 조회로 바꾼다.</summary>
    private void CheckUpdate() => UpdateStatus = Loc.Tr("sysview.update.noServer");

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(BuildTime));
        OnPropertyChanged(nameof(Platform));
        OnPropertyChanged(nameof(Renderer));
        OnPropertyChanged(nameof(UpdateHowTo));
        if (_updateStatus.Length > 0)
        {
            CheckUpdate();
        }
    }
}

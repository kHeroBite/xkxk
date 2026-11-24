// UI 설정의 표시 언어(Q17) — 언어 코드를 저장·복원하고 다국어 서비스(Loc)와 양방향으로 맞춘다
using System.ComponentModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>UiSettings 가 소유하는 표시 언어 설정. 속성 변경 알림은 UiSettings 가 같은 이름으로 다시 낸다.</summary>
internal sealed class UiLanguageSettings : ObservableObject
{
    private readonly UiSettings _owner;

    public UiLanguageSettings(UiSettings owner) => _owner = owner;

    /// <summary>현재 표시 언어 코드. 바꾸면 모든 화면 문자열이 즉시 바뀌고 저장된다. 사전이 없는 코드는 기본 언어(ko)가 된다.</summary>
    public string Language
    {
        get => Loc.Instance.Language;
        set => Loc.Instance.Language = value; // 결과는 OnLocChanged 에서 알림·저장
    }

    /// <summary>Install 때 1회: 원격(lang.set) 등 다른 경로로 언어가 바뀌어도 설정값 알림·저장이 따라오게 한다.</summary>
    public void Install(Application? app)
    {
        Loc.Instance.PropertyChanged += OnLocChanged;
        if (app is not null)
        {
            DockStrings.Install(app); // Dock 메뉴·표시 문자열도 현재 언어로(Localization/DockStrings.cs) — 시작·언어 변경 때만
        }
    }

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Loc.Language))
        {
            return;
        }

        using (UiSettings.Op("ui.language")) { OnPropertyChanged(nameof(Language)); }
        _owner.SaveUnlessLoading();
    }

    public void FillDto(UiSettingsDto dto) => dto.Language = Loc.Instance.Language;

    public void LoadDto(UiSettingsDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Language))
        {
            Language = dto.Language;
        }
    }
}

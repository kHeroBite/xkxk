// 설정 창 '언어' 페이지 뷰모델(요건 7) — 표시 언어 선택. 바꾸면 UiSettings.Language 로 저장되고 모든 화면이 즉시 바뀐다
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels.Settings;

public sealed class LanguagePageViewModel : SettingsPageViewModel
{
    // 언어 목록은 언어 전환과 무관하다. 목록을 다시 알리면 콤보가 순번 0 을 되써서 언어가 되돌아간다(Q17)
    private readonly IReadOnlyList<string> _codes = Loc.Instance.Languages.Select(l => l.Code).ToList();

    public LanguagePageViewModel(MainViewModel main)
        : base(main)
    {
        LanguageNames = Loc.Instance.Languages.Select(l => l.Name).ToList();
    }

    /// <summary>각 언어의 자기 이름(한국어, English …).</summary>
    public IReadOnlyList<string> LanguageNames { get; }

    public int LanguageIndex
    {
        get
        {
            var index = _codes.ToList().IndexOf(Loc.Instance.Language);
            return index < 0 ? 0 : index;
        }
        set
        {
            if (value >= 0 && value < _codes.Count && _codes[value] != Loc.Instance.Language)
            {
                Settings.Language = _codes[value];
            }
        }
    }

    public override void ResetToDefaults() => Settings.Language = Loc.DefaultLanguage;

    // 전체 다시 알림(string.Empty)을 쓰지 않는다 — 목록까지 다시 읽혀 콤보 선택이 흔들리면 안 된다
    protected override void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(LanguageIndex));

    protected override void OnLanguageChanged() => OnPropertyChanged(nameof(LanguageIndex));
}

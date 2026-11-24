// 언어를 따라 표시 문구만 바뀌는 선택지 항목(Q17) — 항목 객체는 그대로라 콤보의 선택 순번이 흔들리지 않는다(언어 되돌림 결함 근본 수정)
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>
/// 콤보 선택지 1개. 언어가 바뀌면 같은 객체의 Text 만 다시 알린다.
/// 목록(ItemsSource)을 새로 만들어 알리면 콤보가 선택을 잃고 순번 0 을 되써서 설정값이 바뀌었다(웹·Android 에서 언어가 ko 로 되돌아간 원인 — odev-6r 분석).
/// </summary>
public sealed class LocalizedOption : ObservableObject
{
    public LocalizedOption(string key)
    {
        Key = key;
        Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(Text));
    }

    /// <summary>사전 키.</summary>
    public string Key { get; }

    /// <summary>현재 언어 문구.</summary>
    public string Text => Loc.Tr(Key);

    public override string ToString() => Text;

    /// <summary>키 목록으로 고정 순서 선택지 목록을 만든다(앱 수명 동안 1회 — 항목은 해제되지 않는 단일 인스턴스).</summary>
    public static IReadOnlyList<LocalizedOption> List(params string[] keys) => keys.Select(k => new LocalizedOption(k)).ToList();
}

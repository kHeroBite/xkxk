// 문구 서비스 공개 계약 — 최소 표면만 두고 뒤에는 멤버 추가만 한다(UADR-0011)
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 현재 언어로 UI 문구를 찾아 주는 서비스다. <see cref="IUiAppContext.Localization"/> 으로 얻는다.
/// </summary>
/// <remarks>
/// 프레임워크만 구현한다. 소비자는 이 인터페이스를 구현하지 않는다.
/// 확장 규칙: 최소 표면만 공개한다. 진단 멤버(전체 키 목록·누락 키 목록)는 넣지 않으며,
/// 필요해지면 뒤 batch 에서 멤버를 추가만 한다(제거·서명 변경 금지).
/// 언어가 바뀌면 <see cref="LanguageChanged"/> 와 <see cref="INotifyPropertyChanged.PropertyChanged"/> 가 발생한다.
/// </remarks>
public interface IUiLocalizer : INotifyPropertyChanged
{
    /// <summary>현재 언어 코드(예 "ko"). 지원하지 않는 코드를 넣었을 때의 처리는 구현이 정한다.</summary>
    string Language { get; set; }

    /// <summary>현재 언어가 바뀐 뒤 발생한다.</summary>
    event Action? LanguageChanged;

    /// <summary>지원 언어 목록. 첫 항목이 기본 언어다. null 이 아니다.</summary>
    IReadOnlyList<UiLanguage> Languages { get; }

    /// <summary>키의 현재 언어 문구. <see cref="Get(string)"/> 과 같다.</summary>
    /// <param name="key">문구 키.</param>
    string this[string key] { get; }

    /// <summary>키의 현재 언어 문구를 돌려준다. 없는 키는 null 대신 대체 문자열을 돌려준다.</summary>
    /// <param name="key">문구 키.</param>
    string Get(string key);

    /// <summary>키의 문구에 <paramref name="args"/> 를 자리표시({0} 등)로 채워 돌려준다.</summary>
    /// <param name="key">문구 키.</param>
    /// <param name="args">자리표시 값.</param>
    string Format(string key, params object?[] args);

    /// <summary>현재 사전에 키가 있으면 true.</summary>
    /// <param name="key">문구 키.</param>
    bool HasKey(string key);

    /// <summary>키 문구를 관찰한다. 구독 즉시 현재 값을 내고, 언어가 바뀔 때마다 새 값을 낸다.</summary>
    /// <param name="key">문구 키.</param>
    IObservable<string> Observe(string key);
}

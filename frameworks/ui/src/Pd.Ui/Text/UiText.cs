// UI 층 문자열 훅 — 순수 UI 컨트롤이 다국어 서비스를 직접 참조하지 않고 키로 문자열을 받게 한다
using System;

namespace Pd.Ui.Text;

/// <summary>
/// 키 → 현재 언어 문자열 조회 훅. 상위 층(셸)이 기동 시 <see cref="Provider"/> 를 채운다.
/// 공급자가 없으면 빈 문자열을 돌려준다(키 문자열은 화면에 내보내지 않는다).
/// </summary>
public static class UiText
{
    /// <summary>상위 층이 기동 시 채우는 조회 함수.</summary>
    public static Func<string, string>? Provider { get; set; }

    /// <summary>키에 해당하는 현재 언어 문자열.</summary>
    public static string Tr(string key) => Provider?.Invoke(key) ?? string.Empty;
}

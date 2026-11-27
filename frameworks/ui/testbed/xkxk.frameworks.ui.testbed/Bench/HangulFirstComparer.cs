// 앱 고유 문자열 비교자(벤치 시험용) — ICU·OS 데이터에 기대지 않고 세 플랫폼에서 같은 순서를 낸다: 공백·기호 < 숫자 < 한글 < 한자 < 라틴(대소문자 무시) < 기타
using xkxk.frameworks.ui.shell;
using System;
using System.Collections;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.testbed.Bench;

/// <summary>
/// 데스크톱(Windows ICU 의 ko 맞춤 규칙)과 같은 큰 틀(한글이 라틴보다 앞)을 코드로 고정한 비교자다.
/// - 한글 음절(U+AC00–D7A3)은 코드 포인트 순이다. 유니코드 한글 음절은 초성·중성·종성 순으로 배열돼 있어 가나다 순과 같다.
///   호환 자모(ㄱ U+3131…)는 음절 앞에 둔다(같은 분류 안에서 코드 포인트 순).
/// - 라틴은 대소문자를 무시해 비교하고, 같으면 소문자를 앞에 둔다(a &lt; A &lt; b).
/// - 언어학적 완전 정렬(악센트·한자 독음·옛한글)은 하지 않는다. 그 대신 플랫폼과 무관하게 결정적이다.
/// </summary>
public sealed class HangulFirstComparer : IComparer, IComparer<string?>
{
    public static readonly HangulFirstComparer Instance = new();

    private static int Category(char c)
    {
        if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) { return 0; }
        if (c is >= '0' and <= '9') { return 1; }
        if (c is >= '가' and <= '힣' or >= 'ㄱ' and <= 'ㆎ' or >= 'ᄀ' and <= 'ᇿ') { return 2; }
        if (c is >= '一' and <= '鿿') { return 3; }
        if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') { return 4; }
        return 5;
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) { return 0; }
        if (x is null) { return -1; }
        if (y is null) { return 1; }
        var tie = 0;
        var n = Math.Min(x.Length, y.Length);
        for (var i = 0; i < n; i++)
        {
            char a = x[i], b = y[i];
            if (a == b) { continue; }
            int ca = Category(a), cb = Category(b);
            if (ca != cb) { return ca.CompareTo(cb); }
            if (ca == 4)
            {
                var la = char.ToLowerInvariant(a);
                var lb = char.ToLowerInvariant(b);
                if (la != lb) { return la.CompareTo(lb); }
                if (tie == 0) { tie = char.IsLower(a) ? -1 : 1; } // 대소문자만 다르면 끝까지 본 뒤 소문자 먼저
                continue;
            }

            return a.CompareTo(b);
        }

        if (x.Length != y.Length) { return x.Length.CompareTo(y.Length); }
        return tie;
    }

    int IComparer.Compare(object? x, object? y) => Compare(x?.ToString(), y?.ToString());
}

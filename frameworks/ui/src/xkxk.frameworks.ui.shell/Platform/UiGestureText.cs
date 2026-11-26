// 별별UI 단축키 문법(T-GESTURE) 검사·비교용 정규화 — 자체 문법 문자열 검사 + Avalonia KeyGesture 2단 검사(명령 레지스트리 내부용)
using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// 명령 단축키 문자열이 별별UI 단축키 문법에 맞는지 검사한다.
/// </summary>
/// <remarks>
/// <para>문법(자체 정의, Avalonia 판에 무관): 앞뒤 공백을 뺀 문자열을 '+' 로 나눈 구간. 마지막 구간은 키 1개,
/// 앞 구간들은 수정자(Ctrl·Shift·Alt·Meta, 대소문자 무시, 각 1회, 순서 무관) 0개 이상이다.</para>
/// <para>구현은 2단이다. ① 위 문법을 문자열로 검사하고 ② 통과하면 Avalonia <see cref="KeyGesture.Parse"/> 로 다시 해석해
/// 키 값·수정자 플래그를 확인한다. 키는 이름이 아니라 열거형 값으로 비교한다(Enter/Return·PageUp/Prior·PageDown/Next 는 같은 값).</para>
/// <para>목록 확장은 키를 더하는 추가만 한다. 이미 유효한 문자열이 무효로 바뀌는 변경은 하지 않는다.</para>
/// </remarks>
internal static class UiGestureText
{
    private const KeyModifiers DefinedModifiers = KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta;

    private static readonly string[] Modifiers = { "Ctrl", "Shift", "Alt", "Meta" };

    private static readonly HashSet<string> KeyNames = BuildKeyNames();

    /// <summary>단축키 문자열이 유효하면 true. null·빈 문자열은 false.</summary>
    internal static bool IsValid(string? text)
    {
        if (!TryParseSyntax(text, out var trimmed, out var keyName))
        {
            return false;
        }

        if (!Enum.TryParse<Key>(keyName, true, out var expected))
        {
            return false;
        }

        KeyGesture gesture;
        try
        {
            gesture = KeyGesture.Parse(trimmed);
        }
        catch (Exception)
        {
            // 특정 예외 형식에 묶지 않는다 — 판이 바뀌어 예외 형식이 달라져도 무효로만 본다.
            return false;
        }

        return gesture.Key != Key.None
            && Enum.IsDefined(typeof(Key), gesture.Key)
            && gesture.Key == expected
            && (gesture.KeyModifiers & ~DefinedModifiers) == 0;
    }

    /// <summary>
    /// 비교용 정규형 — 수정자(Ctrl·Shift·Alt·Meta 순)와 키를 '+' 로 잇고 대소문자를 위 목록 표기로 맞춘다. 무효면 null.
    /// </summary>
    internal static string? Normalize(string? gesture)
    {
        if (!IsValid(gesture) || !TryParseSyntax(gesture, out var trimmed, out var keyName)
            || !KeyNames.TryGetValue(keyName, out var key))
        {
            return null;
        }

        var parts = trimmed.Split('+');
        var result = new List<string>(parts.Length);
        foreach (var m in Modifiers)
        {
            if (Array.Exists(parts, p => string.Equals(p, m, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(m);
            }
        }

        result.Add(key);
        return string.Join("+", result);
    }

    /// <summary>1단 — 자체 문법 문자열 검사. 통과하면 앞뒤 공백을 뺀 문자열과 키 구간을 돌려준다.</summary>
    private static bool TryParseSyntax(string? text, out string trimmed, out string keyName)
    {
        trimmed = string.Empty;
        keyName = string.Empty;
        if (text is null)
        {
            return false;
        }

        trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var parts = trimmed.Split('+');
        foreach (var part in parts)
        {
            if (part.Length == 0 || ContainsWhiteSpace(part))
            {
                return false;
            }
        }

        var last = parts[parts.Length - 1];
        if (!KeyNames.Contains(last))
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!IsModifier(parts[i]) || !seen.Add(parts[i]))
            {
                return false;
            }
        }

        keyName = last;
        return true;
    }

    private static bool IsModifier(string part)
    {
        foreach (var m in Modifiers)
        {
            if (string.Equals(m, part, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWhiteSpace(string part)
    {
        foreach (var c in part)
        {
            if (char.IsWhiteSpace(c))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildKeyNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            names.Add(c.ToString());
        }

        for (var i = 0; i <= 9; i++)
        {
            names.Add("D" + i);
            names.Add("NumPad" + i);
        }

        for (var i = 1; i <= 24; i++)
        {
            names.Add("F" + i);
        }

        foreach (var n in new[]
        {
            "Tab", "Enter", "Escape", "Space", "Back", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
            "Left", "Up", "Right", "Down", "OemPlus", "OemMinus", "OemComma", "OemPeriod", "Add", "Subtract", "Multiply", "Divide",
        })
        {
            names.Add(n);
        }

        return names;
    }
}

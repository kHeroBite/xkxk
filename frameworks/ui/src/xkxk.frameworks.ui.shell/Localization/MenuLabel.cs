// 메뉴 문구의 표시용 이름 — 액세스 키 표기를 뺀다. 키보드 메뉴가 아닌 곳(모바일형 서랍·도움말 메뉴 줄)이 쓴다. 데스크톱 메뉴는 원문을 그대로 쓴다
using System;

namespace xkxk.frameworks.ui.shell.Localization;

public static class MenuLabel
{
    /// <summary>"파일(_F)" → "파일", "_File" → "File".</summary>
    public static string Strip(string text)
    {
        var open = text.IndexOf("(_", StringComparison.Ordinal);
        var close = open >= 0 ? text.IndexOf(')', open) : -1;
        return (close > open ? text.Remove(open, close - open + 1) : text).Replace("_", string.Empty);
    }

    /// <summary>사전 키의 표시용 이름.</summary>
    public static string Of(string key) => Strip(Loc.Tr(key));
}

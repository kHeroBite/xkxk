// 아이콘 저장소의 명시 등록 키 열거(internal) — 원격 registry.dump 의 icons.explicit 근거(B07, a8 한계 e 해소)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>아이콘 저장소 중 명시 등록 키 조회 부분. 공개 멤버가 아니다(IUiIconRegistry 무변경).</summary>
internal sealed partial class UiIconStore
{
    /// <summary>명시 등록(<see cref="IUiIconRegistry.Add"/>)된 키의 Ordinal 정렬 사본. 공급자 키는 들어 있지 않다.</summary>
    internal IReadOnlyList<string> ExplicitKeys
    {
        get
        {
            string[] keys;
            lock (_gate)
            {
                keys = new string[_explicit.Count];
                _explicit.Keys.CopyTo(keys, 0);
            }

            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }
    }
}

// 기본 생성 아이콘 공급자(xkxk.generated) — 생성 상수의 아이콘 키 169개를 MenuIcons.axaml 리소스 정의로 돌려준다(UADR-0013)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using xkxk.frameworks.ui.data.Navigation;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 프레임워크 기본 아이콘 공급자. 키는 명령 아이콘(<see cref="CmdIconKeys.All"/>)·메뉴 아이콘(<see cref="MenuIconCatalog.IconKeyByMenuId"/> 값)·
/// 셸 아이콘(<see cref="MenuIconCatalog.ShellIconKeyPrefix"/>+이름)·상태 아이콘 3개이며, 그림은 views/Themes/MenuIcons.axaml 의 StreamGeometry 다.
/// </summary>
/// <remarks>
/// 레지스트리 공급자 목록의 맨 끝에 항상 놓인다. 해석 결과는 리소스 키 = 아이콘 키인 <see cref="UiIconKind.Resource"/> 정의다.
/// 키 목록은 생성 상수에서 계산하므로 생성기(plan/_build/menu_icons.py)가 키를 더하면 자동으로 따라온다.
/// </remarks>
internal sealed class GeneratedIconProvider : UiIconProvider
{
    private readonly HashSet<string> _set;
    private readonly string[] _keys;

    internal GeneratedIconProvider()
    {
        _set = new HashSet<string>(StringComparer.Ordinal);
        _set.UnionWith(CmdIconKeys.All);
        _set.UnionWith(MenuIconCatalog.IconKeyByMenuId.Values);
        foreach (var name in MenuIconCatalog.ShellIconNames)
        {
            _set.Add(MenuIconCatalog.ShellIconKeyPrefix + name);
        }

        _set.Add(MenuIconCatalog.BusyStaticKey);
        _set.Add(MenuIconCatalog.BadgeSlowKey);
        _set.Add(MenuIconCatalog.BadgeErrorKey);

        _keys = new string[_set.Count];
        _set.CopyTo(_keys);
        Array.Sort(_keys, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public override string Id => UiRegistryOwners.Generated;

    /// <inheritdoc/>
    public override IReadOnlyCollection<string> Keys => _keys;

    /// <inheritdoc/>
    public override bool TryGetIcon(string key, [NotNullWhen(true)] out UiIconDefinition? icon)
    {
        if (key is not null && _set.Contains(key))
        {
            icon = UiIconDefinition.FromResource(key, key);
            return true;
        }

        icon = null;
        return false;
    }
}

// UI 설정 시스템 묶음의 시험 인자 해석 — --ui-mode·--ui-style 을 이번 실행 강제값으로 넣고, 옛 화면 모드를 UI 형으로 옮긴다(UiSystemSettings.cs 에서 줄 수 상한으로 나눔, 동작 동일)
using System;
using System.Collections.Generic;

namespace Pd.Poc.Shell.Theming;

internal sealed partial class UiSystemSettings
{
    /// <summary>옛 TabletUiMode → UiStyle 이전(Auto→자동, Tablet→태블릿형, Phone→모바일형). --ui-mode 별칭에도 쓴다.</summary>
    public static UiStyle FromLegacy(TabletUiMode mode)
        => mode switch { TabletUiMode.Tablet => Theming.UiStyle.Tablet, TabletUiMode.Phone => Theming.UiStyle.Mobile, _ => Theming.UiStyle.Auto };

    /// <summary>인자에서 --ui-style auto|pc|tablet|mobile(또는 --ui-style=값)을 소비해 UiStyleOverride 로 넣고 나머지 인자를 돌려준다.</summary>
    public static IReadOnlyList<string> TakeUiStyleArg(IReadOnlyList<string> args)
    {
        var rest = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string? value;
            if (a.StartsWith(UiStyleArg + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = a[(UiStyleArg.Length + 1)..];
            }
            else if (a.Equals(UiStyleArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                value = args[++i];
            }
            else
            {
                rest.Add(a);
                continue;
            }

            if (Enum.TryParse<UiStyle>(value?.Trim(), ignoreCase: true, out var style))
            {
                UiSettings.Current.UiStyleOverride = style;
            }
        }

        return rest;
    }

    /// <summary>인자에서 --ui-mode auto|tablet|phone(또는 --ui-mode=값)을 소비해 TabletUiModeOverride 로 넣고 나머지 인자를 돌려준다.</summary>
    public static IReadOnlyList<string> TakeUiModeArg(IReadOnlyList<string> args)
    {
        var rest = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string? value = null;
            if (a.StartsWith(UiModeArg + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = a[(UiModeArg.Length + 1)..];
            }
            else if (a.Equals(UiModeArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                value = args[++i];
            }
            else
            {
                rest.Add(a);
                continue;
            }

            if (Enum.TryParse<TabletUiMode>(value?.Trim(), ignoreCase: true, out var mode))
            {
                UiSettings.Current.TabletUiModeOverride = mode;
                UiSettings.Current.UiStyleOverride ??= FromLegacy(mode); // 호환 별칭(tablet→Tablet, phone→Mobile, auto→Auto). --ui-style 이 먼저 와 있으면 유지, 나중에 오면 덮어쓴다
            }
        }

        return rest;
    }
}

// SDI(플로팅 OS 창) 작업표시줄·OS 제목줄 아이콘을 그 창 활성 문서의 메뉴 아이콘으로 맞춘다(22 §2.3, B2)
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// SDI 호스트 창(IHostWindow)이 열리거나 활성화되거나 셸 활성 문서가 바뀌면, 그 창의 활성 문서(없으면 첫 문서) IconKey 로 Window.Icon 을 바꾼다.
/// 키는 제목줄·탭이 쓰는 값(PocDocument.IconKey, 없으면 MenuIconCatalog.IconKeyFor)과 같다. 문서가 없거나 비트맵을 못 만들면 앱 아이콘.
/// 창에 MenuIconBitmap.KeyProperty 로 적용 키를 남겨 진단 window.icon 이 읽는다. 메인 창은 건드리지 않는다(앱 아이콘 유지 — AC-026).
/// </summary>
internal static class SdiDocumentIcon
{
    private sealed class State
    {
        public PocDocument? Doc;
        public PropertyChangedEventHandler? Handler;
    }

    private static readonly ConditionalWeakTable<Window, State> s_states = new();
    private static bool s_installed;
    private static bool s_shellHooked;

    /// <summary>창을 만들기 전에 한 번 부른다. AppWindowIcon 은 이 클래스가 맡는 창을 건너뛴다(Handles).</summary>
    public static void Install()
    {
        s_installed = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (!Handles(window))
            {
                return;
            }

            window.Activated += (_, _) => Update(window);
            window.Closed += (_, _) => Detach(window);
            Update(window);
        });
    }

    /// <summary>이 클래스가 아이콘을 맡는 창(SDI 호스트)인가.</summary>
    public static bool Handles(Window window) => s_installed && window is IHostWindow;

    private static void HookShell()
    {
        if (s_shellHooked || App.Shell is not WindowCommands commands)
        {
            return;
        }

        s_shellHooked = true;
        commands.ActiveChanged += _ =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
            {
                foreach (var w in lt.Windows.Where(Handles).ToList())
                {
                    Update(w);
                }
            }
        };
    }

    private static void Update(Window window)
    {
        try
        {
            HookShell();
            var doc = window is IHostWindow { Window.Layout: { } root } ? WindowCommands.FindDocument(root) : null;
            Track(window, doc);
            var key = doc is null ? null : doc.IconKey ?? MenuIconCatalog.IconKeyFor(doc.ScreenId, doc.Kind.ToString());
            if (window.Icon is not null && string.Equals(window.GetValue(MenuIconBitmap.KeyProperty), key, StringComparison.Ordinal))
            {
                return; // 같은 키 — 다시 만들지 않는다
            }

            var icon = key is null ? null : MenuIconBitmap.Create(key);
            window.Icon = icon ?? ShellPlatform.AppIcon;
            window.SetValue(MenuIconBitmap.KeyProperty, icon is null ? null : key);
            Program.Log($"sdi-icon '{window.Title}' key={key ?? "-"} applied={(icon is null ? "app" : "doc")}");
        }
        catch (Exception ex)
        {
            Program.Log($"sdi-icon failed '{window.Title}': {ex.Message}"); // 아이콘 실패가 창 동작을 막지 않는다
            try
            {
                window.Icon = ShellPlatform.AppIcon; // 실패해도 Icon 을 비워 두지 않는다(AC-026 ICON_BIG·ICON_SMALL 유지)
                window.SetValue(MenuIconBitmap.KeyProperty, null);
            }
            catch (Exception ex2)
            {
                Program.Log($"sdi-icon fallback failed '{window.Title}': {ex2.Message}");
            }
        }
    }

    /// <summary>문서 IconKey 가 늦게 정해지는 경우(EnsureIdentity)를 위해 현재 문서의 속성 변경을 듣는다.</summary>
    private static void Track(Window window, PocDocument? doc)
    {
        var state = s_states.GetOrCreateValue(window);
        if (ReferenceEquals(state.Doc, doc))
        {
            return;
        }

        if (state.Doc is { } old && state.Handler is { } h)
        {
            old.PropertyChanged -= h;
        }

        state.Doc = doc;
        state.Handler = null;
        if (doc is not null)
        {
            state.Handler = (_, e) =>
            {
                if (e.PropertyName == nameof(PocDocument.IconKey))
                {
                    Update(window);
                }
            };
            doc.PropertyChanged += state.Handler;
        }
    }

    private static void Detach(Window window)
    {
        if (s_states.TryGetValue(window, out var state) && state.Doc is { } doc && state.Handler is { } h)
        {
            doc.PropertyChanged -= h;
        }

        s_states.Remove(window);
    }
}

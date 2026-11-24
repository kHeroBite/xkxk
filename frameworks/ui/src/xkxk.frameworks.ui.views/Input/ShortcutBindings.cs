// 메인 화면 키 바인딩 생성(요건 6) — tour_catalog.json 의 shortcuts 에서 binding 이 있는 줄을 KeyBinding 으로 건다. 등록 하나로 동작과 도움말 표시가 함께 생긴다
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Tour;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views.Input;

/// <summary>명령 이름 → 명령 표. 있는 명령에 키만 더할 때는 카탈로그만 고치고, 새 명령은 여기 한 줄을 더한다(검사기가 이름을 대조한다).</summary>
public static class ShortcutBindings
{
    public static void Apply(InputElement view, MainViewModel? vm)
    {
        view.KeyBindings.Clear();
        if (vm is null)
        {
            return;
        }

        foreach (var shortcut in ShortcutCatalog.Shortcuts)
        {
            if (shortcut.Binding is not { } name)
            {
                continue;
            }

            if (Command(vm, name) is { } command)
            {
                view.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse(shortcut.Keys), Command = command });
            }
            else
            {
                PdLog.WriteSystem(PdLogLevel.Error, $"shortcut binding not found id={shortcut.Id} binding={name}");
            }
        }
    }

    private static ICommand? Command(MainViewModel vm, string name) => name switch
    {
        // 키 바인딩은 WindowKeyGestures 터널보다 먼저 실행되므로 keyboard 출처를 여기서 넘긴다(메뉴의 NextCommand 는 출처 없음 그대로)
        "Next" => new RelayCommand(() => vm.Commands.ActivateNext(ActivationOrigin.Keyboard)),
        "Previous" => new RelayCommand(() => vm.Commands.ActivatePrevious(ActivationOrigin.Keyboard)),
        "Close" => vm.CloseCommand,
        "SaveLayout" => vm.SaveLayoutCommand,
        _ => null,
    };
}

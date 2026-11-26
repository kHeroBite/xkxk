// 메뉴 항목 → 명령 레지스트리 실행 어댑터 — 메뉴가 닫힌 뒤 Dispatcher Post 로 XkxkUi.Current.Commands.Execute(id, param) 를 부른다(UADR-0014)
using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>
/// 레지스트리 메뉴 항목의 <see cref="ICommand"/>. 실행 가능 여부는 명령 레지스트리에 그대로 묻고,
/// 실행은 메뉴가 닫힌 다음 프레임에 하도록 UI 스레드에 Post 한다(대화상자·덮개가 닫히는 메뉴와 겹치지 않게 — 옛 '창 전체 보기'·스냅 칸과 같은 방식).
/// </summary>
/// <remarks>레지스트리에는 실행 가능 여부 변경 알림이 없으므로 화면이 메뉴를 열 때 <see cref="RaiseCanExecuteChanged"/> 로 다시 묻게 한다.</remarks>
internal sealed class RegistryMenuCommand : ICommand
{
    internal RegistryMenuCommand(string commandId, object? parameter)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        CommandId = commandId;
        Parameter = parameter;
    }

    /// <summary>실행할 명령 ID.</summary>
    internal string CommandId { get; }

    /// <summary>정의에 적힌 명령 값(메뉴 항목의 CommandParameter 대신 이 값을 넘긴다).</summary>
    internal object? Parameter { get; }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => XkxkUi.Current.Commands.CanExecute(CommandId, Parameter);

    public void Execute(object? parameter)
    {
        var id = CommandId;
        var value = Parameter;
        Dispatcher.UIThread.Post(() => XkxkUi.Current.Commands.Execute(id, value));
    }

    /// <summary>메뉴가 열릴 때 실행 가능 여부를 다시 묻게 한다.</summary>
    internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 명령 단축키 문자열 → 메뉴 단축키 표시. 글자 초기화(Ctrl+0)는 실제 키가 플랫폼 명령 수정키(macOS = Cmd)라 표기도 그 수정키로 바꾼다(옛 ViewMenu 생성자 로직).
    /// 메뉴 템플릿(PlatformKeyGestureConverter)이 D0 을 '0' 으로 보인다.
    /// </summary>
    internal static KeyGesture? DisplayGesture(string? text, string? commandId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        KeyGesture gesture;
        try
        {
            gesture = KeyGesture.Parse(text);
        }
        catch (Exception)
        {
            return null; // 레지스트리가 이미 검사한 값이라 오지 않는다 — 와도 단축키 표시만 뺀다.
        }

        if (commandId == ShellCommandIds.ViewFontReset
            && Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers is { } cmd && cmd != KeyModifiers.None
            && gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return new KeyGesture(gesture.Key, (gesture.KeyModifiers & ~KeyModifiers.Control) | cmd);
        }

        return gesture;
    }
}

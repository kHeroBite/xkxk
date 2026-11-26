// 소비자 명령 단축키 연결 — 메인 TopLevel 의 처리 안 된 KeyDown 에서 레지스트리 명령(프레임워크 주체 제외)의 Gesture 를 맞춰 실행한다(UADR-0013·0014)
using System;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Input;

/// <summary>
/// 모듈·앱이 등록한 명령의 단축키(<see cref="UiCommandDefinition.Gesture"/>)를 실제 키 입력에 잇는다.
/// 셸 기본 명령(주체가 <see cref="UiRegistryOwners.ReservedPrefix"/> 로 시작)은 기존 경로(ShortcutBindings·WindowKeyGestures)가 그대로 맡으므로 여기서는 건너뛴다.
/// </summary>
/// <remarks>
/// 다른 처리기가 이미 처리한 키는 보지 않는다(버블 단계, handledEventsToo=false). 맞는 명령 중 지금 실행할 수 있는 첫 명령을 실행하고 Handled 로 둔다.
/// 형식이 틀린 단축키 문자열은 등록 때 XKUI0305 로 알렸으므로 여기서는 조용히 무시한다.
/// </remarks>
internal static class RegistryGestureInput
{
    // 단축키 문자열 → 해석 결과(틀린 형식은 null). 명령 수만큼만 쌓인다
    private static readonly Dictionary<string, KeyGesture?> s_parsed = new(StringComparer.Ordinal);

    /// <summary>TopLevel 에 처리기를 붙인다. 돌려받은 값을 Dispose 하면 뗀다.</summary>
    /// <param name="topLevel">메인 TopLevel. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static IDisposable Attach(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        return new Detacher(topLevel);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key == Key.None)
        {
            return;
        }

        var commands = XkxkUi.Current.Commands;
        foreach (var command in commands.Commands)
        {
            if (command.Owner.StartsWith(UiRegistryOwners.ReservedPrefix, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(command.Gesture)
                || Parse(command.Gesture) is not { } gesture
                || !gesture.Matches(e))
            {
                continue;
            }

            if (commands.CanExecute(command.Id))
            {
                e.Handled = true;
                commands.Execute(command.Id);
                return;
            }
        }
    }

    private static KeyGesture? Parse(string text)
    {
        lock (s_parsed)
        {
            if (s_parsed.TryGetValue(text, out var cached))
            {
                return cached;
            }
        }

        KeyGesture? parsed;
        try
        {
            parsed = KeyGesture.Parse(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            parsed = null;
        }

        lock (s_parsed)
        {
            s_parsed[text] = parsed;
        }

        return parsed;
    }

    private sealed class Detacher : IDisposable
    {
        private TopLevel? _topLevel;

        public Detacher(TopLevel topLevel) => _topLevel = topLevel;

        public void Dispose()
        {
            _topLevel?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _topLevel = null;
        }
    }
}

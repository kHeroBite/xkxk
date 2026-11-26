// 명령 저장소(UiCommandStore)의 단축키 진단 — 문법 무효(XKUI0305)·다른 명령과의 충돌(XKUI0310)
using System;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.shell.Framework;

internal sealed partial class UiCommandStore
{
    /// <summary>
    /// 프레임워크 아닌 주체의 명령과 단축키가 같은(<see cref="UiGestureText.Normalize"/> 비교) 다른 명령 중 목록에서 첫 것.
    /// 프레임워크 주체·단축키 없음·겹침 없음이면 null. 같은 ID(교체 대상)는 비교하지 않는다. 호출자가 잠금을 쥔다.
    /// </summary>
    private UiCommandDefinition? FindGestureConflict(UiCommandDefinition command)
    {
        if (UiRegistryOwners.IsFramework(command.Owner) || UiGestureText.Normalize(command.Gesture) is not { } key)
        {
            return null;
        }

        return _entries.Find(e => !string.Equals(e.Definition.Id, command.Id, StringComparison.Ordinal)
            && string.Equals(UiGestureText.Normalize(e.Definition.Gesture), key, StringComparison.Ordinal))?.Definition;
    }

    /// <summary>등록된 명령의 단축키 진단을 남긴다. 무효라 버렸으면 XKUI0305, 겹치면 XKUI0310(Warning). 어느 쪽이든 등록은 유지한다.</summary>
    /// <param name="sink">진단을 남길 곳.</param>
    /// <param name="command">등록한 명령(무효 단축키는 <see cref="UiCommandDefinition.Gesture"/> 가 null).</param>
    /// <param name="requested">등록 때 요청한 단축키 문자열.</param>
    /// <param name="conflict"><see cref="FindGestureConflict"/> 결과.</param>
    private static void ReportGesture(UiDiagnostics sink, UiCommandDefinition command, string? requested, UiCommandDefinition? conflict)
    {
        if (requested is not null && command.Gesture is null)
        {
            Fail(sink, UiDiagnosticIds.CommandGestureInvalid, $"명령 '{command.Id}' 의 단축키 '{requested}' 가 문법에 맞지 않아 버린다.", command.Owner); // i18n-ok 개발자 진단
        }
        else if (conflict is not null)
        {
            sink.Report(new UiDiagnostic(UiDiagnosticIds.CommandGestureConflict, UiDiagnosticSeverity.Warning,
                $"명령 '{command.Id}' 의 단축키 '{command.Gesture}' 가 명령 '{conflict.Id}'('{conflict.Gesture}')과 겹친다. 키는 한 명령만 받는다.", command.Owner)); // i18n-ok 개발자 진단
        }
    }
}

// 메뉴 저장소 규칙 부분 — 종류 검사·부모 검사·자리 배치·트리 순회·표시 상태(GetState)·Build 끝 명령 검사(UADR-0014)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>메뉴 저장소의 규칙·조회 부분. 이름이 Locked 로 끝나지 않아도 _items 를 만지는 도우미는 잠금 안에서만 부른다.</summary>
internal sealed partial class UiMenuStore
{
    private const string VisibleKey = "|visible";
    private const string ToolTipKey = "|toolTip";

    /// <summary>
    /// Build 끝 검사: 그 세션 동안 모듈이 등록한 Command 항목 중 명령이 끝까지 없는 것마다 XKUI0405 를 sink 에 남긴다.
    /// 세션 완료(<see cref="UiBuildSession.Complete"/>) 전에 부른다. 그 사이 지웠거나 다른 명령으로 바꾼 항목은 검사하지 않는다.
    /// </summary>
    internal void CompleteBuild(UiBuildSession session, UiDiagnostics sink)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sink);
        var pending = new List<(string Id, string CommandId, string Owner)>();
        lock (_gate)
        {
            if (_pending.Remove(session, out var all))
            {
                pending = all.FindAll(p => IndexOf(p.Id) is var i && i >= 0
                    && string.Equals(_items[i].CommandId, p.CommandId, StringComparison.Ordinal));
            }
        }

        foreach (var p in pending.FindAll(x => !_app.Commands.Contains(x.CommandId)))
        {
            ReportCommandMissing(sink, p.Id, p.CommandId, p.Owner);
        }
    }

    /// <summary>Build 도중이면 명령 검사를 Build 끝으로 미루고 true. 세션이 없으면(실행 중) false — 호출자가 즉시 검사한다.</summary>
    private bool DeferCommand(UiBuildSession? session, string id, string commandId, string owner)
    {
        if (session is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_pending.TryGetValue(session, out var list))
            {
                _pending[session] = list = new List<(string Id, string CommandId, string Owner)>();
            }

            list.Add((id, commandId, owner));
        }

        return true;
    }

    /// <summary>종류와 속성 조합 위반 설명(XKUI0406). 맞으면 null.</summary>
    private static string? KindError(UiMenuItemOptions o) => o.Kind switch
    {
        UiMenuItemKind.Command when string.IsNullOrWhiteSpace(o.CommandId) => "Command 항목에 CommandId 가 없다.", // i18n-ok 개발자 진단
        UiMenuItemKind.Command => null,
        UiMenuItemKind.Submenu or UiMenuItemKind.Separator or UiMenuItemKind.Section when o.CommandId is not null =>
            "Command 가 아닌 항목에 CommandId 가 있다.", // i18n-ok 개발자 진단
        UiMenuItemKind.Section when string.IsNullOrWhiteSpace(o.Section) => "Section 항목에 Section 키가 없다.", // i18n-ok 개발자 진단
        UiMenuItemKind.Submenu or UiMenuItemKind.Separator or UiMenuItemKind.Section => null,
        _ => "알 수 없는 종류다.", // i18n-ok 개발자 진단
    };

    /// <summary>parent 가 있고 하위 메뉴이며 self 자신·self 의 자손이 아니면 true.</summary>
    private bool ValidParent(string parent, string self)
    {
        var index = IndexOf(parent);
        return index >= 0 && _items[index].Kind == UiMenuItemKind.Submenu
            && !string.Equals(parent, self, StringComparison.Ordinal) && !Subtree(self).Exists(d => d.Id == parent);
    }

    private bool HasChildren(string id) => _items.Exists(d => string.Equals(d.Parent, id, StringComparison.Ordinal));

    /// <summary>
    /// def 를 부모 아래 InsertAfter(우선)·InsertBefore 자리에 넣는다. 둘 다 없으면 맨 뒤. 기준이 같은 부모 아래 없으면 맨 뒤에 넣고 false.
    /// </summary>
    private bool Place(UiMenuItemDefinition def, string? after, string? before)
    {
        var anchor = after ?? before;
        var index = anchor is null ? -1 : _items.FindIndex(d => string.Equals(d.Id, anchor, StringComparison.Ordinal)
            && string.Equals(d.Parent, def.Parent, StringComparison.Ordinal));
        if (index < 0)
        {
            _items.Add(def);
            return anchor is null;
        }

        // 형제 순서는 평면 목록 안 순서다. 기준 바로 뒤(또는 앞) 칸이 곧 형제 사이의 그 자리다.
        _items.Insert(after is not null ? index + 1 : index, def);
        return true;
    }

    /// <summary>id 항목과 그 자손 전부(깊이 우선). 없으면 빈 목록.</summary>
    private List<UiMenuItemDefinition> Subtree(string id)
    {
        var result = new List<UiMenuItemDefinition>();
        var index = IndexOf(id);
        if (index >= 0)
        {
            Collect(_items[index], result);
        }

        return result;
    }

    private void Collect(UiMenuItemDefinition item, List<UiMenuItemDefinition> result)
    {
        result.Add(item);
        foreach (var child in _items.FindAll(d => string.Equals(d.Parent, item.Id, StringComparison.Ordinal)))
        {
            Collect(child, result);
        }
    }

    private int IndexOf(string id) => _items.FindIndex(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    private UiMenuItemDefinition? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate) { var i = IndexOf(id); return i < 0 ? null : _items[i]; }
    }

    /// <summary>트리 깊이 우선 순서 사본(부모 다음에 그 자식들).</summary>
    private IReadOnlyList<UiMenuItemDefinition> Snapshot()
    {
        var result = new List<UiMenuItemDefinition>();
        lock (_gate)
        {
            foreach (var root in _items.FindAll(d => d.Parent is null))
            {
                Collect(root, result);
            }
        }

        return result.ToArray();
    }

    private IReadOnlyList<UiMenuItemDefinition> Children(string? parentId)
    {
        lock (_gate) { return _items.FindAll(d => string.Equals(d.Parent, parentId, StringComparison.Ordinal)).ToArray(); }
    }

    /// <summary>열 때 평가하는 표시 상태. 대리자·명령 레지스트리는 잠금 밖에서 부른다.</summary>
    private UiMenuItemState GetState(string id, UiMenuSurface surface)
    {
        if (Find(id) is not { } def)
        {
            return new UiMenuItemState(false, false, null, null, null, null);
        }

        var ctx = new UiMenuContext(_app, def, surface);
        var visible = !def.IsHidden && Evaluate(def, VisibleKey, () => def.VisibilityHandler is not { } f || f(ctx), false);
        var toolTip = def.ToolTipHandler is { } tip ? Evaluate(def, ToolTipKey, () => tip(ctx), null) : null;
        UiCommandDefinition? cmd = null;
        if (def.CommandId is { } commandId)
        {
            _app.Commands.TryGet(commandId, out cmd);
        }

        var enabled = def.Kind switch
        {
            UiMenuItemKind.Command => cmd is not null && _app.Commands.CanExecute(cmd.Id, def.CommandParameter),
            _ => true,
        };
        // 명령 제목은 명령 계약대로 문구 키 우선(UiCommandOptions.TitleKey). 항목 제목은 계약대로 Title ?? 문구 키.
        var title = def.Title ?? Text(def.TitleKey) ?? Text(cmd?.TitleKey) ?? cmd?.Title;
        return new UiMenuItemState(visible, enabled, title, def.Icon ?? cmd?.Icon, cmd?.Gesture, toolTip);
    }

    private string? Text(string? key) => key is null ? null : _app.Localization.Get(key);

    /// <summary>조건 대리자를 부른다. 예외면 항목·키당 1회 XKUI0411(Warning, 앱 Diagnostics)을 남기고 fallback.</summary>
    private T Evaluate<T>(UiMenuItemDefinition def, string key, Func<T> evaluate, T fallback)
    {
        try
        {
            return evaluate();
        }
        catch (Exception ex)
        {
            bool first;
            lock (_gate) { first = _conditionReported.Add(def.Id + key); }
            if (first)
            {
                Warn(_runtime, UiDiagnosticIds.MenuConditionFailed,
                    $"메뉴 '{def.Id}' 의 {key.TrimStart('|')} 대리자 예외 {ex.GetType().FullName}: {ex.Message}", def.Owner); // i18n-ok 개발자 진단
            }

            return fallback;
        }
    }

    /// <summary>교체·제거된 항목은 조건 진단 1회 기록을 지운다(새 대리자는 다시 1회 보고).</summary>
    private void ForgetConditionReports(string id)
    {
        _conditionReported.Remove(id + VisibleKey);
        _conditionReported.Remove(id + ToolTipKey);
    }

    private static void ReportCommandMissing(UiDiagnostics sink, string id, string commandId, string owner) =>
        Fail(sink, UiDiagnosticIds.MenuCommandMissing, $"메뉴 '{id}' 의 명령 '{commandId}' 가 명령 레지스트리에 없다. 항목은 비활성으로 보인다.", owner); // i18n-ok 개발자 진단

    private static bool Fail(UiDiagnostics sink, string id, string message, string owner)
    {
        sink.Report(new UiDiagnostic(id, UiDiagnosticSeverity.Error, message, owner));
        return false;
    }

    private static void Warn(UiDiagnostics sink, string id, string message, string owner) =>
        sink.Report(new UiDiagnostic(id, UiDiagnosticSeverity.Warning, message, owner));

    /// <summary>잠금 안 등록 결과. Code 가 있으면 위반(등록 안 함).</summary>
    private readonly record struct Outcome(bool Replaced, bool AnchorMissing, string? Code = null, string? Message = null)
    {
        internal static Outcome Error(string code, string message) => new(false, false, code, message);
    }
}

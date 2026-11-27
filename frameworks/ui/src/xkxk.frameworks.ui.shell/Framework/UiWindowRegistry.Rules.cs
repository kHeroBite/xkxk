// 창 저장소 규칙 부분 — 등록(ID·예약·옛 이름 예약어·ID/별칭 이름공간 충돌·별칭 검사, XKUI0501~0504)·제거(소유 P2, XKUI0508)·Remove/Replace 와 열린 문서의 관계(a10)
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>창 저장소의 등록·제거 규칙. 이름이 Locked 로 끝나는 도우미는 잠금 안에서만 부른다.</summary>
/// <remarks>
/// <para>정책 P1~P4·Replace 는 명령 레지스트리와 같다. P1 Replace 인데 없으면 새로 등록(Added), P2 제거는 등록한 주체만,
/// P3 교체는 제자리(Items 순서 유지)·Owner 는 교체한 주체로 옮긴다.</para>
/// <para>Remove·Replace 는 이미 열린 문서를 닫거나 바꾸지 않는다(저장소는 셸을 부르지 않는다). 그 뒤 새로 열기·복원은
/// Replace 면 새 정의를, Remove 면 등록 안 된 창(자리표시, XKUI0505)으로 처리한다.</para>
/// <para>ID 와 별칭은 한 이름공간이다(a10 §3). 새 ID 가 다른 창의 ID·별칭과 대소문자 무시로 같으면 등록하지 않고
/// XKUI0502(false)다. 같은 ID(Ordinal)의 Replace 는 교체 대상 자신을 비교에서 뺀다. 대소문자만 다른 ID 는 새 ID 라 Replace 와 무관하게 거부한다.</para>
/// <para>옛 이름·숫자(Grid·Gantt·Form·"0"·"1"·"2", 대소문자 무시)는 영구 예약어다(D-V2, a10 §4). 셸 주체(<c>xkxk.shell</c>) 밖의
/// 등록이 이를 ID 로 쓰면 Replace·<c>xkxk.</c> 모듈과 무관하게 XKUI0502(false), 별칭으로 쓰면 그 별칭만 버리고 XKUI0503(Error) 후 등록한다(true).</para>
/// <para>진단 우선순위: 옛 이름 ID(0502) → 중복(0502) → 예약 ID(0503) → ID 충돌(0502). 한 등록에 등록 거부 진단은 하나만 남는다.</para>
/// <para>별칭: 프레임워크 아닌 주체의 <c>xkxk.</c> 별칭(대소문자 무시)·셸 밖 주체의 옛 이름·숫자 별칭은 버리고 XKUI0503(Error) 후 등록한다.
/// 그다음 다른 창의 ID·별칭과 대소문자 무시로 같은 별칭은 그것만 버리고 XKUI0504(Warning) 후 등록한다. 버린 별칭은 정의 Aliases 에도 남지 않는다.
/// 같은 ID 를 교체하며 자기 별칭을 유지하는 것은 충돌이 아니다. 빈 별칭·자기 ID 와 같은 별칭·목록 안 중복은 진단 없이 버린다.</para>
/// <para>ID 예약 판정(<c>xkxk.</c>)은 대소문자를 구분하고, 별칭 예약 판정은 별칭 해석처럼 대소문자를 무시한다.</para>
/// </remarks>
internal sealed partial class UiWindowStore
{
    private bool Add(string owner, UiDiagnostics sink, string id, Action<UiWindowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(configure);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Fail(sink, UiDiagnosticIds.WindowIdInvalid, "창 ID 가 비어 있어 건너뛴다.", owner); // i18n-ok 개발자 진단
        }

        var o = new UiWindowOptions();
        configure(o);
        var framework = UiRegistryOwners.IsFramework(owner);
        var shell = string.Equals(owner, UiRegistryOwners.Shell, StringComparison.Ordinal);
        var legacyId = !shell && IsLegacyName(id);
        var reserved = !framework && UiRegistryOwners.IsReserved(id);
        var rejected = new List<(string Id, UiDiagnosticSeverity Severity, string Message)>();
        int index;
        string? conflict = null;
        lock (_gate)
        {
            // 옛 이름 ID(D-V2)·중복(Replace 아님)·예약(T-RESERVED ①: 없는 xkxk.* ID 를 새로 만듦, Replace 무관)·
            // ID 충돌(새 ID 가 다른 창의 ID·별칭과 대소문자 무시로 같음, R1)은 등록하지 않는다.
            index = IndexOf(id);
            if (!legacyId && index < 0 && !reserved)
            {
                conflict = FindOtherLocked(id, id)?.Id;
            }

            if (!legacyId && ((index >= 0 && o.Replace) || (index < 0 && !reserved && conflict is null)))
            {
                var def = new UiWindowDefinition(id, owner, o, FilterAliasesLocked(id, o.Aliases, framework, shell, rejected));
                if (index >= 0)
                {
                    _items[index] = def; // P3 제자리 교체, P2 교체는 주체 무관·Owner 이동.
                }
                else
                {
                    _items.Add(def); // P1 Replace 인데 없으면 새로 등록(Added).
                }
            }
        }

        if (legacyId)
        {
            return Fail(sink, UiDiagnosticIds.WindowDuplicate, $"창 ID '{id}' 는 옛 문서 종류 이름·숫자(셸 기본 창 영구 별칭)라 셸 밖에서 쓸 수 없다.", owner); // i18n-ok 개발자 진단
        }

        if (index >= 0 && !o.Replace)
        {
            return Fail(sink, UiDiagnosticIds.WindowDuplicate, $"창 '{id}' 가 이미 등록돼 있다. 첫 등록을 유지한다.", owner); // i18n-ok 개발자 진단
        }

        if (index < 0 && reserved)
        {
            return Fail(sink, UiDiagnosticIds.WindowReservedId, $"창 ID '{id}' 는 프레임워크 예약 접두어({UiRegistryOwners.ReservedPrefix})를 쓴다.", owner); // i18n-ok 개발자 진단
        }

        if (conflict is not null)
        {
            return Fail(sink, UiDiagnosticIds.WindowDuplicate, $"창 ID '{id}' 가 창 '{conflict}' 의 ID·별칭과 겹쳐(대소문자 무시) 등록하지 않는다.", owner); // i18n-ok 개발자 진단
        }

        foreach (var r in rejected)
        {
            sink.Report(new UiDiagnostic(r.Id, r.Severity, r.Message, owner));
        }

        Raise(index >= 0 ? UiRegistryChangeKind.Replaced : UiRegistryChangeKind.Added, id);
        return true;
    }

    private bool Remove(string owner, UiDiagnostics sink, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        string? holder;
        lock (_gate)
        {
            var index = IndexOf(id);
            holder = index < 0 ? null : _items[index].Owner;
            if (holder == owner)
            {
                _items.RemoveAt(index); // P2: 등록한 주체만 지운다(Ordinal). 별칭도 정의와 함께 사라진다.
            }
        }

        if (holder is null)
        {
            return false; // 없는 창: 진단 없이 false.
        }

        if (holder != owner)
        {
            return Fail(sink, UiDiagnosticIds.WindowRemoveDenied, $"창 '{id}' 는 '{holder}' 가 등록해 지울 수 없다.", owner); // i18n-ok 개발자 진단
        }

        Raise(UiRegistryChangeKind.Removed, id);
        return true;
    }

    /// <summary>등록할 별칭을 고른다. 버린 충돌·예약 별칭은 진단 내용을 rejected 에 모은다(보고는 잠금 밖).</summary>
    private List<string> FilterAliasesLocked(string id, IEnumerable<string> aliases, bool framework, bool shell,
        List<(string Id, UiDiagnosticSeverity Severity, string Message)> rejected)
    {
        var kept = new List<string>();
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.Equals(alias, id, StringComparison.OrdinalIgnoreCase)
                || kept.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!framework && alias.StartsWith(UiRegistryOwners.ReservedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                rejected.Add((UiDiagnosticIds.WindowReservedId, UiDiagnosticSeverity.Error,
                    $"창 '{id}' 의 별칭 '{alias}' 은 프레임워크 예약 접두어({UiRegistryOwners.ReservedPrefix})를 써서 버린다.")); // i18n-ok 개발자 진단
                continue;
            }

            // 옛 이름·숫자 별칭은 셸 주체만 둔다(D-V2). 별칭 충돌(0504)보다 먼저 판정한다.
            if (!shell && IsLegacyName(alias))
            {
                rejected.Add((UiDiagnosticIds.WindowReservedId, UiDiagnosticSeverity.Error,
                    $"창 '{id}' 의 별칭 '{alias}' 은 옛 문서 종류 이름·숫자(셸 기본 창 영구 별칭)라 버린다.")); // i18n-ok 개발자 진단
                continue;
            }

            if (FindOtherLocked(id, alias) is { } other)
            {
                rejected.Add((UiDiagnosticIds.WindowAliasConflict, UiDiagnosticSeverity.Warning,
                    $"창 '{id}' 의 별칭 '{alias}' 이 창 '{other.Id}' 와 겹쳐 그 별칭만 버린다.")); // i18n-ok 개발자 진단
                continue;
            }

            kept.Add(alias);
        }

        return kept;
    }

    /// <summary>ID 가 id(Ordinal)가 아닌 창 중 ID·별칭이 name 과 대소문자 무시로 같은 창. 교체 대상 자신은 뺀다.
    /// Add 의 새 ID 충돌 검사(XKUI0502)와 <see cref="FilterAliasesLocked"/> 의 별칭 충돌 검사(XKUI0504)가 함께 쓰는 단일 검색이다.</summary>
    private UiWindowDefinition? FindOtherLocked(string id, string name) =>
        _items.Find(w => !string.Equals(w.Id, id, StringComparison.Ordinal)
            && (string.Equals(w.Id, name, StringComparison.OrdinalIgnoreCase) || HasAlias(w, name)));

    /// <summary>옛 문서 종류 이름·숫자(Grid·Gantt·Form 대소문자 무시, "0"·"1"·"2")인지 — 영구 예약어(D-V2).</summary>
    private static bool IsLegacyName(string value) => LegacyWindowKinds.ToId(value) is not null;

    private static bool HasAlias(UiWindowDefinition window, string alias) =>
        window.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase);

    private static bool Fail(UiDiagnostics sink, string id, string message, string owner)
    {
        sink.Report(new UiDiagnostic(id, UiDiagnosticSeverity.Error, message, owner));
        return false;
    }
}

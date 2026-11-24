// 창 활성화 출처 — 전환 효과(ui.motion.switch_sources)를 고르는 근거. 판정은 컨트롤 계층, 화면은 출처를 넘기기만 한다(21 설계 §3)
using System;

namespace Pd.Poc.Shell.Docking;

/// <summary>창을 활성화한 출처. Program 은 효과를 내지 않는다.</summary>
public enum ActivationOrigin
{
    /// <summary>프로그램이 연 창·그 밖(효과 없음).</summary>
    Program,

    /// <summary>Ctrl+Tab·Ctrl+F6(웹은 Ctrl+F6).</summary>
    Keyboard,

    /// <summary>제목 탭 클릭.</summary>
    TitleTab,

    /// <summary>열린 창 목록·창 더 보기.</summary>
    WindowList,

    /// <summary>본문 클릭.</summary>
    BodyClick,
}

/// <summary>
/// 입력 순간 출처 표식의 보관·소비. 표식 뒤 유효 시간 안의 다음 '바뀐' 활성 1회만 출처를 가져간다.
/// 창 열기·닫기·복원 경로는 Clear 로 표식을 지워, 사용자 전환이 아닌 활성 변경이 출처를 가져가지 않게 한다(오귀속 방지).
/// </summary>
public sealed class ActivationOriginTracker(Func<long> nowMs, long validMs = 600)
{
    private ActivationOrigin _pending;
    private long _until;

    /// <summary>입력 순간 표식.</summary>
    public void Mark(ActivationOrigin origin)
    {
        _pending = origin;
        _until = nowMs() + validMs;
    }

    /// <summary>남은 표식을 지운다(열기·닫기·복원).</summary>
    public void Clear() => _pending = ActivationOrigin.Program;

    /// <summary>활성이 바뀌었으면 유효한 표식을 한 번만 돌려준다. 그 밖에는 Program.</summary>
    public ActivationOrigin Take(bool changed)
    {
        if (!changed || _pending == ActivationOrigin.Program)
        {
            return ActivationOrigin.Program;
        }

        var origin = nowMs() <= _until ? _pending : ActivationOrigin.Program;
        _pending = ActivationOrigin.Program;
        return origin;
    }
}

/// <summary>출처 ↔ 설정 값(ui.motion.switch_sources 목록 항목) 변환.</summary>
public static class ActivationOrigins
{
    /// <summary>설정 값 이름(keyboard·title_tab·window_list·body_click·program).</summary>
    public static string ToKey(this ActivationOrigin origin) => origin switch
    {
        ActivationOrigin.Keyboard => "keyboard",
        ActivationOrigin.TitleTab => "title_tab",
        ActivationOrigin.WindowList => "window_list",
        ActivationOrigin.BodyClick => "body_click",
        _ => "program",
    };

    /// <summary>설정 값 이름을 출처로 읽는다. 모르는 값은 false.</summary>
    public static bool TryParse(string? key, out ActivationOrigin origin)
    {
        origin = key?.Trim().ToLowerInvariant() switch
        {
            "keyboard" => ActivationOrigin.Keyboard,
            "title_tab" => ActivationOrigin.TitleTab,
            "window_list" => ActivationOrigin.WindowList,
            "body_click" => ActivationOrigin.BodyClick,
            "program" => ActivationOrigin.Program,
            _ => (ActivationOrigin)(-1),
        };
        return Enum.IsDefined(origin);
    }
}

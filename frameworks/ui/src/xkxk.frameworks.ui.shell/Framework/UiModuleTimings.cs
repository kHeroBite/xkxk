// 별별UI 빌더의 마지막 Build 1회분 모듈별 구성 시간을 보관하는 내부 기록기(UADR 공개 멤버 없음)
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="XkxkUiBuilder.Build"/> 의 모듈별 Configure 시간과 Build 전체 시간을 기록한다.
/// </summary>
/// <remarks>
/// 마지막 Build 1회분만 보관한다(Begin 이 이전 기록을 지운다). 제품판에서도 기록하며 비용은 모듈당 타임스탬프 2회다.
/// 한계: 기록은 프로세스 전역 1벌이라 여러 빌더가 동시에(병렬로) Build 하면 모듈 기록이 섞일 수 있다.
/// </remarks>
internal static class UiModuleTimings
{
    private static readonly object Gate = new();
    private static readonly List<(int Order, string Id, double ConfigureMs, bool Ok)> Entries = new();
    private static bool _recorded;
    private static double? _buildMs;

    /// <summary>새 Build 기록을 시작한다. 이전 기록을 지우고 Complete 전까지 Recorded=false 다.</summary>
    internal static void Begin()
    {
        lock (Gate)
        {
            Entries.Clear();
            _recorded = false;
            _buildMs = null;
        }
    }

    /// <summary>모듈 1건의 구성 시간을 더한다. 순번은 Begin 뒤 1부터 매기며 실패(ok=false)도 포함한다.</summary>
    internal static void Add(string id, double configureMs, bool ok)
    {
        lock (Gate)
        {
            Entries.Add((Entries.Count + 1, id ?? string.Empty, configureMs, ok));
        }
    }

    /// <summary>Build 전체 시간을 기록하고 Recorded=true 로 만든다.</summary>
    internal static void Complete(double buildMs)
    {
        lock (Gate)
        {
            _buildMs = buildMs;
            _recorded = true;
        }
    }

    /// <summary>마지막 Build 기록의 사본을 돌려준다. Build 전이면 Recorded=false, BuildMs=null 이다.</summary>
    internal static (bool Recorded, double? BuildMs, IReadOnlyList<(int Order, string Id, double ConfigureMs, bool Ok)> Modules) Snapshot()
    {
        lock (Gate)
        {
            return (_recorded, _buildMs, Entries.ToArray());
        }
    }
}

// 문서 화면 진단 계약(진단 빌드 전용) — 셸의 진단 코드가 화면 형식을 모른 채 그리드·간트를 조작·측정한다
#if PD_DIAGNOSTICS
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>그리드 문서 화면의 진단 표면.</summary>
public interface IGridDocumentDiagnostics
{
    Task ExerciseAsync(TopLevel top);

    Size DiagLastMeasureGiven { get; }

    int DiagRowCount { get; }

    (string Hash, bool WasMaterialized) DiagComputeHash();

    void DiagSortByName(bool descending);

    void DiagScrollStep(int round);
}

/// <summary>간트 문서 화면의 진단 표면.</summary>
public interface IGanttDocumentDiagnostics
{
    Task ExerciseAsync(TopLevel top);

    void DiagZoomStep(int round);

    void DiagScrollStep();
}
#endif

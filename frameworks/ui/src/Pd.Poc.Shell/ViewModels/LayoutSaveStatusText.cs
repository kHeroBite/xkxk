// 레이아웃 저장·이름 변경·삭제 결과 상태를 사용자 문구로 바꾸는 헬퍼 — LayoutManagerViewModel 에서 분리
using Pd.Client.Data.Layout;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.ViewModels;

internal static class LayoutSaveStatusText
{
    public static string Describe(LayoutSaveStatus status) => status switch
    {
        LayoutSaveStatus.NameEmpty => Loc.Tr("layout.err.nameEmpty"),
        LayoutSaveStatus.NameExists => Loc.Tr("layout.err.nameExists"),
        LayoutSaveStatus.ReadOnly => Loc.Tr("layout.err.readOnly"),
        LayoutSaveStatus.NotFound => Loc.Tr("layout.err.notFound"),
        _ => Loc.Tr("layout.err.failed"),
    };
}

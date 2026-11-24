// 문서 종류 → 뷰 컨트롤 생성 계약(UI 계층) — 저장소 계약 파일 ILayoutStore.cs 에서 분리(D-LAYER)
using Pd.Client.Data.Layout;
using Avalonia.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>문서 종류 → 뷰 컨트롤 생성기. 기본 구현은 App 의 DefaultDocumentViewFactory.</summary>
public interface IDocumentViewFactory
{
    Control Create(PocDocument document);
}

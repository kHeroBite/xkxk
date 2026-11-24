// 문서 래퍼 표식 — 셸이 화면 형식 ZoomDecorator 를 모른 채 문서 내용 경계를 찾는다
namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>문서 내용을 감싼 래퍼. Tag 에 문서(PocDocument)가 들어 있다.</summary>
public interface IDocumentZoomHost
{
    object? Tag { get; }
}

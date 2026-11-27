// 창 내용 뷰가 "열림 측정 시작" 신호를 받는 공개 인터페이스(B10 batch 2, 부록 a11 §8 ⑦)
namespace xkxk.frameworks.ui.views.Windows;

/// <summary>
/// 창 내용 뷰가 구현하면 풀·예열 경로에서 재사용될 때 열림 측정을 새로 시작할 수 있다.
/// </summary>
public interface IWindowOpenTracking
{
    /// <summary>창이 (다시) 열릴 때 열림 측정을 시작한다.</summary>
    void BeginOpenTracking();
}

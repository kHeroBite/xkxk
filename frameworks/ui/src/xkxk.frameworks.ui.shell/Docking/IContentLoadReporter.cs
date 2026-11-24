// 창 콘텐츠 불러오는 중 계약 — 불러오기 토큰(ILoadToken)과 콘텐츠 보고 계약(IContentLoadReporter). 정본 plan/09_화면설계/23_창_불러오는중_표시_설계.md §2
using System;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 불러오기 작업 1건. Complete·Fail·Cancel·Dispose 중 어느 것이든 처음 한 번만 수를 줄이고 나머지는 무시한다.
/// 어느 스레드에서 불러도 되며 상태 변경은 UI 스레드에서 일어난다(WindowLoadState 가 넘긴다).
/// </summary>
public interface ILoadToken : IDisposable
{
    /// <summary>작업 사유(open·search·refresh 등 — 진단 표시용).</summary>
    string Reason { get; }

    /// <summary>이미 끝났는가(완료·실패·취소·해제·닫힘 무효 포함).</summary>
    bool IsEnded { get; }

    /// <summary>정상 완료.</summary>
    void Complete();

    /// <summary>실패로 끝낸다. 창 아이콘에 오류 표지가 붙는다.</summary>
    void Fail(Exception? error = null);

    /// <summary>취소로 끝낸다.</summary>
    void Cancel();
}

/// <summary>
/// 콘텐츠(뷰모델)가 구현하는 보고 계약. 셸이 뷰를 붙일 때 열기 토큰과 작업 시작 함수를 넘긴다.
/// 콘텐츠는 첫 데이터 조회가 끝나면 openToken.Complete() 를 부르고, 검색·새로고침은 begin("search")·begin("refresh") 로 같은 계약을 쓴다.
/// 이 계약을 구현하지 않은 콘텐츠는 뷰가 화면에 붙은 순간을 완료로 본다(23 §2.2 한계). 새 화면은 구현이 필수다(01 §2-1).
/// </summary>
public interface IContentLoadReporter
{
    /// <summary>열기 토큰(이미 끝났을 수 있다)과 작업 시작 함수를 받는다. 셸이 문서마다 한 번 부른다.</summary>
    void Attach(ILoadToken openToken, Func<string, ILoadToken> begin);
}

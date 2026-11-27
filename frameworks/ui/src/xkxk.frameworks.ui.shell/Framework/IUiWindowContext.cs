// 열린 창 하나의 문맥 — 뷰모델이 자기 창 제목·닫기·바쁨 표시를 다루는 공개 계약(UADR-0009, UADR-0017)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 열린 창(문서) 하나의 문맥이다. 뷰모델은 <see cref="UiWindowViewModel.Window"/> 로 이 문맥을 받아
/// 자기 창의 제목을 바꾸고, 창을 닫고, 바쁨 표시를 켠다.
/// </summary>
/// <remarks>
/// 프레임워크만 구현한다. 소비자는 구현하지 않는다(시험용 가짜 구현은 예외).
/// 확장 규칙: 새 기능은 멤버 추가로만 넓힌다. 기존 멤버의 서명·의미는 바꾸지 않는다.
/// 창이 닫힌 뒤에는 <see cref="SetTitle"/>·<see cref="Close"/> 가 <see langword="false"/> 를 돌려주고,
/// <see cref="BeginBusy()"/> 는 아무 일도 하지 않는 <see cref="IDisposable"/> 을 돌려준다.
/// </remarks>
public interface IUiWindowContext
{
    /// <summary>창 ID(<see cref="UiWindowDefinition.Id"/>). 별칭으로 열었어도 등록된 ID 다.</summary>
    string WindowId { get; }

    /// <summary>이 창을 담은 문서의 ID. 같은 창을 여러 개 열면 문서마다 다르다.</summary>
    string DocumentId { get; }

    /// <summary>현재 문서 제목.</summary>
    string Title { get; }

    /// <summary>창을 열 때 넘긴 값. 없으면 <see langword="null"/>.</summary>
    object? Parameter { get; }

    /// <summary>창이 아직 열려 있으면 <see langword="true"/>. 닫힌 뒤에는 <see langword="false"/>.</summary>
    bool IsOpen { get; }

    /// <summary>문서 제목을 바꾼다.</summary>
    /// <param name="title">새 제목. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>바꿨으면 <see langword="true"/>. 창이 이미 닫혔으면 <see langword="false"/>.</returns>
    bool SetTitle(string title);

    /// <summary>이 창을 닫는다.</summary>
    /// <returns>닫기를 시작했으면 <see langword="true"/>. 이미 닫혔으면 <see langword="false"/>.</returns>
    bool Close();

    /// <summary>바쁨 표시를 켠다. 돌려받은 값을 해제하면 꺼진다. 여러 번 겹쳐 켤 수 있다.</summary>
    /// <returns>해제하면 바쁨 표시를 끄는 값. 창이 닫혔으면 아무 일도 하지 않는 값.</returns>
    IDisposable BeginBusy();

    /// <summary>사유를 붙여 바쁨 표시를 켠다. 돌려받은 값을 해제하면 꺼진다.</summary>
    /// <param name="reason">바쁨 사유(표시·진단용). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>해제하면 바쁨 표시를 끄는 값. 창이 닫혔으면 아무 일도 하지 않는 값.</returns>
    IDisposable BeginBusy(string reason);

    /// <summary>창이 닫힌 뒤 한 번 발생한다.</summary>
    event EventHandler? Closed;
}

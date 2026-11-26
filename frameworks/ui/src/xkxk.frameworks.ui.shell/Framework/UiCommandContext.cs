// 명령 실행 문맥 — 실행·실행 가능 판단 대리자에 넘기는 읽기 전용 값(UADR-0009 원칙 1)
using System;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// <see cref="UiCommandOptions.Execute"/>·<see cref="UiCommandOptions.CanExecute"/> 대리자가 받는 실행 문맥이다.
/// </summary>
/// <remarks>
/// 공개 생성자가 없다. 프레임워크(레지스트리)가 호출마다 만든다.
/// 확장 규칙: 새 정보(예 호출 출처·취소 토큰)는 읽기 전용 속성 추가로만 넓힌다.
/// </remarks>
public sealed class UiCommandContext
{
    /// <summary>실행 문맥을 만든다(레지스트리 전용).</summary>
    /// <param name="app">앱 컨텍스트. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="commandId">실행하는 명령 ID. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="parameter">호출자가 넘긴 값. null 허용.</param>
    internal UiCommandContext(IUiAppContext app, string commandId, object? parameter)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(commandId);
        App = app;
        CommandId = commandId;
        Parameter = parameter;
    }

    /// <summary>앱 컨텍스트. null 이 아니다. 셸이 아직 없으면 <see cref="IUiAppContext.Shell"/> 이 null 일 수 있다.</summary>
    public IUiAppContext App { get; }

    /// <summary>실행하는 명령 ID.</summary>
    public string CommandId { get; }

    /// <summary>
    /// 호출자가 <see cref="IUiCommandRegistry.Execute(string, object?)"/>·<see cref="IUiCommandRegistry.CanExecute(string, object?)"/> 로 넘긴 값.
    /// 1인자 오버로드로 부르면 null.
    /// </summary>
    /// <remarks>셸 기본 명령은 이 값이 문서이면 그 문서를, 아니면 활성 문서를 대상으로 삼는다.</remarks>
    public object? Parameter { get; }
}

// WindowCommands 에서 나온 서비스 클래스 표식 — D-12 진단 로그 via= 의 형식 이름을 옛 이름(WindowCommands)으로 유지하는 데 쓴다
using System;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// W3(partial 해소)에서 WindowCommands 본문을 옮겨 받은 클래스에 붙인다. 진단 로그(D-12 via=)는 호출 스택의 형식 이름을 남기는데,
/// 이 표식이 있는 형식은 nameof(WindowCommands) 로 표시해 이전 판(i2400rc) 원자료·시험 도구의 grep 과 같은 문자열이 나오게 한다.
/// 메서드 이름은 스택에서 그대로 읽는다(문자열 표 없음).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class WindowCommandsPartAttribute : Attribute
{
}

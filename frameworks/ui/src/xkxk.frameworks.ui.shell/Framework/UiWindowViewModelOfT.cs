// 강타입 창 뷰모델 — 열 때 넘긴 값을 TArgs 로 받는 기반 형식(UADR-0009, UADR-0017)
namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 열 때 넘긴 값을 <typeparamref name="TArgs"/> 로 받는 창 뷰모델 기반 형식이다.
/// </summary>
/// <typeparam name="TArgs">열 때 넘기는 값의 형식.</typeparam>
/// <remarks>
/// 값이 없으면(<see langword="null"/>) <see cref="Args"/> 는 기본값이다.
/// <typeparamref name="TArgs"/> 가 아닌 값을 넘기면 연결되지 않고 프레임워크가 XKUI0509 진단을 남긴다.
/// </remarks>
public abstract class UiWindowViewModel<TArgs> : UiWindowViewModel
{
    /// <summary>뷰모델을 만든다. 창 문맥과 값은 프레임워크가 나중에 연결한다.</summary>
    protected UiWindowViewModel()
    {
    }

    /// <summary>열 때 넘긴 값. 없거나 연결 전이면 기본값.</summary>
    public TArgs? Args => Parameter is TArgs args ? args : default;

    /// <summary>값이 <see langword="null"/> 이거나 <typeparamref name="TArgs"/> 면 받는다.</summary>
    /// <param name="parameter">열 때 넘긴 값.</param>
    /// <returns><see langword="null"/>·<typeparamref name="TArgs"/> 면 <see langword="true"/>, 그 밖은 <see langword="false"/>.</returns>
    protected override bool AcceptParameter(object? parameter) => parameter is null || parameter is TArgs;
}

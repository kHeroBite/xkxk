// 강타입 창 등록 확장 메서드 — 뷰모델·뷰(·인자 형식) 한 줄 등록 6 오버로드(UADR-0017, 부록 a11 §4)
using System;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Windows;

/// <summary>
/// 창 한 종류를 뷰모델과 뷰 형식으로 등록하는 확장 메서드다(부록 a11 §4).
/// </summary>
/// <remarks>
/// <para>
/// 모든 오버로드는 <see cref="IUiWindowRegistry.Add(string, Action{UiWindowOptions})"/> 위에 얹고
/// <see cref="UiWindowOptions.CreateContent"/> 를 채운다. 구성 대리자가 <see cref="UiWindowOptions.CreateContent"/> 를 설정해도
/// 이 등록이 덮어쓴다(진단 없음). 반환값은 <c>Add</c> 결과다.
/// </para>
/// <para>
/// 내용을 만들 때의 흐름: 뷰모델 생성 → 창 문맥·파라미터 연결(형식이 맞지 않으면 XKUI0509 와 자리표시) →
/// <c>new TView { DataContext = 뷰모델 }</c> → 뷰가 처음 시각 트리에 붙을 때 <c>OnOpenedAsync</c> 1회(예외는 XKUI0510, 뷰 유지) →
/// 창이 닫힐 때 토큰 취소와 <c>OnClosed</c> 1회. 생성 함수가 null 을 돌려주거나 예외를 던지면 XKUI0507 과 자리표시다.
/// </para>
/// <para>선택 파라미터는 쓰지 않는다(UADR-0009). 인자가 null 이면 <see cref="ArgumentNullException"/> 이다.</para>
/// </remarks>
public static class UiWindowViewRegistration
{
    /// <summary>뷰모델을 <c>new()</c> 로 만드는 창을 기본 옵션으로 등록한다.</summary>
    /// <typeparam name="TViewModel">창 뷰모델 형식.</typeparam>
    /// <typeparam name="TView">창 뷰 형식. <c>DataContext</c> 에 뷰모델이 들어간다.</typeparam>
    /// <param name="registry">창 레지스트리.</param>
    /// <param name="id">창 ID.</param>
    /// <returns>등록(또는 교체)했으면 <see langword="true"/>.</returns>
    public static bool Register<TViewModel, TView>(this IUiWindowRegistry registry, string id)
        where TViewModel : UiWindowViewModel, new()
        where TView : Control, new()
        => Core<TViewModel, TView>(registry, id, NoConfigure, static _ => new TViewModel());

    /// <summary>뷰모델을 <c>new()</c> 로 만드는 창을 구성 대리자로 등록한다.</summary>
    /// <typeparam name="TViewModel">창 뷰모델 형식.</typeparam>
    /// <typeparam name="TView">창 뷰 형식.</typeparam>
    /// <param name="registry">창 레지스트리.</param>
    /// <param name="id">창 ID.</param>
    /// <param name="configure">창 옵션 구성 대리자. <c>CreateContent</c> 는 이 등록이 덮어쓴다.</param>
    /// <returns>등록(또는 교체)했으면 <see langword="true"/>.</returns>
    public static bool Register<TViewModel, TView>(this IUiWindowRegistry registry, string id, Action<UiWindowOptions> configure)
        where TViewModel : UiWindowViewModel, new()
        where TView : Control, new()
        => Core<TViewModel, TView>(registry, id, configure, static _ => new TViewModel());

    /// <summary>뷰모델 생성 함수를 받아 창을 등록한다.</summary>
    /// <typeparam name="TViewModel">창 뷰모델 형식.</typeparam>
    /// <typeparam name="TView">창 뷰 형식.</typeparam>
    /// <param name="registry">창 레지스트리.</param>
    /// <param name="id">창 ID.</param>
    /// <param name="configure">창 옵션 구성 대리자. <c>CreateContent</c> 는 이 등록이 덮어쓴다.</param>
    /// <param name="createViewModel">내용을 만들 때마다 뷰모델을 만드는 함수. null 반환·예외는 XKUI0507.</param>
    /// <returns>등록(또는 교체)했으면 <see langword="true"/>.</returns>
    public static bool Register<TViewModel, TView>(this IUiWindowRegistry registry, string id, Action<UiWindowOptions> configure,
        Func<UiWindowContentContext, TViewModel> createViewModel)
        where TViewModel : UiWindowViewModel
        where TView : Control, new()
        => Core<TViewModel, TView>(registry, id, configure, createViewModel);

    /// <summary>열 때 <typeparamref name="TArgs"/> 를 받는 창(뷰모델 <c>new()</c>)을 기본 옵션으로 등록한다.</summary>
    /// <typeparam name="TViewModel">창 뷰모델 형식.</typeparam>
    /// <typeparam name="TView">창 뷰 형식.</typeparam>
    /// <typeparam name="TArgs">열 때 넘기는 값의 형식. 다른 형식이면 XKUI0509 와 자리표시다.</typeparam>
    /// <param name="registry">창 레지스트리.</param>
    /// <param name="id">창 ID.</param>
    /// <returns>등록(또는 교체)했으면 <see langword="true"/>.</returns>
    public static bool Register<TViewModel, TView, TArgs>(this IUiWindowRegistry registry, string id)
        where TViewModel : UiWindowViewModel<TArgs>, new()
        where TView : Control, new()
        => Core<TViewModel, TView>(registry, id, NoConfigure, static _ => new TViewModel());

    /// <summary>열 때 <typeparamref name="TArgs"/> 를 받는 창(뷰모델 <c>new()</c>)을 구성 대리자로 등록한다.</summary>
    /// <typeparam name="TViewModel">창 뷰모델 형식.</typeparam>
    /// <typeparam name="TView">창 뷰 형식.</typeparam>
    /// <typeparam name="TArgs">열 때 넘기는 값의 형식.</typeparam>
    /// <param name="registry">창 레지스트리.</param>
    /// <param name="id">창 ID.</param>
    /// <param name="configure">창 옵션 구성 대리자. <c>CreateContent</c> 는 이 등록이 덮어쓴다.</param>
    /// <returns>등록(또는 교체)했으면 <see langword="true"/>.</returns>
    public static bool Register<TViewModel, TView, TArgs>(this IUiWindowRegistry registry, string id, Action<UiWindowOptions> configure)
        where TViewModel : UiWindowViewModel<TArgs>, new()
        where TView : Control, new()
        => Core<TViewModel, TView>(registry, id, configure, static _ => new TViewModel());

    /// <summary>열 때 <typeparamref name="TArgs"/> 를 받는 창을 뷰모델 생성 함수로 등록한다.</summary>
    /// <typeparam name="TViewModel">창 뷰모델 형식.</typeparam>
    /// <typeparam name="TView">창 뷰 형식.</typeparam>
    /// <typeparam name="TArgs">열 때 넘기는 값의 형식.</typeparam>
    /// <param name="registry">창 레지스트리.</param>
    /// <param name="id">창 ID.</param>
    /// <param name="configure">창 옵션 구성 대리자. <c>CreateContent</c> 는 이 등록이 덮어쓴다.</param>
    /// <param name="createViewModel">내용을 만들 때마다 뷰모델을 만드는 함수. null 반환·예외는 XKUI0507.</param>
    /// <returns>등록(또는 교체)했으면 <see langword="true"/>.</returns>
    public static bool Register<TViewModel, TView, TArgs>(this IUiWindowRegistry registry, string id, Action<UiWindowOptions> configure,
        Func<UiWindowContentContext, TViewModel> createViewModel)
        where TViewModel : UiWindowViewModel<TArgs>
        where TView : Control, new()
        => Core<TViewModel, TView>(registry, id, configure, createViewModel);

    /// <summary>구성 대리자를 받지 않는 오버로드가 쓰는 빈 구성.</summary>
    private static readonly Action<UiWindowOptions> NoConfigure = static _ => { };

    /// <summary>6 오버로드 공통 구현: 인자 검사 후 <c>Add</c> 에 구성을 넘기고 마지막에 <c>CreateContent</c> 를 채운다.</summary>
    private static bool Core<TViewModel, TView>(IUiWindowRegistry registry, string id, Action<UiWindowOptions> configure,
        Func<UiWindowContentContext, TViewModel> createViewModel)
        where TViewModel : UiWindowViewModel
        where TView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(createViewModel);
        return registry.Add(id, options =>
        {
            configure(options);
            options.CreateContent = context => WindowViewModelBinder.CreateContent<TViewModel, TView>(context, createViewModel); // 구성 대리자가 채운 값을 덮어쓴다
        });
    }
}

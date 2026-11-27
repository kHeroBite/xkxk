// 창 뷰모델 기반 형식 — 창 문맥 연결·열림/닫힘 수명 훅·속성 변경 알림(UADR-0009, UADR-0017)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 창 내용의 뷰모델 기반 형식이다. 프레임워크가 창을 열 때 만들어 <see cref="Window"/> 에 창 문맥을 연결하고,
/// 열린 뒤 <see cref="OnOpenedAsync"/>, 닫힌 뒤 <see cref="OnClosed"/> 를 한 번씩 부른다.
/// </summary>
/// <remarks>
/// 파생 형식은 매개변수 없는 protected 생성자를 쓴다. 열 때 넘긴 값의 형식을 강제하려면
/// <see cref="UiWindowViewModel{TArgs}"/> 를 상속한다.
/// <see cref="AttachWindow"/>·<see cref="NotifyOpenedAsync"/>·<see cref="NotifyClosed"/> 는 프레임워크 전용 공개 진입점이다.
/// 소비자 코드는 부르지 않는다(편집기 자동 완성에서 숨김).
/// </remarks>
public abstract class UiWindowViewModel : INotifyPropertyChanged
{
    private IUiWindowContext? _window;
    private object? _parameter;
    private int _attached;
    private int _opened;
    private int _closed;

    /// <summary>뷰모델을 만든다. 창 문맥은 프레임워크가 나중에 연결한다.</summary>
    protected UiWindowViewModel()
    {
    }

    /// <summary>이 뷰모델이 담긴 창의 문맥.</summary>
    /// <exception cref="InvalidOperationException">아직 창에 연결되지 않았다(생성자 안 등).</exception>
    public IUiWindowContext Window => _window ?? throw new InvalidOperationException("창 문맥이 아직 연결되지 않았다. 생성자가 아니라 OnOpenedAsync 이후에 읽는다.");

    /// <summary>창을 열 때 넘긴 값. 없거나 연결 전이면 <see langword="null"/>.</summary>
    public object? Parameter => _parameter;

    /// <summary>속성 값이 바뀌면 발생한다.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>창이 열린 뒤 한 번 불린다. 자료 읽기 등 비동기 초기화를 여기서 한다.</summary>
    /// <param name="cancellationToken">창이 닫히는 등 초기화를 그만둘 때 취소된다.</param>
    /// <returns>초기화 작업. 기본 구현은 바로 끝난다.</returns>
    protected virtual Task OnOpenedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>창이 닫힌 뒤 한 번 불린다. 구독 해제 등 정리를 여기서 한다. 기본 구현은 아무 일도 하지 않는다.</summary>
    protected virtual void OnClosed()
    {
    }

    /// <summary>열 때 넘긴 값을 이 뷰모델이 받을 수 있는지 판정한다.</summary>
    /// <param name="parameter">열 때 넘긴 값. null 허용.</param>
    /// <returns>받을 수 있으면 <see langword="true"/>. 기본 구현은 항상 <see langword="true"/>.</returns>
    /// <remarks><see langword="false"/> 면 <see cref="AttachWindow"/> 가 <see cref="ArgumentException"/> 을 던지고 프레임워크는 XKUI0509 진단을 남긴다.</remarks>
    protected virtual bool AcceptParameter(object? parameter) => true;

    /// <summary><see cref="PropertyChanged"/> 를 발생시킨다.</summary>
    /// <param name="propertyName">바뀐 속성 이름. null 이면 <see cref="ArgumentNullException"/>.</param>
    protected void OnPropertyChanged(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>필드 값이 다르면 바꾸고 <see cref="PropertyChanged"/> 를 발생시킨다.</summary>
    /// <typeparam name="T">필드 형식.</typeparam>
    /// <param name="field">대상 필드.</param>
    /// <param name="value">새 값.</param>
    /// <param name="propertyName">속성 이름. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <returns>값이 바뀌었으면 <see langword="true"/>.</returns>
    protected bool SetProperty<T>(ref T field, T value, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>창 문맥과 열 때 넘긴 값을 연결한다(프레임워크 전용).</summary>
    /// <param name="window">창 문맥. null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="parameter">열 때 넘긴 값. null 허용.</param>
    /// <exception cref="InvalidOperationException">이미 연결됐다(두 번째 호출).</exception>
    /// <exception cref="ArgumentException"><see cref="AcceptParameter"/> 가 값을 거부했다(연결되지 않는다).</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AttachWindow(IUiWindowContext window, object? parameter)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!AcceptParameter(parameter))
        {
            throw new ArgumentException($"{GetType().FullName} 이 받을 수 없는 파라미터 형식이다: {parameter?.GetType().FullName}", nameof(parameter));
        }

        if (Interlocked.CompareExchange(ref _attached, 1, 0) != 0)
        {
            throw new InvalidOperationException("창 문맥은 한 번만 연결할 수 있다.");
        }

        _parameter = parameter;
        _window = window;
    }

    /// <summary>창이 열렸음을 알린다(프레임워크 전용). 첫 호출만 <see cref="OnOpenedAsync"/> 를 부르고 이후는 바로 끝난다.</summary>
    /// <param name="cancellationToken">초기화 취소 토큰.</param>
    /// <returns><see cref="OnOpenedAsync"/> 작업 또는 이미 끝난 작업.</returns>
    /// <exception cref="InvalidOperationException">아직 창에 연결되지 않았다.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Task NotifyOpenedAsync(CancellationToken cancellationToken)
    {
        if (_window is null)
        {
            throw new InvalidOperationException("창 문맥을 연결하기 전에는 열림을 알릴 수 없다.");
        }

        return Interlocked.Exchange(ref _opened, 1) == 0 ? OnOpenedAsync(cancellationToken) : Task.CompletedTask;
    }

    /// <summary>창이 닫혔음을 알린다(프레임워크 전용). 첫 호출만 <see cref="OnClosed"/> 를 부른다.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void NotifyClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            OnClosed();
        }
    }
}

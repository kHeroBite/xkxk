// 강타입 창 등록의 첫 소비자인 표본 뷰모델(수명 훅·제목·바쁨 표시 사용 예)
using System.Threading;
using System.Threading.Tasks;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.testbed.Samples;

/// <summary>
/// 열 때 문자열 인자(창 제목·본문 문구)를 받는 최소 창 뷰모델이다.
/// <see cref="OnOpenedAsync"/>·<see cref="UiWindowViewModel{TArgs}.Args"/>·SetTitle·BeginBusy·<see cref="OnClosed"/> 사용법을 보여 준다.
/// </summary>
public sealed class TypedSampleViewModel : UiWindowViewModel<string>
{
    private bool _opened;
    private bool _closed;

    /// <summary>열린 뒤 초기화가 끝났으면 <see langword="true"/>.</summary>
    public bool Opened
    {
        get => _opened;
        private set => SetProperty(ref _opened, value, nameof(Opened));
    }

    /// <summary>닫힘 훅이 불렸으면 <see langword="true"/>.</summary>
    public bool Closed => _closed;

    /// <summary>본문에 보일 문구. 인자가 없으면 기본 문구.</summary>
    public string Message => Args ?? DefaultTitle;

    private const string DefaultTitle = "Typed sample";

    /// <inheritdoc />
    protected override async Task OnOpenedAsync(CancellationToken cancellationToken)
    {
        // 초기화 중 바쁨 표시를 켜고, 끝나면 해제한다.
        using (Window.BeginBusy("typed sample open"))
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Window.SetTitle(Args ?? DefaultTitle);
            Opened = true;
            OnPropertyChanged(nameof(Message));
        }
    }

    /// <inheritdoc />
    protected override void OnClosed()
    {
        _closed = true;
    }
}

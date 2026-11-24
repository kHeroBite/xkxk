// 모바일형 화면 코드비하인드 — Body 슬롯(활성 문서 영역)과 MainViewModel 로부터 MobileNavViewModel 연결. 문서 선택의 활성화 출처(window_list)는 선택 바인딩 대상(MobileNavViewModel.SelectedDocument)이 넘긴다
using Pd.Poc.Shell;
using Avalonia;
using Avalonia.Controls;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

public partial class MobileNavView : UserControl
{
    /// <summary>가운데 활성 문서 영역. MainView 가 문서 뷰 재활용(ControlRecycling) 프레젠터를 넣는다.</summary>
    public static readonly StyledProperty<object?> BodyProperty = AvaloniaProperty.Register<MobileNavView, object?>(nameof(Body));

    public MobileNavView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            // 상위(MainView)의 MainViewModel 로 모바일 탐색 뷰모델을 한 번만 만든다.
            NavRoot.DataContext = DataContext is MainViewModel main ? new MobileNavViewModel(main) : null;
        };
    }

    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    // 즐겨찾기로 연 뒤에는 다른 메뉴 실행과 같이 서랍을 닫아 결과 화면을 보인다
    private void OnFavoriteOpened(object? sender, System.EventArgs e)
    {
        if (NavRoot.DataContext is MobileNavViewModel nav)
        {
            nav.Compact.IsDrawerOpen = false;
        }
    }
}

// 요소별 디자인 편집 패널 코드 — 폭에 따른 2열/1열 칸 배치만 담당(1열은 요소 목록부터, 선택하면 속성 화면). 값·명령은 전부 ThemeDesignViewModel 바인딩
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

public partial class ThemeDesignPanel : UserControl
{
    /// <summary>이 폭보다 좁으면 요소 목록(180)+간격(12)+속성 행이 한 줄에 들어가지 않는다 — 1열(모바일형·좁은 태블릿형 창, 17 §8).</summary>
    private const double NarrowWidth = 520;

    private const string NarrowClass = "narrow";

    private bool _narrow;
    private ThemeDesignViewModel? _vm;

    public ThemeDesignPanel()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as ThemeDesignViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            if (_narrow)
            {
                _vm.BackCommand.Execute(null); // 1열은 요소 목록부터
            }
        }

        UpdatePanes();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    /// <summary>좁은 폭은 1열: 들어갈 때 목록부터 보이게 BackCommand, 넓은 폭으로 돌아오면 선택이 없을 때 첫 요소를 고른다.</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var narrow = e.NewSize.Width < NarrowWidth;
        if (narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        Classes.Set(NarrowClass, narrow);
        PanelGrid.ColumnDefinitions = ColumnDefinitions.Parse(narrow ? "*" : "180,12,*");
        Grid.SetColumn(DetailPane, narrow ? 0 : 2);
        if (_vm is not null)
        {
            if (narrow)
            {
                _vm.BackCommand.Execute(null);
            }
            else if (!_vm.HasSelection)
            {
                _vm.SelectedElement = _vm.Elements.FirstOrDefault();
            }
        }

        UpdatePanes();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeDesignViewModel.HasSelection))
        {
            UpdatePanes();
        }
    }

    /// <summary>2열: 목록·속성 둘 다(속성은 선택이 있을 때). 1열: 선택 전 목록만, 선택 후 속성만 + 목록으로 단추.</summary>
    private void UpdatePanes()
    {
        var selected = _vm?.HasSelection ?? false;
        ElementPane.IsVisible = !_narrow || !selected;
        DetailPane.IsVisible = selected;
        BackButton.IsVisible = _narrow && selected;
    }
}

// PdDataGrid 전용 스타일 묶음의 XAML 로더(표 인스턴스마다 자기 Styles 에 넣는다)
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace xkxk.frameworks.ui.views.Documents;

public partial class PdDataGridStyles : Styles
{
    public PdDataGridStyles() => AvaloniaXamlLoader.Load(this);
}

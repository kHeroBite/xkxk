// '창' 메뉴 열린 창 목록의 한 칸(1~9) — 번호 붙은 제목·활성 표시·표시 여부와 가리키는 문서
using CommunityToolkit.Mvvm.ComponentModel;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>열린 창 목록의 고정 칸 하나. 메뉴 항목은 axaml 에 9개 고정 배치하고 이 칸의 값만 바뀐다(코드에서 컨트롤을 만들지 않는다).</summary>
public sealed class OpenWindowSlot : ObservableObject
{
    private PocDocument? _document;
    private string _header = "";
    private bool _isActive;

    /// <summary>이 칸이 가리키는 문서. 없으면 칸을 숨긴다.</summary>
    public PocDocument? Document
    {
        get => _document;
        set
        {
            if (SetProperty(ref _document, value))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    public bool IsVisible => _document is not null;

    /// <summary>메뉴 표시 문구('_1 제목' — 숫자가 접근 키, MDI 관례).</summary>
    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    /// <summary>현재 활성 문서이면 true(메뉴에 ✓).</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

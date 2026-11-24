// 모바일형 문서 선택(상단 콤보·서랍 문서 목록) — 선택 동작이 활성화 출처(window_list)를 넘긴다. 원본 파일 300줄 상한 때문에 나눈 partial
using System.ComponentModel;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.ViewModels;

public sealed partial class MobileNavViewModel
{
    /// <summary>상단 문서 콤보·서랍 문서 목록의 선택. 여기로 들어온 다른 문서 선택은 포인터·키 구분 없이 window_list 출처로 활성화한다.</summary>
    public PocDocument? SelectedDocument
    {
        get => Main.ActiveDocument;
        set
        {
            if (value is not null && !ReferenceEquals(value, Main.Commands.ActiveDocument))
            {
                Main.Commands.Activate(value, ActivationOrigin.WindowList);
            }
            else
            {
                Main.ActiveDocument = value; // null·같은 값(목록 갱신이 되돌려 쓴 값)은 활성화가 아니다 — 현재 활성으로 되돌리기만 한다
            }
        }
    }

    // 프로그램이 바꾼 활성은 알림으로만 목록에 간다(원본 → 목록 방향이라 setter 를 거치지 않아 출처가 붙지 않는다)
    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveDocument))
        {
            OnPropertyChanged(nameof(SelectedDocument)); // 값이 같아도 알려 목록 선택을 활성 문서에 맞춘다(MainViewModel.SyncActive 와 같은 규칙)
        }
    }

    // D-7: 콤보 선택은 활성 문서 변경 알림으로만 맞춰져, 목록에 없는 활성이 먼저 왔거나 숨은 동안 선택이 풀리면(selectedIndex -1)
    // 활성이 그대로인 한 다시 맞춰지지 않았다. PC형 → 모바일형 전환으로 보이게 될 때와 열린 문서 목록이 바뀔 때 한 번 더 알린다.
    private void OnCompactChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompactModeViewModel.IsCompact) && Compact.IsCompact)
        {
            ResyncSelection();
        }
    }

    private void ResyncSelection() => OnPropertyChanged(nameof(SelectedDocument));
}

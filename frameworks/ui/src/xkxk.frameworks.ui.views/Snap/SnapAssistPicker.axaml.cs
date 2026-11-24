// 남은 칸 창 고르기 후보 카드 목록 동작 — 카드 항목을 받아 보이고, 카드 누름(마우스·터치)을 카드 번호로 알린다(배치는 SnapAssistSession, 키 입력은 SnapAssistLayer)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.views.Snap;

/// <summary>
/// 활성 칸 하나에 붙는 카드 목록. 포커스를 받지 않는다(초점 표시는 <see cref="SnapAssistCard.IsFocused"/> 로 그린다).
/// 카드 누름은 Tapped(마우스 클릭·터치 탭 공통)로 받아 <see cref="CardPicked"/> 를 낸다.
/// </summary>
public partial class SnapAssistPicker : UserControl
{
    private IReadOnlyList<SnapAssistCard> _cards = Array.Empty<SnapAssistCard>();

    public SnapAssistPicker()
    {
        InitializeComponent();
        Cards.AddHandler(TappedEvent, OnCardTapped, RoutingStrategies.Bubble, handledEventsToo: false);
    }

    /// <summary>카드를 눌렀다(카드 번호 = 후보 번호).</summary>
    public event Action<int>? CardPicked;

    /// <summary>지금 보이는 카드(시험·진단용).</summary>
    public IReadOnlyList<SnapAssistCard> CurrentCards => _cards;

    /// <summary>카드 목록과 이 목록이 붙은 칸 번호를 바꾼다. 같은 목록이면 다시 만들지 않는다.</summary>
    public void SetCards(IReadOnlyList<SnapAssistCard> cards, int cell)
    {
        AutomationProperties.SetAutomationId(this, $"snap.assist.zone.{cell}");
        if (!ReferenceEquals(_cards, cards))
        {
            _cards = cards;
            Cards.ItemsSource = cards;
        }
    }

    /// <summary>초점 카드가 보이도록 스크롤한다(카드가 칸보다 많을 때).</summary>
    public void BringFocusedIntoView(int index)
    {
        if (index >= 0 && index < _cards.Count)
        {
            Cards.ContainerFromIndex(index)?.BringIntoView();
        }
    }

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        for (var v = e.Source as Visual; v is not null && !ReferenceEquals(v, Cards); v = v.GetVisualParent())
        {
            if (v is StyledElement { DataContext: SnapAssistCard card })
            {
                e.Handled = true;
                CardPicked?.Invoke(card.Index);
                return;
            }
        }
    }
}

/// <summary>
/// 카드 한 장(후보 창 하나). 제목·썸네일은 표시 동안 바뀌지 않고, 키보드 초점만 바뀐다.
/// 구성(11 문서 §3.9): 위 = 고정 크기 썸네일 칸(창 전체 비율 축소, 가운데 맞춤·칸 밖 자름), 아래 = 이름표(제목 최대 2줄 + 보조 표기). 이름표는 썸네일 칸 위에 겹치지 않는다.
/// 썸네일이 없으면(캡처 실패·썸네일 끔) 같은 크기 칸에 대체 그림(큰 창 아이콘 + 제목 첫 글자 + 보조 표기 Note)을 그린다.
/// 크기 수치는 초기 제시값이다(썸네일 칸 가로 200·세로 120 DIP, 계획 §5.4).
/// </summary>
public sealed class SnapAssistCard : INotifyPropertyChanged
{
    /// <summary>썸네일 칸 크기(DIP).</summary>
    public static double ThumbWidth { get; } = 200;

    public static double ThumbHeight { get; } = 120;

    /// <summary>썸네일 옵션을 끈 세션의 대체 그림 칸 높이(DIP). 대체 카드도 썸네일 카드와 같은 크기다(11 문서 §3.9).</summary>
    public static double IconSlotHeight { get; } = 120;

    /// <summary>카드 바깥 너비(썸네일 + 안쪽 여백 6×2 + 테두리 2×2).</summary>
    public static double CardWidth { get; } = 216;

    private bool _isFocused;

    public SnapAssistCard(int index, string key, string title, string subtitle, IImage? image, bool thumbnailSlot, string accessibleName, string note = "")
    {
        Index = index;
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Note = note;
        Image = image;
        SlotHeight = thumbnailSlot ? ThumbHeight : IconSlotHeight;
        AccessibleName = accessibleName;
        var t = title.TrimStart();
        Initial = t.Length == 0 ? string.Empty : char.IsSurrogate(t[0]) && t.Length > 1 ? t[..2] : t[..1].ToUpperInvariant();
    }

    /// <summary>보조 표기(같은 제목 후보가 여럿일 때 #순번, 아니면 빈 문자열).</summary>
    public string Subtitle { get; }

    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>대체 카드 그림 칸 아래 보조 표기('미리보기 없음' 또는 '최소화됨'). 썸네일이 있으면 쓰지 않는다.</summary>
    public string Note { get; }

    /// <summary>대체 그림 안에 크게 그리는 제목 첫 글자.</summary>
    public string Initial { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>후보 번호(SnapAssistSession.Pick 인자).</summary>
    public int Index { get; }

    /// <summary>후보 창 Key(같은 목록인지 비교할 때 쓴다).</summary>
    public string Key { get; }

    public string Title { get; }

    public IImage? Image { get; }

    public bool HasImage => Image is not null;

    /// <summary>썸네일 칸 높이. 썸네일 옵션이 켜져 있으면 썸네일을 못 찍은 카드도 같은 높이로 맞춘다(카드 줄 정렬).</summary>
    public double SlotHeight { get; }

    public string AutomationId => $"snap.assist.card.{Index}";

    public string AccessibleName { get; }

    /// <summary>키보드 초점 카드인가(강조색 테두리).</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused == value)
            {
                return;
            }

            _isFocused = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFocused)));
        }
    }
}

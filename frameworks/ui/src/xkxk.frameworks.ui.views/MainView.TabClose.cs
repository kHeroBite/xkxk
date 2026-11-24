// 셸 메인 화면의 제목 탭 닫기(×) 표시 부분 — 제목 끝 흐림(TitleFade)·표시 방식 클래스·키보드 포커스 표시·가운데 클릭 닫기(01 §2-1, 2026-11-12)
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

public partial class MainView
{
    /// <summary>
    /// 제목 탭 제목 끝 흐림(× 와 겹치는 부분). 스타일이 × 보임 조건에서 True 로 둔다. 대상(제목 단추) 오른쪽 끝에서
    /// <see cref="TitleFadeOpaqueInset"/>~<see cref="TitleFadeClearInset"/> DIP 구간을 불투명→투명으로 가린다(글자 위치는 그대로).
    /// </summary>
    public static readonly AttachedProperty<bool> TitleFadeProperty =
        AvaloniaProperty.RegisterAttached<MainView, Control, bool>("TitleFade");

    /// <summary>오른쪽 끝에서 이 거리까지는 원래 글자 그대로(× 왼쪽 여유 포함).</summary>
    public const double TitleFadeOpaqueInset = 30;

    /// <summary>오른쪽 끝에서 이 거리부터는 투명(× 기호가 그려지는 자리).</summary>
    public const double TitleFadeClearInset = 16;

    public static bool GetTitleFade(Control c) => c.GetValue(TitleFadeProperty);

    public static void SetTitleFade(Control c, bool value) => c.SetValue(TitleFadeProperty, value);

    static MainView()
    {
        TitleFadeProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.SizeChanged -= OnTitleFadeSizeChanged;
            if (e.GetNewValue<bool>())
            {
                c.SizeChanged += OnTitleFadeSizeChanged;
            }

            UpdateTitleFade(c);
        });
    }

    private static void OnTitleFadeSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is Control c)
        {
            UpdateTitleFade(c);
        }
    }

    /// <summary>폭에 맞춘 선형 그라데이션 마스크(상대 좌표 0~1 로 오른쪽 끝 고정 거리를 환산). 꺼지면 마스크를 없앤다.</summary>
    private static void UpdateTitleFade(Control c)
    {
        var w = c.Bounds.Width;
        if (!GetTitleFade(c) || w <= 0)
        {
            c.OpacityMask = null;
            return;
        }

        var opaque = Math.Clamp((w - TitleFadeOpaqueInset) / w, 0, 1);
        var clear = Math.Clamp((w - TitleFadeClearInset) / w, opaque, 1);
        c.OpacityMask = new Avalonia.Media.LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(Avalonia.Media.Colors.Black, 0),
                new Avalonia.Media.GradientStop(Avalonia.Media.Colors.Black, opaque),
                new Avalonia.Media.GradientStop(Avalonia.Media.Colors.Transparent, clear),
                new Avalonia.Media.GradientStop(Avalonia.Media.Colors.Transparent, 1),
            },
        };
    }

    /// <summary>설정(TitleTabCloseMode)을 실제 방식으로 풀어 탭 목록 클래스를 단다. 없음=항상, closeHover, closeActive(MainView.axaml 스타일).</summary>
    private void ApplyTabCloseMode()
    {
        var mode = xkxk.frameworks.ui.shell.Theming.UiSettings.Current.EffectiveTitleTabCloseMode;
        TaskbarTabs.Classes.Set("closeHover", mode == xkxk.frameworks.ui.shell.Theming.TitleTabCloseMode.Hover);
        TaskbarTabs.Classes.Set("closeActive", mode == xkxk.frameworks.ui.shell.Theming.TitleTabCloseMode.ActiveAndHover);
        ApplyTitleStripOptions(); // 표시 방식·즐겨찾기 자리도 같은 시점(로드·설정 변경)에 맞춘다(MainView.FavoriteBar.cs)
    }

    private void OnUiSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(xkxk.frameworks.ui.shell.Theming.UiSettings.TitleTabCloseMode) or null or "")
        {
            ApplyTabCloseMode();
        }
        else if (IsTitleStripOption(e.PropertyName))
        {
            ApplyTitleStripOptions();
        }
    }

    private static ListBoxItem? TabItemOf(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);

    private void OnTabGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (TabItemOf(e.Source) is { } item)
        {
            item.Classes.Set("kbfocus", e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional);
        }
    }

    private void OnTabLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (TabItemOf(e.Source) is { } item && TabItemOf(e.NewFocusedElement) != item)
        {
            item.Classes.Set("kbfocus", false);
        }
    }

    private PocDocument? _middlePressed;

    private void OnTabMiddlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _middlePressed = (e.Source as StyledElement)?.DataContext as PocDocument;
            e.Handled = _middlePressed is not null; // 가운데 누름이 탭 활성화·끌기로 가지 않게 한다
        }
    }

    private void OnTabMiddleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle)
        {
            return;
        }

        var pressed = _middlePressed;
        _middlePressed = null;
        // 누른 탭 위에서 뗐을 때만 닫는다(누른 채 다른 탭으로 옮기면 취소). 누름이 포인터를 잡으므로 뗀 위치를 다시 히트 판정한다
        var under = TaskbarTabs.InputHitTest(e.GetPosition(TaskbarTabs)) as StyledElement;
        if (pressed is not null && ReferenceEquals(under?.DataContext, pressed)
            && DataContext is MainViewModel vm)
        {
            xkxk.frameworks.ui.data.Logging.PdLog.Info(xkxk.frameworks.ui.data.Logging.PdLogCategory.User, $"제목 탭 가운데 클릭 닫기 doc={pressed.Id}"); // i18n-ok 진단 로그
            vm.Commands.CloseDocument(pressed);
            e.Handled = true;
        }
    }
}

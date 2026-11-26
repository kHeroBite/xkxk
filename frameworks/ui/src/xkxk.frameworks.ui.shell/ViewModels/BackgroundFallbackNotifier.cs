// 배경 이미지를 못 보여 '없음'으로 낮춘 일을 알림 패널에 실행당 1회 넣는 구독기(09/21 §6.2)
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>테마 적용기의 낮춤 이벤트를 알림으로 옮긴다. 같은 파일·사유는 한 번만 넣는다.</summary>
internal sealed class BackgroundFallbackNotifier
{
    private readonly NotificationPanelViewModel _panel;
    private readonly HashSet<(string File, string ReasonKey)> _sent = new();
    private bool _ready;

    public BackgroundFallbackNotifier(NotificationPanelViewModel panel)
    {
        _panel = panel;
        DesignEffectsApplier.BackgroundFallback += Notify;

        // 상태 표시줄 배지는 화면이 DataContext 를 받은 뒤에 생긴다. 그 전에 넣으면 세지 못하므로 한 박자 늦게 켜고 앞선 실패를 재생한다
        Dispatcher.UIThread.Post(Start, DispatcherPriority.Background);
    }

    private void Start()
    {
        _ready = true;
        if (DesignEffectsApplier.LastBackgroundFallback is { } reasonKey)
        {
            Notify(reasonKey);
        }
    }

    private void Notify(string reasonKey)
    {
        if (_ready && _sent.Add((XkxkUi.Current.Settings.BackgroundImageFile ?? "", reasonKey)))
        {
            _panel.Push("notify.cat.system", "notify.background.title", reasonKey);
        }
    }
}

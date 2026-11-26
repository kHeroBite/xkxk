// WindowIcon 상태 부분 — 불러오는 중 표시 지연·최소 표시·정지 조건·오류 표지(plan/09_화면설계/23 §3·§4)
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

public sealed partial class WindowIcon
{
    private void Watch(PocDocument? doc)
    {
        if (_watched is { } old)
        {
            ((INotifyPropertyChanged)old).PropertyChanged -= OnDocumentChanged;
        }

        // 다른 문서로 바뀌거나 떼어지면 앞 문서의 예약·표지를 넘기지 않는다
        Cancel(ref _showTimer);
        Cancel(ref _hideTimer);
        Cancel(ref _errorTimer);
        SetBusy(false);
        _badge.Error = false;
        _watched = doc;
        if (doc is not null)
        {
            ((INotifyPropertyChanged)doc).PropertyChanged += OnDocumentChanged;
        }

        UpdateBusy();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PocDocument.IsLoading):
                UpdateBusy();
                break;
            case nameof(PocDocument.IsLoadSlow):
                Apply();
                break;
            case nameof(PocDocument.LastLoadError):
                UpdateError();
                break;
            case nameof(PocDocument.IconKey):
            case nameof(PocDocument.Title):
                InvalidateVisual();
                break;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiSettings.BusyIconEnabled))
        {
            UpdateBusy();
        }
    }

    private void UpdateBusy()
    {
        var settings = XkxkUi.Current.Settings;
        if (_watched is { IsLoading: true } && settings.BusyIconEnabled)
        {
            Cancel(ref _hideTimer);
            if (!_busy && _showTimer is null)
            {
                int delay = settings.BusyIconShowDelayMs;
                if (delay > 0)
                {
                    _showTimer = DispatcherTimer.RunOnce(Show, TimeSpan.FromMilliseconds(delay));
                }
                else
                {
                    Show();
                }
            }
        }
        else
        {
            Cancel(ref _showTimer);
            long left = _busy && settings.BusyIconEnabled && _watched is not null
                ? _shownAt + settings.BusyIconMinVisibleMs - Environment.TickCount64
                : 0;
            if (left > 0)
            {
                _hideTimer ??= DispatcherTimer.RunOnce(Hide, TimeSpan.FromMilliseconds(left));
            }
            else
            {
                Hide();
            }
        }

        Apply();
    }

    private void Show()
    {
        _showTimer = null;
        _shownAt = Environment.TickCount64;
        SetBusy(true);
        Apply();
    }

    private void Hide()
    {
        Cancel(ref _hideTimer);
        SetBusy(false);
        Apply();
    }

    private void SetBusy(bool value) => SetAndRaise(IsBusyShownProperty, ref _busy, value);

    private void UpdateError()
    {
        Cancel(ref _errorTimer);
        int seconds = XkxkUi.Current.Settings.BusyIconErrorBadgeSeconds;
        _badge.Error = _watched?.LastLoadError is not null && seconds > 0;
        if (_badge.Error)
        {
            _errorTimer = DispatcherTimer.RunOnce(() => { _errorTimer = null; _badge.Error = false; Apply(); }, TimeSpan.FromSeconds(seconds));
        }

        Apply();
    }

    // 정지 조건(23 §4): 움직임 수준 reduced·off, 소프트웨어 렌더, 실효 motion=none
    private static bool Still() =>
        DesignThemeResolver.EffectiveMotionLevel is "reduced" or "off"
        || (DesignThemeResolver.MotionLevel == "system" && xkxk.frameworks.ui.Motion.MotionPreference.Reduced) // system 의 OS 선호는 플랫폼 공급자로도 본다(GanttSpinner 와 같음, TODO-182)
        || DesignThemeResolver.Effective("motion") == "none"
        || DesignThemeResolver.FallbackReason?.Contains("software", StringComparison.Ordinal) == true;

    private void Apply()
    {
        _spinner.IsVisible = _attached && IsVisible && _busy && !Still();
        _badge.Slow = _busy && _watched is { IsLoadSlow: true };
        var slowText = _badge.Slow ? Loc.Tr("shell.icon.loadingSlow") : null;
        ToolTip.SetTip(this, slowText);
        if (_busy)
        {
            AutomationProperties.SetName(this, slowText ?? Loc.Tr("shell.icon.loading"));
        }
        else
        {
            ClearValue(AutomationProperties.NameProperty);
        }

        _badge.InvalidateVisual();
        InvalidateVisual();
    }

    private static void Cancel(ref IDisposable? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    /// <summary>오른쪽 아래 표지 — 지연은 시계, 실패는 느낌표.</summary>
    private sealed class BadgeLayer : Control
    {
        public bool Slow;
        public bool Error;
        public IBrush? SlowBrush;
        public IBrush? ErrorBrush;

        public BadgeLayer() => IsHitTestVisible = false;

        public override void Render(DrawingContext context)
        {
            var fill = Slow ? SlowBrush : Error ? ErrorBrush : null;
            if (fill is null)
            {
                return;
            }

            double r = Math.Min(Bounds.Width, Bounds.Height) * 0.28;
            var c = new Point(Bounds.Width - r, Bounds.Height - r);
            var ink = new Pen(Brushes.White, Math.Max(1, r / 3));
            context.DrawEllipse(fill, null, c, r, r);
            if (Slow)
            {
                context.DrawLine(ink, c, c + new Point(0, -r * 0.65));
                context.DrawLine(ink, c, c + new Point(r * 0.5, 0));
            }
            else
            {
                context.DrawLine(ink, c + new Point(0, -r * 0.65), c + new Point(0, r * 0.15));
                context.DrawLine(ink, c + new Point(0, r * 0.45), c + new Point(0, r * 0.7));
            }
        }
    }
}

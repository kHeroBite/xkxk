// Android 창 인셋 — 뷰를 화면 전체에 그리고 안전 영역을 레이어 층 여백으로 준다. 접근성 좌표가 뷰 기준이라 뷰가 화면 원점에 있어야 실제 위치와 맞는다
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.android;

/// <summary>
/// Avalonia Android 는 접근성 노드에 뷰 기준 좌표를 화면 좌표로 그대로 넣는다. 뷰가 상태 표시줄 아래에 있으면 노드가 그만큼 위로 어긋나고 맨 위 줄은 트리에서 빠진다.
/// 내용과 오버레이 층(투어·Flyout 크기 기준)에 여백을 주므로 화면 배치는 이전과 같다. 키보드가 올라오면 그 높이만큼 아래 여백을 늘린다.
/// 팝업 층에는 주지 않는다. Avalonia 는 앵커를 TopLevel 기준으로 계산해 팝업 층 좌표로 그대로 쓰고, 안전 영역 제약도 직접 적용하므로 팝업 층은 창 전체여야 한다(층에 여백을 주면 팝업이 그만큼 밀린다).
/// </summary>
internal sealed class AndroidSafeArea
{
    private readonly TopLevel _top;
    private readonly IInsetsManager _insets;
    private VisualLayerManager? _layers;
    private OverlayLayer? _overlay;

    private AndroidSafeArea(TopLevel top, IInsetsManager insets)
    {
        _top = top;
        _insets = insets;
    }

    /// <summary>메인 화면이 붙으면 적용한다. 아직 없으면 붙을 때 다시 한다. 여러 번 불러도 한 번만 건다.</summary>
    public static void Attach(Android.Views.Window? window, Action<AndroidSafeArea> keep)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime { MainView: { } view })
        {
            return;
        }

        void Start()
        {
            if (TopLevel.GetTopLevel(view) is { InsetsManager: { } insets } top)
            {
                var area = new AndroidSafeArea(top, insets);
                TopLevel.SetAutoSafeAreaPadding(view, false); // 기본값 true 면 내용 Padding 이 아래 레이어 여백과 겹쳐 인셋이 두 번 들어간다
                top.Bind(TemplatedControl.BackgroundProperty, top.GetResourceObservable("PdBrushBackground")); // 시스템 바 뒤는 레이어 밖이라 TopLevel 배경이 보인다
                insets.DisplayEdgeToEdgePreference = true;
                if (window is not null && !OperatingSystem.IsAndroidVersionAtLeast(35))
                {
                    // API 34 이하: Avalonia 가 위에서 켜는 반투명 플래그는 바에 회색 막을 치고, 끄면 시작 때의 바 색이 남아 테마를 따라가지 않는다. 투명으로 두어 TopLevel 배경이 보이게 한다
                    window.ClearFlags(Android.Views.WindowManagerFlags.TranslucentStatus | Android.Views.WindowManagerFlags.TranslucentNavigation);
                    window.AddFlags(Android.Views.WindowManagerFlags.DrawsSystemBarBackgrounds);
                    window.SetStatusBarColor(Android.Graphics.Color.Transparent);
                    window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
                }
                insets.SafeAreaChanged += (_, _) => area.Apply();
                top.ScalingChanged += (_, _) => area.Apply(); // 배율이 정해지기 전 값은 물리 픽셀 그대로라 크게 들어온다(Fold5 첫 기동 80px → 80dp)
                if (top.InputPane is { } pane)
                {
                    pane.StateChanged += (_, _) => area.Apply();
                }

                area.Apply();
                keep(area);
            }
        }

        if (view.IsLoaded)
        {
            Start();
            return;
        }

        void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            view.Loaded -= OnLoaded;
            Start();
        }

        view.Loaded += OnLoaded;
    }

    private void Apply()
    {
        if (_layers is null)
        {
            _layers = _top.GetVisualDescendants().OfType<VisualLayerManager>().FirstOrDefault();
            if (_layers is null)
            {
                return;
            }

            // 오버레이 층은 처음 쓸 때 만들어지는데 그때는 측정을 다시 하지 않아 첫 투어가 크기 0 으로 배치된다. 미리 만들고 측정시킨다
            _overlay = OverlayLayer.GetOverlayLayer(_layers);
            _layers.InvalidateMeasure();
        }

        var safe = _insets.DisplaysEdgeToEdge ? _insets.SafeAreaPadding : default;
        var keyboard = _top.InputPane is { State: InputPaneState.Open } pane ? pane.OccludedRect.Height : 0;
        var margin = new Thickness(safe.Left, safe.Top, safe.Right, safe.Bottom + keyboard); // OccludedRect 높이는 내비게이션 바를 뺀 값이다
        if (_layers.Child is { } content)
        {
            content.Margin = margin;
        }

        if (_overlay is not null)
        {
            _overlay.Margin = margin;
        }
    }
}

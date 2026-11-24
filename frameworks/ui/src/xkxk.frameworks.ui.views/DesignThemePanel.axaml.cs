// 디자인 테마 탭 코드 — 자기 ViewModel 수명(붙을 때 생성·떨어질 때 해제)과 폭에 따른 2열/1열 칸 배치만 담당. 값·명령은 전부 DesignThemeViewModel 바인딩
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

public partial class DesignThemePanel : UserControl
{
    private const string NarrowClass = "narrow";
    private const string NarrowWidthKey = "DesignNarrowWidth";

    private bool _narrow;
    private DesignThemeViewModel? _ownedVm; // 이 패널이 직접 만든 VM(편집기가 DataContext 를 주지 않았을 때만) — 이것만 해제한다

    public DesignThemePanel()
    {
        InitializeComponent();
    }

    /// <summary>붙을 때 DataContext 가 이미 DesignThemeViewModel(편집기 VM 의 DesignTheme)이면 그대로 쓰고, 없을 때만 직접 만든다(VM 중복 방지).</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_ownedVm is null && DataContext is not DesignThemeViewModel)
        {
            _ownedVm = new DesignThemeViewModel();
            DataContext = _ownedVm;
        }
    }

    /// <summary>떨어질 때는 자기가 만든 VM 만 해제한다(편집기가 준 VM 은 편집기 VM 이 Dispose 한다).</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_ownedVm is not null)
        {
            _ownedVm.Dispose();
            _ownedVm = null;
            ClearValue(DataContextProperty);
        }
    }

    /// <summary>배경 이미지 고르기: 플랫폼 파일 고르기 창(StorageProvider)을 띄워 이름·바이트만 VM 에 넘긴다(검사·저장은 VM·데이터 계층).</summary>
    private async void OnPickBackground(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DesignThemeViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Tr("theme.style.bg.pick"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.Tr("theme.style.bg")) { Patterns = vm.BackgroundPatterns } },
        });
        if (files.Count == 0)
        {
            return;
        }

        await using var stream = await files[0].OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        await vm.ImportBackgroundAsync(files[0].Name, buffer.ToArray());
    }

    /// <summary>좁은 폭은 1열: 효과 칸을 카드 칸 아래로 옮긴다. 기준 폭은 리소스 DesignNarrowWidth.</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var narrow = this.TryFindResource(NarrowWidthKey, out var w) && w is double limit && e.NewSize.Width < limit;
        if (narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        Classes.Set(NarrowClass, narrow);
        PanelGrid.ColumnDefinitions = ColumnDefinitions.Parse(narrow ? "*" : "*,12,*");
        Grid.SetColumn(EffectPane, narrow ? 0 : 2);
        Grid.SetRow(EffectPane, narrow ? 1 : 0);
        EffectPane.Margin = narrow ? new Thickness(0, 12, 0, 0) : default;
    }
}

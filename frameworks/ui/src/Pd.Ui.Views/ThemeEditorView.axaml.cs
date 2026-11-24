// 테마 편집기 화면 코드 — 열기(데스크톱=별도 창 1개, 브라우저=플라이아웃)와 JSON 파일 내보내기/가져오기(StorageProvider)
using Pd.Poc.Shell;
using Pd.Client.Data.Theming;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.Theming;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

public partial class ThemeEditorView : UserControl
{
    /// <summary>이 폭보다 좁으면 목록(220)+간격(12)+색 편집이 한 줄에 들어가지 않는다.</summary>
    private const double NarrowWidth = 560;
    private const double NarrowListHeight = 160;
    private const double NarrowTokensHeight = 320;

    private static Window? _window;
    private bool _narrow;

    /// <summary>파일 대화상자 형식 — 이름은 열 때의 현재 언어(Q17).</summary>
    private static FilePickerFileType JsonType => new(Loc.Tr("theme.ui.jsonFileType")) { Patterns = new[] { "*.json" }, MimeTypes = new[] { "application/json" } };

    public ThemeEditorView() => InitializeComponent();

    /// <summary>창 닫힘·플라이아웃 닫힘 = 화면 분리 → ViewModel 의 언어 전환 구독 해제.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }

    private ThemeEditorViewModel? Vm => DataContext as ThemeEditorViewModel;

    /// <summary>
    /// 편집기 열기. 데스크톱 = 별도 창(이미 열려 있으면 활성화), 브라우저(단일 뷰) = anchor 아래 플라이아웃.
    /// anchor 는 메뉴 항목·버튼 등 호출 위치 컨트롤. null(자동화)이면 메인 창/메인 뷰 기준.
    /// </summary>
    public static void Open(Control? anchor = null)
    {
        var hasCallerAnchor = anchor is not null;
        anchor ??= Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime d => d.MainWindow,
            ISingleViewApplicationLifetime s => s.MainView,
            _ => null,
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            // 비전경(화면 밖) 시험 모드: 시스템 창(SystemDialogHost.BackgroundMode, --bg-pos 기동)과 같은 규칙 — 활성화하지 않고 소유 창 좌상단 기준(화면 밖)에 띄운다.
            // 제품 기본(BackgroundMode=false)은 이전과 같다(활성화, 위치는 OS 기본).
            var activate = !Pd.Poc.Shell.Platform.SystemDialogHost.BackgroundMode;
            if (_window is { } existing)
            {
                if (activate)
                {
                    existing.Activate();
                }

                return;
            }

            _window = new Window
            {
                Width = 820,
                Height = 720,
                Content = new ThemeEditorView { DataContext = new ThemeEditorViewModel() },
                ShowActivated = activate,
            };
            _window.Bind(Window.TitleProperty, Loc.Instance.Observe("theme.ui.windowTitle")); // 언어 전환 시 창 제목도 즉시
            _window.Closed += (_, _) => _window = null;
            if (TopLevel.GetTopLevel(anchor) is Window owner)
            {
                if (!activate)
                {
                    _window.WindowStartupLocation = WindowStartupLocation.Manual; // 가운데 배치가 화면 안 좌표로 보정되며 사용자 화면에 나타나는 것을 피한다
                    _window.Position = owner.Position + new PixelPoint(40, 40);
                }

                _window.Show(owner);
            }
            else
            {
                _window.Show();
            }

            return;
        }

        if (anchor is null)
        {
            return;
        }

        var content = new ThemeEditorView { DataContext = new ThemeEditorViewModel() };
        // 목록·편집 칸이 내용 크기로 줄지 않게 높이를 고정한다
        var flyout = Pd.Poc.Shell.Platform.FittedFlyout.Create(content, anchor, hasCallerAnchor, 760, 600, fixedHeight: true);
        flyout.ShowAt(anchor);
    }

    /// <summary>좁은 폭은 두 칸이 들어가지 않으므로 위아래로 쌓고 화면 전체를 스크롤한다.</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var narrow = e.NewSize.Width < NarrowWidth;
        if (narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        EditorGrid.ColumnDefinitions = ColumnDefinitions.Parse(narrow ? "*" : "220,12,*");
        EditorGrid.RowDefinitions = RowDefinitions.Parse(narrow ? "Auto,12,Auto" : "*");
        Grid.SetColumn(ColorsPanel, narrow ? 0 : 2);
        Grid.SetRow(ColorsPanel, narrow ? 2 : 0);
        ThemeList.MaxHeight = narrow ? NarrowListHeight : double.PositiveInfinity;
        TokenScroll.MaxHeight = narrow ? NarrowTokensHeight : double.PositiveInfinity;
        RootScroll.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private async void OnExportFile(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.CurrentJson is not { } json)
        {
            return;
        }

        try
        {
            // 시험 모드(BackgroundMode)는 OS 파일 대화상자를 띄우지 않는다(전경·화면 노출 방지) — 텍스트 내보내기로 대신한다
            if (Pd.Poc.Shell.Platform.SystemDialogHost.BackgroundMode || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } sp)
            {
                vm.ExchangeText = json; // 파일 저장 불가 환경(웹 일부)은 텍스트로 대신 제공
                vm.Status = Loc.Tr("theme.status.saveUnsupported");
                return;
            }

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Tr("theme.ui.exportTitle"),
                SuggestedFileName = ThemeFileExchange.SuggestedFileName(vm.SelectedTheme?.Name),
                DefaultExtension = "json",
                FileTypeChoices = new[] { JsonType },
            });
            if (file is null)
            {
                return;
            }

            await using (var stream = await file.OpenWriteAsync())
            {
                await ThemeFileExchange.WriteAsync(stream, json);
            }

            vm.Status = Loc.Tr("theme.status.savedFile", file.Name);
        }
        catch (Exception ex)
        {
            vm.Status = Loc.Tr("theme.status.exportFailed", ex.Message);
        }
    }

    private async void OnImportFile(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        try
        {
            if (Pd.Poc.Shell.Platform.SystemDialogHost.BackgroundMode || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } sp) // 시험 모드는 OS 파일 대화상자 금지
            {
                vm.Status = Loc.Tr("theme.status.openUnsupported");
                return;
            }

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Tr("theme.ui.importTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[] { JsonType },
            });
            if (files.FirstOrDefault() is not { } file)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            vm.ImportJson(await ThemeFileExchange.ReadAsync(stream), $"'{file.Name}'");
        }
        catch (Exception ex)
        {
            vm.Status = Loc.Tr("theme.status.importFailed", ex.Message);
        }
    }
}

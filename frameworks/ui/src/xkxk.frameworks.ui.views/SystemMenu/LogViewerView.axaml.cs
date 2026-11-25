// 로그 보기 화면 코드비하인드 — 열기 진입점과 로그 내보내기(파일 저장 대화, 저장 불가 환경은 안내)만 담당
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Platform;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.ViewModels.SystemMenu;

namespace xkxk.frameworks.ui.views.SystemMenu;

public partial class LogViewerView : UserControl
{
    public LogViewerView() => InitializeComponent();

    /// <summary>로그 보기 창을 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("log", "sysview.log.windowTitle", () => new LogViewerView { DataContext = new LogViewerViewModel() }, anchor, 760, 520, iconKey: MenuIconCatalog.ShellIconKey("Log"));

    /// <summary>
    /// 메모리 로그 전체(필터 없음)를 파일로 저장한다. 메뉴 '로그 내보내기'도 이 함수를 쓴다. 저장 대화만 여기서, 내용 쓰기는 LogExport.
    /// 결과: true = 저장함, false = 저장 대화를 쓸 수 없는 환경, null = 사용자가 취소함.
    /// </summary>
    public static async Task<bool?> ExportAllAsync(TopLevel? top)
    {
        if (top?.StorageProvider is not { CanSave: true } sp)
        {
            return false;
        }

        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Tr("sysview.log.export"),
            SuggestedFileName = LogExport.SuggestedFileName(),
            DefaultExtension = "txt",
        });
        if (file is null)
        {
            return null;
        }

        await using var stream = await file.OpenWriteAsync();
        await LogExport.WriteAllAsync(stream);
        return true;
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = await ExportAllAsync(TopLevel.GetTopLevel(this));
            if (result is { } ok)
            {
                StatusText.Text = ok ? Loc.Tr("sysview.log.exported") : Loc.Tr("sysview.log.exportUnsupported");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Tr("sysview.log.exportFailed");
            PdLog.Error(PdLogCategory.System, "log.export failed", ex);
        }
    }
}

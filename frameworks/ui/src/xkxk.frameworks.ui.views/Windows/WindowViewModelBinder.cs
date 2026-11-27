// 창 뷰모델 연결부 — 뷰모델 생성·문맥 연결·뷰 DataContext·첫 부착 시 열림·닫힘 시 취소와 정리(부록 a11 §5·§6)
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Windows;

/// <summary>
/// <see cref="UiWindowViewRegistration"/> 가 채운 <see cref="UiWindowOptions.CreateContent"/> 의 본체다.
/// 문서 하나의 내용을 만들 때마다 뷰모델 하나와 뷰 하나를 만들고 수명 호출을 잇는다.
/// </summary>
/// <remarks>
/// 오류 규칙(a11 §6): 생성 함수 null 반환 → XKUI0507 자리표시(생성 함수 예외는 셸이 XKUI0507 로 처리),
/// 파라미터 형식 불일치 → XKUI0509 자리표시, <c>OnOpenedAsync</c>·<c>OnClosed</c>·닫힘 처리 예외 → XKUI0510(뷰 유지, 앱 비정지).
/// UI 스레드 전제다.
/// </remarks>
internal static class WindowViewModelBinder
{
    /// <summary>뷰모델과 뷰를 만들어 연결한다. 실패하면 자리표시 컨트롤을 돌려준다.</summary>
    /// <typeparam name="TViewModel">창 뷰모델 형식.</typeparam>
    /// <typeparam name="TView">창 뷰 형식.</typeparam>
    /// <param name="context">셸이 넘긴 내용 생성 문맥.</param>
    /// <param name="createViewModel">뷰모델 생성 함수. 예외는 그대로 올려 셸이 XKUI0507 로 남긴다.</param>
    /// <returns>뷰 또는 자리표시 컨트롤.</returns>
    public static Control CreateContent<TViewModel, TView>(UiWindowContentContext context, Func<UiWindowContentContext, TViewModel> createViewModel)
        where TViewModel : UiWindowViewModel
        where TView : Control, new()
    {
        ArgumentNullException.ThrowIfNull(context);
        var viewModel = createViewModel(context);
        if (viewModel is null)
        {
            Report(context, UiDiagnosticIds.WindowContentFailed,
                $"창 '{context.WindowId}' 의 뷰모델 생성 함수가 null 을 돌려줬다({typeof(TViewModel).FullName} 이어야 한다)."); // i18n-ok 개발자 진단
            return Placeholder(context, UiDiagnosticIds.WindowContentFailed);
        }

        try
        {
            viewModel.AttachWindow(context.Window, context.Parameter); // AcceptParameter 가 거부하면 연결하지 않고 ArgumentException
        }
        catch (ArgumentException ex)
        {
            Report(context, UiDiagnosticIds.WindowArgumentMismatch,
                $"창 '{context.WindowId}' 에 넘긴 값의 형식이 뷰모델 {typeof(TViewModel).FullName} 과 맞지 않는다: {context.Parameter?.GetType().FullName}. {ex.Message}"); // i18n-ok 개발자 진단
            return Placeholder(context, UiDiagnosticIds.WindowArgumentMismatch);
        }

        var view = new TView { DataContext = viewModel };
        Connect(context, viewModel, view);
        return view;
    }

    /// <summary>뷰의 첫 시각 트리 부착에 열림을, 창 문맥의 닫힘에 취소·정리를 잇는다.</summary>
    private static void Connect(UiWindowContentContext context, UiWindowViewModel viewModel, Control view)
    {
        var window = context.Window;
        var cancellation = new CancellationTokenSource();
        EventHandler<VisualTreeAttachmentEventArgs>? onAttached = null;
        EventHandler? onClosed = null;

        onAttached = (_, _) =>
        {
            view.AttachedToVisualTree -= onAttached; // SDI 이동·탭 재부착으로 다시 붙어도 열림은 1회
            if (!cancellation.IsCancellationRequested)
            {
                Open(context, viewModel, cancellation.Token);
            }
        };

        onClosed = (_, _) =>
        {
            window.Closed -= onClosed;
            view.AttachedToVisualTree -= onAttached;
            try
            {
                cancellation.Cancel(); // 진행 중인 OnOpenedAsync 를 멈춘다(토큰 등록 콜백 예외도 여기서 잡는다)
                viewModel.NotifyClosed();
            }
            catch (Exception ex)
            {
                Report(context, UiDiagnosticIds.WindowViewModelFailed,
                    $"창 '{context.WindowId}' 의 뷰모델 닫힘 처리가 예외로 실패했다: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 개발자 진단
            }
        };

        view.AttachedToVisualTree += onAttached;
        window.Closed += onClosed;
        if (!window.IsOpen)
        {
            onClosed(window, EventArgs.Empty); // 내용을 만드는 사이에 이미 닫혔으면 Closed 가 다시 오지 않는다
        }
    }

    /// <summary><c>NotifyOpenedAsync</c> 를 부르고 실패를 XKUI0510 으로 남긴다. 취소로 끝난 것은 실패가 아니다.</summary>
    private static void Open(UiWindowContentContext context, UiWindowViewModel viewModel, CancellationToken cancellationToken)
    {
        Task task;
        try
        {
            task = viewModel.NotifyOpenedAsync(cancellationToken) ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ReportOpenFailed(context, ex, cancellationToken);
            return;
        }

        if (task.IsCompleted)
        {
            Observe(context, task, cancellationToken);
            return;
        }

        _ = task.ContinueWith(t => Dispatcher.UIThread.Post(() => Observe(context, t, cancellationToken)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>끝난 열림 작업의 결과를 확인한다.</summary>
    private static void Observe(UiWindowContentContext context, Task task, CancellationToken cancellationToken)
    {
        if (task.IsFaulted && task.Exception is { } aggregate)
        {
            ReportOpenFailed(context, aggregate.GetBaseException(), cancellationToken);
        }
    }

    /// <summary>열림 실패를 XKUI0510 으로 남긴다. 창이 닫혀 토큰이 취소된 뒤의 취소 예외는 남기지 않는다.</summary>
    private static void ReportOpenFailed(UiWindowContentContext context, Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Report(context, UiDiagnosticIds.WindowViewModelFailed,
            $"창 '{context.WindowId}' 의 뷰모델 OnOpenedAsync 가 예외로 실패했다(뷰는 유지): {ex.GetType().Name}: {ex.Message}"); // i18n-ok 개발자 진단
    }

    /// <summary>진단을 Error 로 남긴다. 출처는 창 정의의 등록 주체다.</summary>
    private static void Report(UiWindowContentContext context, string id, string message)
    {
        var app = XkxkUi.Current;
        var owner = app.Windows.TryResolve(context.WindowId, out var definition) ? definition.Owner : null;
        app.Diagnostics.Report(new UiDiagnostic(id, UiDiagnosticSeverity.Error, message, owner));
    }

    /// <summary>자리표시 내용: 창 ID 와 진단 ID. 문서 ID·제목·위치는 그대로라 다시 저장해도 사라지지 않는다.</summary>
    private static Control Placeholder(UiWindowContentContext context, string diagnosticId) => new TextBlock
    {
        Text = $"{context.WindowId} ({diagnosticId})",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(16),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };
}

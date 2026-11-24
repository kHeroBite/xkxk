// 창 단위 확대·축소(UI-06, 옛 WindowCommands.Zoom) — 문서 배율 명령과 Ctrl+휠 대상 창 찾기
using System.Linq;
using Avalonia;
using Avalonia.VisualTree;
using Pd.Poc.Shell.Diagnostics;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Docking;

/// <summary>대상 문서를 주지 않으면 s.Owner.ActiveDocument 를 쓴다.</summary>
[WindowCommandsPart]
internal sealed class DocumentZoom
{
    private readonly WindowSession _s;

    internal DocumentZoom(WindowSession s) => _s = s;

    /// <summary>Ctrl+휠·Ctrl+0 1회당 배율 변화(10%).</summary>
    public const double ZoomStep = 0.1;

    /// <summary>true = Ctrl+휠이 현재 창 확대·축소(UI-06), false = 전체 기준 글자 크기(UI-05). 설정에 저장된다.</summary>
    public bool WindowZoomMode
    {
        get => UiSettings.Current.WheelMode == CtrlWheelMode.WindowZoom;
        set => UiSettings.Current.WheelMode = value ? CtrlWheelMode.WindowZoom : CtrlWheelMode.FontSize;
    }

    public void SetDocumentZoom(PocDocument document, double zoom)
    {
        using var op = PerfProbe.Current.TimeOperation("ui.zoom"); // PERF-UI(Ctrl+휠 창 배율·자동화·원격 공통 경로)
        document.Zoom = zoom;
        _s.Log(Loc.Tr("log.window.zoom", document.Zoom, document.Title));
    }

    public void ZoomIn(PocDocument? document = null) => ZoomBy(document, ZoomStep);

    public void ZoomOut(PocDocument? document = null) => ZoomBy(document, -ZoomStep);

    public void ResetZoom(PocDocument? document = null)
    {
        if ((document ?? _s.Owner.ActiveDocument) is { } doc)
        {
            SetDocumentZoom(doc, 1.0);
        }
    }

    /// <summary>
    /// SettingsInput.WindowZoomHandler 로 등록. 이벤트 원천의 시각 조상 중 문서 래퍼(ZoomDecorator, Tag=PocDocument)를 찾아
    /// 포인터 아래 창을 대상으로 하고, 없으면(메뉴·탭 위 등) 활성 문서를 대상으로 한다. 문서가 하나도 없으면 false.
    /// </summary>
    public bool HandleZoomInput(Visual? source, int delta)
    {
        var target = FindDocumentAt(source) ?? _s.Owner.ActiveDocument;
        if (target is null)
        {
            return false;
        }

        if (delta == 0)
        {
            ResetZoom(target);
        }
        else
        {
            ZoomBy(target, delta * ZoomStep);
        }

        return true;
    }

    private void ZoomBy(PocDocument? document, double step)
    {
        if ((document ?? _s.Owner.ActiveDocument) is { } doc)
        {
            SetDocumentZoom(doc, doc.Zoom + step);
        }
    }

    private static PocDocument? FindDocumentAt(Visual? source)
    {
        if (source is null)
        {
            return null;
        }

        return source.GetSelfAndVisualAncestors()
            .OfType<IDocumentZoomHost>()
            .Select(c => c.Tag as PocDocument)
            .FirstOrDefault(d => d is not null);
    }
}

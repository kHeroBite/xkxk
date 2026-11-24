// 남은 칸 판 그리기·카드 생성·배치(Measure/Arrange) 부분 — SnapAssistLayer partial(판 불투명도 ZoneOpacity 포함)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.views.Snap;

/// <summary>남은 칸 판 상수, 후보 카드 생성, 판 그리기(ZonesVisual), 크기·배치 계산.</summary>
public sealed partial class SnapAssistLayer
{
    /// <summary>남은 칸 판 불투명도(표면색). 카드 사이로 뒤 창 글자가 비치지 않게 완전 불투명하게 덮는다(0.6 → 1.0, 0.96 은 다크에서 밝은 글자 잔상이 남았다).</summary>
    public const double ZoneOpacity = 1.0;

    /// <summary>남은 칸 판 외곽선 두께(DIP).</summary>
    public const double ZoneBorder = 1;

    /// <summary>남은 칸 판 모서리 반지름(DIP).</summary>
    public const double ZoneRadius = 8;

    /// <summary>활성 칸 사각형 안 카드 목록 여백(DIP).</summary>
    public const double PickerMargin = 12;

    private const string SurfaceKey = "PdBrushSurface";
    private const string BorderKey = "PdBrushBorder";

    private List<SnapAssistCard> BuildCards(SnapAssistView view)
    {
        var list = new List<SnapAssistCard>(view.Candidates.Count);
        var titles = view.Candidates.Select(c => string.IsNullOrWhiteSpace(c.Title) ? Loc.Tr("snapAssist.untitled") : c.Title).ToList();
        var seen = new Dictionary<string, int>(StringComparer.CurrentCulture);
        for (var i = 0; i < view.Candidates.Count; i++)
        {
            var c = view.Candidates[i];
            var title = titles[i];

            // 같은 제목 후보가 둘 이상이면 보조 표기 #순번(1부터, 후보 순서)으로 구분한다(11 문서 §3.9 대체 카드)
            var subtitle = string.Empty;
            if (titles.Count(t => string.Equals(t, title, StringComparison.CurrentCulture)) > 1)
            {
                seen[title] = seen.TryGetValue(title, out var n) ? n + 1 : 1;
                subtitle = "#" + seen[title].ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var name = subtitle.Length > 0 ? $"{title} {subtitle}" : title;
            var image = view.Thumbnails ? ThumbOf(c) : null;

            // 대체 카드 보조 표기: 최소화로 못 찍었으면 '최소화됨', 그 밖(실패·썸네일 끔)은 '미리보기 없음'(11 문서 §3.9)
            var note = image is not null ? string.Empty
                : _thumbStatus.TryGetValue(c.Key, out var st) && st == "fallback:minimized" ? Loc.Tr("snapAssist.minimized") : Loc.Tr("snapAssist.noPreview");
            list.Add(new SnapAssistCard(i, c.Key, title, subtitle, image, view.Thumbnails, Loc.Tr("snapAssist.card", name), note));
        }

        return list;
    }

    /// <summary>후보 썸네일을 세션 안에서 한 번만 찍는다. 실패하면 그 카드는 아이콘+제목. 웹은 첫 실패 뒤 나머지를 찍지 않는다(계획 §5.4).</summary>
    private IImage? ThumbOf(SnapAssistCandidate c)
    {
        if (c.ThumbSource is null || _thumbsBroken)
        {
            ReportThumb(c.Key, c.ThumbSource is null ? "fallback:no-source" : "fallback:web-fallback");
            return null;
        }

        if (_thumbs.TryGetValue(c.Key, out var cached))
        {
            ReportThumb(c.Key, _thumbStatus.TryGetValue(c.Key, out var st) ? st : (cached is null ? "fallback:unknown" : "bitmap"));
            return cached;
        }

        IImage? image = null;
        string status;
        var detail = string.Empty;
        try
        {
            var ok = SnapAssistThumbnail.TryCapture(c.ThumbSource, new Size(SnapAssistCard.ThumbWidth, SnapAssistCard.ThumbHeight), out image, out var reason);
            detail = SnapAssistThumbnail.LastDetail;
            if (!ok)
            {
                image = null;
                status = "fallback:" + reason;
                PdLog.Info(LogTag, $"thumb skip key={c.Key} reason={reason} {detail}");
                if (OperatingSystem.IsBrowser())
                {
                    _thumbsBroken = true;
                }
            }
            else
            {
                status = "bitmap";
            }
        }
        catch (Exception ex)
        {
            image = null;
            status = "fallback:exception";
            PdLog.Info(LogTag, $"thumb failed key={c.Key}: {ex.Message}");
            if (OperatingSystem.IsBrowser())
            {
                _thumbsBroken = true;
            }
        }

        _thumbs[c.Key] = image;
        _thumbStatus[c.Key] = status;
        ReportThumb(c.Key, status, detail);
        return image;
    }

    /// <summary>카드별 썸네일 결과를 세션 진단(snapassist.state thumb·thumbs 필드)에 알린다. 진단 실패가 카드 표시를 막지 않게 삼킨다.</summary>
    private static void ReportThumb(string key, string status, string? detail = null)
    {
        try
        {
            SnapAssistSession.ReportThumb(key, status, detail);
        }
        catch (Exception ex)
        {
            PdLog.Info(LogTag, $"thumb report failed key={key}: {ex.Message}");
        }
    }

    private Rect Local(Rect r) => r.Translate(new Vector(-_origin.X, -_origin.Y));

    /// <summary>카드 목록이 놓일 사각형(활성 칸 안 여백 12). 칸이 여백보다 작으면 칸 그대로.</summary>
    private Rect PickerRect()
    {
        if (_view is not { } v || ActiveZone(v) is not { } z)
        {
            return default;
        }

        var r = Local(z.Rect);
        return r.Width > PickerMargin * 2 && r.Height > PickerMargin * 2 ? r.Deflate(PickerMargin) : r;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _zonesVisual.Measure(availableSize);
        if (_picker is not null)
        {
            var r = PickerRect();
            _picker.Measure(new Size(Math.Max(0, r.Width), Math.Max(0, r.Height)));
        }

        // 어도너 층·호스트 창 내용이 주는 크기를 그대로 쓴다(무한이면 0)
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _zonesVisual.Arrange(new Rect(finalSize));
        _picker?.Arrange(PickerRect());
        return finalSize;
    }

    /// <summary>남은 칸 판 그리기 자식(Panel.Render 는 봉인이라 따로 둔다). 첫 자식이라 카드 목록보다 아래에 그려진다. 입력은 덮개가 받는다.</summary>
    private sealed class ZonesVisual : Control
    {
        private readonly SnapAssistLayer _owner;

        public ZonesVisual(SnapAssistLayer owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            Focusable = false;
        }

        public override void Render(DrawingContext context)
        {
            if (_owner._view is not { } v)
            {
                return;
            }

            // 테마 토큰(표면색·외곽선색). 세션 동안만 보이므로 그릴 때마다 찾는다(테마 전환 즉시 반영)
            var surface = this.TryFindResource(SurfaceKey, ActualThemeVariant, out var s) && s is IBrush sb ? sb : Brushes.Gray;
            var border = this.TryFindResource(BorderKey, ActualThemeVariant, out var b) && b is IBrush bb ? bb : Brushes.DarkGray;
            var pen = new Pen(border, ZoneBorder);
            SnapAssistSession.DiagBackdrop = $"{(surface as ISolidColorBrush is { } sc ? sc.Color.A / 255.0 * sc.Opacity * ZoneOpacity : ZoneOpacity):0.00}/{ActualThemeVariant}";
            foreach (var z in v.Zones)
            {
                if (z.Filled || z.Rect.Width <= 0 || z.Rect.Height <= 0)
                {
                    continue; // 채운 칸은 그리지 않는다(배치한 창이 그대로 보인다)
                }

                var r = _owner.Local(z.Rect).Deflate(ZoneBorder / 2);
                using (context.PushOpacity(ZoneOpacity))
                {
                    context.DrawRectangle(surface, null, r, ZoneRadius, ZoneRadius);
                }

                context.DrawRectangle(null, pen, r, ZoneRadius, ZoneRadius);
            }
        }
    }
}

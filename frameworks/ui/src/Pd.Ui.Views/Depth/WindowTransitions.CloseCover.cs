// 창 닫기 덮개 1건(WindowTransitions 의 partial, 21 설계 §4.1·§7) — 최상위 덮개 층에서 나감 효과를 재생하고, 앞 창 사각형을 프레임마다 빼며, 중단 때 비트맵까지 정리한다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Theming;
using Pd.Ui.Controls;

namespace Pd.Ui.Views.Depth;

public static partial class WindowTransitions
{
    private static readonly List<CloseCover> s_covers = new();

    /// <summary>앞선 닫기 덮개를 끝낸다(다음 닫기·다른 전환). 같은 호출 묶음에서 막 시작한 덮개는 그대로 둔다.</summary>
    private static void EndCovers(string reason)
    {
        foreach (var cover in s_covers.Where(c => c.Armed).ToArray())
        {
            cover.End(reason);
        }
    }

    private sealed class CloseCover
    {
        private readonly PendingClose _pending;
        private readonly TopLevel _top;
        private readonly AdornerLayer _layer;
        private readonly Canvas _canvas;
        private readonly SnapshotPicture _image;
        private bool _started;
        private bool _ended;

        private CloseCover(PendingClose pending, TopLevel top, AdornerLayer layer)
        {
            _pending = pending;
            _top = top;
            _layer = layer;
            _canvas = new Canvas { IsHitTestVisible = false };
            var bounds = pending.Shot.Bounds;
            _image = new SnapshotPicture(pending.Shot.Image, pending.Shot.Geometry);
            Canvas.SetLeft(_image, bounds.X);
            Canvas.SetTop(_image, bounds.Y);
            _canvas.Children.Add(_image);
        }

        public bool Armed { get; private set; }

        public void Arm() => Armed = true;

        public static CloseCover? Start(PendingClose pending, TopLevel top)
        {
            if (pending.Shot.Host is not AdornerLayer layer)
            {
                pending.Note.Stage = "noLayer";
                pending.Discard();
                return null;
            }

            var cover = new CloseCover(pending, top, layer);
            return cover.Run() ? cover : null;
        }

        private bool Run()
        {
            _pending.Scope.Apply(_canvas);
            _layer.Children.Add(_canvas); // AdornedElement 를 주지 않는다 — 사라질 호스트에 매이지 않는다
            Play(_image, MotionDirection.Exit, _pending.Effect, null, () => End("done"));
            if (_ended || !s_running.ContainsKey(_image))
            {
                End("noPlay");
                return false;
            }

            _started = true;
            s_closeStarted++;
            s_covers.Add(this);
            _pending.Note.Stage = "played";
            _image.DetachedFromVisualTree += OnDetached;
            _top.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            _top.RequestAnimationFrame(Follow);
            return true;
        }

        private void Follow(TimeSpan _)
        {
            if (_ended)
            {
                return;
            }

            if (IsMainBusy())
            {
                End("busy");
                return;
            }

            _pending.Scope.Apply(_canvas);
            _top.RequestAnimationFrame(Follow);
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e) => End("pressed");

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => End("detached");

        /// <summary>끝·중단 공통. 여러 번 불려도 1회만 정리한다.</summary>
        public void End(string reason)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            _image.DetachedFromVisualTree -= OnDetached;
            _top.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            s_covers.Remove(this);
            _canvas.IsVisible = false; // Finish 가 불투명도를 되돌리므로 먼저 숨긴다(되돌아온 그림이 한 프레임 보이지 않게)
            if (s_running.TryGetValue(_image, out var playback))
            {
                playback.Finish();
            }

            if (_started)
            {
                s_closeEnded++;
                _pending.Note.Stage = "ended";
                _pending.Note.Reason = reason;
            }
            else
            {
                _pending.Note.Stage = "noPlay";
            }

            if (reason == "detached")
            {
                Dispatcher.UIThread.Post(Release); // 트리에서 떼는 순회 중에는 덮개 층의 자식 목록을 바꾸지 않는다
                return;
            }

            Release();
        }

        private void Release()
        {
            _layer.Children.Remove(_canvas);
            _image.Image = null;
            _pending.Discard();
        }
    }

    /// <summary>
    /// 덮개가 보일 범위. 배치 영역에서 앞 창(닫힐 때 아래가 아니던 창·관리형 창·NAV 본문 위 시트)의 사각형을 뺀다(앞 창 바깥 그림자 띠는 21 §4.1 한계).
    /// '아래'는 문서로 기억한다 — 탭 묶음이 풀리면 남은 문서가 새 창 컨트롤로 다시 만들어진다.
    /// </summary>
    private sealed class CoverScope
    {
        private readonly AdornerLayer _layer;
        private readonly Rect _area;
        private readonly Panel? _panel;
        private readonly HashSet<object> _below = new(ReferenceEqualityComparer.Instance);
        private readonly ManagedWindowLayer? _managed;
        private readonly Control[] _sheets;
        private List<(Rect Rect, double Radius)>? _holes;

        private CoverScope(Located at, AdornerLayer layer)
        {
            _layer = layer;
            var window = at.View as MdiDocumentWindow ?? at.View.FindAncestorOfType<MdiDocumentWindow>();
            var slot = window?.GetVisualParent() as Control;
            _panel = slot?.GetVisualParent() as MdiLayoutPanel;
            Control areaOf = at.View is MdiDocumentWindow && _panel is not null ? _panel : at.Host;
            _area = RectIn(areaOf, layer) ?? new Rect(layer.Bounds.Size);
            if (_panel is not null && slot is not null)
            {
                var children = _panel.Children;
                var index = children.IndexOf(slot);
                for (var i = 0; i < children.Count; i++)
                {
                    var below = children[i].ZIndex < slot.ZIndex || (children[i].ZIndex == slot.ZIndex && i <= index); // 자기 포함
                    if (below && WindowIn(children[i]) is { } w)
                    {
                        _below.UnionWith(Members(w.DataContext));
                    }
                }
            }

            _managed = Root()?.GetVisualDescendants().OfType<ManagedWindowLayer>().FirstOrDefault();
            _sheets = at.Placement == MotionPlacement.Nav && at.Host.GetVisualParent() is Panel body
                ? body.Children.Skip(body.Children.IndexOf(at.Host) + 1).OfType<Border>().ToArray<Control>()
                : Array.Empty<Control>();
        }

        public static CoverScope Capture(Located at, AdornerLayer layer) => new(at, layer);

        /// <summary>지금 자리로 잘라냄 모양을 고친다(사각형이 그대로면 손대지 않는다).</summary>
        public void Apply(Canvas canvas)
        {
            var holes = Holes();
            if (_holes is not null && _holes.SequenceEqual(holes))
            {
                return;
            }

            _holes = holes;
            Geometry shape = new RectangleGeometry(_area);
            foreach (var (rect, radius) in holes)
            {
                shape = new CombinedGeometry(GeometryCombineMode.Exclude, shape, new RectangleGeometry(rect, radius, radius));
            }

            canvas.Clip = shape;
        }

        private List<(Rect Rect, double Radius)> Holes()
        {
            var holes = new List<(Rect Rect, double Radius)>();
            if (_panel is not null && TopLevel.GetTopLevel(_panel) is not null)
            {
                foreach (var child in _panel.Children)
                {
                    if (WindowIn(child) is { } w && !Members(w.DataContext).Any(m => _below.Contains(m)))
                    {
                        AddWindow(holes, w);
                    }
                }
            }

            if (_managed is { IsEffectivelyVisible: true }
                && _managed.GetVisualDescendants().OfType<MdiLayoutPanel>().FirstOrDefault() is { } floating)
            {
                foreach (var child in floating.Children)
                {
                    if (WindowIn(child) is { } w)
                    {
                        AddWindow(holes, w);
                    }
                }
            }

            foreach (var sheet in _sheets)
            {
                if (sheet.IsEffectivelyVisible)
                {
                    Add(holes, sheet, 0);
                }
            }

            return holes;
        }

        private void AddWindow(List<(Rect Rect, double Radius)> holes, MdiDocumentWindow window)
        {
            if (!window.IsEffectivelyVisible)
            {
                return;
            }

            var outer = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == TemplateParts.OuterBorder);
            Add(holes, (Control?)outer ?? window, outer?.CornerRadius.TopLeft ?? 0);
        }

        private void Add(List<(Rect Rect, double Radius)> holes, Control control, double radius)
        {
            if (RectIn(control, _layer) is { } rect && rect.Intersects(_area))
            {
                holes.Add((rect, radius));
            }
        }

        private static MdiDocumentWindow? WindowIn(Control child) =>
            child as MdiDocumentWindow ?? (child as ContentPresenter)?.Child as MdiDocumentWindow;

        private static IEnumerable<object> Members(object? context) => context switch
        {
            MdiTabGroupDock group => group.Documents.ToArray<object>(),
            null => Array.Empty<object>(),
            _ => new[] { context },
        };

        private static Rect? RectIn(Control control, Visual target) =>
            control.Bounds.Width > 0 && control.Bounds.Height > 0 && control.TransformToVisual(target) is { } m
                ? new Rect(control.Bounds.Size).TransformToAABB(m)
                : null;
    }
}

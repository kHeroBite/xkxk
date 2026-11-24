// 창 움직임 효과 재생 1건·덮개(WindowTransitions 의 partial) — 효과 표 한 행의 시작·끝 상태를 불투명도·배율·이동·3D 기울기·잘라냄(Visual.Clip)으로 옮기고, 끝·중단 때 전 값으로 되돌린다
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Pd.Poc.Shell.Theming;

namespace Pd.Ui.Views.Depth;

public static partial class WindowTransitions
{
    private static readonly Dictionary<Control, Playback> s_running = new();

    private const string PlayPhase = "motion.play"; // 진단 구간 이름(설정 키 아님)
    private static int s_marked; // 표식을 낸 진행 중 재생 수 — 들어옴·나감 짝을 한 구간으로 합친다

    /// <summary>
    /// 대상에 효과 1건을 건다(진행 중이던 재생은 먼저 끝내 전 값으로 돌린다). 전 RenderTransform 이 Transform 이 아니면(TransformOperations 등) 합성할 수 없어 null.
    /// startOpacity = 시작 불투명도 하한(MDI 겹침 전환에서 이미 보이던 창의 깜박임 방지, 없으면 null).
    /// </summary>
    private static Playback? Begin(Control target, MotionEffect effect, double? startOpacity = null)
    {
        if (s_running.TryGetValue(target, out var old))
        {
            old.Finish();
        }

        var before = target.RenderTransform;
        if (before is not null and not Transform)
        {
            return null;
        }

        var playback = new Playback(target, effect, before, target.Opacity, target.Clip, startOpacity);
        s_running[target] = playback;
        playback.Step(0);
        return playback;
    }

    /// <summary>
    /// 진행 중 재생 1건. Step(t) 는 표의 가속 곡선(Easing 이름)으로 상태를 옮기고, Finish 는 RenderTransform·Opacity·Clip 을 전 값(같은 객체)으로 되돌린다(여러 번 불러도 된다).
    /// 대상에서 포인터를 누르면(끌기 시작) 즉시 끝낸다(21 설계 §4 중단 규칙).
    /// </summary>
    private sealed class Playback
    {
        private readonly ScaleTransform _scale = new();
        private readonly TranslateTransform _move = new();
        private readonly Rotate3DTransform? _tilt;
        private readonly Easing _easing;
        private readonly double _fromOpacity;
        private readonly double _tiltDeg;

        public Playback(Control target, MotionEffect effect, ITransform? before, double opacity, Geometry? clip, double? startOpacity)
        {
            Target = target;
            Effect = effect;
            Before = before;
            BeforeOpacity = opacity;
            BeforeClip = clip;
            _easing = EasingOf(effect.Easing);
            _fromOpacity = startOpacity is { } s ? Math.Max(effect.From.Opacity, s) : effect.From.Opacity;
            _tiltDeg = effect.Tilt ? DesignThemeResolver.Number("depth.tiltDeg") : 0;
            var group = new TransformGroup { Children = { _scale, _move } };
            if (effect.Tilt)
            {
                _tilt = new Rotate3DTransform { Depth = DesignThemeResolver.Number("depth.perspective") };
                group.Children.Add(_tilt);
            }

            if (before is Transform bt)
            {
                group.Children.Add(bt);
            }

            target.RenderTransform = group;
            target.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        public Control Target { get; }

        public MotionEffect Effect { get; }

        public ITransform? Before { get; }

        public double BeforeOpacity { get; }

        public Geometry? BeforeClip { get; }

        public bool Done { get; private set; }

        /// <summary>마지막 Step 의 상태(원격 motion.probe 표본).</summary>
        public (double Opacity, double Scale, double X, double Y, double Angle, Rect? Clip) Last { get; private set; }

        public void Step(double t)
        {
            if (Done)
            {
                return;
            }

            var p = _easing.Ease(Math.Clamp(t, 0, 1));
            var from = Effect.From;
            var to = Effect.To;
            var size = SizeOf(Target);
            var opacity = Math.Clamp(Lerp(_fromOpacity, to.Opacity, p), 0, 1); // 넘침 곡선(BackEaseOut)은 위치에만 — 불투명도는 범위 안
            var scale = Lerp(from.Scale, to.Scale, p);
            var x = Lerp(from.X, to.X, p) * size.Width;
            var y = Lerp(from.Y, to.Y, p) * size.Height;
            _scale.ScaleX = _scale.ScaleY = scale;
            _move.X = x;
            _move.Y = y;
            Target.Opacity = BeforeOpacity * opacity;
            var angle = 0.0;
            if (_tilt is not null)
            {
                // 들어옴은 설정 각도 → 0, 나감은 0 → -설정 각도(기존 DepthTransitions 열기·닫기와 같은 방향)
                angle = Effect.Direction == MotionDirection.Enter ? _tiltDeg * (1 - p) : -_tiltDeg * p;
                _tilt.AngleY = angle;
            }

            Rect? clipRect = null;
            if (Effect.Clip != MotionClip.None)
            {
                var reveal = Effect.Direction == MotionDirection.Enter ? p : 1 - p;
                var shape = ClipGeometry(Effect.Clip, size, reveal);
                clipRect = shape.Bounds;
                Target.Clip = BeforeClip is null ? shape : new CombinedGeometry(GeometryCombineMode.Intersect, BeforeClip, shape);
            }

            Last = (opacity, scale, x, y, angle, clipRect);
        }

        private bool _marked;

        /// <summary>진단 구간 시작(Play 가 첫 프레임을 걸기 직전). 재생 동작·타이밍은 바꾸지 않는다.</summary>
        public void MarkStart()
        {
            _marked = true;
            Target.DetachedFromVisualTree += OnTargetDetached;
            if (++s_marked == 1)
            {
#if PD_DIAGNOSTICS // 제품판 PerfProbe 에는 NoteInput 이 없다
                Pd.Poc.Shell.Diagnostics.PerfProbe.Current.NoteInput(); // 재생 시작 = 입력 사건(시작 뒤 첫 커밋 측정)
#endif
                Pd.Poc.Shell.Diagnostics.PerfProbe.Current.BeginPhase(PlayPhase);
            }
        }

        // 재생당 1회 — 끝·중단(Finish)과 창 닫힘(떼어짐, 프레임이 더 오지 않을 수 있음) 중 먼저 온 쪽
        private void MarkEnd()
        {
            if (!_marked)
            {
                return;
            }

            _marked = false;
            Target.DetachedFromVisualTree -= OnTargetDetached;
            if (--s_marked == 0)
            {
                Pd.Poc.Shell.Diagnostics.PerfProbe.Current.BeginPhase(null);
            }
        }

        private void OnTargetDetached(object? sender, VisualTreeAttachmentEventArgs e) => MarkEnd();

        public void Finish()
        {
            if (Done)
            {
                return;
            }

            Done = true;
            MarkEnd();
            Target.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            if (s_running.TryGetValue(Target, out var current) && ReferenceEquals(current, this))
            {
                s_running.Remove(Target);
            }

            Target.RenderTransform = Before;
            Target.Opacity = BeforeOpacity;
            if (Effect.Clip != MotionClip.None)
            {
                Target.Clip = BeforeClip;
            }
        }

        /// <summary>전 값으로 모두 돌아왔는가(같은 객체 비교, 원격 motion.probe restored).</summary>
        public bool Restored() =>
            ReferenceEquals(Target.RenderTransform, Before) && Target.Opacity.Equals(BeforeOpacity) && ReferenceEquals(Target.Clip, BeforeClip);

        private void OnPressed(object? sender, PointerPressedEventArgs e) => Finish();

        private static double Lerp(double a, double b, double p) => a + ((b - a) * p);

        /// <summary>대상 크기. 덮개에 막 붙어 아직 배치 전인 그림(닫기·짝 나감)은 Bounds 가 0 이라 지정 Width·Height 를 쓴다.</summary>
        private static Size SizeOf(Control c) =>
            c.Bounds.Width > 0 ? c.Bounds.Size : new Size(double.IsNaN(c.Width) ? 0 : c.Width, double.IsNaN(c.Height) ? 0 : c.Height);
    }

    /// <summary>찍어 둔 화면을 호스트 덮개(AdornerLayer)의 같은 자리에 놓고 나감 효과를 재생한 뒤 뗀다.</summary>
    private static void Ghost(Snapshot shot, MotionDirection direction, string id, SwitchNote note)
    {
        if (AdornerLayer.GetAdornerLayer(shot.Host) is not { } layer)
        {
            note.Ghost = "none";
            shot.Image.Dispose();
            return;
        }

        note.Ghost = "started";

        var ghost = new Canvas { IsHitTestVisible = false };
        var image = new SnapshotPicture(shot.Image, shot.Geometry); // Image 는 화소 원본을 DIP 로 넘겨 배율만큼 커진다
        Canvas.SetLeft(image, shot.Bounds.X);
        Canvas.SetTop(image, shot.Bounds.Y);
        ghost.Children.Add(image);
        AdornerLayer.SetAdornedElement(ghost, shot.Host);
        layer.Children.Add(ghost);
        Play(image, direction, id, null, () =>
        {
            note.Ghost = "done";
            layer.Children.Remove(ghost);
            shot.Image.Dispose();
        });
    }

    /// <summary>
    /// 잘라냄 모양. reveal = 보이는 비율(0 = 전부 가림, 1 = 전부 보임). wipeLeft 는 오른쪽 끝에서 왼쪽으로 드러나고(나머지 방향도 이름 방향으로 드러남),
    /// split 은 가운데에서 좌우로, circle 은 가운데 원이 대각선 반만큼 커진다.
    /// </summary>
    internal static Geometry ClipGeometry(MotionClip clip, Size size, double reveal)
    {
        var w = size.Width;
        var h = size.Height;
        var r = Math.Clamp(reveal, 0, 1);
        return clip switch
        {
            MotionClip.WipeLeft => new RectangleGeometry(new Rect(w * (1 - r), 0, w * r, h)),
            MotionClip.WipeRight => new RectangleGeometry(new Rect(0, 0, w * r, h)),
            MotionClip.WipeUp => new RectangleGeometry(new Rect(0, h * (1 - r), w, h * r)),
            MotionClip.WipeDown => new RectangleGeometry(new Rect(0, 0, w, h * r)),
            MotionClip.Split => new RectangleGeometry(new Rect(w * (1 - r) / 2, 0, w * r, h)),
            MotionClip.Circle => Circle(w, h, r),
            _ => new RectangleGeometry(new Rect(0, 0, w, h)),
        };
    }

    private static Geometry Circle(double w, double h, double r)
    {
        var radius = r * Math.Sqrt((w * w) + (h * h)) / 2;
        return new EllipseGeometry(new Rect((w / 2) - radius, (h / 2) - radius, radius * 2, radius * 2));
    }

    /// <summary>표의 가속 곡선 이름(Avalonia Easing 이름)을 읽는다. 모르는 이름은 선형.</summary>
    private static Easing EasingOf(string name)
    {
        try
        {
            return Easing.Parse(name);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            return new LinearEasing();
        }
    }
}

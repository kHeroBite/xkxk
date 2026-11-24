// 문서 창 열기·닫기 원근 기울기 전환과 내용 바뀔 때 페이지 뒤집기(REQ-104, 18 문서 §3D) — Rotate3DTransform AngleY 를 설정 각도에서 0 으로, 끝나면 RenderTransform 을 전환 전 값으로 복원
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Pd.Poc.Shell.Theming;

namespace Pd.Ui.Views.Depth;

/// <summary>
/// 각도(theme.style.depth.tiltDeg)·원근(theme.style.depth.perspective)·시간(theme.style.motion.&lt;값&gt;.ms)은 모두 DesignThemeResolver 로 읽는다(REQ-99, review M-3).
/// 실효값이 꺼짐이거나 폴백(소프트웨어 렌더·움직임 줄이기·창 수 초과, DesignThemeResolver.FallbackReason)이면 전환을 생략한다.
/// 열기 = MDI·관리형 문서 창(MdiDocumentWindow)이 같은 문서로 처음 화면에 붙을 때, 닫기 = Dock 의 DockableClosing 직전 모습을 찍어 덮개에서 기울이며 사라지게 한다(실제 닫기는 늦추지 않는다).
/// 끝난 뒤 RenderTransform 은 null 이 아니라 전환 전 값(같은 객체)으로 되돌린다(review 낮음). 전환 전 값이 Transform 이 아니면(TransformOperations 등) 합성할 수 없어 생략한다.
/// </summary>
public static partial class DepthTransitions
{
    /// <summary>내용(Content)이 바뀔 때 뒤집기 전환을 할 컨테이너 표시(첨부 bool, 계약 이름 고정).</summary>
    public static readonly AttachedProperty<bool> PageFlipProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("PageFlip", typeof(DepthTransitions));

    private const string OpenKey = "depth.open";
    private const string OpenOn = "tilt";
    private const string PageKey = "depth.page";
    private const string PageOn = "flip3d";
    private const string ContentPropertyName = "Content";

    /// <summary>원격 depth.probe 표본 구간 수(0~1 을 이만큼 나눠 각 경계에서 AngleY 를 읽는다). 진단 전용.</summary>
    private const int ProbeSegments = 4;

    private static readonly Dictionary<Control, Tilt> s_running = new();
    private static readonly ConditionalWeakTable<object, object> s_seenWindows = new();
    private static readonly List<WeakReference<Control>> s_pageHosts = new();
    private static readonly object s_marker = new();
    private static WeakReference<Visual>? s_root;
    private static IFactory? s_factory;
    private static bool s_windowHooked;

    static DepthTransitions()
    {
        PageFlipProperty.Changed.AddClassHandler<Control, bool>(OnPageFlipChanged);
    }

    public static bool GetPageFlip(Control element) => element.GetValue(PageFlipProperty);

    public static void SetPageFlip(Control element, bool value) => element.SetValue(PageFlipProperty, value);

    /// <summary>시작 때 1회(MainView): 문서 창 열림 감시(클래스 처리기)와 닫기 직전 알림(DockableClosing)을 건다. root = 기준 화면(덮개·표본 대상 탐색 시작점).</summary>
    public static void Attach(IFactory factory, Visual root)
    {
        s_root = new WeakReference<Visual>(root);
        if (!s_windowHooked)
        {
            s_windowHooked = true;
            Control.LoadedEvent.AddClassHandler<MdiDocumentWindow>(OnWindowLoaded);
        }

        if (s_factory is not null)
        {
            s_factory.DockableClosing -= OnDockableClosing;
        }

        s_factory = factory;
        factory.DockableClosing += OnDockableClosing;
        WindowTransitions.Attach(factory, root); // 열기·닫기·전환 움직임 효과 재생기(REQ-112)
        DepthPrewarm.Attach(root); // 기울기 첫 렌더 비용을 유휴 때 화면 밖에서 미리(K13)
    }

    /// <summary>열기 기울기가 지금 실제로 돌 조건인가(DepthPrewarm 이 예열 여부를 같은 판단으로 정한다). MDI 겹침 열기 효과가 tilt3d 인가로 판단한다.</summary>
    internal static bool OpenAllowed() => WindowTransitions.TiltOpenAllowed();

    /// <summary>열기 전환 첫 프레임과 같은 3D 회전(각도·원근 = 설정값). 예열 견본용.</summary>
    internal static Rotate3DTransform OpenStartTransform() =>
        new() { AngleY = TiltDeg(), Depth = DesignThemeResolver.Number("depth.perspective") };

    /// <summary>
    /// 원격 depth.probe(kind=open|page) — 실제 전환 코드를 동기로 단계 진행해 AngleY 표본을 모으고, 끝 뒤 RenderTransform 이 전 값(같은 객체)인지 본다.
    /// 반환 JSON {"samples":[{"angleY":n}],"restored":bool}. 꺼짐·폴백·대상 없음이면 표본 0개.
    /// </summary>
    public static string Probe(string kind)
    {
        var page = string.Equals(kind, "page", StringComparison.OrdinalIgnoreCase);
        var target = page ? LivePageHost() ?? OpenTarget() : OpenTarget();
        var samples = new List<double>();
        var restored = true;
        if (target is not null && Allowed(page ? PageKey : OpenKey, page ? PageOn : OpenOn) && Begin(target, TiltDeg(), 0) is { } tilt)
        {
            for (var i = 0; i <= ProbeSegments; i++)
            {
                tilt.Step((double)i / ProbeSegments);
                samples.Add(tilt.Rot.AngleY);
            }

            tilt.Finish();
            restored = ReferenceEquals(target.RenderTransform, tilt.Before);
        }

        return "{\"samples\":[" + string.Join(",", samples.Select(a => "{\"angleY\":" + a.ToString("0.###", CultureInfo.InvariantCulture) + "}"))
            + "],\"restored\":" + (restored ? "true" : "false") + "}";
    }

    /// <summary>효과가 켜져 있고(실효값 = on) 폴백이 없고 시간·각도가 0 보다 크면 true.</summary>
    private static bool Allowed(string key, string on) =>
        DesignThemeResolver.Effective(key) == on && DesignThemeResolver.FallbackReason is null && DesignThemeResolver.MotionMs() > 0 && TiltDeg() > 0;

    private static double TiltDeg() => DesignThemeResolver.Number("depth.tiltDeg");

    private static void OnPageFlipChanged(Control host, AvaloniaPropertyChangedEventArgs<bool> e)
    {
        host.PropertyChanged -= OnPageHostPropertyChanged;
        s_pageHosts.RemoveAll(w => !w.TryGetTarget(out var c) || ReferenceEquals(c, host));
        if (e.NewValue.GetValueOrDefault())
        {
            host.PropertyChanged += OnPageHostPropertyChanged;
            s_pageHosts.Add(new WeakReference<Control>(host));
        }
    }

    private static void OnPageHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 처음 내용이 채워질 때는 뒤집지 않는다(바뀔 때만)
        if (e.Property.Name == ContentPropertyName && e.OldValue is not null && sender is Control { IsLoaded: true } host && Allowed(PageKey, PageOn))
        {
            Run(host, TiltDeg(), 0, null, null);
        }
    }

    /// <summary>닫기 효과(기울기 포함 motion.close 전 효과)는 창 움직임 재생기가 배치 상태별로 맡는다(REQ-112).</summary>
    private static void OnDockableClosing(object? sender, DockableClosingEventArgs e) => WindowTransitions.OnDockableClosing(e);

    /// <summary>기울기 전환을 시작해 화면 프레임마다 진행한다. 시작할 수 없으면(합성 불가·화면 밖) onEnd 만 바로 부른다.</summary>
    private static void Run(Control target, double from, double to, Action<double>? onStep, Action? onEnd)
    {
        var ms = (double)DesignThemeResolver.MotionMs();
        var top = TopLevel.GetTopLevel(target);
        if (top is null || ms <= 0 || Begin(target, from, to) is not { } tilt)
        {
            onEnd?.Invoke();
            return;
        }

        var clock = Stopwatch.StartNew();
        void Frame(TimeSpan _)
        {
            if (tilt.Done)
            {
                onEnd?.Invoke();
                return;
            }

            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
            tilt.Step(t);
            onStep?.Invoke(t);
            if (t >= 1)
            {
                tilt.Finish();
                onEnd?.Invoke();
                return;
            }

            top.RequestAnimationFrame(Frame);
        }

        top.RequestAnimationFrame(Frame);
    }

    /// <summary>대상에 회전을 건다(진행 중이던 전환은 먼저 끝내 원래 값으로 돌린다). 전환 전 값이 Transform 이 아니면 null.</summary>
    private static Tilt? Begin(Control target, double from, double to)
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

        var rot = new Rotate3DTransform { AngleY = from, Depth = DesignThemeResolver.Number("depth.perspective") };
        target.RenderTransform = before is Transform bt ? new TransformGroup { Children = { rot, bt } } : rot;
        var tilt = new Tilt(target, before, rot, from, to);
        s_running[target] = tilt;
        return tilt;
    }

    private static Visual? Root() => s_root is not null && s_root.TryGetTarget(out var r) ? (Visual?)TopLevel.GetTopLevel(r) ?? r : null;

    /// <summary>열기 표본 대상: 화면에 보이는 문서 창 중 위(나중) 것, 없으면(모바일형 등) 기준 화면.</summary>
    private static Control? OpenTarget() =>
        Root()?.GetVisualDescendants().OfType<MdiDocumentWindow>().LastOrDefault(w => w.IsEffectivelyVisible && w.Bounds.Width > 0)
        ?? (s_root is not null && s_root.TryGetTarget(out var r) ? r as Control : null);

    private static Control? LivePageHost() =>
        s_pageHosts.Select(w => w.TryGetTarget(out var c) ? c : null).FirstOrDefault(c => c is { IsEffectivelyVisible: true } && TopLevel.GetTopLevel(c) is not null);

    /// <summary>진행 중 전환 1건. Step(t) 는 cubic ease-out 으로 AngleY 를 옮기고, Finish 는 RenderTransform 을 전 값으로 되돌린다(여러 번 불러도 된다).</summary>
    private sealed record Tilt(Control Target, ITransform? Before, Rotate3DTransform Rot, double From, double To)
    {
        public bool Done { get; private set; }

        public void Step(double t) => Rot.AngleY = From + ((To - From) * (1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3)));

        public void Finish()
        {
            if (Done)
            {
                return;
            }

            Done = true;
            if (s_running.TryGetValue(Target, out var current) && ReferenceEquals(current, this))
            {
                s_running.Remove(Target);
            }

            Target.RenderTransform = Before;
        }
    }
}

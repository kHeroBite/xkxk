// LiveCharts2 차트를 창 끌기 동안 비트맵 스냅샷으로 대신 보이게 하는 변형 L3 — 데스크톱(Windows) 전용
// 초안(odev-gm, 2026-10-30, 컴파일 확인 전, 빌드 안 함). 대상 경로: testbed/xkxk.frameworks.ui.testbed/Bench/Dash.LiveCharts.DragSnapshot.cs
// 방식: 끌기 시작에 각 차트를 RenderTargetBitmap 으로 한 장 찍어 차트를 감싼 Border 의 Background(ImageBrush)에 붙이고 차트는 IsVisible=false 로 숨긴다. 끝나면 되돌린다.
//   차트를 시각 트리에서 떼지 않는다(떼면 재부착 때 진입 애니가 다시 돌 수 있다 — 추정). 숨김이 LiveCharts 내부 크기 이벤트를 일으키는지는 확인 필요.
// 범위: 웹(WASM)·Android 는 지원 확인 못 함 → OperatingSystem.IsBrowser()/IsAndroid() 면 아무것도 하지 않는다.
//   근거: Avalonia 문서는 RenderTargetBitmap.Render 가 창에 붙은 컨트롤을 요구한다고만 적고 브라우저(WASM) 지원은 언급이 없다
//   (docs.avaloniaui.net/docs/graphics-animation/custom-rendering, 확인 못 함 → 데스크톱 전용 변형으로 좁힘).
// 호출: DashDragGate.SetDragging 이 Enabled 와 별개로 DashDragSnapshot.Enabled 를 보고 Begin/End 를 부른다(기본 false → L0~L2 무영향).
#if PD_CHART_LIVECHARTS
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace xkxk.frameworks.ui.testbed.Bench;

public static class DashDragSnapshot
{
    private sealed record Held(Border Cell, Control Chart, IBrush? OldBackground, RenderTargetBitmap Bitmap);

    private static readonly List<Held> s_held = new();

    public static bool Enabled { get; set; }

    // 끌기 시작: 캡처 시간(ms)을 돌려준다. 결과 JSON 에 남겨 첫 프레임 지연과 구분한다
    public static double Begin(IEnumerable<Control> charts)
    {
        if (!Enabled || OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid()) { return 0; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var c in charts)
        {
            if (c.Parent is not Border cell || c.Bounds.Width < 1 || c.Bounds.Height < 1) { continue; }
            var scale = TopLevel.GetTopLevel(c)?.RenderScaling ?? 1.0;
            var size = new PixelSize(Math.Max(1, (int)(c.Bounds.Width * scale)), Math.Max(1, (int)(c.Bounds.Height * scale)));
            var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bmp.Render(c);
            s_held.Add(new Held(cell, c, cell.Background, bmp));
        }

        // 모두 찍은 뒤 한꺼번에 바꾼다(찍는 도중 숨겨지는 차트가 없게)
        foreach (var h in s_held)
        {
            h.Cell.Background = new ImageBrush(h.Bitmap) { Stretch = Stretch.Fill };
            h.Chart.IsVisible = false;
        }

        return sw.Elapsed.TotalMilliseconds;
    }

    public static void End()
    {
        foreach (var h in s_held)
        {
            h.Chart.IsVisible = true;
            h.Cell.Background = h.OldBackground;
            h.Bitmap.Dispose();
        }

        s_held.Clear();
    }
}
#endif

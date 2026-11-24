// 원격 render.overlay(진단) — Avalonia 렌더러 디버그 오버레이(다시 그린 영역 등)를 켜고 끈다. 끌기 중 정지 창이 매 프레임 다시 그려지는지 캡처로 가르는 용도(D-1, 제품 빌드 제외)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Rendering;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// TopLevel.RendererDiagnostics.DebugOverlays(Avalonia 공개 API)를 바꾼다. dirtyRects 를 켜면 프레임마다 다시 그린 영역이 화면에 표시된다.
/// 오버레이가 켜지면 렌더 경로가 전체 화면 층을 쓰도록 바뀔 수 있어(ServerCompositionTarget 의 _overlays.RequireLayer, 추정) 켠 실행의 시간 값은 판정에 쓰지 않는다. 캡처 관찰 전용이다.
/// 설계 근거: /home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/work/odev-11_next_design.md §2 D-1.
/// </summary>
internal static class RemoteRenderOverlayCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("render.overlay", "렌더러 디버그 오버레이 dirtyRects=true|false fps=true|false(준 값만 설정, 생략=조회, 이번 실행만·시간 판정 미사용) → {dirtyRects,fps,value}", a =>
        {
            var diag = TopLevel.GetTopLevel(VisualTreeInspector.Main())?.RendererDiagnostics
                ?? throw new RemoteCommandException("메인 창이 아직 없습니다.");
            var v = diag.DebugOverlays;
            if (a.ContainsKey("dirtyRects")) { v = Flag(v, RendererDebugOverlays.DirtyRects, a.Bool("dirtyRects")); }
            if (a.ContainsKey("fps")) { v = Flag(v, RendererDebugOverlays.Fps, a.Bool("fps")); }
            diag.DebugOverlays = v;
            return new JsonObject
            {
                ["dirtyRects"] = v.HasFlag(RendererDebugOverlays.DirtyRects), ["fps"] = v.HasFlag(RendererDebugOverlays.Fps), ["value"] = v.ToString(),
            };
        });
    }

    private static RendererDebugOverlays Flag(RendererDebugOverlays v, RendererDebugOverlays f, bool on) => on ? v | f : v & ~f;
}
#endif

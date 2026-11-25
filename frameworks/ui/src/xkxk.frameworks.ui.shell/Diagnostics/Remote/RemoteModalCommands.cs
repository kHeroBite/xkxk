// 원격 명령(설정 창 앱 모달 판정 B3) — modal.state: ModalScope 활성·이름과 최상위 창별 앱 관문 차단·OS 활성(IsWindowEnabled) 상태
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령이 없다
using xkxk.frameworks.ui.shell.Platform;
using System;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Dock.Avalonia.Controls;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// modal.state 는 읽기만 한다(창 상태·설정을 바꾸지 않는다). 창 목록은 데스크톱 수명이면 lifetime.Windows, 단일 뷰(웹·Android)면 TopLevels().
/// osEnabled 는 호스트가 채우는 <see cref="OsEnabledProbe"/>(데스크톱 Win32 IsWindowEnabled) 값이고, 훅이 없으면 null 이다.
/// </summary>
public static class RemoteModalCommands
{
    /// <summary>OS 수준 창 활성 여부(데스크톱 Win32ModalBlock 이 설치). null 반환 = 모름.</summary>
    public static Func<TopLevel, bool?>? OsEnabledProbe { get; set; }

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("modal.state", "앱 모달 상태(B3): {active,name,windows[{kind:main|sdi|system,title,gateBlocked(ModalScope.IsBlocked),osEnabled(Win32 IsWindowEnabled, 훅 없으면 null),avaloniaEnabled,visible}],blockedCount,othersAllBlocked(모달 중 메인·SDI 전부 gateBlocked),allOsEnabled(닫은 뒤 복구 확인용),probe}. 읽기 전용", _ => State());
    }

    private static JsonObject State()
    {
        var list = new JsonArray();
        var blocked = 0;
        var othersAll = true;
        var others = 0;
        bool? allOs = true;
        foreach (var (top, kind) in Windows())
        {
            var gate = ModalScope.IsBlocked(top);
            var os = OsEnabledProbe?.Invoke(top);
            if (gate) { blocked++; }
            if (kind is "main" or "sdi")
            {
                others++;
                othersAll &= gate;
            }

            allOs = os is null ? null : allOs is null ? null : allOs.Value && os.Value;
            list.Add(new JsonObject
            {
                ["kind"] = kind,
                ["title"] = (top as Window)?.Title,
                ["gateBlocked"] = gate,
                ["isModal"] = ReferenceEquals(ModalScope.ActiveModal, top),
                ["platformBlocked"] = ModalScope.IsPlatformBlocked(top), // 범위가 OS 훅으로 막은 창(ShowDialog owner 는 Avalonia 몫이라 false)
                ["osEnabled"] = os,
                ["avaloniaEnabled"] = top.IsEnabled,
                ["visible"] = top.IsVisible,
            });
        }

        return new JsonObject
        {
            ["active"] = ModalScope.IsActive,
            ["name"] = ModalScope.ActiveName,
            ["windows"] = list,
            ["blockedCount"] = blocked,
            ["othersAllBlocked"] = others > 0 && othersAll,
            ["allOsEnabled"] = allOs,
            ["probe"] = OsEnabledProbe is not null,
            ["rawGateInstalled"] = ModalScope.RawGateInstalled, // 원시 입력 관문(IInputManager.PreProcess 구독) 설치 여부 — 웹 트리밍 뒤 확인용
            ["pid"] = Environment.ProcessId,
        };
    }

    private static System.Collections.Generic.IEnumerable<(TopLevel Top, string Kind)> Windows()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
        {
            foreach (var w in lt.Windows)
            {
                yield return (w, ReferenceEquals(w, lt.MainWindow) ? "main" : w is HostWindow ? "sdi" : "system");
            }

            yield break;
        }

        var first = true;
        foreach (var top in VisualTreeInspector.TopLevels())
        {
            yield return (top, first ? "main" : "system"); // 단일 뷰: 첫 TopLevel 이 메인(설정은 같은 TopLevel 안 플라이아웃)
            first = false;
        }
    }
}
#endif

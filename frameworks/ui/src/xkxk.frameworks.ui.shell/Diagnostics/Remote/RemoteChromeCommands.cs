// 원격 명령(문서 창 제목줄 Mars 9버튼 UI-09 — SDI·항상 위·중복 허용·동기화·캐시/새로고침·도움말·그림자·외곽선) — WindowCommands.Chrome(odev-7c) 을 그대로 호출
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품 빌드에는 원격 명령 코드가 들어가지 않는다
using System.Text.Json.Nodes;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>문서 창 크롬(고정·잠금·SDI·중복·캐시) 원격 명령 군(옛 RemoteCommands.Chrome). RemoteCommandRouter 가 Register 를 부른다.</summary>
internal static class RemoteChromeCommands
{
    // 공용 멤버 전달자(W2) — 본문은 옛 이름 그대로 쓴다.
    private static xkxk.frameworks.ui.shell.Docking.IShellCommands Shell => RemoteShellAccess.Shell;

    private static xkxk.frameworks.ui.shell.Docking.PocDocument Doc(JsonObject a, string key = "doc") => RemoteShellAccess.Doc(a, key);

    private static WindowCommands Chrome => Shell as WindowCommands
        ?? throw new RemoteCommandException("셸 구현이 WindowCommands 가 아니라 제목줄 명령을 쓸 수 없습니다.");

    private static JsonObject ChromeJson(PocDocument d)
    {
        var c = WindowCommands.ChromeOf(d);
        return new JsonObject
        {
            ["doc"] = d.Id, ["title"] = d.Title, ["floating"] = Chrome.IsFloating(d), ["pinned"] = c.IsPinned,
            ["allowDuplicate"] = c.AllowDuplicate, ["sync"] = c.SyncEnabled,
            ["cacheHits"] = c.CacheHits, ["cacheTotal"] = c.CacheTotal, ["locked"] = c.IsLocked,
            // 위치 고정 확인용: MDI 위치·크기, SDI 실 창의 크기 조절 가능 여부
            ["mdiBounds"] = new JsonObject { ["x"] = d.MdiBounds.X, ["y"] = d.MdiBounds.Y, ["w"] = d.MdiBounds.Width, ["h"] = d.MdiBounds.Height },
            ["canResize"] = VisualTreeInspector.TopLevelOf(d) is Avalonia.Controls.Window w && Chrome.IsFloating(d) ? w.CanResize : null,
            // 위치 고정 해제 뒤 끌기 진단: 문서 모델 끌기 허용(Dock 이 누를 때마다 DockCapabilityResolver 로 읽는다 — 고정이 이 값을 바꾸면 안 된다)
            ["canDrag"] = d.CanDrag,
        };
    }

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("chrome.state", "제목줄 상태 [doc] → floating·pinned·allowDuplicate·sync·cacheHits·cacheTotal·locked·mdiBounds·canResize(SDI)", a => ChromeJson(Doc(a)));
        r.Register("chrome.sdi", "제목줄 MDI↔SDI 버튼 [doc] → 상태", a => { var d = Doc(a); Chrome.ChromeToggleSdi(d); return ChromeJson(d); });
        r.Register("chrome.pin", "제목줄 항상 위 버튼 [doc] → 상태", a => { var d = Doc(a); Chrome.ChromeTogglePin(d); return ChromeJson(d); });
        r.Register("chrome.dup", "제목줄 중복 허용 버튼 [doc] → 상태", a => { var d = Doc(a); Chrome.ChromeToggleDuplicate(d); return ChromeJson(d); });
        r.Register("chrome.lock", "제목줄 위치 고정 버튼 [doc] → 상태(locked·mdiBounds·canResize)", a => { var d = Doc(a); Chrome.ChromeToggleLock(d); return ChromeJson(d); });
        r.Register("chrome.sync", "제목줄 동기화 버튼 [doc] → 상태", a => { var d = Doc(a); Chrome.ChromeToggleSync(d); return ChromeJson(d); });
        // 새로고침은 원래 문서를 닫고 새 문서를 연다 → 새 문서 상태(새 doc Id)와 닫힌 Id 를 돌려준다
        r.Register("chrome.refresh", "제목줄 새로고침 [doc] → 새 문서 상태(doc=새 Id, closed=옛 Id)", a => Refreshed(Doc(a), Chrome.ChromeRefresh));
        r.Register("chrome.cache", "제목줄 캐시 버튼(=새로고침, 원래 문서는 닫힘) [doc] → 새 문서 상태", a => Refreshed(Doc(a), d =>
        {
            var before = new System.Collections.Generic.HashSet<PocDocument>(Shell.Documents);
            Chrome.ChromeToggleCache(d);
            return System.Linq.Enumerable.FirstOrDefault(Shell.Documents, x => !before.Contains(x)) ?? Shell.ActiveDocument ?? d;
        }));
        r.Register("chrome.help", "제목줄 도움말 버튼 문구 [doc]", a => JsonValue.Create(Chrome.ChromeHelpText(Doc(a))));
        r.Register("chrome.shadow", "문서 창 입체 그림자 on=true|false(생략=조회) → 현재 값", a =>
        {
            if (a.ContainsKey("on")) { WindowCommands.ShadowEnabled = a.Bool("on"); }
            return JsonValue.Create(WindowCommands.ShadowEnabled);
        });
        r.Register("chrome.tune",
            "입체감 조절(Mars 비교) 준 값만 설정 spread=0.25~3 opacity=0~3 highlight=0~2 style=Windows|MacOS|MarsClassic|Auto(저장됨) forced=Windows|MacOS|MarsClassic|Auto(저장 안 함, Auto=해제) accentTitle=true|false → 현재 값 전체", a =>
            {
                if (a.EnumArg<ChromeStyleKind>("forced") is { } fs) { ChromePlatform.ForcedStyle = fs; } // 검수용(웹은 호스트 인자가 없다)
                if (a.Num("spread") is { } sp) { WindowCommands.ShadowSpread = sp; }
                if (a.Num("opacity") is { } op) { WindowCommands.ShadowOpacity = op; }
                if (a.Num("highlight") is { } hl) { WindowCommands.HighlightThickness = hl; }
                if (a.EnumArg<ChromeStyleKind>("style") is { } st) { WindowCommands.ChromeStyle = st; }
                if (a.ContainsKey("accentTitle")) { WindowCommands.AccentTitleBar = a.Bool("accentTitle"); }
                return new JsonObject
                {
                    ["spread"] = WindowCommands.ShadowSpread, ["opacity"] = WindowCommands.ShadowOpacity,
                    ["highlight"] = WindowCommands.HighlightThickness, ["style"] = WindowCommands.ChromeStyle.ToString(),
                    ["forced"] = ChromePlatform.ForcedStyle?.ToString(), ["effective"] = WindowCommands.EffectiveStyle.ToString(), ["hostApple"] = ChromePlatform.HostIsApple,
                    ["accentTitle"] = WindowCommands.AccentTitleBar, ["shadow"] = WindowCommands.ShadowEnabled, ["mars"] = WindowCommands.MarsFrame,
                };
            });
        r.Register("chrome.frame", "외곽선 mars=true(Mars 1px)|false(PD 3D 2px)(생략=조회) → 현재 값", a =>
        {
            if (a.ContainsKey("mars")) { WindowCommands.MarsFrame = a.Bool("mars"); }
            return JsonValue.Create(WindowCommands.MarsFrame);
        });
    }

    private static JsonObject Refreshed(PocDocument old, System.Func<PocDocument, PocDocument> refresh)
    {
        var oldId = old.Id;
        var fresh = refresh(old);
        var o = ChromeJson(fresh);
        o["closed"] = oldId;
        return o;
    }
}
#endif

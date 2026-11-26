// 원격 명령(효과 성능 측정용) — background.reload·style.moveShadow.set·docs.activateFrom (PERF-13·15·16·17)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Text.Json.Nodes;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>측정에 필요한 조작만 낸다. 값 쓰기는 화면과 같은 설정 경로이고 적용 여부는 실효값으로 돌려준다.</summary>
internal static class RemoteEffectPerfCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("background.reload", "배경 이미지 캐시를 비우고 다시 읽어 적용(PERF-15 표본 1건) → {started,file}", _ =>
        {
            var file = UiSettings.Current.BackgroundImageFile ?? throw new RemoteCommandException("배경 이미지가 선택되지 않았습니다.");
            DesignEffectsApplier.ResetBackgroundImage();
            return new JsonObject { ["started"] = true, ["file"] = file };
        });
        r.Register("style.moveShadow.set", "끌기 중 그림자 사용자 값 value=hide|keep(생략=고르지 않음) → {user,effective}", a =>
        {
            var value = a.Str("value");
            UiSettings.Current.MdiMoveLightShadowUser = value switch
            {
                null or "" => null,
                "hide" => MoveShadowMode.Hide,
                "keep" => MoveShadowMode.Keep,
                _ => throw new RemoteCommandException("value=hide|keep"),
            };
            return new JsonObject
            {
                ["user"] = UiSettings.Current.MdiMoveLightShadowUser is { } u ? MdiMoveLight.ShadowKey(u) : null,
                ["effective"] = MdiMoveLight.ShadowKey(MdiMoveLight.Shadow),
            };
        });
        r.Register("docs.activateFrom", "출처를 붙여 활성화 doc= origin=Keyboard|TitleTab|WindowList|BodyClick(전환 효과 측정용) → {active,origin}", a =>
        {
            var commands = XkxkUi.Current.MainViewModel?.Commands ?? throw new RemoteCommandException("셸이 없습니다.");
            var doc = RemoteShellAccess.Doc(a, "doc");
            var origin = a.EnumArg<ActivationOrigin>("origin") ?? throw new RemoteCommandException("origin=Keyboard|TitleTab|WindowList|BodyClick 이 필요합니다.");
            commands.Activate(doc, origin);
            return new JsonObject { ["active"] = commands.ActiveDocument?.Id, ["origin"] = origin.ToString() };
        });
    }
}
#endif

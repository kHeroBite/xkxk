// 원격 명령(UI-11 레이아웃 라이브러리 — 자동 저장 슬롯·개인 레이아웃 저장/불러오기/이름 변경/삭제·템플릿, 윈도우 메뉴 ⑪⑫) — App.Layouts(ILayoutLibrary, odev-2) 를 그대로 호출
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using xkxk.frameworks.ui.data.Layout;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>개인 레이아웃·템플릿·백업 원격 명령 군(옛 RemoteCommands.Layouts). RemoteCommandRouter 가 Register 를 부른다.</summary>
internal static class RemoteLayoutsCommands
{
    // 공용 멤버 전달자(W2) — 본문은 옛 이름 그대로 쓴다.
    private static IShellCommands Shell => RemoteShellAccess.Shell;

    private static ILayoutLibrary Layouts => App.Layouts ?? throw new RemoteNotReadyException("no-layouts", "레이아웃 라이브러리(App.Layouts)가 아직 없습니다.");

    /// <summary>저장·이름 변경·삭제 결과. 거절도 ok:true 로 보내고 status 로 판정한다(리드 원격 규칙).</summary>
    private static JsonObject ResultJson(LayoutSaveResult r) => new() { ["status"] = r.Status.ToString(), ["id"] = r.Id, ["ok"] = r.Ok };

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("layout.list", "자동 저장 슬롯(id=auto, 있을 때만)+개인 레이아웃: id·name·kind·updatedAt·docCount·readOnly", async _ =>
        {
            var list = await Layouts.ListAsync();
            return new JsonArray(list.Select(e => (JsonNode?)new JsonObject
            {
                ["id"] = e.Id, ["name"] = e.Name, ["kind"] = e.Kind.ToString(), ["updatedAt"] = e.UpdatedAt.ToString("o"),
                ["docCount"] = e.DocumentCount, ["readOnly"] = e.IsReadOnly,
            }).ToArray());
        });
        r.Register("layout.defaultName", "새 저장 기본 이름(현재 시각 yyyy-MM-dd HH:mm:ss)", _ => JsonValue.Create(Layouts.DefaultName()));
        r.Register("layout.save",
            "⑪ 인자 없음 = 현재 화면을 자동 저장 슬롯에 저장(종료 자동 저장과 같은 성격) → true / name= [overwrite=true] = 개인 레이아웃 저장 → {status,id}", async a =>
            {
                if (!a.ContainsKey("name"))
                {
                    await Shell.SaveLayoutAsync();
                    return JsonValue.Create(true);
                }

                return ResultJson(await Layouts.SaveCurrentAsync(a.Str("name"), a.Bool("overwrite")));
            });
        r.Register("layout.load", "인자 없음 = 자동 저장 슬롯 / id= 또는 name= = 개인 레이아웃 → 복원 여부(현재 문서를 닫고 저장 상태로)", async a =>
        {
            var key = a.Str("id") ?? a.Str("name");
            return JsonValue.Create(key is null ? await Shell.LoadLayoutAsync() : await Layouts.LoadAsync(key));
        });
        r.Register("layout.rename", "이름만 변경 id= name= → {status: Saved|NameEmpty|NameExists(덮어쓰기 없음)|ReadOnly|NotFound}", async a =>
            ResultJson(await Layouts.RenameAsync(a.ReqStr("id"), a.Str("name") ?? "")));
        r.Register("layout.delete", "개인 레이아웃 삭제 id= confirm=true(필수) → {status}", async a =>
        {
            if (!a.Bool("confirm")) { throw new RemoteCommandException("삭제는 confirm=true 가 필요합니다."); }
            return ResultJson(await Layouts.DeleteAsync(a.ReqStr("id")));
        });
        r.Register("layout.template.list", "레이아웃 템플릿(읽기 전용): id·name·description", _ =>
            new JsonArray(Layouts.Templates.Select(t => (JsonNode?)new JsonObject
            {
                ["id"] = t.Id, ["name"] = t.Name, ["description"] = t.Description,
            }).ToArray()));
        r.Register("layout.template.apply", "템플릿 적용 id=(열린 문서 유지, 배치만) → 적용 여부", a =>
            JsonValue.Create(Layouts.ApplyTemplate(a.ReqStr("id"))));

        // ⑫ 전체 백업·복원(리드 결정 — 설정+자동 슬롯+개인 레이아웃 묶음, 복원 직전 자동 백업, 재시작 없이 재적용; odev-2 API)
        r.Register("backup.list", "전체 백업 목록(최근 순): id·name·createdAt·layoutCount·isAuto(복원 직전 자동 백업)", async _ =>
        {
            var list = await Layouts.ListBackupsAsync();
            return new JsonArray(list.Select(b => (JsonNode?)new JsonObject
            {
                ["id"] = b.Id, ["name"] = b.Name, ["createdAt"] = b.CreatedAt.ToString("o"),
                ["layoutCount"] = b.LayoutCount, ["isAuto"] = b.IsAuto,
            }).ToArray());
        });
        r.Register("backup.create", "전체 백업 name= [overwrite=true] → {status: Saved|NameEmpty|NameExists|Failed, id}", async a =>
            ResultJson(await Layouts.BackupAllAsync(a.Str("name"), a.Bool("overwrite"))));
        r.Register("backup.restore", "전체 복원 id= confirm=true(필수) — 먼저 현재 상태 자동 백업, 재시작 없이 재적용 → {status: Saved|NotFound|Failed}", async a =>
        {
            if (!a.Bool("confirm")) { throw new RemoteCommandException("복원은 confirm=true 가 필요합니다."); }
            return ResultJson(await Layouts.RestoreAllAsync(a.ReqStr("id")));
        });
        r.Register("backup.delete", "전체 백업 삭제 id= confirm=true(필수, 자동 백업 포함) → {status}", async a =>
        {
            if (!a.Bool("confirm")) { throw new RemoteCommandException("삭제는 confirm=true 가 필요합니다."); }
            return ResultJson(await Layouts.DeleteBackupAsync(a.ReqStr("id")));
        });
    }
}
#endif

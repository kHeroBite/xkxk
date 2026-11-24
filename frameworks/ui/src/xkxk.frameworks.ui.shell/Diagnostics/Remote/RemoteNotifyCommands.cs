// 원격 명령 notify.post — 시험에서 새 알림 1건 이상을 기존 알림 도구 목록에 넣는다(배지·패널은 평소 경로로 반응한다)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true). 제품 빌드에는 명령이 없다
using System;
using System.Text.Json.Nodes;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemoteNotifyCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("notify.post", "새 알림 추가(시험용) [title=] [message=] [category=] [count=1] → {posted, total}", a =>
        {
            var items = RemoteDockAccess.Factory.Notifications.Items;
            var count = Math.Max(1, a.Int("count") ?? 1);
            for (var i = 0; i < count; i++)
            {
                items.Add(new NotificationItem(DateTime.Now, a.Str("category") ?? "test", a.Str("title") ?? "test", a.Str("message") ?? "test"));
            }

            return new JsonObject { ["posted"] = count, ["total"] = items.Count };
        });
    }
}
#endif

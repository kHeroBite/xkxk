// 알림 도구 모델(UI-15) — 다른 도구와 같은 패널 탭이다. 제목은 건수를 포함한다. 문구는 사전 키(UI-16)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>알림 1건(PoC 샘플). 분류·제목·내용은 표시 시점 언어로 만든 문자열이다.</summary>
public sealed record NotificationItem(DateTime Time, string Category, string Title, string Message)
{
    public string TimeText => Time.ToString("HH:mm");
}

/// <summary>
/// 알림 도구. 탐색기·로그와 같이 끌기·메뉴로 옮긴다(PanelMoveService).
/// </summary>
public sealed class NotificationPanelViewModel : Tool
{
    public const string ToolId = "Notifications";

    /// <summary>샘플 알림(분 단위 경과, 사전 키 접미사). 내용은 언어가 바뀌면 다시 만든다.</summary>
    private static readonly (int MinutesAgo, string Kind)[] Samples =
    {
        (2, "approval"), (9, "ticket"), (25, "contract"), (60, "messenger"), (180, "system"),
    };

    private readonly DateTime _seededAt = DateTime.Now;

    /// <summary>실행 중 들어온 알림(새것이 앞). 언어가 바뀌어도 남도록 문구가 아니라 사전 키로 보관한다.</summary>
    private readonly List<(DateTime Time, string CategoryKey, string TitleKey, string MessageKey)> _pushed = new();

    public NotificationPanelViewModel()
    {
        Id = ToolId;

        CanClose = false; // 닫기는 패널 단위다(탐색기·로그와 같다)
        Items.CollectionChanged += (_, _) => UpdateTitle(); // 건수가 바뀌면 제목을 바로 고친다
        Loc.Instance.LanguageChanged += Relocalize;
        Relocalize();
    }

    public ObservableCollection<NotificationItem> Items { get; } = new();

    /// <summary>새 알림 1건을 맨 위에 넣는다. 인자는 모두 사전 키다.</summary>
    internal void Push(string categoryKey, string titleKey, string messageKey)
    {
        var entry = (DateTime.Now, categoryKey, titleKey, messageKey);
        _pushed.Insert(0, entry);
        Items.Insert(0, Localized(entry));
    }

    private static NotificationItem Localized((DateTime Time, string CategoryKey, string TitleKey, string MessageKey) n) =>
        new(n.Time, Loc.Tr(n.CategoryKey), Loc.Tr(n.TitleKey), Loc.Tr(n.MessageKey));

    /// <summary>제목: '알림(5건)'. 0건이면 건수 없이 '알림'이다(0건 표시는 정보가 없고, 목록이 비어 있어 알 수 있다).</summary>
    private void UpdateTitle() => Title = Items.Count > 0 ? Loc.Tr("notify.titleCount", Items.Count) : Loc.Tr("notify.title");

    /// <summary>언어가 바뀌면 제목·샘플 문구를 다시 만든다(같은 시각·순서 유지).</summary>
    private void Relocalize()
    {
        Items.Clear();
        foreach (var pushed in _pushed)
        {
            Items.Add(Localized(pushed));
        }

        foreach (var (minutesAgo, kind) in Samples)
        {
            Items.Add(new NotificationItem(
                _seededAt.AddMinutes(-minutesAgo),
                Loc.Tr("notify.cat." + kind),
                Loc.Tr("notify.sample." + kind + ".title"),
                Loc.Tr("notify.sample." + kind + ".message")));
        }

        UpdateTitle(); // 항목이 0건인 경우 CollectionChanged 가 없다
    }
}

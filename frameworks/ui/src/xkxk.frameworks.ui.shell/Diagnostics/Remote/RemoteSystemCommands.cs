// 원격 명령(UI-17 시스템 메뉴 판정용) — sys.get 상태 조회(채널·디버그모드·로그 레벨·중복모드·GPU 저장/적용·렌더러·메뉴 상태·열린 창·최근 로그), sys.open/sys.close 창 열기·닫기
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령이 없다(Q9)
using xkxk.frameworks.ui.data;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Rendering;
using xkxk.frameworks.ui.shell.Platform;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree; // GetVisualDescendants(dock.chrome)
using xkxk.frameworks.ui.shell.Rendering;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.shell.ViewModels.SystemMenu;
using xkxk.frameworks.ui.Controls;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemoteSystemCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("sys.get", "시스템 메뉴 상태: 채널·디버그모드(기본·실제)·로그 레벨(설정·유효)·중복모드·GPU(저장·적용·출처·재시작 대기)·렌더러·메뉴 항목(shell.mnuSys*)·열린 시스템 창 [tail=20 최근 로그 줄 수]", SysGet);
        r.Register("sys.open", "시스템 창 열기 view=LogView|AppMonitor|Inquiry|InquiryList|Update|Changelog|Help|About|RestartRequired|LogFolder|LogExport [activate=false(기본, 전경을 뺏지 않음)|true] → 열린 창 목록", a =>
        {
            var view = a.EnumArg<SystemView>("view") ?? throw new RemoteCommandException("인자 'view' 가 필요합니다(SystemView 이름).");
            var views = SystemHost.Views ?? throw new RemoteCommandException("SystemHost.Views 가 연결되지 않았습니다(App 기동 연결 전).");
            if (!views.CanOpen(view))
            {
                throw new RemoteCommandException($"이 플랫폼에서는 {view} 를 열 수 없습니다(CanOpen=false).");
            }

            SystemDialogHost.SetActivateOnce(a.Bool("activate"));
            try
            {
                views.Open(view);
            }
            finally
            {
                SystemDialogHost.SetActivateOnce(null); // 창이 아닌 동작(LogFolder·LogExport)이면 남은 1회 값을 지워 다음 메뉴 열기에 새지 않게 한다
            }

            return new JsonObject { ["opened"] = view.ToString(), ["open"] = OpenArray(), ["suppressed"] = ViewModels.SystemHost.LastSuppressed };
        });
        r.Register("menu.list", "메뉴 하위 항목 목록(팝업을 열지 않고 논리 트리 순회) id=<메뉴 AutomationId, 예 shell.mnuSys> [depth=0(기본, 전부)|1(바로 아래만)] → 항목별 id·header(현재 언어)·enabled·canExecute·visible·checked·tip·gesture·depth·parent", MenuList);
        r.Register("dock.chrome", "도구 창 머리줄(ToolChromeControl)마다 ▾ 메뉴가 대상으로 삼는 값: dock(DataContext)·그 Owner.Factory 유무·ActiveDockable(메뉴 명령 인자)의 형식·Id·CanFloat/CanPin/CanDockAsDocument/CanClose·OriginalOwner·Owner 형식·정책 CanPin·핀 버튼 바인딩(Command·CanExecute·인자 형식/Id·Enabled·Visible)", _ => DockChrome());
        r.Register("sys.close", "시스템 창 닫기 name=log|monitor|inquiry|inquiryList|update|changelog|help|about|restartRequired → 열린 창 목록", a =>
        {
            SystemDialogHost.Close(a.ReqStr("name"));
            return new JsonObject { ["open"] = OpenArray() };
        });
    }

    private static JsonNode? SysGet(JsonObject a)
    {
        var s = UiSettings.Current;
        var tail = Math.Clamp(a.Int("tail") ?? 20, 0, 500);
        var log = PdLog.Memory.Snapshot();
        return new JsonObject
        {
            ["channel"] = AppChannel.Current,
            ["debugDefault"] = AppChannel.DebugDefault,
            ["debugMode"] = s.DebugMode,
            ["logMinLevelSetting"] = s.LogMinLevel?.ToString(),
            ["logMinLevel"] = PdLog.MinLevel.ToString(),
            ["allowDuplicate"] = s.AllowDuplicateInstance,
            ["gpuSetting"] = s.GpuMode.ToString(),
            ["gpuApplied"] = GpuSetting.Applied.ToString(),
            ["gpuAppliedSource"] = GpuSetting.AppliedSource,
            ["gpuRestartPending"] = s.GpuRestartPending,
            ["renderer"] = RenderInfo.DisplayText(),
            ["rendererKind"] = RenderInfo.ActiveKind,
            ["logFileDirectory"] = PdLog.FileDirectory,
            ["viewsConnected"] = SystemHost.Views is not null,
            ["restartAvailable"] = SystemHost.Restart is not null,
            ["backgroundMode"] = SystemDialogHost.BackgroundMode,
            ["suppressed"] = ViewModels.SystemHost.LastSuppressed,
            ["menu"] = MenuArray(),
            ["open"] = OpenArray(),
            ["inquiryLastSaved"] = (SystemDialogHost.ContentOf("inquiry") as InquiryViewModel)?.LastSavedId,
            ["monitorSamples"] = (SystemDialogHost.ContentOf("monitor") as AppMonitorViewModel)?.SampleCount,
            ["logViewerCount"] = (SystemDialogHost.ContentOf("log") as LogViewerViewModel)?.Count,
            ["logTotal"] = log.Count,
            ["logTail"] = new JsonArray(log.Skip(Math.Max(0, log.Count - tail)).Select(e => (JsonNode?)new JsonObject
            {
                ["time"] = e.Time.ToString("O"),
                ["lvl"] = e.Level.ToString(),
                ["cat"] = e.Category,
                ["msg"] = e.Message,
            }).ToArray()),
        };
    }

    /// <summary>시스템 메뉴 항목(닫혀 있어도 논리 트리에 있다) — id·활성·체크·툴팁.</summary>
    private static JsonArray MenuArray()
    {
        var items = new JsonArray();
        foreach (var m in VisualTreeInspector.Main().GetLogicalDescendants().OfType<MenuItem>())
        {
            var id = Avalonia.Automation.AutomationProperties.GetAutomationId(m);
            if (id is null || !id.StartsWith("shell.mnuSys", StringComparison.Ordinal))
            {
                continue;
            }

            items.Add(new JsonObject
            {
                ["id"] = id,
                ["enabled"] = m.IsEffectivelyEnabled,
                ["checked"] = m.ToggleType == MenuItemToggleType.None ? null : m.IsChecked,
                ["tip"] = ToolTip.GetTip(m)?.ToString(),
            });
        }

        return items;
    }

    /// <summary>
    /// menu.list: 이름(AutomationId)이 같은 MenuItem(또는 Menu)을 모든 TopLevel 의 논리 트리에서 찾아 그 하위 MenuItem 을 순회한다.
    /// 하위 메뉴 항목은 팝업 TopLevel 안에 그려져 ui.find·windows.list 에 잡히지 않지만, 논리 트리(Items)에는 닫혀 있어도 있다.
    /// 구분선(Separator)은 separator=true 한 줄로 넣어 순서를 그대로 보인다.
    /// </summary>
    private static JsonNode? MenuList(JsonObject a)
    {
        var id = a.ReqStr("id");
        var maxDepth = a.Int("depth") ?? 0;
        var root = VisualTreeInspector.TopLevels()
            .SelectMany(t => t.GetLogicalDescendants())
            .OfType<Control>()
            .FirstOrDefault(c => c is MenuItem or Menu && Avalonia.Automation.AutomationProperties.GetAutomationId(c) == id)
            ?? throw new RemoteCommandException($"AutomationId '{id}' 인 메뉴를 찾지 못했습니다.");
        var items = new JsonArray();
        Walk(root, id, 1);
        return new JsonObject { ["id"] = id, ["count"] = items.Count(n => n?["separator"] is null), ["items"] = items };

        void Walk(ILogical parent, string parentId, int depth)
        {
            foreach (var child in parent.LogicalChildren)
            {
                if (child is Separator)
                {
                    items.Add(new JsonObject { ["separator"] = true, ["depth"] = depth, ["parent"] = parentId });
                    continue;
                }

                if (child is not MenuItem m)
                {
                    continue;
                }

                var mid = Avalonia.Automation.AutomationProperties.GetAutomationId(m);
                items.Add(new JsonObject
                {
                    ["id"] = mid,
                    ["header"] = m.Header switch { string s => s, Control c => VisualTreeInspector.TextOf(c), var o => o?.ToString() },
                    ["enabled"] = m.IsEnabled,
                    ["effectivelyEnabled"] = m.IsEffectivelyEnabled,
                    ["canExecute"] = m.Command is { } cmd ? cmd.CanExecute(m.CommandParameter) : null,
                    ["visible"] = m.IsVisible,
                    ["checked"] = m.ToggleType == MenuItemToggleType.None ? null : m.IsChecked,
                    ["toggle"] = m.ToggleType.ToString(),
                    ["tip"] = ToolTip.GetTip(m)?.ToString(),
                    ["gesture"] = m.InputGesture?.ToString(),
                    ["hasChildren"] = ((ILogical)m).LogicalChildren.OfType<MenuItem>().Any(), // Control.LogicalChildren 는 protected — 인터페이스로 읽는다
                    ["depth"] = depth,
                    ["parent"] = parentId,
                });
                if (maxDepth == 0 || depth < maxDepth)
                {
                    Walk(m, mid ?? parentId, depth + 1);
                }
            }
        }
    }

    /// <summary>
    /// dock.chrome: Dock 테마의 ▾ 메뉴(ToolChromeControlContextMenu)는 항목마다 DataContext(IDock).ActiveDockable 을 명령 인자·표시 조건으로,
    /// DataContext.Owner.Factory 를 명령 대상으로 쓴다(Dock.Avalonia.Themes.Fluent 12.1.0.12 디컴파일 확인). 그 실값을 머리줄마다 보여 준다.
    /// </summary>
    private static JsonNode? DockChrome()
    {
        var list = new JsonArray();
        foreach (var c in VisualTreeInspector.TopLevels().SelectMany(t => t.GetVisualDescendants()).OfType<Dock.Avalonia.Controls.ToolChromeControl>())
        {
            var dock = c.DataContext as Dock.Model.Core.IDock;
            var a = dock?.ActiveDockable;
            // 핀 버튼(템플릿 PART_PinButton·PART_ToolPinButton)의 명령 바인딩 실값
            var pin = c.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
                .FirstOrDefault(b => b.Name is TemplateParts.PinButton or TemplateParts.ToolPinButton);
            var pinParam = pin?.CommandParameter;
            list.Add(new JsonObject
            {
                ["visible"] = c.IsEffectivelyVisible,
                ["dockId"] = dock?.Id,
                ["dockType"] = c.DataContext?.GetType().Name,
                ["ownerFactory"] = dock?.Owner?.Factory is not null,
                ["activeId"] = a?.Id,
                ["activeType"] = a?.GetType().Name,
                ["activeOwnerType"] = a?.Owner?.GetType().Name,
                ["canFloat"] = a?.CanFloat,
                ["canPin"] = a?.CanPin,
                ["canDockAsDocument"] = a?.CanDockAsDocument,
                ["canClose"] = a?.CanClose,
                ["originalOwner"] = a?.OriginalOwner?.Id,
                ["overridesCanPin"] = a?.DockCapabilityOverrides is { } ov ? ov.CanPin.ToString() : null, // bool·bool? 어느 쪽이든 컴파일되게 ToString 만 쓴다
                ["policyCanPin"] = dock?.DockCapabilityPolicy is { } pol ? pol.CanPin.ToString() : null,
                ["innerActiveId"] = (a as Dock.Model.Core.IDock)?.ActiveDockable?.Id,
                ["pinButton"] = pin?.Name,
                ["pinHasCommand"] = pin is null ? null : pin.Command is not null,
                ["pinCanExecute"] = pin?.Command?.CanExecute(pinParam),
                ["pinParamType"] = pinParam?.GetType().Name,
                ["pinParamId"] = (pinParam as Dock.Model.Core.IDockable)?.Id,
                ["pinEnabled"] = pin?.IsEffectivelyEnabled,
                ["pinVisible"] = pin?.IsEffectivelyVisible,
            });
        }

        return new JsonObject { ["count"] = list.Count, ["chromes"] = list };
    }

    private static JsonArray OpenArray() =>
        new(SystemDialogHost.OpenNames.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
}
#endif

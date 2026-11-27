// 옛 판 앱 대역 DTO(저장 형식 5, 열거형 Kind)와 그 JSON 소스 생성 컨텍스트 — 계약 C67 전용, LayoutMigrationChecks.cs 에서 분리(B09)
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Layout;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.test.framework.contract;

// 아래는 61ffef5 의 Docking/LayoutSnapshot.cs DTO(속성 이름·형식)·소스 생성 옵션을 그대로 옮긴 옛 판 앱 대역이다(형식 이름만 LegacyV5 접두).
internal enum LegacyKindV5 { Grid, Gantt, Form }

internal sealed class LegacyV5LayoutSnapshot
{
    public int Version { get; set; } = 5;
    public DocumentLayoutMode Mode { get; set; } = DocumentLayoutMode.Mdi;
    public LegacyV5PanelsSnapshot? Panels { get; set; }
    public bool? SidePanelsHidden { get; set; }
    public string? ActiveId { get; set; }
    public List<LegacyV5DocumentSnapshot> Documents { get; set; } = new();
    public List<LegacyKindV5> DuplicateKinds { get; set; } = new();
    public List<LegacyV5MdiGroupSnapshot>? Groups { get; set; }
}

internal sealed class LegacyV5MdiGroupSnapshot
{
    public string Id { get; set; } = "";
    public double X { get; set; } public double Y { get; set; } public double Width { get; set; } public double Height { get; set; }
    public MdiWindowState State { get; set; }
    public int ZIndex { get; set; }
    public string? ActiveId { get; set; }
    public List<string> DocIds { get; set; } = new();
}

internal sealed class LegacyV5DocumentSnapshot
{
    public string Id { get; set; } = "";
    public LegacyKindV5 Kind { get; set; }
    public int Seq { get; set; }
    public string Title { get; set; } = "";
    public double X { get; set; } public double Y { get; set; } public double Width { get; set; } public double Height { get; set; }
    public MdiWindowState MdiState { get; set; }
    public int ZIndex { get; set; }
    public int? ZOrder { get; set; }
    public double Zoom { get; set; } = 1.0;
    public bool IsPinned { get; set; }
    public bool SyncEnabled { get; set; } = true;
    public bool IsLocked { get; set; }
    public LegacyV5WindowSnapshot? Window { get; set; }
    public bool? AutoTitle { get; set; } public string? PanelId { get; set; }
}

internal sealed class LegacyV5PanelsSnapshot
{
    public double ExplorerProportion { get; set; } public double LogProportion { get; set; }
    public string? NotificationEdge { get; set; } public double? NotificationProportion { get; set; }
    public string? ExplorerEdge { get; set; } public string? LogEdge { get; set; }
    public string? ExtraEdge { get; set; } public double? ExtraProportion { get; set; }
    public List<PanelTabsSnapshot>? Tabs { get; set; }
    [JsonPropertyName("cornerOwners")]
    public Dictionary<string, string>? CornerOwners { get; set; }
}

internal sealed class LegacyV5WindowSnapshot
{
    public double X { get; set; } public double Y { get; set; } public double Width { get; set; } public double Height { get; set; }
    public bool Topmost { get; set; }
    public DockWindowState State { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LegacyV5LayoutSnapshot))]
internal sealed partial class LegacyV5JsonContext : JsonSerializerContext
{
}

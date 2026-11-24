// 레이아웃 전체 백업 묶음 1개의 저장 형식 — 각 값은 원래 저장소에 있던 JSON 문자열 그대로다
using System.Collections.Generic;

namespace xkxk.frameworks.ui.data.Layout;

/// <summary>백업 묶음 1개(저장소 키 backup.&lt;id&gt;). 각 값은 원래 저장소에 있던 JSON 문자열 그대로다.</summary>
public sealed class BackupBundle
{
    public int Version { get; set; } = 1;

    public string? SettingsJson { get; set; }

    public string? AutoLayoutJson { get; set; }

    public string? LibraryIndexJson { get; set; }

    /// <summary>개인 레이아웃 내용(Id → 레이아웃 JSON).</summary>
    public Dictionary<string, string> Layouts { get; set; } = new();
}

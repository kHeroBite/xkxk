// 레이아웃 백업 목록의 항목 1개(이름·생성 시각·개인 레이아웃 수·자동 백업 여부)
using System;

namespace xkxk.frameworks.ui.data.Layout;

public sealed class BackupIndexItem
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public int LayoutCount { get; set; }

    public bool IsAuto { get; set; }
}

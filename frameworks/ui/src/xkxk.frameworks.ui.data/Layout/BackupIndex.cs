// 레이아웃 백업 목록의 저장 형식 — 키 backups 에 JSON 으로 저장된다
using System.Collections.Generic;

namespace xkxk.frameworks.ui.data.Layout;

public sealed class BackupIndex
{
    public int Version { get; set; } = 1;

    public List<BackupIndexItem> Items { get; set; } = new();
}

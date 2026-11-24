// 패널 모서리 소유 값(요건 3) — 화면 네 모서리를 어느 패널이 차지했는지 기본값과 다른 것만 담는 불변 값과 그 직렬 표현
using System;
using System.Collections.Generic;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>창 사각형의 네 모서리.</summary>
public enum PanelCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// 모서리마다 '이 패널까지 그 모서리에 닿는다'를 기록한다. 없는 모서리는 기본 규칙(PanelCornerRules.DefaultPrecedes)을 따른다.
/// 저장 키는 모서리 이름, 값은 패널 Id 다(LayoutSerializer 의 cornerOwners 선택 필드). 없으면 기본 배치라 옛 저장본과 같은 모양이다.
/// </summary>
public sealed class PanelCornerOwnership
{
    private readonly Dictionary<PanelCorner, string> _owners;

    private PanelCornerOwnership(Dictionary<PanelCorner, string> owners) => _owners = owners;

    /// <summary>기본 배치(모든 모서리가 기본 규칙).</summary>
    public static PanelCornerOwnership Default { get; } = new(new Dictionary<PanelCorner, string>());

    public bool IsDefault => _owners.Count == 0;

    /// <summary>모서리를 차지한 패널 Id. 기본 규칙이면 null.</summary>
    public string? OwnerOf(PanelCorner corner) => _owners.GetValueOrDefault(corner);

    public PanelCornerOwnership With(PanelCorner corner, string panelId) =>
        new(new Dictionary<PanelCorner, string>(_owners) { [corner] = panelId });

    public PanelCornerOwnership Without(PanelCorner corner)
    {
        var copy = new Dictionary<PanelCorner, string>(_owners);
        copy.Remove(corner);
        return copy.Count == 0 ? Default : new PanelCornerOwnership(copy);
    }

    /// <summary>저장 표현. 기본 배치면 null(저장 파일에 값을 남기지 않는다).</summary>
    public Dictionary<string, string>? ToStored() =>
        IsDefault ? null : _owners.OrderBy(p => p.Key).ToDictionary(p => p.Key.ToString(), p => p.Value);

    /// <summary>저장 표현을 읽는다. null·알 수 없는 모서리·빈 값은 건너뛴다(옛 파일과 손상 값은 기본 규칙으로 돌아간다).</summary>
    public static PanelCornerOwnership FromStored(IReadOnlyDictionary<string, string>? stored)
    {
        var owners = new Dictionary<PanelCorner, string>();
        foreach (var (key, value) in stored ?? new Dictionary<string, string>())
        {
            if (Enum.TryParse<PanelCorner>(key, ignoreCase: false, out var corner) && Enum.IsDefined(corner) && !string.IsNullOrEmpty(value))
            {
                owners[corner] = value;
            }
        }

        return owners.Count == 0 ? Default : new PanelCornerOwnership(owners);
    }
}

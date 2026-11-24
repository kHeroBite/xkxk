// --bg-pos 인자 값(비전경 배치) — auto 면 VirtualScreen 밖 자동 계산, 아니면 x,y 고정
namespace xkxk.frameworks.ui.desktop;

/// <summary>--bg-pos 값. auto 면 VirtualScreen 밖 자동 계산, 아니면 x,y 고정.</summary>
public sealed record BgPlacement(bool Auto, int X, int Y)
{
    public static BgPlacement? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return new BgPlacement(true, 0, 0);
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
        {
            return new BgPlacement(false, x, y);
        }

        throw new ArgumentException($"--bg-pos 값 형식 오류: '{value}' (auto 또는 x,y)");
    }
}

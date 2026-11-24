// SDI 그림자 창의 화소 계산 — 보이는 프레임(둥근 사각형) 바깥에만 활성·비활성별 감쇠 그림자 알파를 premultiplied BGRA 로 채운다
namespace Pd.Poc.Desktop;

internal static class SdiShadowBitmap
{
    /// <summary>
    /// buf(위→아래 32bpp 화소, 0 으로 초기화된 배열)에 그림자를 채운다. 창 크기 = 프레임 + 사방 ext.
    /// 프레임 안(둥근 사각형 안)은 0(투명)으로 두고, 바깥 고리만 계산한다(가운데는 건드리지 않음).
    /// </summary>
    public static void Fill(int[] buf, int width, int height, int ext, double radius, SdiShadowShape shape, double scale)
    {
        var peak = shape.Peak;
        var tau = shape.Tau * scale;
        var oy = shape.OffsetY * scale;
        var fw = width - 2 * ext;
        var fh = height - 2 * ext;
        if (fw <= 0 || fh <= 0)
        {
            return;
        }

        var lut = BuildLut(ext, peak, tau);
        var cx = ext + fw / 2.0;
        var cy = ext + fh / 2.0;
        var hx = fw / 2.0;
        var hy = fh / 2.0;
        var band = ext + (int)Math.Ceiling(radius) + 1; // 모서리 둥근 부분까지 고리 폭
        for (var y = 0; y < height; y++)
        {
            var full = y < band || y >= height - band;
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (!full && x == band)
                {
                    x = width - band - 1; // 가운데 줄의 안쪽은 프레임 안이라 건너뛴다
                    continue;
                }

                var px = x + 0.5;
                var py = y + 0.5;
                var inner = RoundRectDistance(px - cx, py - cy, hx, hy, radius); // <0 = 프레임 안
                if (inner <= -0.5)
                {
                    continue;
                }

                var d = RoundRectDistance(px - cx, py - cy - oy, hx, hy, radius); // 그림자 원천(아래로 oy 이동)
                var a = d <= 0 ? peak : lut[Math.Min(lut.Length - 1, (int)(d * 4))];
                a *= Math.Clamp(inner + 0.5, 0, 1); // 프레임 가장자리 반 화소 경계 부드럽게
                var a8 = Math.Clamp((int)Math.Round(a * 255), 0, 255);
                buf[row + x] = a8 << 24; // 검정 premultiplied: 색 0, 알파만
            }
        }
    }

    // 거리(1/4px 단위) → 알파. 지수 감쇠 × 끝(ext)에서 0 으로 닿는 완만한 꼬리.
    private static double[] BuildLut(int ext, double peak, double tau)
    {
        var lut = new double[ext * 4 + 1];
        for (var i = 0; i < lut.Length; i++)
        {
            var d = i / 4.0;
            var t = Math.Clamp(d / ext, 0, 1);
            lut[i] = peak * Math.Exp(-d / tau) * (1 - t * t);
        }

        return lut;
    }

    // 중심 기준 좌표의 둥근 사각형 부호 거리(밖 +, 안 -). hx·hy 는 반폭·반높이, r 은 모서리 반경.
    private static double RoundRectDistance(double x, double y, double hx, double hy, double r)
    {
        var qx = Math.Abs(x) - (hx - r);
        var qy = Math.Abs(y) - (hy - r);
        var ox = Math.Max(qx, 0);
        var oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }
}

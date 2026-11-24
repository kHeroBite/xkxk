// 찍어 둔 화면 그림을 덮개에 1:1 로 그리는 컨트롤 — Image 컨트롤은 원본 사각형을 DIP 로 넘겨 배율만큼 커지므로 쓰지 않는다
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Pd.Ui.Views.Depth;

/// <summary>닫기 덮개·전환 짝 나감 그림. 크기는 Width·Height(DIP)로 정하고, 레이아웃 반올림과 무관하게 Geometry.Dest 에 그린다.</summary>
internal sealed class SnapshotPicture : Control
{
    public SnapshotPicture(Bitmap image, SnapshotGeometry geometry)
    {
        Image = image;
        Geometry = geometry;
        Width = geometry.Size.Width;
        Height = geometry.Size.Height;
        IsHitTestVisible = false;
    }

    public Bitmap? Image { get; set; }

    public SnapshotGeometry Geometry { get; }

    public override void Render(DrawingContext context)
    {
        if (Image is { } image)
        {
            Geometry.Draw(context, image);
        }
    }
}

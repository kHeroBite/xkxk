// 둥근 모서리 안으로 자식을 잘라내는 Border — Border 의 ClipToBounds 가 CornerRadius 를 따라 둥근 사각형으로 자르게 켜 둔다(Avalonia 12.x CompositionBorderVisual)
using System;
using Avalonia;
using Avalonia.Controls;

namespace Pd.Ui.Controls;

/// <summary>
/// CornerRadius 가 있으면 ClipToBounds 를 켜서 자식(제목줄·문서 내용)을 둥근 사각형으로 잘라낸다.
/// Avalonia 12.x 의 Border 는 합성 비주얼(CompositionBorderVisual)이 ClipToBounds 를 PushClip(RoundedRect) → Skia ClipRoundRect 로 그린다(12.1.4 디컴파일 확인).
/// 예전 판은 원호 경로(StreamGeometry)를 Clip 지오메트리로 걸었는데, Skia GPU(Ganesh)는 경로 잘라내기를 해석적으로 못 해 그릴 때마다
/// 소프트웨어 마스크를 만들고 텍스처로 올렸다(웹 끌기 프로파일 ClipStack::GetSWMaskFP·GrSWMaskHelper::toTextureView, A15 d25e prof_sym).
/// 잘리는 모양은 같다(경계 사각형 + 같은 반경). 템플릿이 ClipToBounds 를 이미 켜 둔 경우(PdClip)는 그대로 두고, 이 컨트롤이 켠 경우만 반경이 0 이 될 때 되돌린다.
/// </summary>
public sealed class ClipBorder : Border
{
    private bool _ownsClipToBounds; // ClipToBounds 를 이 컨트롤이 켰는가(반경 0 으로 바뀌면 원래대로 끈다)

    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateClip(); // 템플릿 값(ClipToBounds·CornerRadius)이 다 들어온 뒤 한 번 맞춘다
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CornerRadiusProperty)
        {
            UpdateClip();
        }
    }

    private void UpdateClip()
    {
        if (CornerRadius != default)
        {
            if (!ClipToBounds)
            {
                SetCurrentValue(ClipToBoundsProperty, true);
                _ownsClipToBounds = true;
            }
        }
        else if (_ownsClipToBounds)
        {
            SetCurrentValue(ClipToBoundsProperty, false);
            _ownsClipToBounds = false;
        }
    }
}

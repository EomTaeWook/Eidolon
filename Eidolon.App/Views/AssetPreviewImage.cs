using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Eidolon.App.Views
{
    public class AssetPreviewImage : Image
    {
        public static readonly StyledProperty<BitmapInterpolationMode> InterpolationProperty =
            AvaloniaProperty.Register<AssetPreviewImage, BitmapInterpolationMode>(nameof(Interpolation), BitmapInterpolationMode.HighQuality);

        public AssetPreviewImage()
        {
            RenderOptions.SetBitmapInterpolationMode(this, Interpolation);
        }

        public BitmapInterpolationMode Interpolation
        {
            get
            {
                return GetValue(InterpolationProperty);
            }
            set
            {
                SetValue(InterpolationProperty, value);
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == InterpolationProperty)
            {
                RenderOptions.SetBitmapInterpolationMode(this, Interpolation);
                InvalidateVisual();
            }
        }
    }
}

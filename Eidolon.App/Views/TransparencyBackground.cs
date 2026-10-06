using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Eidolon.App.Views
{
    public class TransparencyBackground : Control
    {
        private readonly IBrush _light;
        private readonly IBrush _dark;

        public TransparencyBackground()
        {
            _light = new SolidColorBrush(Color.Parse("#FFFFFF"));
            _dark = new SolidColorBrush(Color.Parse("#EDF0F3"));
            IsHitTestVisible = false;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawRectangle(_light, null, new Rect(Bounds.Size));
            const double size = 16;
            for (int y = 0; y * size < Bounds.Height; y++)
            {
                for (int x = 0; x * size < Bounds.Width; x++)
                {
                    if ((x + y) % 2 == 0)
                    {
                        double width = Math.Min(size, Bounds.Width - x * size);
                        double height = Math.Min(size, Bounds.Height - y * size);
                        context.DrawRectangle(_dark, null, new Rect(x * size, y * size, width, height));
                    }
                }
            }
        }
    }
}

using Eidolon.Core.Domain;
using SkiaSharp;

namespace Eidolon.Core.Infrastructure
{
    public class AssetFrameRenderer
    {
        private const byte SubjectAlphaThreshold = 16;
        private const float SpriteContentRatio = 0.9f;

        public AssetFrameRenderer()
        {
        }

        public SKRectI MeasureSubjectBounds(SKBitmap bitmap, CancellationToken token)
        {
            int left = bitmap.Width;
            int top = bitmap.Height;
            int right = 0;
            int bottom = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha > SubjectAlphaThreshold)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x + 1);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }
            }
            if (right <= left)
            {
                throw new StudioException(StudioMessageCode.BackgroundSubjectNotFound);
            }
            if (bottom <= top)
            {
                throw new StudioException(StudioMessageCode.BackgroundSubjectNotFound);
            }
            return new SKRectI(left, top, right, bottom);
        }

        public void DrawSprite(SKBitmap target, SKBitmap original, SKRectI subject, bool pixelArt)
        {
            float scale = Math.Min(target.Width * SpriteContentRatio / subject.Width,
                target.Height * SpriteContentRatio / subject.Height);
            float width = subject.Width * scale;
            float height = subject.Height * scale;
            SKRect destination = SKRect.Create((target.Width - width) / 2, target.Height - height, width, height);
            SKSamplingOptions sampling = new SKSamplingOptions(SKFilterMode.Linear);
            if (pixelArt == true)
            {
                sampling = new SKSamplingOptions(SKFilterMode.Nearest);
            }
            using SKCanvas canvas = new SKCanvas(target);
            using SKImage source = SKImage.FromBitmap(original);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(source, new SKRect(subject.Left, subject.Top, subject.Right, subject.Bottom), destination, sampling, null);
        }
    }
}

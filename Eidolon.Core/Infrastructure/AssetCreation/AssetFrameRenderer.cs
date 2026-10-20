using Eidolon.Core.Domain;
using SkiaSharp;

namespace Eidolon.Core.Infrastructure
{
    public class AssetFrameRenderer
    {
        private const byte SubjectAlphaThreshold = 16;

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

    }
}

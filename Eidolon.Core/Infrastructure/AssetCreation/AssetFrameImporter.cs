using Eidolon.Core.Domain;
using SkiaSharp;

namespace Eidolon.Core.Infrastructure
{
    public class AssetFrameImporter
    {
        private readonly JobStore _jobs;

        public AssetFrameImporter(JobStore jobs)
        {
            _jobs = jobs;
        }

        public string Import(string collectionId, byte[] data, CancellationToken token)
        {
            if (data == null || data.Length < 1 || data.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            using SKMemoryStream stream = new SKMemoryStream(data);
            using SKCodec codec = SKCodec.Create(stream);
            if (codec == null || codec.Info.Width < 1 || codec.Info.Height < 1
                || (long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            using SKBitmap decoded = SKBitmap.Decode(codec);
            if (decoded == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            int width = decoded.Width;
            int height = decoded.Height;
            if (codec.EncodedOrigin == SKEncodedOrigin.LeftTop || codec.EncodedOrigin == SKEncodedOrigin.RightTop
                || codec.EncodedOrigin == SKEncodedOrigin.RightBottom || codec.EncodedOrigin == SKEncodedOrigin.LeftBottom)
            {
                width = decoded.Height;
                height = decoded.Width;
            }
            using SKBitmap oriented = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (SKCanvas canvas = new SKCanvas(oriented))
            {
                canvas.Clear(SKColors.Transparent);
                switch (codec.EncodedOrigin)
                {
                    case SKEncodedOrigin.TopRight:
                        canvas.Translate(width, 0);
                        canvas.Scale(-1, 1);
                        break;
                    case SKEncodedOrigin.BottomRight:
                        canvas.Translate(width, height);
                        canvas.Scale(-1, -1);
                        break;
                    case SKEncodedOrigin.BottomLeft:
                        canvas.Translate(0, height);
                        canvas.Scale(1, -1);
                        break;
                    case SKEncodedOrigin.LeftTop:
                        canvas.RotateDegrees(90);
                        canvas.Scale(1, -1);
                        break;
                    case SKEncodedOrigin.RightTop:
                        canvas.Translate(width, 0);
                        canvas.RotateDegrees(90);
                        break;
                    case SKEncodedOrigin.RightBottom:
                        canvas.Translate(width, height);
                        canvas.RotateDegrees(90);
                        canvas.Scale(-1, 1);
                        break;
                    case SKEncodedOrigin.LeftBottom:
                        canvas.Translate(0, height);
                        canvas.RotateDegrees(270);
                        break;
                }
                canvas.DrawBitmap(decoded, 0, 0);
            }
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(_jobs.DirectoryFor(collectionId), "Replacement-" + Guid.NewGuid().ToString("N") + ".png");
            using SKImage image = SKImage.FromBitmap(oriented);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            if (png == null || png.Size > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            using FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            png.SaveTo(output);
            output.Flush(true);
            return path;
        }
    }
}

using Avalonia.Media.Imaging;
using Dignus.DependencyInjection.Attributes;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using SkiaSharp;

namespace Eidolon.App.Services
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class AssetImageLoader
    {
        private readonly AssetFrameRenderer _renderer;

        public AssetImageLoader(AssetFrameRenderer renderer)
        {
            _renderer = renderer;
        }

        public byte[] Read(string path)
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length < 1 || stream.Length > GenerationReferenceInput.MaximumImageBytes)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        public Bitmap Load(byte[] bytes, int maximumSize, bool pixelArt)
        {
            using SKMemoryStream header = new SKMemoryStream(bytes);
            using SKCodec codec = SKCodec.Create(header);
            if (codec == null || codec.Info.Width < 1 || codec.Info.Height < 1
                || (long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            BitmapInterpolationMode interpolation = BitmapInterpolationMode.HighQuality;
            if (pixelArt == true)
            {
                interpolation = BitmapInterpolationMode.None;
            }
            using MemoryStream stream = new MemoryStream(bytes, false);
            if (codec.Info.Width >= codec.Info.Height)
            {
                return Bitmap.DecodeToWidth(stream, maximumSize, interpolation);
            }
            return Bitmap.DecodeToHeight(stream, maximumSize, interpolation);
        }

        public Bitmap LoadFrame(byte[] bytes, AssetCollection collection, int maximumSize, CancellationToken token)
        {
            if (collection.Kind != AssetCreationKind.SpriteAnimation)
            {
                return Load(bytes, maximumSize, collection.PixelArt);
            }
            using SKMemoryStream header = new SKMemoryStream(bytes);
            using SKCodec codec = SKCodec.Create(header);
            if (codec == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (codec.Info.Width < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if (codec.Info.Height < 1)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            if ((long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            using SKBitmap original = SKBitmap.Decode(codec);
            if (original == null)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            SKRectI subject = _renderer.MeasureSubjectBounds(original, token);
            using SKBitmap normalized = new SKBitmap(collection.FrameWidth, collection.FrameHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            _renderer.DrawSprite(normalized, original, subject, collection.PixelArt);
            using SKImage image = SKImage.FromBitmap(normalized);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            if (png == null)
            {
                throw new StudioException(StudioMessageCode.AssetCreationFailed);
            }
            token.ThrowIfCancellationRequested();
            return Load(png.ToArray(), maximumSize, collection.PixelArt);
        }
    }
}

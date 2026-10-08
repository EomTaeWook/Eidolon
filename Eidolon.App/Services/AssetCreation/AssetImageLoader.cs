using Avalonia.Media.Imaging;
using Dignus.DependencyInjection.Attributes;
using Eidolon.Core.Domain;
using SkiaSharp;

namespace Eidolon.App.Services
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class AssetImageLoader
    {
        public AssetImageLoader()
        {
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
    }
}

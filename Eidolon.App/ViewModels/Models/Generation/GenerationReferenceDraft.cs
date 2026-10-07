using Avalonia.Media.Imaging;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class GenerationReferenceDraft : IDisposable
    {
        private int _loadRequest;

        public GenerationReferenceDraft(GenerationReferenceMode mode, double strength)
        {
            Mode = mode;
            ChangeStrength = strength;
        }

        public GenerationReferenceMode Mode { get; private set; }
        public double ChangeStrength { get; set; }
        public byte[] ImageData { get; private set; } = Array.Empty<byte>();
        public string ImageName { get; private set; } = string.Empty;
        public Bitmap Thumbnail { get; private set; }
        public bool HasImage
        {
            get
            {
                return ImageData.Length > 0 || string.IsNullOrWhiteSpace(ImageName) == false;
            }
        }
        public bool AreOptionsValid
        {
            get
            {
                return HasImage == false || (ImageData.Length > 0 && double.IsFinite(ChangeStrength) == true
                    && ChangeStrength >= 0.05 && ChangeStrength <= 0.95);
            }
        }

        public int BeginLoad()
        {
            _loadRequest++;
            return _loadRequest;
        }

        public bool IsCurrentLoad(int request)
        {
            return request == _loadRequest;
        }

        public void SetImage(byte[] data, Bitmap thumbnail, string name)
        {
            Bitmap previous = Thumbnail;
            ImageData = data;
            Thumbnail = thumbnail;
            ImageName = name;
            previous?.Dispose();
        }

        public void SetMissingImage(string name)
        {
            Clear();
            ImageName = name;
            if (string.IsNullOrWhiteSpace(ImageName) == true)
            {
                ImageName = "Reference.png";
            }
        }

        public void Clear()
        {
            _loadRequest++;
            Bitmap previous = Thumbnail;
            ImageData = Array.Empty<byte>();
            Thumbnail = null;
            ImageName = string.Empty;
            previous?.Dispose();
        }

        public void Dispose()
        {
            Clear();
        }
    }
}

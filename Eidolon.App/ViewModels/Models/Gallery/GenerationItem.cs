using Eidolon.Core.Domain;
using Eidolon.App.Localization;
using Avalonia.Media.Imaging;
using System.Globalization;

namespace Eidolon.App.ViewModels
{
    public class GenerationItem : ObservableObject, IDisposable
    {
        private readonly StringHelper _strings;
        private Bitmap _thumbnail;
        private bool _isThumbnailLoading;

        public GenerationItem(GenerationImage image, StringHelper strings)
        {
            _strings = strings;
            Image = image;
            Label = Path.GetFileNameWithoutExtension(image.FilePath);
            if (image.Metadata != null && string.IsNullOrWhiteSpace(image.Metadata.Title) == false)
            {
                Label = image.Metadata.Title;
            }
            Localize();
        }

        public GenerationImage Image { get; private set; }
        public GenerationMetadata Metadata
        {
            get
            {
                return Image.Metadata;
            }
        }
        public bool HasMetadata
        {
            get
            {
                return Metadata != null;
            }
        }
        public string Label { get; private set; }
        public string FileName
        {
            get
            {
                return Path.GetFileName(Image.FilePath);
            }
        }
        public string StateCaption { get; private set; }
        public string CreatedCaption
        {
            get
            {
                DateTimeOffset time = Image.CreatedAtUtc;
                if (HasMetadata == true && Metadata.GeneratedAtUtc != default)
                {
                    time = Metadata.GeneratedAtUtc;
                }
                return time.ToLocalTime().ToString("MM/dd HH:mm", CultureInfo.InvariantCulture);
            }
        }
        public string DimensionsCaption
        {
            get
            {
                if (HasMetadata == false)
                {
                    return string.Empty;
                }
                return Metadata.Width + " × " + Metadata.Height;
            }
        }
        public string SeedCaption
        {
            get
            {
                if (HasMetadata == false)
                {
                    return string.Empty;
                }
                if (Metadata.GenerationBackend == GenerationBackend.Codex)
                {
                    return string.Empty;
                }
                return Metadata.Seed.ToString(CultureInfo.InvariantCulture);
            }
        }
        public bool HasSeed
        {
            get
            {
                if (HasMetadata == false)
                {
                    return false;
                }
                return Metadata.GenerationBackend == GenerationBackend.ComfyUI;
            }
        }
        public string AppliedLoras
        {
            get
            {
                if (HasMetadata == false)
                {
                    return string.Empty;
                }
                return string.Join(", ", Metadata.Loras.Select(lora => lora.Name));
            }
        }
        public bool HasAppliedLoras
        {
            get
            {
                return HasMetadata == true && Metadata.Loras.Count > 0;
            }
        }
        public bool HasReferenceImage
        {
            get
            {
                return HasMetadata == true && Metadata.ReferenceMode != GenerationReferenceMode.None;
            }
        }
        public string ReferenceCaption
        {
            get
            {
                if (HasReferenceImage == false)
                {
                    return string.Empty;
                }
                string mode = _strings.GetString("EidolonText444");
                if (Metadata.ReferenceMode == GenerationReferenceMode.Restyle)
                {
                    mode = _strings.GetString("EidolonText445");
                }
                if (Metadata.GenerationBackend == GenerationBackend.Codex)
                {
                    return mode + " · " + Metadata.ReferenceImageName;
                }
                return mode + " · " + Metadata.Denoise.ToString("P0", CultureInfo.InvariantCulture) + " · " + Metadata.ReferenceImageName;
            }
        }
        public string ErrorMessage { get; private set; }
        public bool HasError
        {
            get
            {
                return string.IsNullOrWhiteSpace(ErrorMessage) == false;
            }
        }
        public string ThumbnailCaption
        {
            get
            {
                if (_isThumbnailLoading == true)
                {
                    return _strings.GetString("EidolonText378");
                }
                return _strings.GetString("EidolonText381");
            }
        }
        public Bitmap Thumbnail
        {
            get
            {
                return _thumbnail;
            }
        }
        public bool HasThumbnail
        {
            get
            {
                return _thumbnail != null;
            }
        }

        public void Localize()
        {
            StateCaption = _strings.GetString("EidolonText383");
            if (HasMetadata == true)
            {
                StateCaption = _strings.GetString("EidolonText210");
            }
            ErrorMessage = string.Empty;
            if (Image.MetadataError != null)
            {
                ErrorMessage = _strings.GetString("EidolonText384") + "\n" + _strings.GetExceptionMessage(Image.MetadataError);
            }
            Raise(nameof(StateCaption));
            Raise(nameof(ErrorMessage));
            Raise(nameof(HasError));
            Raise(nameof(ThumbnailCaption));
            Raise(nameof(ReferenceCaption));
        }

        public void BeginThumbnailLoad()
        {
            _isThumbnailLoading = true;
            Raise(nameof(ThumbnailCaption));
        }

        public void SetThumbnail(Bitmap thumbnail)
        {
            _isThumbnailLoading = false;
            Raise(nameof(ThumbnailCaption));
            Bitmap previous = _thumbnail;
            if (Set(ref _thumbnail, thumbnail, nameof(Thumbnail)) == true)
            {
                previous?.Dispose();
                Raise(nameof(HasThumbnail));
            }
        }

        public void Dispose()
        {
            SetThumbnail(null);
        }
    }
}

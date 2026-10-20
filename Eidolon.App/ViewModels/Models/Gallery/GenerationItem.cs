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
            if (image.Metadata != null)
            {
                if (string.IsNullOrWhiteSpace(image.Metadata.Title) == false)
                {
                    Label = image.Metadata.Title;
                }
            }
            foreach (GenerationImage collectionImage in image.CollectionImages)
            {
                GenerationItem previous = null;
                if (collectionImage.Metadata != null)
                {
                    if (string.IsNullOrEmpty(collectionImage.Metadata.AssetFrameLabel) == false)
                    {
                        previous = CollectionImages.FirstOrDefault(item =>
                        {
                            if (item.Metadata == null)
                            {
                                return false;
                            }
                            return item.Metadata.AssetFrameLabel == collectionImage.Metadata.AssetFrameLabel;
                        });
                    }
                    else if (collectionImage.Metadata.AssetFrameNumber > 0)
                    {
                        previous = CollectionImages.FirstOrDefault(item =>
                        {
                            if (item.Metadata == null)
                            {
                                return false;
                            }
                            return item.Metadata.AssetFrameNumber == collectionImage.Metadata.AssetFrameNumber;
                        });
                    }
                }
                if (previous != null)
                {
                    DateTimeOffset previousCreatedAtUtc = previous.Image.CreatedAtUtc;
                    if (previous.Metadata != null)
                    {
                        if (previous.Metadata.GeneratedAtUtc != default)
                        {
                            previousCreatedAtUtc = previous.Metadata.GeneratedAtUtc;
                        }
                    }
                    DateTimeOffset createdAtUtc = collectionImage.CreatedAtUtc;
                    if (collectionImage.Metadata != null)
                    {
                        if (collectionImage.Metadata.GeneratedAtUtc != default)
                        {
                            createdAtUtc = collectionImage.Metadata.GeneratedAtUtc;
                        }
                    }
                    if (previousCreatedAtUtc >= createdAtUtc)
                    {
                        continue;
                    }
                    CollectionImages.Remove(previous);
                    previous.Dispose();
                }
                CollectionImages.Add(new GenerationItem(collectionImage, strings));
            }
            CollectionImages = CollectionImages.OrderBy(item =>
            {
                if (item.Metadata == null)
                {
                    return 0;
                }
                return item.Metadata.AssetFrameNumber;
            }).ToList();
            Localize();
        }

        public GenerationImage Image { get; private set; }
        public List<GenerationItem> CollectionImages { get; private set; } = new List<GenerationItem>();
        public bool IsAssetCollection
        {
            get
            {
                return CollectionImages.Count > 0;
            }
        }
        public bool IsCollectionImage
        {
            get
            {
                if (Metadata == null)
                {
                    return false;
                }
                return string.IsNullOrEmpty(Metadata.AssetCollectionId) == false;
            }
        }
        public string CollectionCaption
        {
            get
            {
                return _strings.Format("EidolonText706", CollectionImages.Count);
            }
        }
        public string ViewCaption
        {
            get
            {
                if (Metadata == null)
                {
                    return string.Empty;
                }
                if (string.IsNullOrEmpty(Metadata.AssetCollectionId) == true)
                {
                    return string.Empty;
                }
                if (Metadata.AssetFrameLabel == "front")
                {
                    return _strings.GetString("EidolonText637");
                }
                if (Metadata.AssetFrameLabel == "right")
                {
                    return _strings.GetString("EidolonText702");
                }
                if (Metadata.AssetFrameLabel == "left")
                {
                    return _strings.GetString("EidolonText703");
                }
                if (Metadata.AssetFrameLabel == "side")
                {
                    return _strings.GetString("EidolonText638");
                }
                if (Metadata.AssetFrameLabel == "back")
                {
                    return _strings.GetString("EidolonText639");
                }
                if (Metadata.AssetFrameLabel == "top")
                {
                    return _strings.GetString("EidolonText640");
                }
                return _strings.Format("EidolonText620", Metadata.AssetFrameNumber);
            }
        }
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
                if (IsAssetCollection == false)
                {
                    if (HasMetadata == true)
                    {
                        if (Metadata.GeneratedAtUtc != default)
                        {
                            time = Metadata.GeneratedAtUtc;
                        }
                    }
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
            foreach (GenerationItem collectionImage in CollectionImages)
            {
                collectionImage.Localize();
            }
            if (IsAssetCollection == true)
            {
                Label = _strings.GetString("EidolonText606");
                if (Metadata != null)
                {
                    if (string.IsNullOrWhiteSpace(Metadata.ReferenceImageName) == false)
                    {
                        string referenceImageName = Path.GetFileName(Metadata.ReferenceImageName);
                        if (string.IsNullOrWhiteSpace(referenceImageName) == false)
                        {
                            Label = referenceImageName;
                        }
                    }
                }
            }
            else if (IsCollectionImage == true)
            {
                Label = ViewCaption;
            }
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
            Raise(nameof(Label));
            Raise(nameof(ErrorMessage));
            Raise(nameof(HasError));
            Raise(nameof(ThumbnailCaption));
            Raise(nameof(ReferenceCaption));
            Raise(nameof(CollectionCaption));
            Raise(nameof(ViewCaption));
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
            foreach (GenerationItem collectionImage in CollectionImages)
            {
                collectionImage.Dispose();
            }
            SetThumbnail(null);
        }
    }
}

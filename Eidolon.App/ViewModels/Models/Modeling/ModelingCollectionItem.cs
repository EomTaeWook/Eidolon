using Avalonia.Media.Imaging;
using Eidolon.App.Localization;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class ModelingCollectionItem : ObservableObject, IDisposable
    {
        private Bitmap _thumbnail;
        private readonly int _imageCount;

        public ModelingCollectionItem(AssetCollection collection, StringHelper strings)
        {
            Collection = collection;
            List<AssetFrame> completedFrames = collection.Frames.Where(frame =>
            {
                if (frame.State != JobState.Completed)
                {
                    return false;
                }
                return File.Exists(frame.ImagePath);
            }).ToList();
            _imageCount = completedFrames.Count;
            AssetFrame representative = completedFrames.FirstOrDefault(frame => string.Equals(frame.Label, "front", StringComparison.OrdinalIgnoreCase));
            if (representative == null)
            {
                representative = completedFrames.FirstOrDefault();
            }
            ImagePath = string.Empty;
            if (representative != null)
            {
                ImagePath = representative.ImagePath;
            }
            Localize(strings);
        }

        public AssetCollection Collection
        {
            get;
            private set;
        }

        public string ImagePath
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            private set;
        }

        public string Caption
        {
            get;
            private set;
        }

        public Bitmap Thumbnail
        {
            get
            {
                return _thumbnail;
            }
        }

        public void Localize(StringHelper strings)
        {
            Name = Path.GetFileName(Collection.ReferenceName);
            if (string.IsNullOrWhiteSpace(Name) == true)
            {
                Name = strings.GetString("EidolonText606");
            }
            Caption = strings.Format("EidolonText706", _imageCount);
            Raise(nameof(Name));
            Raise(nameof(Caption));
        }

        public void SetThumbnail(Bitmap thumbnail)
        {
            Bitmap previousThumbnail = _thumbnail;
            _thumbnail = thumbnail;
            Raise(nameof(Thumbnail));
            previousThumbnail?.Dispose();
        }

        public void Dispose()
        {
            SetThumbnail(null);
        }
    }
}

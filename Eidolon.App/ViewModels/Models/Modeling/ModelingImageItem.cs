using Avalonia.Media.Imaging;
using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class ModelingImageItem : ObservableObject, IDisposable
    {
        private readonly string _captionKey;
        private string _imagePath = string.Empty;
        private Bitmap _thumbnail;
        private CancellationTokenSource _imageLoadCancellationTokenSource;

        public ModelingImageItem(string direction, string captionKey, StringHelper strings)
        {
            Direction = direction;
            _captionKey = captionKey;
            Localize(strings);
        }

        public string Direction
        {
            get;
            private set;
        }

        public string Caption
        {
            get;
            private set;
        }

        public string ImagePath
        {
            get
            {
                return _imagePath;
            }
        }

        public string FileName
        {
            get
            {
                return Path.GetFileName(_imagePath);
            }
        }

        public Bitmap Thumbnail
        {
            get
            {
                return _thumbnail;
            }
        }

        public bool HasImage
        {
            get
            {
                return _thumbnail != null;
            }
        }

        public bool CanRemove
        {
            get
            {
                if (HasImage == true)
                {
                    return true;
                }
                return ImageLoadCancellationTokenSource != null;
            }
        }

        public CancellationTokenSource ImageLoadCancellationTokenSource
        {
            get
            {
                return _imageLoadCancellationTokenSource;
            }
            set
            {
                _imageLoadCancellationTokenSource = value;
                Raise(nameof(CanRemove));
            }
        }

        public Task ImageLoadTask
        {
            get;
            set;
        } = Task.CompletedTask;

        public void Localize(StringHelper strings)
        {
            Caption = strings.GetString(_captionKey);
            if (Direction == "side")
            {
                Caption = strings.Format("EidolonText781", Caption);
            }
            Raise(nameof(Caption));
        }

        public void SetImage(string imagePath, Bitmap thumbnail)
        {
            Bitmap previousThumbnail = _thumbnail;
            _imagePath = imagePath;
            _thumbnail = thumbnail;
            Raise(nameof(ImagePath));
            Raise(nameof(FileName));
            Raise(nameof(Thumbnail));
            Raise(nameof(HasImage));
            Raise(nameof(CanRemove));
            previousThumbnail?.Dispose();
        }

        public void Clear()
        {
            CancellationTokenSource imageLoadCancellationTokenSource = ImageLoadCancellationTokenSource;
            ImageLoadCancellationTokenSource = null;
            imageLoadCancellationTokenSource?.Cancel();
            SetImage(string.Empty, null);
        }

        public void Dispose()
        {
            Clear();
        }
    }
}

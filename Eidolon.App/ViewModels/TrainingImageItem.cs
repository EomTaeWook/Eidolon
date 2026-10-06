using Avalonia.Media.Imaging;

namespace Eidolon.App.ViewModels
{
    public class TrainingImageItem : IDisposable
    {
        public TrainingImageItem(string filePath, Bitmap thumbnail)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            Thumbnail = thumbnail;
        }

        public string FilePath { get; private set; }
        public string FileName { get; private set; }
        public Bitmap Thumbnail { get; private set; }

        public void Dispose()
        {
            Thumbnail.Dispose();
        }
    }
}

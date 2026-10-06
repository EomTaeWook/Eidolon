using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Eidolon.App.Localization;
using Eidolon.App.Services;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class TrainingImageGroup : ObservableObject, IDisposable
    {
        private readonly string _labelKey;
        private readonly StringHelper _strings;
        private readonly DesktopDialogs _dialogs;
        private readonly Func<bool> _canEdit;
        private readonly Action _changed;
        private readonly CancellationToken _lifetime;
        private bool _disposed;
        private int _loadingCount;

        public TrainingImageGroup(TrainingBackground background, string labelKey, IBrush swatch,
            StringHelper strings, DesktopDialogs dialogs, Func<bool> canEdit, Action changed,
            Action<Exception> onError, CancellationToken lifetime)
        {
            Background = background;
            Swatch = swatch;
            _labelKey = labelKey;
            _strings = strings;
            _dialogs = dialogs;
            _canEdit = canEdit;
            _changed = changed;
            _lifetime = lifetime;
            Images.CollectionChanged += OnImagesChanged;
            AddImagesCommand = new AsyncCommand(PickImagesAsync,
                () => _disposed == false && _canEdit() == true && IsLoading == false, onError);
            AddFolderCommand = new AsyncCommand(PickFolderAsync,
                () => _disposed == false && _canEdit() == true && IsLoading == false, onError);
            RemoveImageCommand = new AsyncCommand(RemoveImageAsync,
                () => _disposed == false && _canEdit() == true && IsLoading == false && HasImages == true, onError);
            ClearCommand = new AsyncCommand(ClearAsync,
                () => _disposed == false && _canEdit() == true && IsLoading == false && HasImages == true, onError);
            Commands = new[] { AddImagesCommand, AddFolderCommand, RemoveImageCommand, ClearCommand };
        }

        public TrainingBackground Background { get; private set; }
        public IBrush Swatch { get; private set; }
        public ObservableCollection<TrainingImageItem> Images { get; private set; } = new ObservableCollection<TrainingImageItem>();
        public AsyncCommand AddImagesCommand { get; private set; }
        public AsyncCommand AddFolderCommand { get; private set; }
        public AsyncCommand RemoveImageCommand { get; private set; }
        public AsyncCommand ClearCommand { get; private set; }
        public IReadOnlyList<AsyncCommand> Commands { get; private set; }

        public string Label
        {
            get
            {
                return _strings.GetString(_labelKey);
            }
        }

        public string CountCaption
        {
            get
            {
                return _strings.Format("EidolonText465", Images.Count);
            }
        }

        public bool HasImages
        {
            get
            {
                return Images.Count > 0;
            }
        }

        public bool IsLoading
        {
            get
            {
                return _loadingCount > 0;
            }
        }

        private void BeginLoading()
        {
            _loadingCount++;
            Raise(nameof(IsLoading));
            _changed();
        }

        private void EndLoading()
        {
            _loadingCount--;
            if (_disposed == false)
            {
                Raise(nameof(IsLoading));
                _changed();
            }
        }

        public void Localize()
        {
            Raise(nameof(Label));
            Raise(nameof(CountCaption));
        }

        private void OnImagesChanged(object sender, NotifyCollectionChangedEventArgs arguments)
        {
            Raise(nameof(CountCaption));
            Raise(nameof(HasImages));
            _changed();
        }

        private async Task PickImagesAsync()
        {
            IReadOnlyList<string> paths = await _dialogs.PickTrainingImagesAsync(Label);
            await AddPathsAsync(paths);
        }

        private async Task PickFolderAsync()
        {
            string directory = await _dialogs.PickFolderAsync(Label);
            if (string.IsNullOrEmpty(directory) == true || _disposed == true)
            {
                return;
            }
            BeginLoading();
            try
            {
                List<string> paths = await Task.Run(() =>
                {
                    List<string> selected = new List<string>();
                    string[] extensions = { ".png", ".jpg", ".jpeg", ".bmp" };
                    foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        _lifetime.ThrowIfCancellationRequested();
                        if (extensions.Contains(Path.GetExtension(path).ToLowerInvariant()) == true)
                        {
                            selected.Add(path);
                        }
                    }
                    selected.Sort(StringComparer.OrdinalIgnoreCase);
                    return selected;
                }, _lifetime);
                await AddPathsAsync(paths);
            }
            finally
            {
                EndLoading();
            }
        }

        public async Task AddPathsAsync(IReadOnlyList<string> paths)
        {
            BeginLoading();
            try
            {
                foreach (string path in paths)
                {
                    if (_disposed == true || _canEdit() == false)
                    {
                        return;
                    }
                    string fullPath = Path.GetFullPath(path);
                    if (Images.Any(image => image.FilePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)) == true)
                    {
                        continue;
                    }
                    Bitmap thumbnail = await Task.Run(() =>
                    {
                        _lifetime.ThrowIfCancellationRequested();
                        using FileStream stream = File.OpenRead(fullPath);
                        return Bitmap.DecodeToWidth(stream, 64);
                    }, _lifetime);
                    if (_disposed == true || _canEdit() == false)
                    {
                        thumbnail.Dispose();
                        return;
                    }
                    if (Images.Any(image => image.FilePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)) == true)
                    {
                        thumbnail.Dispose();
                        continue;
                    }
                    Images.Add(new TrainingImageItem(fullPath, thumbnail));
                }
            }
            finally
            {
                EndLoading();
            }
        }

        public IReadOnlyList<TrainingImageInput> Snapshot()
        {
            return Images.Select(image => new TrainingImageInput
            {
                FilePath = image.FilePath,
                Background = Background
            }).ToList();
        }

        private Task RemoveImageAsync(object value)
        {
            if (value is TrainingImageItem image && Images.Remove(image) == true)
            {
                image.Dispose();
            }
            return Task.CompletedTask;
        }

        private Task ClearAsync()
        {
            foreach (TrainingImageItem image in Images)
            {
                image.Dispose();
            }
            Images.Clear();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _disposed = true;
            Images.CollectionChanged -= OnImagesChanged;
            foreach (TrainingImageItem image in Images)
            {
                image.Dispose();
            }
            Images.Clear();
        }
    }
}

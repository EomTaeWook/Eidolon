using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.Services;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Eidolon.App.ViewModels
{
    public class AssetPreviewViewModel : StudioPanelViewModel
    {
        private readonly JobStore _jobs;
        private readonly AssetCreationService _service;
        private readonly AssetImageLoader _images;
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private AssetCollection _collection;
        private AssetFrameItem _selectedFrame;
        private bool _playing;
        private int _request;
        private readonly List<Task> _loads = new List<Task>();
        private string _exportDirectory = string.Empty;
        private string _requestedId = string.Empty;

        public AssetPreviewViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work,
            StringHelper strings, DesktopDialogs dialogs, JobStore jobs, AssetCreationService service, AssetImageLoader images)
            : base(session, navigation, work, strings, dialogs)
        {
            _jobs = jobs;
            _service = service;
            _images = images;
            _timer.Tick += OnTick;
            PlayCommand = Command(TogglePlaybackAsync, () => HasFrames == true && IsSprite == true
                && IsCurrent == true && Session.IsClosing == false);
            ExportCommand = Command(ExportAsync, () => CanExport == true && IsCurrent == true && Session.IsClosing == false);
            OpenExportCommand = Command(() => _dialogs.OpenFolderAsync(_exportDirectory),
                () => string.IsNullOrEmpty(_exportDirectory) == false && Session.IsClosing == false);
            RefreshCommand = Command(RefreshAsync, () => HasCollection == true && Session.IsClosing == false);
            MoveEarlierCommand = Command(() => MoveAsync(-1), () => CanEditSelection == true && SelectedFrame != null
                && Frames.IndexOf(SelectedFrame) > 0 && IsSprite == true);
            MoveLaterCommand = Command(() => MoveAsync(1), () => CanEditSelection == true && SelectedFrame != null
                && Frames.IndexOf(SelectedFrame) < Frames.Count - 1 && IsSprite == true);
        }

        public ObservableCollection<AssetFrameItem> Frames { get; private set; } = new ObservableCollection<AssetFrameItem>();
        public AsyncCommand PlayCommand { get; private set; }
        public AsyncCommand ExportCommand { get; private set; }
        public AsyncCommand OpenExportCommand { get; private set; }
        public AsyncCommand RefreshCommand { get; private set; }
        public AsyncCommand MoveEarlierCommand { get; private set; }
        public AsyncCommand MoveLaterCommand { get; private set; }
        public AssetCollection Collection
        {
            get
            {
                return _collection;
            }
        }
        public string RequestedCollectionId
        {
            get
            {
                return _requestedId;
            }
        }
        public bool IsCurrent
        {
            get
            {
                return _collection != null && _collection.Id == _requestedId;
            }
        }
        public bool CanEditSelection
        {
            get
            {
                return IsCurrent == true && Session.IsIdle == true && IsPlaying == false;
            }
        }
        public bool HasCollection
        {
            get
            {
                return _collection != null;
            }
        }
        public bool HasFrames
        {
            get
            {
                return Frames.Any(frame => frame.HasImage == true);
            }
        }
        public bool IsSprite
        {
            get
            {
                return _collection != null && _collection.Kind == AssetCreationKind.SpriteAnimation;
            }
        }
        public bool CanExport
        {
            get
            {
                return Frames.Count > 0 && Frames.All(frame => frame.HasImage == true);
            }
        }
        public bool IsPlaying
        {
            get
            {
                return _playing;
            }
        }
        public string Title
        {
            get
            {
                if (_collection != null)
                {
                    return _collection.Prompt;
                }
                return _strings.GetString("EidolonText621");
            }
        }
        public string PlayCaption
        {
            get
            {
                if (_playing == true)
                {
                    return _strings.GetString("EidolonText619");
                }
                return _strings.GetString("EidolonText618");
            }
        }
        public bool HasExport
        {
            get
            {
                return string.IsNullOrEmpty(_exportDirectory) == false;
            }
        }
        public double ThumbnailWidth
        {
            get
            {
                if (IsSprite == true)
                {
                    return 136;
                }
                return 200;
            }
        }
        public double ThumbnailHeight
        {
            get
            {
                if (IsSprite == true)
                {
                    return 130;
                }
                return 200;
            }
        }
        public double FrameListHeight
        {
            get
            {
                if (IsSprite == true)
                {
                    return 260;
                }
                return 520;
            }
        }
        public string SeedCaption
        {
            get
            {
                if (_collection == null)
                {
                    return string.Empty;
                }
                return _strings.Format("EidolonText654", _collection.Seed);
            }
        }
        public BitmapInterpolationMode Interpolation
        {
            get
            {
                if (_collection != null && _collection.PixelArt == true)
                {
                    return BitmapInterpolationMode.None;
                }
                return BitmapInterpolationMode.HighQuality;
            }
        }
        public AssetFrameItem SelectedFrame
        {
            get
            {
                return _selectedFrame;
            }
            set
            {
                if (Set(ref _selectedFrame, value) == true)
                {
                    RefreshCommands();
                    Raise(nameof(Preview));
                }
            }
        }
        public Bitmap Preview
        {
            get
            {
                if (_selectedFrame != null)
                {
                    return _selectedFrame.Image;
                }
                return null;
            }
        }

        public Task ShowAsync(string id)
        {
            _requestedId = id;
            Raise(nameof(IsCurrent));
            RefreshCommands();
            int request = ++_request;
            _loads.RemoveAll(task => task.IsCompleted == true);
            Task load = LoadAsync(id, request);
            _loads.Add(load);
            return load;
        }

        public Task RefreshAsync()
        {
            if (_collection == null || Session.IsClosing == true)
            {
                return Task.CompletedTask;
            }
            return ShowAsync(_collection.Id);
        }

        private async Task LoadAsync(string id, int request)
        {
            int selectedNumber = 1;
            if (_collection != null && _collection.Id == id && SelectedFrame != null)
            {
                selectedNumber = SelectedFrame.Frame.Number;
            }
            CancellationToken token = Session.Lifetime;
            (AssetCollection Collection, List<Bitmap> Images, Exception ImageError) loaded = await Task.Run(() =>
            {
                AssetCollection collection = _jobs.LoadAssetCollection(id);
                List<Bitmap> images = new List<Bitmap>();
                Exception imageError = null;
                try
                {
                    foreach (AssetFrame frame in collection.Frames)
                    {
                        token.ThrowIfCancellationRequested();
                        Bitmap bitmap = null;
                        if (File.Exists(frame.ImagePath) == true)
                        {
                            try
                            {
                                bitmap = _images.Load(_images.Read(frame.ImagePath), 512, collection.PixelArt);
                            }
                            catch (Exception error) when (error is IOException || error is StudioException
                                || error is UnauthorizedAccessException || error is ArgumentException)
                            {
                                imageError = error;
                            }
                        }
                        images.Add(bitmap);
                    }
                    return (collection, images, imageError);
                }
                catch
                {
                    foreach (Bitmap image in images)
                    {
                        image?.Dispose();
                    }
                    throw;
                }
            }, token);
            if (Session.IsClosing == true || request != _request)
            {
                foreach (Bitmap image in loaded.Images)
                {
                    image?.Dispose();
                }
                return;
            }
            bool wasPlaying = _playing;
            StopPlayback();
            SelectedFrame = null;
            foreach (AssetFrameItem frame in Frames)
            {
                frame.Dispose();
            }
            Frames.Clear();
            if (_collection == null || _collection.Id != loaded.Collection.Id)
            {
                _exportDirectory = string.Empty;
                Raise(nameof(HasExport));
            }
            _collection = loaded.Collection;
            for (int index = 0; index < _collection.Frames.Count; index++)
            {
                AssetFrameItem item = new AssetFrameItem(_collection.Frames[index], _strings);
                item.SetImage(loaded.Images[index]);
                Frames.Add(item);
            }
            SelectedFrame = Frames.FirstOrDefault(frame => frame.Frame.Number == selectedNumber);
            if (SelectedFrame == null)
            {
                SelectedFrame = Frames.FirstOrDefault();
            }
            if (wasPlaying == true && IsSprite == true && HasFrames == true)
            {
                StartPlayback();
            }
            NotifyCollection();
            if (loaded.ImageError != null)
            {
                Session.ReportError(loaded.ImageError);
            }
        }

        private Task TogglePlaybackAsync()
        {
            if (_playing == true)
            {
                StopPlayback();
            }
            else
            {
                StartPlayback();
            }
            return Task.CompletedTask;
        }
        private void StartPlayback()
        {
            _timer.Interval = TimeSpan.FromSeconds(1.0 / _collection.FramesPerSecond);
            _playing = true;
            _timer.Start();
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayCaption));
            RefreshCommands();
        }
        private void StopPlayback()
        {
            _timer.Stop();
            _playing = false;
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayCaption));
            RefreshCommands();
        }
        private void OnTick(object sender, EventArgs args)
        {
            List<AssetFrameItem> playable = Frames.Where(frame => frame.HasImage == true).ToList();
            if (playable.Count == 0)
            {
                StopPlayback();
                return;
            }
            SelectedFrame = playable[(playable.IndexOf(SelectedFrame) + 1) % playable.Count];
        }
        private async Task MoveAsync(int direction)
        {
            StopPlayback();
            int index = Frames.IndexOf(SelectedFrame);
            AssetFrame frame = _collection.Frames[index];
            _collection.Frames.RemoveAt(index);
            _collection.Frames.Insert(index + direction, frame);
            for (int number = 0; number < _collection.Frames.Count; number++)
            {
                _collection.Frames[number].Number = number + 1;
            }
            AssetCollection collection = _collection;
            await Session.WorkAsync(async token =>
            {
                await Task.Run(() => _jobs.SaveAssetCollection(collection), token);
                await ShowAsync(collection.Id);
            });
        }
        private async Task ExportAsync()
        {
            string id = _collection.Id;
            string parent = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText627"));
            if (string.IsNullOrEmpty(parent) == true || Session.IsClosing == true)
            {
                return;
            }
            _exportDirectory = await Task.Run(() => _service.Export(id, parent, Session.Lifetime), Session.Lifetime);
            Raise(nameof(HasExport));
            RefreshCommands();
            await _dialogs.OpenFolderAsync(_exportDirectory);
        }
        private void NotifyCollection()
        {
            Raise(nameof(IsCurrent));
            Raise(nameof(ThumbnailWidth));
            Raise(nameof(ThumbnailHeight));
            Raise(nameof(FrameListHeight));
            foreach (string property in new[] { nameof(Collection), nameof(HasCollection), nameof(HasFrames), nameof(IsSprite), nameof(CanExport), nameof(Title), nameof(Interpolation), nameof(Preview), nameof(SeedCaption) })
            {
                Raise(property);
            }
            RefreshCommands();
        }
        protected override void OnNavigationPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (Navigation.IsAssetCreationView == false)
            {
                StopPlayback();
            }
        }
        protected override void RefreshCommands()
        {
            Raise(nameof(CanEditSelection));
            base.RefreshCommands();
        }
        public async Task CloseAsync()
        {
            ++_request;
            StopPlayback();
            try
            {
                await Task.WhenAll(_loads);
            }
            catch (OperationCanceledException)
            {
            }
        }
        public override void Localize()
        {
            foreach (AssetFrameItem frame in Frames)
            {
                frame.Localize(_strings);
            }
            NotifyCollection();
            Raise(nameof(PlayCaption));
            base.Localize();
        }
        public override void Dispose()
        {
            StopPlayback();
            _timer.Tick -= OnTick;
            foreach (AssetFrameItem frame in Frames)
            {
                frame.Dispose();
            }
            Frames.Clear();
            base.Dispose();
        }
    }
}

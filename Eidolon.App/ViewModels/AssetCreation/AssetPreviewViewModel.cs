using Avalonia.Media.Imaging;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.Services;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using System.Collections.ObjectModel;

namespace Eidolon.App.ViewModels
{
    public class AssetPreviewViewModel : StudioPanelViewModel
    {
        private readonly JobStore _jobs;
        private readonly AssetCreationService _service;
        private readonly AssetImageLoader _images;
        private AssetCollection _collection;
        private AssetFrameItem _selectedFrame;
        private int _request;
        private readonly List<Task> _loads = new List<Task>();
        private CancellationTokenSource _loadLifetime;
        private string _exportDirectory = string.Empty;
        private string _requestedId = string.Empty;

        public AssetPreviewViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs, JobStore jobs, AssetCreationService service, AssetImageLoader images) : base(session, navigation, work, strings, dialogs)
        {
            _jobs = jobs;
            _service = service;
            _images = images;
            ExportCommand = Command(ExportAsync, () =>
            {
                if (CanExport == false)
                {
                    return false;
                }
                if (IsCurrent == false)
                {
                    return false;
                }
                return Session.IsClosing == false;
            });
            OpenExportCommand = Command(() => _dialogs.OpenFolderAsync(_exportDirectory), () =>
            {
                if (string.IsNullOrEmpty(_exportDirectory) == true)
                {
                    return false;
                }
                return Session.IsClosing == false;
            });
            RefreshCommand = Command(RefreshAsync, () =>
            {
                if (HasCollection == false)
                {
                    return false;
                }
                return Session.IsClosing == false;
            });
        }

        public ObservableCollection<AssetFrameItem> Frames { get; private set; } = new ObservableCollection<AssetFrameItem>();
        public AsyncCommand ExportCommand { get; private set; }
        public AsyncCommand OpenExportCommand { get; private set; }
        public AsyncCommand RefreshCommand { get; private set; }
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
                if (IsCurrent == false)
                {
                    return false;
                }
                return Session.IsIdle;
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
        public bool CanExport
        {
            get
            {
                return Frames.Count > 0 && Frames.All(frame => frame.HasImage == true);
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
                return 200;
            }
        }
        public double ThumbnailHeight
        {
            get
            {
                return 200;
            }
        }
        public double FrameListHeight
        {
            get
            {
                return 520;
            }
        }
        public bool HasSeed
        {
            get
            {
                if (_collection == null)
                {
                    return false;
                }
                return _collection.Settings.GenerationBackend == GenerationBackend.ComfyUI;
            }
        }
        public string SeedCaption
        {
            get
            {
                if (HasSeed == false)
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
            if (Session.IsClosing == true)
            {
                return Task.CompletedTask;
            }
            _requestedId = id;
            Raise(nameof(IsCurrent));
            RefreshCommands();
            int requestNumber = ++_request;
            _loadLifetime?.Cancel();
            _loads.RemoveAll(loadTask => loadTask.IsCompleted == true);
            Task previousLoad = Task.WhenAll(_loads);
            Task load = LoadAsync(id, requestNumber, previousLoad);
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

        private async Task LoadAsync(string collectionId, int requestNumber, Task previousLoad)
        {
            await previousLoad;
            if (Session.IsClosing == true)
            {
                return;
            }
            if (requestNumber != _request)
            {
                return;
            }
            int selectedNumber = 1;
            if (_collection != null)
            {
                if (_collection.Id == collectionId)
                {
                    if (SelectedFrame != null)
                    {
                        selectedNumber = SelectedFrame.Frame.Number;
                    }
                }
            }
            using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            _loadLifetime = lifetime;
            CancellationToken cancellationToken = lifetime.Token;
            try
            {
                (AssetCollection Collection, List<Bitmap> Images, Exception ImageError) loaded = await Task.Run(() =>
                {
                    AssetCollection collection = _jobs.LoadAssetCollection(collectionId);
                    if (collection.Kind == AssetCreationKind.SpriteAnimation)
                    {
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                    }
                    List<Bitmap> images = new List<Bitmap>();
                    Exception imageError = null;
                    try
                    {
                        foreach (AssetFrame frame in collection.Frames)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Bitmap bitmap = null;
                            if (File.Exists(frame.ImagePath) == true)
                            {
                                try
                                {
                                    bitmap = _images.Load(_images.Read(frame.ImagePath), 512, collection.PixelArt);
                                }
                                catch (IOException exception)
                                {
                                    imageError = exception;
                                }
                                catch (StudioException exception)
                                {
                                    imageError = exception;
                                }
                                catch (UnauthorizedAccessException exception)
                                {
                                    imageError = exception;
                                }
                                catch (ArgumentException exception)
                                {
                                    imageError = exception;
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
                }, cancellationToken);
                if (Session.IsClosing == true)
                {
                    foreach (Bitmap image in loaded.Images)
                    {
                        image?.Dispose();
                    }
                    return;
                }
                if (requestNumber != _request)
                {
                    foreach (Bitmap image in loaded.Images)
                    {
                        image?.Dispose();
                    }
                    return;
                }
                SelectedFrame = null;
                foreach (AssetFrameItem frame in Frames)
                {
                    frame.Dispose();
                }
                Frames.Clear();
                if (_collection != null)
                {
                    if (_collection.Id != loaded.Collection.Id)
                    {
                        _exportDirectory = string.Empty;
                        Raise(nameof(HasExport));
                    }
                }
                else
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
                NotifyCollection();
                if (loaded.ImageError != null)
                {
                    Session.ReportError(loaded.ImageError);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
            }
            catch
            {
                if (Session.IsClosing == true)
                {
                    return;
                }
                if (requestNumber != _request)
                {
                    return;
                }
                throw;
            }
            finally
            {
                if (_loadLifetime == lifetime)
                {
                    _loadLifetime = null;
                }
            }
        }

        private async Task ExportAsync()
        {
            string collectionId = _collection.Id;
            string parent = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText627"));
            if (string.IsNullOrEmpty(parent) == true)
            {
                return;
            }
            if (Session.IsClosing == true)
            {
                return;
            }
            string exportDirectory = await Task.Run(() => _service.Export(collectionId, parent, Session.Lifetime), Session.Lifetime);
            if (Session.IsClosing == true)
            {
                return;
            }
            if (_collection != null)
            {
                if (_collection.Id == collectionId)
                {
                    if (_requestedId == collectionId)
                    {
                        _exportDirectory = exportDirectory;
                        Raise(nameof(HasExport));
                        RefreshCommands();
                    }
                }
            }
            await _dialogs.OpenFolderAsync(exportDirectory);
        }
        private void NotifyCollection()
        {
            Raise(nameof(IsCurrent));
            Raise(nameof(ThumbnailWidth));
            Raise(nameof(ThumbnailHeight));
            Raise(nameof(FrameListHeight));
            foreach (string property in new[] { nameof(Collection), nameof(HasCollection), nameof(HasFrames), nameof(CanExport), nameof(Title), nameof(Interpolation), nameof(Preview), nameof(HasSeed), nameof(SeedCaption) })
            {
                Raise(property);
            }
            RefreshCommands();
        }
        protected override void RefreshCommands()
        {
            Raise(nameof(CanEditSelection));
            base.RefreshCommands();
        }
        public async Task CloseAsync()
        {
            ++_request;
            _loadLifetime?.Cancel();
            try
            {
                await Task.WhenAll(_loads);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async Task ClearAsync()
        {
            _requestedId = string.Empty;
            Raise(nameof(IsCurrent));
            RefreshCommands();
            await CloseAsync();
            SelectedFrame = null;
            foreach (AssetFrameItem frame in Frames)
            {
                frame.Dispose();
            }
            Frames.Clear();
            _collection = null;
            _exportDirectory = string.Empty;
            Raise(nameof(HasExport));
            NotifyCollection();
        }
        public override void Localize()
        {
            foreach (AssetFrameItem frame in Frames)
            {
                frame.Localize(_strings);
            }
            NotifyCollection();
            base.Localize();
        }
        public override void Dispose()
        {
            foreach (AssetFrameItem frame in Frames)
            {
                frame.Dispose();
            }
            Frames.Clear();
            base.Dispose();
        }
    }
}

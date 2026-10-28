using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.App.ViewModels
{
    public class GalleryViewModel : StudioPanelViewModel
    {
        private GenerationViewModel Generation { get; set; }
        private TrainingViewModel Training { get; set; }
        private readonly AssetCreationViewModel _creation;

        private const int GalleryPageSize = 12;
        private const int GalleryThumbnailWidth = 384;
        private readonly JobStore _jobs;
        private GenerationItem _selectedResult;
        private GenerationItem _selectedGeneration;
        private int _galleryPageNumber = 1;
        private int _requestedGalleryPageNumber = 1;
        private int _galleryPageSize = GalleryPageSize;
        private int _requestedGalleryPageSize = GalleryPageSize;
        private int _galleryPageCount = 1;
        private int _generationCount;
        private int _galleryLoadRequest;
        private int _galleryFilterIndex;
        private bool _isGalleryLoading;
        private bool _hasMigratedGenerationMetadata;
        private string _galleryError = string.Empty;
        private CancellationTokenSource _galleryLoadCancellation;
        private Task _galleryLoadTask = Task.CompletedTask;
        private Bitmap _preview;
        private string _previewPath = string.Empty;
        public ObservableCollection<GenerationItem> Generations { get; private set; } = new ObservableCollection<GenerationItem>();

        public int GalleryFilterIndex
        {
            get
            {
                return _galleryFilterIndex;
            }
            set
            {
                if (value < 0)
                {
                    return;
                }
                if (value > 2)
                {
                    return;
                }
                if (Set(ref _galleryFilterIndex, value) == false)
                {
                    return;
                }
                GallerySelection.Clear();
                SelectedGeneration = null;
                SelectedResult = null;
                _ = LoadGalleryPageAsync(1, false, false, true);
            }
        }

        public bool HasAssetCollectionSelection
        {
            get
            {
                foreach (GenerationItem item in GallerySelection)
                {
                    if (item.IsAssetCollection == true)
                    {
                        return true;
                    }
                    if (item.IsCollectionImage == true)
                    {
                        return true;
                    }
                }
                if (SelectedGeneration != null)
                {
                    if (SelectedGeneration.IsAssetCollection == true)
                    {
                        return true;
                    }
                }
                if (SelectedResult != null)
                {
                    if (SelectedResult.Metadata != null)
                    {
                        return string.IsNullOrEmpty(SelectedResult.Metadata.AssetCollectionId) == false;
                    }
                }
                return false;
            }
        }

        public bool CanDeleteGallerySelection
        {
            get
            {
                if (Session.IsIdle == false)
                {
                    return false;
                }
                if (IsGalleryLoading == true)
                {
                    return false;
                }
                if (HasAssetCollectionSelection == true)
                {
                    return false;
                }
                return HasGallerySelection;
            }
        }

        public bool CanDeleteAllGenerations
        {
            get
            {
                if (Session.IsIdle == false)
                {
                    return false;
                }
                if (IsGalleryLoading == true)
                {
                    return false;
                }
                if (HasAssetCollectionSelection == true)
                {
                    return false;
                }
                if (GalleryFilterIndex == 2)
                {
                    return false;
                }
                return HasGenerations;
            }
        }

        public bool IsGalleryLoading
        {
            get
            {
                return _isGalleryLoading;
            }
            private set
            {
                if (Set(ref _isGalleryLoading, value) == true)
                {
                    Raise(nameof(IsGalleryEmpty));
                    RefreshCommands();
                }
            }
        }

        public bool IsGalleryEmpty
        {
            get
            {
                if (HasGenerations == true)
                {
                    return false;
                }
                if (IsGalleryLoading == true)
                {
                    return false;
                }
                return HasGalleryError == false;
            }
        }

        public string GalleryError
        {
            get
            {
                return _galleryError;
            }
            private set
            {
                if (Set(ref _galleryError, value) == true)
                {
                    Raise(nameof(HasGalleryError));
                    Raise(nameof(IsGalleryEmpty));
                }
            }
        }

        public bool HasGalleryError
        {
            get
            {
                return string.IsNullOrWhiteSpace(GalleryError) == false;
            }
        }

        public string GalleryPageCaption
        {
            get
            {
                return _strings.Format("EidolonText376", _galleryPageNumber, _galleryPageCount);
            }
        }

        public string GalleryCountCaption
        {
            get
            {
                return _strings.Format("EidolonText377", _generationCount);
            }
        }

        public ObservableCollection<GenerationItem> GallerySelection { get; private set; } = new ObservableCollection<GenerationItem>();

        public string GallerySelectionCaption
        {
            get
            {
                return _strings.Format("EidolonText385", GallerySelection.Count);
            }
        }

        public bool HasGallerySelection
        {
            get
            {
                return GallerySelection.Count > 0;
            }
        }

        public AsyncCommand UseResultAsReferenceCommand { get; private set; }
        public AsyncCommand SelectGenerationCommand { get; private set; }
        public AsyncCommand RefreshGalleryCommand { get; private set; }
        public AsyncCommand PreviousGalleryPageCommand { get; private set; }
        public AsyncCommand NextGalleryPageCommand { get; private set; }
        public AsyncCommand ClearGallerySelectionCommand { get; private set; }
        public AsyncCommand ExportCommand { get; private set; }
        public AsyncCommand DeleteGenerationCommand { get; private set; }
        public AsyncCommand DeleteAllGenerationsCommand { get; private set; }
        public AsyncCommand ReusePromptCommand { get; private set; }
        public AsyncCommand UseResultForTrainingCommand { get; private set; }
        public AsyncCommand UseResultForAssetCreationCommand { get; private set; }
        public AsyncCommand OpenImageFolderCommand { get; private set; }

        public GenerationItem SelectedResult
        {
            get
            {
                return _selectedResult;
            }
            set
            {
                if (value != null)
                {
                    if (value.IsAssetCollection == true)
                    {
                        if (value.CollectionImages.Contains(_selectedResult) == true)
                        {
                            value = _selectedResult;
                        }
                        else
                        {
                            value = value.CollectionImages.FirstOrDefault();
                        }
                    }
                }
                if (Set(ref _selectedResult, value) == true)
                {
                    Raise(nameof(HasSelectedResult));
                    bool keepCollection = false;
                    if (_selectedGeneration != null)
                    {
                        keepCollection = _selectedGeneration.CollectionImages.Contains(value);
                    }
                    if (keepCollection == false)
                    {
                        Set(ref _selectedGeneration, value, nameof(SelectedGeneration));
                    }
                    ShowPreview(value);
                    RefreshCommands();
                }
            }
        }

        public bool HasSelectedResult
        {
            get
            {
                return SelectedResult != null;
            }
        }

        public GenerationItem SelectedGeneration
        {
            get
            {
                return _selectedGeneration;
            }
            set
            {
                if (Set(ref _selectedGeneration, value) == false)
                {
                    return;
                }
                if (Navigation.IsGenerationWorkspace == true)
                {
                    SelectedResult = value;
                    return;
                }
                if (Navigation.IsResultsView == true)
                {
                    SelectedResult = value;
                }
            }
        }

        public bool HasGenerations
        {
            get
            {
                return _generationCount > 0;
            }
        }

        public Bitmap Preview
        {
            get
            {
                return _preview;
            }
            private set
            {
                Bitmap previous = _preview;
                Set(ref _preview, value);
                previous?.Dispose();
                Raise(nameof(HasPreview));
            }
        }

        public bool HasPreview
        {
            get
            {
                return _preview != null;
            }
        }

        private async Task DeleteGenerationsAsync(bool all)
        {
            if (HasAssetCollectionSelection == true)
            {
                return;
            }
            string directory = _jobs.OutputDirectory(Session.Settings.GenerationDirectory);
            List<string> paths = new List<string>();
            if (all == true)
            {
                CancellationToken token = Session.Lifetime;
                paths.AddRange(await Task.Run(() =>
                {
                    List<string> imagePaths = new List<string>();
                    GenerationPage page = _jobs.LoadGenerationPage(directory, 1, GalleryPageSize, token, true, false, true);
                    imagePaths.AddRange(page.Images.Select(image => image.FilePath));
                    for (int pageNumber = 2; pageNumber <= page.PageCount; pageNumber++)
                    {
                        token.ThrowIfCancellationRequested();
                        GenerationPage nextPage = _jobs.LoadGenerationPage(directory, pageNumber, GalleryPageSize, token, true, false, true);
                        imagePaths.AddRange(nextPage.Images.Select(image => image.FilePath));
                    }
                    return imagePaths;
                }, token));
                if (Session.IsClosing == true)
                {
                    return;
                }
            }
            else
            {
                foreach (GenerationItem item in GallerySelection)
                {
                    if (item.IsAssetCollection == true)
                    {
                        return;
                    }
                }
                paths.AddRange(GallerySelection.Select(item => item.Image.FilePath));
            }
            if (paths.Count == 0)
            {
                return;
            }
            string message = _strings.Format("EidolonText319", paths.Count);
            if (all == true)
            {
                message = _strings.Format("EidolonText320", paths.Count);
            }
            if (await _dialogs.ConfirmDeleteAsync(_strings.GetString("EidolonText318"), message) == false)
            {
                return;
            }
            if (HasAssetCollectionSelection == true)
            {
                return;
            }
            await Session.WorkAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await Task.Run(() => _jobs.DeleteGenerationImages(directory, paths, token), token);
                }
                finally
                {
                    await RefreshGalleryAsync();
                }
                Session.Status = _strings.Format("EidolonText323", paths.Count);
            });
        }

        private Task OpenImageFolderAsync()
        {
            if (HasPreview == true)
            {
                if (Navigation.IsResultsView == false)
                {
                    return _dialogs.OpenFolderAsync(Path.GetDirectoryName(_previewPath));
                }
            }
            if (string.IsNullOrWhiteSpace(Session.Settings.GenerationDirectory) == false)
            {
                return _dialogs.OpenFolderAsync(Session.Settings.GenerationDirectory);
            }
            return _dialogs.OpenFolderAsync(Path.Combine(Session.DataDirectory, "Images"));
        }

        private Task SelectGenerationAsync(object parameter)
        {
            if (parameter is GenerationItem item)
            {
                GenerationItem collection = Generations.FirstOrDefault(generation => generation.CollectionImages.Contains(item));
                if (collection != null)
                {
                    Set(ref _selectedGeneration, collection, nameof(SelectedGeneration));
                    SelectedResult = item;
                    RefreshCommands();
                }
                else
                {
                    SelectedGeneration = item;
                }
            }
            return Task.CompletedTask;
        }

        private void OnGallerySelectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            Raise(nameof(GallerySelectionCaption));
            Raise(nameof(HasGallerySelection));
            RefreshCommands();
        }

        internal Task RefreshGalleryAsync(bool markInterrupted = false)
        {
            return LoadGalleryPageAsync(_requestedGalleryPageNumber, true, markInterrupted);
        }

        internal void SetGalleryPageSize(int pageSize)
        {
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }
            if (Session.IsClosing == true)
            {
                return;
            }
            if (pageSize == _requestedGalleryPageSize)
            {
                return;
            }
            int firstItemIndex = (_galleryPageNumber - 1) * _galleryPageSize;
            int selectedItemIndex = Generations.IndexOf(SelectedGeneration);
            if (selectedItemIndex < 0)
            {
                selectedItemIndex = Generations.IndexOf(GallerySelection.FirstOrDefault());
            }
            if (selectedItemIndex >= 0)
            {
                firstItemIndex += selectedItemIndex;
            }
            _requestedGalleryPageSize = pageSize;
            _ = LoadGalleryPageAsync(firstItemIndex / pageSize + 1, true, false, false, true);
        }

        private Task LoadGalleryPageAsync(int number, bool refreshSelection = false, bool markInterrupted = false, bool clearSelection = false, bool preserveGallerySelection = false)
        {
            if (Session.IsClosing == true)
            {
                return _galleryLoadTask;
            }
            _requestedGalleryPageNumber = number;
            int requestNumber = ++_galleryLoadRequest;
            _galleryLoadCancellation?.Cancel();
            Task previousLoad = _galleryLoadTask;
            bool includeImages = GalleryFilterIndex != 2;
            bool includeAssetCollections = GalleryFilterIndex != 1;
            int pageSize = _requestedGalleryPageSize;
            _galleryLoadTask = LoadGalleryPageCoreAsync(previousLoad, requestNumber, number, pageSize, refreshSelection, markInterrupted, includeImages, includeAssetCollections, clearSelection, preserveGallerySelection);
            return _galleryLoadTask;
        }

        private async Task LoadGalleryPageCoreAsync(Task previousLoad, int requestNumber, int pageNumber, int pageSize, bool refreshSelection, bool markInterrupted, bool includeImages, bool includeAssetCollections, bool clearSelection, bool preserveGallerySelection)
        {
            await previousLoad;
            if (Session.IsClosing == true)
            {
                return;
            }
            if (requestNumber != _galleryLoadRequest)
            {
                return;
            }
            using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            _galleryLoadCancellation = lifetime;
            CancellationToken cancellationToken = lifetime.Token;
            List<Bitmap> thumbnails = new List<Bitmap>();
            List<string> rememberedSelection = new List<string>();
            if (preserveGallerySelection == true)
            {
                rememberedSelection.AddRange(GallerySelection.Select(item => item.Image.FilePath));
            }
            IsGalleryLoading = true;
            GalleryError = string.Empty;
            string directory = Session.Settings.GenerationDirectory;
            GenerationImage remembered = null;
            if (SelectedResult != null)
            {
                remembered = SelectedResult.Image;
            }
            if (remembered != null)
            {
                bool rememberedCollection = false;
                if (SelectedGeneration != null)
                {
                    rememberedCollection = SelectedGeneration.IsAssetCollection;
                }
                if (remembered.Metadata != null)
                {
                    if (string.IsNullOrEmpty(remembered.Metadata.AssetCollectionId) == false)
                    {
                        rememberedCollection = true;
                    }
                }
                if (rememberedCollection == true)
                {
                    if (includeAssetCollections == false)
                    {
                        remembered = null;
                    }
                }
                else if (includeImages == false)
                {
                    remembered = null;
                }
            }
            try
            {
                (GenerationPage Page, GenerationImage Preview) snapshot = await Task.Run(() =>
                {
                    if (markInterrupted == true)
                    {
                        _jobs.MarkInterrupted(cancellationToken);
                    }
                    if (_hasMigratedGenerationMetadata == false)
                    {
                        _jobs.MigrateGenerationMetadata(directory, cancellationToken);
                    }
                    GenerationPage page = _jobs.LoadGenerationPage(directory, pageNumber, pageSize, cancellationToken, includeImages, includeAssetCollections, true);
                    if (clearSelection == true)
                    {
                        return (page, (GenerationImage)null);
                    }
                    GenerationImage preview = remembered;
                    if (refreshSelection == true)
                    {
                        if (remembered != null)
                        {
                            preview = _jobs.FindGenerationImage(Path.GetDirectoryName(remembered.FilePath), remembered.FilePath, cancellationToken);
                        }
                    }
                    return (page, preview);
                }, cancellationToken);
                if (cancellationToken.IsCancellationRequested == true)
                {
                    return;
                }
                if (requestNumber != _galleryLoadRequest)
                {
                    return;
                }
                _hasMigratedGenerationMetadata = true;
                List<GenerationItem> items = snapshot.Page.Images.Select(image => new GenerationItem(image, _strings)).ToList();
                List<GenerationItem> previousItems = Generations.ToList();
                GallerySelection.Clear();
                Generations.Clear();
                foreach (GenerationItem item in previousItems)
                {
                    item.Dispose();
                }
                foreach (GenerationItem item in items)
                {
                    Generations.Add(item);
                    if (rememberedSelection.Contains(item.Image.FilePath, StringComparer.OrdinalIgnoreCase) == true)
                    {
                        GallerySelection.Add(item);
                    }
                }
                _galleryPageNumber = snapshot.Page.Number;
                _requestedGalleryPageNumber = snapshot.Page.Number;
                _galleryPageSize = pageSize;
                _galleryPageCount = snapshot.Page.PageCount;
                _generationCount = snapshot.Page.TotalCount;
                Raise(nameof(GalleryPageCaption));
                Raise(nameof(GalleryCountCaption));
                Raise(nameof(HasGenerations));
                Raise(nameof(IsGalleryEmpty));
                bool selectPreview = refreshSelection;
                if (SelectedGeneration == null)
                {
                    selectPreview = true;
                }
                if (clearSelection == true)
                {
                    selectPreview = false;
                }
                if (selectPreview == true)
                {
                    GenerationItem selected = null;
                    GenerationItem selectedImage = null;
                    if (snapshot.Preview != null)
                    {
                        foreach (GenerationItem item in items)
                        {
                            GenerationItem collectionImage = item.CollectionImages.FirstOrDefault(image => image.Image.FilePath == snapshot.Preview.FilePath);
                            if (collectionImage != null)
                            {
                                selected = item;
                                selectedImage = collectionImage;
                                break;
                            }
                            if (item.Image.FilePath == snapshot.Preview.FilePath)
                            {
                                selected = item;
                                break;
                            }
                        }
                        if (selected == null)
                        {
                            selected = new GenerationItem(snapshot.Preview, _strings);
                        }
                    }
                    Set(ref _selectedGeneration, selected, nameof(SelectedGeneration));
                    if (selectedImage != null)
                    {
                        SelectedResult = selectedImage;
                    }
                    else
                    {
                        SelectedResult = selected;
                    }
                }
                if (Navigation.IsResultsView == true)
                {
                    List<GenerationItem> thumbnailItems = new List<GenerationItem>();
                    foreach (GenerationItem item in items)
                    {
                        if (item.IsAssetCollection == true)
                        {
                            thumbnailItems.AddRange(item.CollectionImages);
                        }
                        else
                        {
                            thumbnailItems.Add(item);
                        }
                    }
                    foreach (GenerationItem item in thumbnailItems)
                    {
                        item.BeginThumbnailLoad();
                    }
                    List<GenerationImage> thumbnailImages = thumbnailItems.Select(item => item.Image).ToList();
                    thumbnails = await Task.Run(() => LoadGalleryThumbnails(thumbnailImages, cancellationToken), cancellationToken);
                    if (cancellationToken.IsCancellationRequested == true)
                    {
                        return;
                    }
                    if (requestNumber != _galleryLoadRequest)
                    {
                        return;
                    }
                    for (int index = 0; index < thumbnailItems.Count; index++)
                    {
                        thumbnailItems[index].SetThumbnail(thumbnails[index]);
                        thumbnails[index] = null;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
            }
            catch (Exception error)
            {
                if (Session.IsClosing == false)
                {
                    if (requestNumber == _galleryLoadRequest)
                    {
                        LogHelper.Error(error);
                        GalleryError = _strings.GetExceptionMessage(error);
                    }
                }
            }
            finally
            {
                foreach (Bitmap thumbnail in thumbnails)
                {
                    thumbnail?.Dispose();
                }
                if (ReferenceEquals(_galleryLoadCancellation, lifetime) == true)
                {
                    _galleryLoadCancellation = null;
                }
                if (requestNumber == _galleryLoadRequest)
                {
                    IsGalleryLoading = false;
                }
            }
        }

        private List<Bitmap> LoadGalleryThumbnails(IReadOnlyList<GenerationImage> images, CancellationToken token)
        {
            List<Bitmap> thumbnails = new List<Bitmap>();
            try
            {
                foreach (GenerationImage image in images)
                {
                    token.ThrowIfCancellationRequested();
                    Bitmap thumbnail = null;
                    try
                    {
                        using FileStream stream = File.OpenRead(image.FilePath);
                        thumbnail = Bitmap.DecodeToWidth(stream, GalleryThumbnailWidth);
                    }
                    catch (Exception error)
                    {
                        LogHelper.Error(error);
                    }
                    thumbnails.Add(thumbnail);
                }
                token.ThrowIfCancellationRequested();
                return thumbnails;
            }
            catch
            {
                foreach (Bitmap thumbnail in thumbnails)
                {
                    thumbnail?.Dispose();
                }
                throw;
            }
        }

        private void ShowPreview(GenerationItem item)
        {
            Preview = null;
            _previewPath = string.Empty;
            if (item == null)
            {
                return;
            }
            try
            {
                _previewPath = item.Image.FilePath;
                using FileStream stream = File.OpenRead(_previewPath);
                Preview = new Bitmap(stream);
            }
            catch (Exception error)
            {
                _previewPath = string.Empty;
                Session.Error = _strings.GetString("EidolonText244") + _strings.GetExceptionMessage(error);
            }
        }

        public GalleryViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs, GenerationViewModel generation, TrainingViewModel training, JobStore jobs, AssetCreationViewModel creation) : base(session, navigation, work, strings, dialogs)
        {
            Generation = generation;
            Training = training;
            _creation = creation;
            _jobs = jobs;
            GallerySelection.CollectionChanged += OnGallerySelectionChanged;
            UseResultAsReferenceCommand = Command(() => Generation.UseResultAsReferenceAsync(SelectedResult), () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                if (HasSelectedResult == false)
                {
                    return false;
                }
                return HasPreview;
            });
            UseResultForAssetCreationCommand = Command(UseResultForAssetCreationAsync, () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                if (HasSelectedResult == false)
                {
                    return false;
                }
                return HasPreview;
            });
            SelectGenerationCommand = new AsyncCommand(SelectGenerationAsync, () =>
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return IsGalleryLoading == false;
            }, OnCommandError);
            _commands.Add(SelectGenerationCommand);
            RefreshGalleryCommand = Command(() => RefreshGalleryAsync(), () =>
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return IsGalleryLoading == false;
            });
            PreviousGalleryPageCommand = Command(() => LoadGalleryPageAsync(_galleryPageNumber - 1), () =>
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                if (IsGalleryLoading == true)
                {
                    return false;
                }
                return _galleryPageNumber > 1;
            });
            NextGalleryPageCommand = Command(() => LoadGalleryPageAsync(_galleryPageNumber + 1), () =>
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                if (IsGalleryLoading == true)
                {
                    return false;
                }
                return _galleryPageNumber < _galleryPageCount;
            });
            ClearGallerySelectionCommand = Command(() =>
            {
                GallerySelection.Clear();
                return Task.CompletedTask;
            }, () =>
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return HasGallerySelection;
            });
            ExportCommand = Command(() => _dialogs.ExportImageAsync(_previewPath), () =>
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return HasPreview;
            });
            DeleteGenerationCommand = Command(() => DeleteGenerationsAsync(false), () => CanDeleteGallerySelection);
            DeleteAllGenerationsCommand = Command(() => DeleteGenerationsAsync(true), () => CanDeleteAllGenerations);
            ReusePromptCommand = Command(() => Generation.ReusePromptAsync(SelectedResult.Metadata), () =>
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                if (SelectedResult == null)
                {
                    return false;
                }
                return SelectedResult.HasMetadata;
            });
            UseResultForTrainingCommand = Command(() => Training.UseResultForTrainingAsync(SelectedResult, _previewPath), () =>
            {
                if (Session.CanQueue == false)
                {
                    return false;
                }
                if (HasPreview == false)
                {
                    return false;
                }
                return SelectedResult != null;
            });
            OpenImageFolderCommand = Command(OpenImageFolderAsync, () => Session.IsClosing == false);
        }

        internal void ShowGeneratedImage(GenerationImage image)
        {
            SelectedGeneration = new GenerationItem(image, _strings);
        }

        private Task UseResultForAssetCreationAsync()
        {
            string prompt = string.Empty;
            if (SelectedResult.Metadata != null)
            {
                prompt = SelectedResult.Metadata.UserPrompt;
            }
            return _creation.UseAsReferenceAsync(_previewPath, prompt);
        }

        internal Task WaitForLoadAsync()
        {
            return _galleryLoadTask;
        }

        protected override void OnNavigationPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(Navigation.SelectedTab))
            {
                return;
            }
            if (Navigation.IsGenerationWorkspace == true)
            {
                SelectedResult = SelectedGeneration;
                return;
            }
            if (Navigation.IsResultsView == true)
            {
                SelectedResult = SelectedGeneration;
            }
        }

        public override void Localize()
        {
            foreach (GenerationItem item in Generations)
            {
                item.Localize();
            }
            if (SelectedGeneration != null)
            {
                if (Generations.Contains(SelectedGeneration) == false)
                {
                    SelectedGeneration.Localize();
                }
            }
            Raise(nameof(GalleryPageCaption));
            Raise(nameof(GalleryCountCaption));
            Raise(nameof(GallerySelectionCaption));
            base.Localize();
        }

        public override void Dispose()
        {
            GallerySelection.CollectionChanged -= OnGallerySelectionChanged;
            Preview = null;
            foreach (GenerationItem item in Generations)
            {
                item.Dispose();
            }
            Generations.Clear();
            base.Dispose();
        }
    }
}

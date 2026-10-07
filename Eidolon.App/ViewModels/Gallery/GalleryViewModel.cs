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

        private const int GalleryPageSize = 12;
        private const int GalleryThumbnailWidth = 384;
        private readonly JobStore _jobs;
        private GenerationItem _selectedResult;
        private GenerationItem _selectedGeneration;
        private int _galleryPageNumber = 1;
        private int _requestedGalleryPageNumber = 1;
        private int _galleryPageCount = 1;
        private int _generationCount;
        private int _galleryLoadRequest;
        private bool _isGalleryLoading;
        private bool _hasMigratedGenerationMetadata;
        private string _galleryError = string.Empty;
        private CancellationTokenSource _galleryLoadCancellation;
        private Task _galleryLoadTask = Task.CompletedTask;
        private Bitmap _preview;
        private string _previewPath = string.Empty;
        public ObservableCollection<GenerationItem> Generations { get; private set; } = new ObservableCollection<GenerationItem>();

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
                return HasGenerations == false && IsGalleryLoading == false && HasGalleryError == false;
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
        public AsyncCommand OpenImageFolderCommand { get; private set; }

        public GenerationItem SelectedResult
        {
            get
            {
                return _selectedResult;
            }
            set
            {
                if (Set(ref _selectedResult, value) == true)
                {
                    Raise(nameof(HasSelectedResult));
                    Set(ref _selectedGeneration, value, nameof(SelectedGeneration));
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
                if (Set(ref _selectedGeneration, value) == true && (Navigation.IsGenerationWorkspace == true || Navigation.IsResultsView == true))
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
            string directory = _jobs.OutputDirectory(Session.Settings.GenerationDirectory);
            List<string> paths = new List<string>();
            if (all == true)
            {
                CancellationToken token = Session.Lifetime;
                paths.AddRange(await Task.Run(() => _jobs.LoadGenerationPaths(directory, token), token));
                if (Session.IsClosing == true)
                {
                    return;
                }
            }
            else
            {
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
            if (HasPreview == true && Navigation.IsResultsView == false)
            {
                return _dialogs.OpenFolderAsync(Path.GetDirectoryName(_previewPath));
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
                SelectedGeneration = item;
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

        private Task LoadGalleryPageAsync(int number, bool refreshSelection = false, bool markInterrupted = false)
        {
            if (Session.IsClosing == true)
            {
                return _galleryLoadTask;
            }
            _requestedGalleryPageNumber = number;
            int request = ++_galleryLoadRequest;
            _galleryLoadCancellation?.Cancel();
            Task previous = _galleryLoadTask;
            _galleryLoadTask = LoadGalleryPageCoreAsync(previous, request, number, refreshSelection, markInterrupted);
            return _galleryLoadTask;
        }

        private async Task LoadGalleryPageCoreAsync(Task previous, int request, int number,
            bool refreshSelection, bool markInterrupted)
        {
            await previous;
            if (Session.IsClosing == true || request != _galleryLoadRequest)
            {
                return;
            }
            using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            _galleryLoadCancellation = lifetime;
            CancellationToken token = lifetime.Token;
            List<Bitmap> thumbnails = new List<Bitmap>();
            IsGalleryLoading = true;
            GalleryError = string.Empty;
            string directory = Session.Settings.GenerationDirectory;
            GenerationImage remembered = null;
            if (SelectedGeneration != null)
            {
                remembered = SelectedGeneration.Image;
            }
            try
            {
                (GenerationPage Page, GenerationImage Preview) snapshot = await Task.Run(() =>
                {
                    if (markInterrupted == true)
                    {
                        _jobs.MarkInterrupted(token);
                    }
                    if (_hasMigratedGenerationMetadata == false)
                    {
                        _jobs.MigrateGenerationMetadata(directory, token);
                    }
                    GenerationPage page = _jobs.LoadGenerationPage(directory, number, GalleryPageSize, token);
                    GenerationImage preview = remembered;
                    if (refreshSelection == true && remembered != null)
                    {
                        preview = _jobs.FindGenerationImage(Path.GetDirectoryName(remembered.FilePath), remembered.FilePath, token);
                    }
                    if (preview == null && string.IsNullOrEmpty(page.LatestImagePath) == false)
                    {
                        preview = page.Images.FirstOrDefault(image => image.FilePath == page.LatestImagePath);
                        if (preview == null)
                        {
                            preview = _jobs.FindGenerationImage(directory, page.LatestImagePath, token);
                        }
                    }
                    return (page, preview);
                }, token);
                if (token.IsCancellationRequested == true || request != _galleryLoadRequest)
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
                }
                _galleryPageNumber = snapshot.Page.Number;
                _requestedGalleryPageNumber = snapshot.Page.Number;
                _galleryPageCount = snapshot.Page.PageCount;
                _generationCount = snapshot.Page.TotalCount;
                Raise(nameof(GalleryPageCaption));
                Raise(nameof(GalleryCountCaption));
                Raise(nameof(HasGenerations));
                Raise(nameof(IsGalleryEmpty));
                if (refreshSelection == true || SelectedGeneration == null)
                {
                    GenerationItem selected = null;
                    if (snapshot.Preview != null)
                    {
                        selected = items.FirstOrDefault(item => item.Image.FilePath == snapshot.Preview.FilePath);
                        if (selected == null)
                        {
                            selected = new GenerationItem(snapshot.Preview, _strings);
                        }
                    }
                    SelectedGeneration = selected;
                    SelectedResult = selected;
                }
                if (Navigation.IsResultsView == true)
                {
                    foreach (GenerationItem item in items)
                    {
                        item.BeginThumbnailLoad();
                    }
                    thumbnails = await Task.Run(() => LoadGalleryThumbnails(snapshot.Page.Images, token), token);
                    if (token.IsCancellationRequested == true || request != _galleryLoadRequest)
                    {
                        return;
                    }
                    for (int index = 0; index < items.Count; index++)
                    {
                        items[index].SetThumbnail(thumbnails[index]);
                        thumbnails[index] = null;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested == true)
            {
            }
            catch (Exception error)
            {
                if (Session.IsClosing == false && request == _galleryLoadRequest)
                {
                    LogHelper.Error(error);
                    GalleryError = _strings.GetExceptionMessage(error);
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
                if (request == _galleryLoadRequest)
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

        public GalleryViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs,
            GenerationViewModel generation, TrainingViewModel training, JobStore jobs) : base(session, navigation, work, strings, dialogs)
        {
            Generation = generation;
            Training = training;
            _jobs = jobs;
            GallerySelection.CollectionChanged += OnGallerySelectionChanged;
            UseResultAsReferenceCommand = Command(() => Generation.UseResultAsReferenceAsync(SelectedResult),
                () => Session.CanEditGenerationInputs == true && HasSelectedResult == true && HasPreview == true);
            SelectGenerationCommand = new AsyncCommand(SelectGenerationAsync,
                () => Session.IsClosing == false && IsGalleryLoading == false, OnCommandError);
            _commands.Add(SelectGenerationCommand);
            RefreshGalleryCommand = Command(() => RefreshGalleryAsync(),
                () => Session.IsClosing == false && IsGalleryLoading == false);
            PreviousGalleryPageCommand = Command(() => LoadGalleryPageAsync(_galleryPageNumber - 1),
                () => Session.IsClosing == false && IsGalleryLoading == false && _galleryPageNumber > 1);
            NextGalleryPageCommand = Command(() => LoadGalleryPageAsync(_galleryPageNumber + 1),
                () => Session.IsClosing == false && IsGalleryLoading == false && _galleryPageNumber < _galleryPageCount);
            ClearGallerySelectionCommand = Command(() =>
            {
                GallerySelection.Clear();
                return Task.CompletedTask;
            }, () => Session.IsClosing == false && HasGallerySelection == true);
            ExportCommand = Command(() => _dialogs.ExportImageAsync(_previewPath), () => Session.IsClosing == false && HasPreview == true);
            DeleteGenerationCommand = Command(() => DeleteGenerationsAsync(false),
                () => Session.IsIdle == true && IsGalleryLoading == false && HasGallerySelection == true);
            DeleteAllGenerationsCommand = Command(() => DeleteGenerationsAsync(true),
                () => Session.IsIdle == true && IsGalleryLoading == false && HasGenerations == true);
            ReusePromptCommand = Command(() => Generation.ReusePromptAsync(SelectedResult.Metadata),
                () => Session.IsClosing == false && SelectedResult != null && SelectedResult.HasMetadata == true);
            UseResultForTrainingCommand = Command(() => Training.UseResultForTrainingAsync(SelectedResult, _previewPath),
                () => Session.CanQueue == true && HasPreview == true && SelectedResult != null);
            OpenImageFolderCommand = Command(OpenImageFolderAsync, () => Session.IsClosing == false);
        }

        internal void ShowGeneratedImage(GenerationImage image)
        {
            SelectedGeneration = new GenerationItem(image, _strings);
        }

        internal Task WaitForLoadAsync()
        {
            return _galleryLoadTask;
        }

        protected override void OnNavigationPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Navigation.SelectedTab)
                && (Navigation.IsGenerationWorkspace == true || Navigation.IsResultsView == true))
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
            if (SelectedGeneration != null && Generations.Contains(SelectedGeneration) == false)
            {
                SelectedGeneration.Localize();
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

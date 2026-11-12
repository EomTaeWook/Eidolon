using Avalonia.Media.Imaging;
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
    public class AssetCreationViewModel : StudioPanelViewModel
    {
        private readonly AssetCreationService _service;
        private readonly JobStore _jobs;
        private readonly ISeedProvider _seeds;
        private readonly AssetImageLoader _images;
        private readonly GenerationReferenceDraft _reference = new GenerationReferenceDraft(GenerationReferenceMode.Reimagine, 0.45);
        private int _frameSize = AssetCreationInput.DefaultViewFrameSize;
        private int _columns = AssetCreationInput.ViewCount;
        private string _prompt = string.Empty;
        private bool _removeBackground = true;
        private bool _pixelArt;
        private bool _running;
        private bool _preparing;
        private int _collectionsRefreshRequest;
        private string _activeId = string.Empty;
        private int _shownFrame;

        public AssetCreationViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs, AssetCreationService service, JobStore jobs, ISeedProvider seeds, AssetImageLoader images) : base(session, navigation, work, strings, dialogs)
        {
            _service = service;
            _jobs = jobs;
            _seeds = seeds;
            _images = images;
            Preview = new AssetPreviewViewModel(session, navigation, work, strings, dialogs, jobs, service, images);
            Preview.PropertyChanged += OnPreviewChanged;
            _work.ProgressChanged += OnProgressChanged;
            CreateCommand = Command(CreateAsync, CanCreate);
            PickReferenceCommand = Command(PickReferenceAsync, () => Session.CanEditGenerationInputs == true);
            OpenResumeCollectionCommand = new AsyncCommand(OpenResumeCollectionAsync, CanOpenCollection, OnCommandError);
            _commands.Add(OpenResumeCollectionCommand);
            DeleteCollectionCommand = Command(DeleteCollectionAsync, CanDeleteCollection);
            ResumeCommand = Command(ResumeAsync, () =>
            {
                if (Preview.CanEditSelection == false)
                {
                    return false;
                }
                if (Preview.HasCollection == false)
                {
                    return false;
                }
                foreach (AssetFrameItem frame in Preview.Frames)
                {
                    if (frame.Frame.State != JobState.Completed)
                    {
                        return true;
                    }
                    if (File.Exists(frame.Frame.ImagePath) == false)
                    {
                        return true;
                    }
                }
                return false;
            });
            RegenerateCommand = Command(RegenerateAsync, () =>
            {
                if (Preview.CanEditSelection == false)
                {
                    return false;
                }
                return Preview.SelectedFrame != null;
            });
            ReplaceCommand = Command(ReplaceAsync, () =>
            {
                if (Preview.CanEditSelection == false)
                {
                    return false;
                }
                return Preview.SelectedFrame != null;
            });
            CancelCommand = Command(_work.CancelCurrentAsync, () =>
            {
                if (_running == false)
                {
                    return false;
                }
                return Session.IsClosing == false;
            });
            Localize();
        }

        public AssetPreviewViewModel Preview { get; private set; }
        public ObservableCollection<AssetCollectionChoice> Collections { get; private set; } = new ObservableCollection<AssetCollectionChoice>();
        public AsyncCommand CreateCommand { get; private set; }
        public AsyncCommand PickReferenceCommand { get; private set; }
        public AsyncCommand OpenResumeCollectionCommand { get; private set; }
        public AsyncCommand DeleteCollectionCommand { get; private set; }
        public AsyncCommand ResumeCommand { get; private set; }
        public AsyncCommand RegenerateCommand { get; private set; }
        public AsyncCommand ReplaceCommand { get; private set; }
        public AsyncCommand CancelCommand { get; private set; }
        public bool IsRunning
        {
            get
            {
                return _running;
            }
        }
        public bool HasReference
        {
            get
            {
                return _reference.HasImage;
            }
        }
        public Bitmap ReferenceThumbnail
        {
            get
            {
                return _reference.Thumbnail;
            }
        }
        public string ReferenceName
        {
            get
            {
                return _reference.ImageName;
            }
        }
        public string Prompt
        {
            get
            {
                return _prompt;
            }
            set
            {
                if (Set(ref _prompt, value) == true)
                {
                    RefreshCommands();
                }
            }
        }
        public int FrameSize
        {
            get
            {
                return _frameSize;
            }
            set
            {
                Set(ref _frameSize, value);
                RefreshCommands();
            }
        }
        public int Columns
        {
            get
            {
                return _columns;
            }
            set
            {
                Set(ref _columns, value);
                RefreshCommands();
            }
        }
        public bool RemoveBackground
        {
            get
            {
                return _removeBackground;
            }
            set
            {
                Set(ref _removeBackground, value);
            }
        }
        public bool PixelArt
        {
            get
            {
                return _pixelArt;
            }
            set
            {
                Set(ref _pixelArt, value);
            }
        }
        public bool HasResumeCollections
        {
            get
            {
                return Collections.Count > 0;
            }
        }

        public string CollectionStateCaption
        {
            get
            {
                if (Preview.Collection == null)
                {
                    return string.Empty;
                }
                return AssetCollectionChoice.GetStateCaption(Preview.Collection.State, _strings);
            }
        }

        private bool CanCreate()
        {
            if (Session.CanQueue == false)
            {
                return false;
            }
            if (_preparing == true)
            {
                return false;
            }
            if (HasReference == false)
            {
                return false;
            }
            if (_reference.AreOptionsValid == false)
            {
                return false;
            }
            if (FrameSize < 16)
            {
                return false;
            }
            if (FrameSize > AssetCreationInput.MaximumFrameSize)
            {
                return false;
            }
            if (Columns < 1)
            {
                return false;
            }
            if (Columns > AssetCreationInput.ViewCount)
            {
                return false;
            }
            return true;
        }

        private async Task PickReferenceAsync()
        {
            string path = await _dialogs.PickReferenceImageAsync();
            await LoadReferenceAsync(path);
        }

        internal async Task UseAsReferenceAsync(string path, string prompt)
        {
            if (Session.CanEditGenerationInputs == false)
            {
                return;
            }
            if (await LoadReferenceAsync(path) == false)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(prompt) == false)
            {
                Prompt = prompt;
            }
            await Navigation.OpenAssetCreationAsync();
        }

        private async Task<bool> LoadReferenceAsync(string path)
        {
            if (string.IsNullOrEmpty(path) == true)
            {
                return false;
            }
            if (Session.IsClosing == true)
            {
                return false;
            }
            int request = _reference.BeginLoad();
            byte[] data = await Task.Run(() => _images.Read(path), Session.Lifetime);
            Bitmap thumbnail = await Task.Run(() => _images.Load(data, 512, false), Session.Lifetime);
            try
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                if (_reference.IsCurrentLoad(request) == false)
                {
                    return false;
                }
                await Preview.ClearAsync();
                if (Session.IsClosing == true)
                {
                    return false;
                }
                if (_reference.IsCurrentLoad(request) == false)
                {
                    return false;
                }
                _reference.SetImage(data, thumbnail, Path.GetFileName(path));
                thumbnail = null;
                Raise(nameof(HasReference));
                Raise(nameof(ReferenceThumbnail));
                Raise(nameof(ReferenceName));
                RefreshCommands();
                return true;
            }
            finally
            {
                thumbnail?.Dispose();
            }
        }

        internal bool CanOpenCollection()
        {
            if (Session.CanQueue == false)
            {
                return false;
            }
            return _preparing == false;
        }

        internal async Task OpenCollectionAsync(string id, int frameNumber = 0)
        {
            if (CanOpenCollection() == false)
            {
                return;
            }
            _reference.BeginLoad();
            await Preview.ShowAsync(id, frameNumber);
            if (Session.IsClosing == true)
            {
                return;
            }
            if (Preview.IsCurrent == false)
            {
                return;
            }
            if (Preview.Collection.Id != id)
            {
                return;
            }
            if (Navigation.IsAssetCreationView == false)
            {
                await Navigation.OpenAssetCreationAsync();
            }
        }

        private Task OpenResumeCollectionAsync(object parameter)
        {
            if (parameter is AssetCollectionChoice choice)
            {
                if (Collections.Contains(choice) == true)
                {
                    return OpenCollectionAsync(choice.Id);
                }
            }
            return Task.CompletedTask;
        }

        private async Task CreateAsync()
        {
            StudioSettings settings = Session.Settings.Copy();
            settings.GenerationBackend = GenerationBackend.Codex;
            AssetCreationInput input = new AssetCreationInput
            {
                Kind = AssetCreationKind.FourViews,
                Prompt = Prompt,
                FrameWidth = FrameSize,
                FrameHeight = FrameSize,
                Columns = Columns,
                RemoveBackground = RemoveBackground,
                PixelArt = PixelArt,
                Reference = new GenerationReferenceInput
                {
                    ImageData = _reference.ImageData.ToArray(),
                    ImageName = _reference.ImageName,
                    Mode = GenerationReferenceMode.Reimagine
                }
            };
            await EnqueueAsync(settings, input, Session.Lifetime);
        }

        internal async Task<AssetCollection> EnqueueAsync(StudioSettings settings, AssetCreationInput input, CancellationToken token)
        {
            if (Session.CanQueue == false)
            {
                throw new StudioException(StudioMessageCode.GenerationRecordBusy);
            }
            if (_preparing == true)
            {
                throw new StudioException(StudioMessageCode.GenerationRecordBusy);
            }
            if (input.Kind != AssetCreationKind.FourViews)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (input.Columns > AssetCreationInput.ViewCount)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            _preparing = true;
            RefreshCommands();
            AssetCollection collection = null;
            bool accepted = false;
            try
            {
                collection = await Task.Run(() => _service.Prepare(settings, input, token), token);
                token.ThrowIfCancellationRequested();
                if (Session.CanQueue == false)
                {
                    throw new StudioException(StudioMessageCode.GenerationRecordBusy);
                }
                string collectionId = collection.Id;
                _work.Enqueue(_strings.GetString("EidolonText603") + " · " + collection.Prompt, workToken => RunAsync(collectionId, null, workToken), () => _service.Discard(collectionId));
                accepted = true;
                await UpdatePreviewAsync(collectionId);
                return collection;
            }
            catch
            {
                if (accepted == false)
                {
                    if (collection != null)
                    {
                        _service.Discard(collection.Id);
                    }
                }
                throw;
            }
            finally
            {
                _preparing = false;
                RefreshCommands();
            }
        }

        internal Task EnqueueExistingAsync(string id, int number = 0, string prompt = null, long seed = 0)
        {
            if (Session.IsIdle == false)
            {
                throw new StudioException(StudioMessageCode.GenerationRecordBusy);
            }
            if (_preparing == true)
            {
                throw new StudioException(StudioMessageCode.GenerationRecordBusy);
            }
            AssetCollection collection = _jobs.LoadAssetCollection(id);
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (number < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (number > collection.Frames.Count)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (number > 0)
            {
                if (string.IsNullOrWhiteSpace(prompt) == true)
                {
                    throw new StudioException(StudioMessageCode.PromptRequired);
                }
                if (prompt.Length > 16000)
                {
                    throw new StudioException(StudioMessageCode.PromptRequired);
                }
            }
            collection.State = JobState.Preparing;
            _jobs.SaveAssetCollection(collection);
            Func<CancellationToken, Task> action = null;
            if (number > 0)
            {
                action = token => _service.RegenerateAsync(id, number, prompt, seed, _work.Progress, token);
            }
            _work.Enqueue(_strings.GetString("EidolonText603") + " · " + collection.Prompt, token => RunAsync(id, action, token), () => _service.Discard(id));
            return Task.CompletedTask;
        }

        private async Task RunAsync(string id, Func<CancellationToken, Task> action, CancellationToken token)
        {
            _running = true;
            _activeId = id;
            _shownFrame = 0;
            Raise(nameof(IsRunning));
            RefreshCommands();
            try
            {
                if (Preview.RequestedCollectionId == id)
                {
                    await UpdatePreviewAsync(id);
                }
                if (action != null)
                {
                    await action(token);
                }
                else
                {
                    await _service.GenerateAsync(id, _work.Progress, token);
                }
            }
            finally
            {
                _running = false;
                Raise(nameof(IsRunning));
                RefreshCommands();
                if (Session.IsClosing == false)
                {
                    if (Preview.RequestedCollectionId == id)
                    {
                        await UpdatePreviewAsync(id);
                    }
                }
            }
        }

        private Task ResumeAsync()
        {
            return EnqueueExistingAsync(Preview.Collection.Id);
        }

        private bool CanDeleteCollection()
        {
            if (Session.IsIdle == false)
            {
                return false;
            }
            if (Session.CanQueue == false)
            {
                return false;
            }
            if (_preparing == true)
            {
                return false;
            }
            if (Preview.IsCurrent == false)
            {
                return false;
            }
            if (Preview.Collection.State == JobState.Preparing)
            {
                return false;
            }
            return Preview.Collection.State != JobState.Running;
        }

        private async Task DeleteCollectionAsync()
        {
            string id = Preview.Collection.Id;
            string caption = Preview.Title;
            if (await _dialogs.ConfirmDeleteAsync(_strings.GetString("EidolonText656"), _strings.Format("EidolonText657", caption)) == false)
            {
                return;
            }
            if (CanDeleteCollection() == false)
            {
                return;
            }
            if (Preview.Collection.Id != id)
            {
                return;
            }
            await DeleteCollectionAsync(id, caption);
        }

        internal async Task DeleteCollectionAsync(string id, string caption)
        {
            if (Session.IsIdle == false)
            {
                return;
            }
            if (Session.CanQueue == false)
            {
                return;
            }
            if (_preparing == true)
            {
                return;
            }
            await Session.WorkAsync(async token =>
            {
                try
                {
                    if (Preview.RequestedCollectionId == id)
                    {
                        await Preview.ClearAsync();
                    }
                    await Task.Run(() => _jobs.DeleteAssetCollection(id, token), token);
                    Session.Status = _strings.Format("EidolonText658", caption);
                }
                finally
                {
                    await RefreshCollectionsAsync();
                    RefreshCommands();
                }
            });
        }
        private Task RegenerateAsync()
        {
            long seed = 0;
            if (Preview.HasSeed == true)
            {
                seed = _seeds.Next();
            }
            return EnqueueExistingAsync(Preview.Collection.Id, Preview.SelectedFrame.Frame.Number, Preview.SelectedFrame.Prompt, seed);
        }
        private async Task ReplaceAsync()
        {
            string id = Preview.Collection.Id;
            int number = Preview.SelectedFrame.Frame.Number;
            string path = await _dialogs.PickReferenceImageAsync();
            if (string.IsNullOrEmpty(path) == true || Session.IsClosing == true)
            {
                return;
            }
            byte[] bytes = await Task.Run(() => _images.Read(path), Session.Lifetime);
            if (Session.IsIdle == false || _preparing == true)
            {
                throw new StudioException(StudioMessageCode.GenerationRecordBusy);
            }
            await Session.WorkAsync(async token =>
            {
                await Task.Run(() => _service.ReplaceFrame(id, number, bytes, token), token);
                await UpdatePreviewAsync(id);
            });
        }

        private async Task UpdatePreviewAsync(string id)
        {
            if (Session.IsClosing == true)
            {
                return;
            }
            try
            {
                await Preview.ShowAsync(id);
                await RefreshCollectionsAsync();
            }
            catch (Exception error)
            {
                if (Session.IsClosing == false)
                {
                    Session.ReportError(error);
                }
            }
        }

        public async Task RefreshCollectionsAsync()
        {
            if (Session.IsClosing == true)
            {
                return;
            }
            int requestNumber = ++_collectionsRefreshRequest;
            string directory = Session.Settings.GenerationDirectory;
            List<AssetCollection> collections = await Task.Run(() => LoadResumeCollections(directory, Session.Lifetime), Session.Lifetime);
            if (Session.IsClosing == true)
            {
                return;
            }
            if (requestNumber != _collectionsRefreshRequest)
            {
                return;
            }
            Collections.Clear();
            foreach (AssetCollection collection in collections)
            {
                Collections.Add(new AssetCollectionChoice(collection, _strings));
            }
            Raise(nameof(HasResumeCollections));
            RefreshCommands();
        }

        private List<AssetCollection> LoadResumeCollections(string directory, CancellationToken cancellationToken)
        {
            List<AssetCollection> collections = _jobs.LoadAssetCollections(cancellationToken);
            collections.RemoveAll(collection => collection.State is JobState.Preparing or JobState.Running);
            if (collections.Count == 0)
            {
                return collections;
            }
            HashSet<string> hiddenCollectionIds = collections.Select(collection => collection.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string path in _jobs.LoadGenerationPaths(directory, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                GenerationMetadata metadata = _jobs.FindGenerationImage(directory, path, cancellationToken)?.Metadata;
                if (metadata == null)
                {
                    continue;
                }
                if (metadata.AssetFrameNumber < 1)
                {
                    continue;
                }
                AssetCollection savedCollection = collections.FirstOrDefault(collection => string.Equals(collection.Id, metadata.AssetCollectionId, StringComparison.OrdinalIgnoreCase));
                if (savedCollection == null)
                {
                    continue;
                }
                if (metadata.AssetFrameNumber > savedCollection.Frames.Count)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(metadata.AssetFrameLabel) == true)
                {
                    continue;
                }
                hiddenCollectionIds.Remove(metadata.AssetCollectionId);
                if (hiddenCollectionIds.Count == 0)
                {
                    break;
                }
            }
            collections.RemoveAll(collection => hiddenCollectionIds.Contains(collection.Id) == false);
            return collections;
        }
        private async void OnProgressChanged(WorkProgress progress)
        {
            if (_running == false || Preview.RequestedCollectionId != _activeId
                || progress.Code != StudioMessageCode.AssetFrameGenerating || progress.Arguments.Length == 0)
            {
                return;
            }
            int frame = Convert.ToInt32(progress.Arguments[0]);
            if (frame == _shownFrame)
            {
                return;
            }
            _shownFrame = frame;
            try
            {
                await Preview.ShowAsync(_activeId);
            }
            catch (Exception error)
            {
                if (Session.IsClosing == false)
                {
                    Session.ReportError(error);
                }
            }
        }
        private void OnPreviewChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Preview.Collection))
            {
                Raise(nameof(CollectionStateCaption));
            }
            RefreshCommands();
        }
        public override void Localize()
        {
            Preview.Localize();
            foreach (AssetCollectionChoice collection in Collections)
            {
                collection.Localize(_strings);
            }
            base.Localize();
        }
        public override void Dispose()
        {
            _work.ProgressChanged -= OnProgressChanged;
            Preview.PropertyChanged -= OnPreviewChanged;
            _reference.Dispose();
            Preview.Dispose();
            base.Dispose();
        }
    }
}

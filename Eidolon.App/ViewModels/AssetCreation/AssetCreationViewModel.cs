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
        private int _kindIndex;
        private int _frameCount = 8;
        private int _frameSize = AssetCreationInput.DefaultSpriteFrameSize;
        private int _columns = 4;
        private int _fps = 10;
        private string _prompt = string.Empty;
        private string _actionPrompt = string.Empty;
        private string _frameDescriptions = string.Empty;
        private bool _removeBackground = true;
        private bool _pixelArt;
        private bool _running;
        private bool _preparing;
        private bool _localizing;
        private AssetCollectionChoice _selectedCollection;
        private string _activeId = string.Empty;
        private int _shownFrame;

        public AssetCreationViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work,
            StringHelper strings, DesktopDialogs dialogs, AssetsViewModel assets, AssetCreationService service, JobStore jobs,
            ISeedProvider seeds, AssetImageLoader images) : base(session, navigation, work, strings, dialogs)
        {
            Assets = assets;
            _service = service;
            _jobs = jobs;
            _seeds = seeds;
            _images = images;
            Preview = new AssetPreviewViewModel(session, navigation, work, strings, dialogs, jobs, service, images);
            Preview.PropertyChanged += OnPreviewChanged;
            Assets.PropertyChanged += OnAssetsChanged;
            _work.ProgressChanged += OnProgressChanged;
            CreateCommand = Command(CreateAsync, CanCreate);
            PickReferenceCommand = Command(PickReferenceAsync, () => Session.CanEditGenerationInputs == true);
            RefreshCollectionsCommand = Command(RefreshCollectionsAsync, () => Session.IsInitialized == true && Session.IsClosing == false);
            ResumeCommand = Command(ResumeAsync, () => Preview.CanEditSelection == true && Preview.HasCollection == true
                && Preview.Frames.Any(frame => frame.Frame.State != JobState.Completed || File.Exists(frame.Frame.ImagePath) == false) == true);
            RegenerateCommand = Command(RegenerateAsync, () => Preview.CanEditSelection == true && Preview.SelectedFrame != null);
            ReplaceCommand = Command(ReplaceAsync, () => Preview.CanEditSelection == true && Preview.SelectedFrame != null);
            CancelCommand = Command(_work.CancelCurrentAsync, () => _running == true && Session.IsClosing == false);
            Localize();
        }

        public AssetsViewModel Assets { get; private set; }
        public AssetPreviewViewModel Preview { get; private set; }
        public ObservableCollection<string> Kinds { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<AssetCollectionChoice> Collections { get; private set; } = new ObservableCollection<AssetCollectionChoice>();
        public AsyncCommand CreateCommand { get; private set; }
        public AsyncCommand PickReferenceCommand { get; private set; }
        public AsyncCommand RefreshCollectionsCommand { get; private set; }
        public AsyncCommand ResumeCommand { get; private set; }
        public AsyncCommand RegenerateCommand { get; private set; }
        public AsyncCommand ReplaceCommand { get; private set; }
        public AsyncCommand CancelCommand { get; private set; }
        public bool IsSprite
        {
            get
            {
                return _kindIndex == 0;
            }
        }
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
        public int KindIndex
        {
            get
            {
                return _kindIndex;
            }
            set
            {
                if (_localizing == true || value < 0 || value > 2)
                {
                    return;
                }
                if (Set(ref _kindIndex, value) == true)
                {
                    Raise(nameof(IsSprite));
                    if (IsSprite == false && FrameSize == AssetCreationInput.DefaultSpriteFrameSize)
                    {
                        FrameSize = AssetCreationInput.DefaultViewFrameSize;
                    }
                    RefreshCommands();
                }
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
        public string ActionPrompt
        {
            get
            {
                return _actionPrompt;
            }
            set
            {
                if (Set(ref _actionPrompt, value) == true)
                {
                    RefreshCommands();
                }
            }
        }
        public string FrameDescriptions
        {
            get
            {
                return _frameDescriptions;
            }
            set
            {
                Set(ref _frameDescriptions, value);
                RefreshCommands();
            }
        }
        public int FrameCount
        {
            get
            {
                return _frameCount;
            }
            set
            {
                Set(ref _frameCount, value);
                RefreshCommands();
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
        public int FramesPerSecond
        {
            get
            {
                return _fps;
            }
            set
            {
                Set(ref _fps, value);
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
        public double ChangeStrength
        {
            get
            {
                return _reference.ChangeStrength;
            }
            set
            {
                _reference.ChangeStrength = value;
                Raise();
                RefreshCommands();
            }
        }
        public AssetCollectionChoice SelectedCollection
        {
            get
            {
                return _selectedCollection;
            }
            set
            {
                if (Set(ref _selectedCollection, value) == true && value != null)
                {
                    ShowSelectedCollection(value.Id);
                }
            }
        }

        public bool IsComfyAssetCreation
        {
            get
            {
                return Session.Settings.GenerationBackend == GenerationBackend.ComfyUI;
            }
        }

        protected override void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            Raise(nameof(IsComfyAssetCreation));
            base.OnSessionPropertyChanged(sender, args);
        }

        private bool CanCreate()
        {
            if (IsSprite == true && string.IsNullOrWhiteSpace(FrameDescriptions) == false)
            {
                string[] descriptions = FrameDescriptions.Replace("\r", string.Empty).Split('\n');
                if (descriptions.Length != FrameCount || descriptions.Any(text => string.IsNullOrWhiteSpace(text) == true) == true)
                {
                    return false;
                }
            }
            return Session.CanQueue == true && Session.Settings.GenerationBackend == GenerationBackend.ComfyUI
                && _preparing == false && Assets.SelectedModel != null && HasReference == true
                && _reference.AreOptionsValid == true && string.IsNullOrWhiteSpace(Prompt) == false
                && (IsSprite == false || string.IsNullOrWhiteSpace(ActionPrompt) == false)
                && FrameCount >= 2 && FrameCount <= AssetCreationInput.MaximumFrames
                && FrameSize >= 16 && FrameSize <= AssetCreationInput.MaximumFrameSize
                && Columns >= 1 && Columns <= AssetCreationInput.MaximumFrames && FramesPerSecond >= 1 && FramesPerSecond <= 60;
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
            if (string.IsNullOrEmpty(path) == true || Session.IsClosing == true)
            {
                return false;
            }
            int request = _reference.BeginLoad();
            byte[] data = await Task.Run(() => _images.Read(path), Session.Lifetime);
            Bitmap thumbnail = await Task.Run(() => _images.Load(data, 192, false), Session.Lifetime);
            if (Session.IsClosing == true || _reference.IsCurrentLoad(request) == false)
            {
                thumbnail.Dispose();
                return false;
            }
            _reference.SetImage(data, thumbnail, Path.GetFileName(path));
            Raise(nameof(HasReference));
            Raise(nameof(ReferenceThumbnail));
            Raise(nameof(ReferenceName));
            RefreshCommands();
            return true;
        }

        private async Task CreateAsync()
        {
            AssetCreationInput input = new AssetCreationInput
            {
                Kind = (AssetCreationKind)KindIndex, Prompt = Prompt, ActionPrompt = ActionPrompt,
                FrameCount = FrameCount, FrameWidth = FrameSize, FrameHeight = FrameSize, Columns = Columns,
                FramesPerSecond = FramesPerSecond, RemoveBackground = RemoveBackground, PixelArt = PixelArt, Seed = _seeds.Next(),
                Reference = new GenerationReferenceInput
                {
                    ImageData = _reference.ImageData.ToArray(), ImageName = _reference.ImageName,
                    ChangeStrength = ChangeStrength, Mode = GenerationReferenceMode.Reimagine
                }
            };
            if (IsSprite == true && string.IsNullOrWhiteSpace(FrameDescriptions) == false)
            {
                input.FrameDescriptions = FrameDescriptions.Replace("\r", string.Empty).Split('\n').Select(text => text.Trim()).ToList();
            }
            List<ModelAsset> loras = Assets.Loras.Where(item => item.IsSelected == true).Select(item => item.Asset.Copy()).ToList();
            await EnqueueAsync(Session.CreateActiveSettings(Session.Settings), Assets.SelectedModel.Asset.Copy(), loras, input, Session.Lifetime);
        }

        internal async Task<AssetCollection> EnqueueAsync(StudioSettings settings, ModelAsset model, List<ModelAsset> loras,
            AssetCreationInput input, CancellationToken token)
        {
            if (Session.CanQueue == false || _preparing == true)
            {
                throw new StudioException(StudioMessageCode.GenerationRecordBusy);
            }
            _preparing = true;
            RefreshCommands();
            AssetCollection collection = null;
            bool accepted = false;
            try
            {
                collection = await Task.Run(() => _service.Prepare(settings, model, loras, input, token), token);
                token.ThrowIfCancellationRequested();
                if (Session.CanQueue == false)
                {
                    throw new StudioException(StudioMessageCode.GenerationRecordBusy);
                }
                string id = collection.Id;
                _work.Enqueue(_strings.GetString("EidolonText603") + " · " + collection.Prompt,
                    workToken => RunAsync(id, null, workToken), () => _service.Discard(id));
                accepted = true;
                await UpdatePreviewAsync(id);
                return collection;
            }
            catch
            {
                if (accepted == false && collection != null)
                {
                    _service.Discard(collection.Id);
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
            if (Session.IsIdle == false || _preparing == true)
            {
                throw new StudioException(StudioMessageCode.GenerationRecordBusy);
            }
            AssetCollection collection = _jobs.LoadAssetCollection(id);
            if (number < 0 || number > collection.Frames.Count || seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            if (number > 0 && (string.IsNullOrWhiteSpace(prompt) == true || prompt.Length > 16000))
            {
                throw new StudioException(StudioMessageCode.PromptRequired);
            }
            collection.State = JobState.Preparing;
            _jobs.SaveAssetCollection(collection);
            Func<CancellationToken, Task> action = null;
            if (number > 0)
            {
                action = token => _service.RegenerateAsync(id, number, prompt, seed, _work.Progress, token);
            }
            _work.Enqueue(_strings.GetString("EidolonText603") + " · " + collection.Prompt,
                token => RunAsync(id, action, token), () => _service.Discard(id));
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
                await UpdatePreviewAsync(id);
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
        private Task RegenerateAsync()
        {
            return EnqueueExistingAsync(Preview.Collection.Id, Preview.SelectedFrame.Frame.Number, Preview.SelectedFrame.Prompt, _seeds.Next());
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
            List<AssetCollection> collections = await Task.Run(() => _jobs.LoadAssetCollections(Session.Lifetime), Session.Lifetime);
            if (Session.IsClosing == true)
            {
                return;
            }
            string selectedId = string.Empty;
            if (Preview.HasCollection == true)
            {
                selectedId = Preview.Collection.Id;
            }
            else if (_selectedCollection != null)
            {
                selectedId = _selectedCollection.Id;
            }
            _selectedCollection = null;
            Collections.Clear();
            foreach (AssetCollection collection in collections)
            {
                Collections.Add(new AssetCollectionChoice(collection));
            }
            _selectedCollection = Collections.FirstOrDefault(item => item.Id == selectedId);
            Raise(nameof(SelectedCollection));
        }
        private async void ShowSelectedCollection(string id)
        {
            try
            {
                await Preview.ShowAsync(id);
            }
            catch (Exception error)
            {
                if (Session.IsClosing == false)
                {
                    Session.ReportError(error);
                }
            }
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
            RefreshCommands();
        }
        private void OnAssetsChanged(object sender, PropertyChangedEventArgs args)
        {
            RefreshCommands();
        }
        public override void Localize()
        {
            int kind = _kindIndex;
            _localizing = true;
            Kinds.Clear();
            foreach (string key in new[] { "EidolonText605", "EidolonText606", "EidolonText607" })
            {
                Kinds.Add(_strings.GetString(key));
            }
            _kindIndex = kind;
            _localizing = false;
            Raise(nameof(KindIndex));
            Preview.Localize();
            base.Localize();
        }
        public override void Dispose()
        {
            _work.ProgressChanged -= OnProgressChanged;
            Assets.PropertyChanged -= OnAssetsChanged;
            Preview.PropertyChanged -= OnPreviewChanged;
            _reference.Dispose();
            Preview.Dispose();
            base.Dispose();
        }
    }
}

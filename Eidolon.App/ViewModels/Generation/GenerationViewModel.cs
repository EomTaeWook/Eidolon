using System.ComponentModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using SkiaSharp;

namespace Eidolon.App.ViewModels
{
    public class GenerationViewModel : StudioPanelViewModel
    {
        public AssetsViewModel Assets { get; private set; }
        public event Action<GenerationImage> ImageGenerated;
        private readonly JobStore _jobs;
        private readonly StudioService _studio;
        private readonly ISeedProvider _seeds;
        private string _prompt = string.Empty;
        private string _editingPrompt = string.Empty;
        private bool _useRandomGenerationSeed = true;
        private string _generationSeed = "0";
        private string _lastQueuedGenerationSeed = string.Empty;
        private readonly GenerationReferenceDraft _generationReference = new GenerationReferenceDraft(GenerationReferenceMode.Reimagine, 0.65);
        private readonly GenerationReferenceDraft _editingReference = new GenerationReferenceDraft(GenerationReferenceMode.Restyle, 0.35);
        private bool _removeBackground;
        public AsyncCommand GenerateCommand { get; private set; }
        public AsyncCommand EditImageCommand { get; private set; }
        public AsyncCommand PickReferenceImageCommand { get; private set; }
        public AsyncCommand ClearReferenceImageCommand { get; private set; }

        public bool IsComfyGeneration
        {
            get
            {
                return Session.Settings.GenerationBackend == GenerationBackend.ComfyUI;
            }
        }

        public bool IsGenerationBackendReady
        {
            get
            {
                if (IsComfyGeneration == false)
                {
                    return true;
                }
                return Assets.SelectedModel != null;
            }
        }

        private GenerationReferenceDraft ActiveReference
        {
            get
            {
                if (Navigation.IsEditingView == true)
                {
                    return _editingReference;
                }
                return _generationReference;
            }
        }

        public double ReferenceChangeStrength
        {
            get
            {
                return ActiveReference.ChangeStrength;
            }
            set
            {
                if (ActiveReference.ChangeStrength != value)
                {
                    ActiveReference.ChangeStrength = value;
                    Raise(nameof(ReferenceChangeStrength));
                    Raise(nameof(ReferenceStrengthCaption));
                    RefreshCommands();
                }
            }
        }

        public string ReferenceStrengthCaption
        {
            get
            {
                return ReferenceChangeStrength.ToString("P0", CultureInfo.InvariantCulture);
            }
        }

        public bool HasReferenceImage
        {
            get
            {
                return ActiveReference.HasImage;
            }
        }

        public Bitmap ReferenceThumbnail
        {
            get
            {
                return ActiveReference.Thumbnail;
            }
        }

        public string ReferenceImageName
        {
            get
            {
                return ActiveReference.ImageName;
            }
        }

        public bool AreReferenceOptionsValid
        {
            get
            {
                return ActiveReference.AreOptionsValid;
            }
        }

        public string ReferenceImageHint
        {
            get
            {
                if (IsComfyGeneration == false)
                {
                    return _strings.GetString("EidolonText539");
                }
                if (Navigation.IsEditingView == true)
                {
                    return _strings.GetString("EidolonText447");
                }
                return _strings.GetString("EidolonText446");
            }
        }

        public string GenerationActionCaption
        {
            get
            {
                if (Navigation.IsEditingView == true)
                {
                    return _strings.GetString("EidolonText445");
                }
                return _strings.GetString("EidolonText438");
            }
        }

        public string GenerationInputCaption
        {
            get
            {
                if (Navigation.IsEditingView == true)
                {
                    return _strings.GetString("EidolonText456");
                }
                return _strings.GetString("EidolonText106");
            }
        }

        public AsyncCommand GenerationSubmitCommand
        {
            get
            {
                if (Navigation.IsEditingView == true)
                {
                    return EditImageCommand;
                }
                return GenerateCommand;
            }
        }

        public string GenerationPositivePrompt
        {
            get
            {
                return Session.Settings.PositivePrompt;
            }
        }

        public string GenerationNegativePrompt
        {
            get
            {
                return Session.Settings.NegativePrompt;
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

        public bool UseRandomGenerationSeed
        {
            get
            {
                return _useRandomGenerationSeed;
            }
            set
            {
                if (Set(ref _useRandomGenerationSeed, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public string GenerationSeed
        {
            get
            {
                return _generationSeed;
            }
            set
            {
                if (Set(ref _generationSeed, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public bool IsGenerationSeedValid
        {
            get
            {
                if (IsComfyGeneration == false)
                {
                    return true;
                }
                if (UseRandomGenerationSeed == true)
                {
                    return true;
                }
                return long.TryParse(GenerationSeed, NumberStyles.None, CultureInfo.InvariantCulture, out long seed) == true && seed >= 0;
            }
        }

        public string GenerationSeedValidationHint
        {
            get
            {
                return _strings.Format("EidolonText421", 0, long.MaxValue);
            }
        }

        public bool HasQueuedGenerationSeed
        {
            get
            {
                return IsComfyGeneration == true && string.IsNullOrEmpty(_lastQueuedGenerationSeed) == false;
            }
        }

        public string QueuedGenerationSeedCaption
        {
            get
            {
                return _strings.Format("EidolonText422", _lastQueuedGenerationSeed);
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

        public string EditingPrompt
        {
            get
            {
                return _editingPrompt;
            }
            set
            {
                if (Set(ref _editingPrompt, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        private async Task PickReferenceImageAsync()
        {
            GenerationReferenceDraft reference = ActiveReference;
            string path = await _dialogs.PickReferenceImageAsync();
            if (string.IsNullOrWhiteSpace(path) == false && Session.IsClosing == false)
            {
                await LoadReferenceImageAsync(reference, path);
            }
        }

        private async Task<bool> LoadReferenceImageAsync(GenerationReferenceDraft reference, string path, string imageName = null)
        {
            int request = reference.BeginLoad();
            CancellationToken token = Session.Lifetime;
            (byte[] Data, Bitmap Thumbnail) image = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using FileStream input = File.OpenRead(path);
                if (input.Length == 0 || input.Length > GenerationReferenceInput.MaximumImageBytes)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                byte[] data = new byte[(int)input.Length];
                input.ReadExactly(data);
                using SKMemoryStream encoded = new SKMemoryStream(data);
                using SKCodec codec = SKCodec.Create(encoded);
                if (codec == null || codec.Info.Width < 1 || codec.Info.Height < 1
                    || (long)codec.Info.Width * codec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
                token.ThrowIfCancellationRequested();
                using MemoryStream thumbnailInput = new MemoryStream(data, false);
                Bitmap thumbnail = null;
                if (codec.Info.Height > codec.Info.Width)
                {
                    thumbnail = Bitmap.DecodeToHeight(thumbnailInput, 128);
                }
                else
                {
                    thumbnail = Bitmap.DecodeToWidth(thumbnailInput, 128);
                }
                return (data, thumbnail);
            }, token);
            if (Session.IsClosing == true || reference.IsCurrentLoad(request) == false)
            {
                image.Thumbnail.Dispose();
                return false;
            }
            if (string.IsNullOrWhiteSpace(imageName) == true)
            {
                imageName = Path.GetFileName(path);
            }
            reference.SetImage(image.Data, image.Thumbnail, imageName);
            RefreshReferenceInputs();
            Session.Error = string.Empty;
            return true;
        }

        private Task ClearReferenceImageAsync()
        {
            ActiveReference.Clear();
            RefreshReferenceInputs();
            Session.Error = string.Empty;
            return Task.CompletedTask;
        }

        private void RefreshReferenceInputs()
        {
            Raise(nameof(ReferenceThumbnail));
            Raise(nameof(ReferenceImageName));
            Raise(nameof(HasReferenceImage));
            Raise(nameof(GenerationActionCaption));
            Raise(nameof(ReferenceChangeStrength));
            Raise(nameof(ReferenceStrengthCaption));
            Raise(nameof(ReferenceImageHint));
            Raise(nameof(AreReferenceOptionsValid));
            RefreshCommands();
        }

        internal async Task UseResultAsReferenceAsync(GenerationItem result)
        {
            if (await LoadReferenceImageAsync(_editingReference, result.Image.FilePath) == false)
            {
                return;
            }
            _editingReference.ChangeStrength = 0.35;
            if (result.HasMetadata == true)
            {
                EditingPrompt = result.Metadata.UserPrompt;
                AssetItem model = Assets.FindReusableAsset(Assets.Models, result.Metadata.Model);
                if (model != null)
                {
                    Assets.SelectedModel = model;
                }
            }
            else
            {
                EditingPrompt = string.Empty;
            }
            Navigation.SelectedTab = 1;
            RefreshReferenceInputs();
        }

        private Task QueueGenerationAsync()
        {
            return QueueGenerationAsync(false);
        }

        private Task QueueEditingAsync()
        {
            return QueueGenerationAsync(true);
        }

        private Task QueueGenerationAsync(bool editing)
        {
            GenerationReferenceDraft draft = _generationReference;
            string prompt = Prompt;
            string titleKey = "EidolonText106";
            if (editing == true)
            {
                prompt = EditingPrompt;
                titleKey = "EidolonText456";
                draft = _editingReference;
                if (draft.HasImage == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidReferenceImage);
                }
            }
            if (draft.AreOptionsValid == false)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
            }
            if (string.IsNullOrWhiteSpace(prompt) == true)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText001"));
            }
            long seed = 0;
            if (IsComfyGeneration == false)
            {
                seed = 0;
            }
            else if (UseRandomGenerationSeed == true)
            {
                seed = _seeds.Next();
            }
            else
            {
                if (long.TryParse(GenerationSeed, NumberStyles.None, CultureInfo.InvariantCulture, out seed) == false)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationSeed, 0, long.MaxValue);
                }
                if (seed < 0)
                {
                    throw new StudioException(StudioMessageCode.InvalidGenerationSeed, 0, long.MaxValue);
                }
            }
            ModelAsset model = null;
            if (IsComfyGeneration == true)
            {
                if (Assets.SelectedModel != null)
                {
                    model = Assets.SelectedModel.Asset.Copy();
                }
            }
            DesktopSettings settings = Session.CreateActiveSettings(Session.Settings);
            bool removeBackground = RemoveBackground;
            GenerationReferenceInput reference = null;
            if (draft.HasImage == true)
            {
                reference = new GenerationReferenceInput
                {
                    ImageData = draft.ImageData,
                    ImageName = draft.ImageName,
                    Mode = draft.Mode,
                    ChangeStrength = draft.ChangeStrength
                };
            }
            List<ModelAsset> loras = new List<ModelAsset>();
            if (IsComfyGeneration == true)
            {
                loras = Assets.Loras.Where(lora => lora.IsSelected == true).Select(lora => lora.Asset.Copy()).ToList();
            }
            _work.Enqueue(_strings.GetString(titleKey) + " · " + prompt,
                token => GenerateAsync(settings, prompt, model, loras, removeBackground, seed, reference, token));
            _lastQueuedGenerationSeed = string.Empty;
            if (IsComfyGeneration == true)
            {
                _lastQueuedGenerationSeed = seed.ToString(CultureInfo.InvariantCulture);
            }
            Raise(nameof(HasQueuedGenerationSeed));
            Raise(nameof(QueuedGenerationSeedCaption));
            return Task.CompletedTask;
        }

        private async Task GenerateAsync(DesktopSettings settings, string prompt, ModelAsset model,
            List<ModelAsset> loras, bool removeBackground, long seed, GenerationReferenceInput reference, CancellationToken token)
        {
            JobRecord job = await _studio.GenerateAsync(settings, prompt, model, loras, removeBackground, seed, reference, _work.Progress, token);
            if (Navigation.IsResultsView == false)
            {
                string path = _jobs.ImagePath(job, job.ImageFiles.Last());
                GenerationImage image = await Task.Run(() => _jobs.FindGenerationImage(job.OutputDirectory, path, token), token);
                if (image != null)
                {
                    ImageGenerated?.Invoke(image);
                }
            }
            Session.Status = _strings.GetString("EidolonText004");
            Session.Percent = 100;
        }

        internal async Task ReusePromptAsync(GenerationMetadata job)
        {
            GenerationReferenceDraft reference = _generationReference;
            int targetTab = 0;
            if (job.ReferenceMode == GenerationReferenceMode.Restyle)
            {
                reference = _editingReference;
                targetTab = 1;
                EditingPrompt = job.UserPrompt;
            }
            else
            {
                Prompt = job.UserPrompt;
            }
            reference.Clear();
            RefreshReferenceInputs();
            if (job.ReferenceMode != GenerationReferenceMode.None)
            {
                if (job.GenerationBackend == GenerationBackend.ComfyUI)
                {
                    reference.ChangeStrength = job.Denoise;
                }
                try
                {
                    await LoadReferenceImageAsync(reference, job.ReferenceImagePath, job.ReferenceImageName);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    reference.SetMissingImage(job.ReferenceImageName);
                    RefreshReferenceInputs();
                    Session.Error = _strings.GetString("EidolonText454");
                }
            }
            RemoveBackground = job.RemoveBackground;
            if (IsComfyGeneration == false)
            {
                UseRandomGenerationSeed = true;
                Navigation.SelectedTab = targetTab;
                RefreshReferenceInputs();
                return;
            }
            if (job.GenerationBackend == GenerationBackend.Codex)
            {
                UseRandomGenerationSeed = true;
                Navigation.SelectedTab = targetTab;
                RefreshReferenceInputs();
                return;
            }
            GenerationSeed = job.Seed.ToString(CultureInfo.InvariantCulture);
            UseRandomGenerationSeed = false;
            Assets.SelectedModel = Assets.FindReusableAsset(Assets.Models, job.Model);
            bool missingAsset = Assets.SelectedModel == null;
            foreach (AssetItem lora in Assets.Loras)
            {
                lora.IsSelected = false;
            }
            foreach (ModelAsset recordedLora in job.Loras)
            {
                AssetItem lora = Assets.FindReusableAsset(Assets.Loras, recordedLora);
                if (lora != null)
                {
                    lora.IsSelected = true;
                }
                else
                {
                    missingAsset = true;
                }
            }
            if (missingAsset == true)
            {
                Session.Error = _strings.GetString("EidolonText370");
            }
            Navigation.SelectedTab = targetTab;
            RefreshReferenceInputs();
        }

        private void RefreshGenerationInstructions()
        {
            Raise(nameof(GenerationPositivePrompt));
            Raise(nameof(GenerationNegativePrompt));
        }

        public GenerationViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs,
            AssetsViewModel assets, JobStore jobs, StudioService studio, ISeedProvider seeds) : base(session, navigation, work, strings, dialogs)
        {
            Assets = assets;
            _jobs = jobs;
            _studio = studio;
            _seeds = seeds;
            Assets.PropertyChanged += OnAssetsChanged;
            GenerateCommand = Command(QueueGenerationAsync,
                () => Session.CanQueue == true && IsGenerationBackendReady == true && IsGenerationSeedValid == true
                    && _generationReference.AreOptionsValid == true && string.IsNullOrWhiteSpace(Prompt) == false);
            EditImageCommand = Command(QueueEditingAsync,
                () => Session.CanQueue == true && IsGenerationBackendReady == true && IsGenerationSeedValid == true
                    && _editingReference.HasImage == true && _editingReference.AreOptionsValid == true
                    && string.IsNullOrWhiteSpace(EditingPrompt) == false);
            PickReferenceImageCommand = Command(PickReferenceImageAsync, () => Session.CanEditGenerationInputs == true);
            ClearReferenceImageCommand = Command(ClearReferenceImageAsync, () => Session.CanEditGenerationInputs == true && HasReferenceImage == true);
        }

        private void OnAssetsChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Assets.SelectedModel))
            {
                RefreshCommands();
            }
        }

        protected override void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Session.Settings))
            {
                RefreshGenerationInstructions();
                Raise(nameof(IsComfyGeneration));
                Raise(nameof(IsGenerationBackendReady));
                Raise(nameof(ReferenceImageHint));
                Raise(nameof(HasQueuedGenerationSeed));
                RefreshCommands();
            }
        }

        protected override void OnNavigationPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Navigation.SelectedTab))
            {
                Raise(nameof(GenerationInputCaption));
                Raise(nameof(GenerationSubmitCommand));
                RefreshReferenceInputs();
            }
        }

        protected override void RefreshCommands()
        {
            Raise(nameof(IsGenerationSeedValid));
            Raise(nameof(GenerationSeedValidationHint));
            Raise(nameof(QueuedGenerationSeedCaption));
            base.RefreshCommands();
        }

        public override void Localize()
        {
            Raise(nameof(ReferenceImageHint));
            Raise(nameof(GenerationInputCaption));
            RefreshReferenceInputs();
            base.Localize();
        }

        public override void Dispose()
        {
            Assets.PropertyChanged -= OnAssetsChanged;
            _generationReference.Dispose();
            _editingReference.Dispose();
            base.Dispose();
        }
    }
}

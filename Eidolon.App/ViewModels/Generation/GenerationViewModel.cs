using System.ComponentModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
        private readonly PromptConverter _promptConverter;
        private readonly RuntimeInstaller _runtimeInstaller;
        private DesktopSettings _promptConversionSettings;
        private ModelAsset _promptConversionModel;
        private string _prompt = string.Empty;
        private string _editingPrompt = string.Empty;
        private string _convertedPrompt = string.Empty;
        private string _editingConvertedPrompt = string.Empty;
        private bool _hasConvertedPrompt;
        private bool _hasEditingConvertedPrompt;
        private bool _isConvertingPrompt;
        private bool _isConvertingEditingPrompt;
        private int _promptConversionRequest;
        private int _editingPromptConversionRequest;
        private bool _useRandomGenerationSeed = true;
        private string _generationSeed = "0";
        private string _lastQueuedGenerationSeed = string.Empty;
        private readonly GenerationReferenceDraft _generationReference = new GenerationReferenceDraft(GenerationReferenceMode.Reimagine, 0.65);
        private readonly GenerationReferenceDraft _editingReference = new GenerationReferenceDraft(GenerationReferenceMode.Restyle, 0.35);
        private bool _removeBackground;
        private bool _editingMaskOpen;
        private bool _isErasingMask;
        private double _editingBrushSize = 32;
        public AsyncCommand GenerateCommand { get; private set; }
        public AsyncCommand EditImageCommand { get; private set; }
        public AsyncCommand ConvertPromptCommand { get; private set; }
        public AsyncCommand PickReferenceImageCommand { get; private set; }
        public AsyncCommand ClearReferenceImageCommand { get; private set; }
        public AsyncCommand ClearEditingMaskCommand { get; private set; }
        public AsyncCommand OpenEditingMaskCommand { get; private set; }
        public AsyncCommand CloseEditingMaskCommand { get; private set; }

        public bool IsPartialEditing
        {
            get
            {
                return _editingReference.Mode == GenerationReferenceMode.Inpaint;
            }
            set
            {
                if (IsPartialEditing == value)
                {
                    return;
                }
                if (value == true)
                {
                    _editingReference.SetMode(GenerationReferenceMode.Inpaint);
                    _editingReference.ChangeStrength = 0.65;
                }
                else
                {
                    _editingReference.SetMode(GenerationReferenceMode.Restyle);
                    _editingReference.ChangeStrength = 0.35;
                }
                _editingMaskOpen = value;
                RefreshReferenceInputs();
            }
        }

        public bool ShowEditingMaskEditor
        {
            get
            {
                if (Navigation.IsEditingView == false)
                {
                    return false;
                }
                if (IsPartialEditing == false)
                {
                    return false;
                }
                if (_editingReference.HasImage == false)
                {
                    return false;
                }
                return _editingMaskOpen;
            }
        }

        public byte[] EditingImageData
        {
            get
            {
                return _editingReference.ImageData;
            }
        }

        public byte[] EditingMaskData
        {
            get
            {
                return _editingReference.MaskData;
            }
            set
            {
                if (value == null)
                {
                    value = Array.Empty<byte>();
                }
                if (ReferenceEquals(_editingReference.MaskData, value) == true)
                {
                    return;
                }
                _editingReference.BeginLoad();
                _editingReference.MaskData = value;
                Raise(nameof(EditingMaskData));
                Raise(nameof(HasEditingMask));
                Raise(nameof(AreReferenceOptionsValid));
                RefreshCommands();
            }
        }

        public bool HasEditingMask
        {
            get
            {
                return _editingReference.MaskData.Length > 0;
            }
        }

        public bool IsErasingMask
        {
            get
            {
                return _isErasingMask;
            }
            set
            {
                Set(ref _isErasingMask, value);
            }
        }

        public double EditingBrushSize
        {
            get
            {
                return _editingBrushSize;
            }
            set
            {
                if (double.IsFinite(value) == false)
                {
                    return;
                }
                if (value <= 0)
                {
                    return;
                }
                Set(ref _editingBrushSize, value);
            }
        }

        public string EditingInputCaption
        {
            get
            {
                if (IsPartialEditing == true)
                {
                    return _strings.GetString("EidolonText721");
                }
                return _strings.GetString("EidolonText457");
            }
        }

        public string EditingPromptHint
        {
            get
            {
                if (IsPartialEditing == true)
                {
                    return _strings.GetString("EidolonText727");
                }
                return _strings.GetString("EidolonText458");
            }
        }

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
                    if (IsPartialEditing == true)
                    {
                        return _strings.GetString("EidolonText721");
                    }
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
                    if (IsPartialEditing == true)
                    {
                        return _strings.GetString("EidolonText721");
                    }
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
                    if (value == false)
                    {
                        GenerationSeed = _seeds.Next().ToString(CultureInfo.InvariantCulture);
                    }
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
                    InvalidatePromptConversion(false);
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
                    InvalidatePromptConversion(true);
                }
            }
        }

        public bool ShowPromptConversion
        {
            get
            {
                if (Session.IsInitialized == false)
                {
                    return false;
                }
                if (IsComfyGeneration == false)
                {
                    return false;
                }
                return _runtimeInstaller.IsPromptConverterInstalled(Session.Settings.InstallDirectory);
            }
        }

        public bool HasConvertedPrompt
        {
            get
            {
                if (Navigation.IsEditingView == true)
                {
                    return _hasEditingConvertedPrompt;
                }
                return _hasConvertedPrompt;
            }
        }

        public bool IsConvertingPrompt
        {
            get
            {
                if (Navigation.IsEditingView == true)
                {
                    return _isConvertingEditingPrompt;
                }
                return _isConvertingPrompt;
            }
        }

        public string ConvertedPrompt
        {
            get
            {
                if (Navigation.IsEditingView == true)
                {
                    return _editingConvertedPrompt;
                }
                return _convertedPrompt;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Navigation.IsEditingView == true)
                {
                    if (_editingConvertedPrompt == value)
                    {
                        return;
                    }
                    _editingConvertedPrompt = value;
                }
                else
                {
                    if (_convertedPrompt == value)
                    {
                        return;
                    }
                    _convertedPrompt = value;
                }
                Raise(nameof(ConvertedPrompt));
                RefreshCommands();
            }
        }

        private void RefreshPromptConversion()
        {
            Raise(nameof(ConvertedPrompt));
            Raise(nameof(HasConvertedPrompt));
            Raise(nameof(IsConvertingPrompt));
            RefreshCommands();
        }

        private void InvalidatePromptConversion(bool editing)
        {
            if (editing == true)
            {
                _editingPromptConversionRequest++;
                _editingConvertedPrompt = string.Empty;
                _hasEditingConvertedPrompt = false;
            }
            else
            {
                _promptConversionRequest++;
                _convertedPrompt = string.Empty;
                _hasConvertedPrompt = false;
            }
            RefreshPromptConversion();
        }

        private bool IsCurrentPromptConversion(bool editing, int request)
        {
            if (Session.IsClosing == true)
            {
                return false;
            }
            if (editing == true)
            {
                return request == _editingPromptConversionRequest;
            }
            return request == _promptConversionRequest;
        }

        private void FinishPromptConversion(bool editing)
        {
            if (editing == true)
            {
                _isConvertingEditingPrompt = false;
            }
            else
            {
                _isConvertingPrompt = false;
            }
            RefreshPromptConversion();
        }

        private Task ConvertPromptAsync()
        {
            bool editing = Navigation.IsEditingView;
            string prompt = Prompt;
            if (editing == true)
            {
                prompt = EditingPrompt;
            }
            DesktopSettings settings = Session.CreateActiveSettings(Session.Settings);
            ModelAsset model = Assets.SelectedModel.Asset.Copy();
            string modelGuidance = Assets.GetModelPromptGuidance(model);
            InvalidatePromptConversion(editing);
            int request = _promptConversionRequest;
            if (editing == true)
            {
                request = _editingPromptConversionRequest;
                _isConvertingEditingPrompt = true;
            }
            else
            {
                _isConvertingPrompt = true;
            }
            RefreshPromptConversion();
            try
            {
                _work.Enqueue(_strings.GetString("EidolonText729") + " · " + prompt, cancellationToken => ConvertPromptAsync(settings, prompt, modelGuidance, editing, request, cancellationToken), () => Dispatcher.UIThread.Post(() => FinishPromptConversion(editing)));
            }
            catch
            {
                FinishPromptConversion(editing);
                throw;
            }
            return Task.CompletedTask;
        }

        private async Task ConvertPromptAsync(DesktopSettings settings, string prompt, string modelGuidance, bool editing, int request, CancellationToken cancellationToken)
        {
            try
            {
                if (IsCurrentPromptConversion(editing, request) == false)
                {
                    return;
                }
                string convertedPrompt = await _promptConverter.ConvertAsync(settings, prompt, modelGuidance, _work.Progress, cancellationToken);
                if (string.IsNullOrWhiteSpace(convertedPrompt) == true)
                {
                    throw new StudioException(StudioMessageCode.PromptConversionFailed);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (IsCurrentPromptConversion(editing, request) == false)
                {
                    return;
                }
                if (editing == true)
                {
                    _editingConvertedPrompt = convertedPrompt;
                    _hasEditingConvertedPrompt = true;
                }
                else
                {
                    _convertedPrompt = convertedPrompt;
                    _hasConvertedPrompt = true;
                }
                RefreshPromptConversion();
                Session.Status = _strings.GetString("EidolonText004");
                Session.Percent = 100;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested == false)
            {
                if (IsCurrentPromptConversion(editing, request) == true)
                {
                    throw;
                }
            }
            finally
            {
                FinishPromptConversion(editing);
            }
        }

        private bool CanGeneratePrompt(bool editing)
        {
            string prompt = Prompt;
            string convertedPrompt = _convertedPrompt;
            bool hasConvertedPrompt = _hasConvertedPrompt;
            if (editing == true)
            {
                prompt = EditingPrompt;
                convertedPrompt = _editingConvertedPrompt;
                hasConvertedPrompt = _hasEditingConvertedPrompt;
            }
            if (string.IsNullOrWhiteSpace(prompt) == true)
            {
                return false;
            }
            if (ShowPromptConversion == true)
            {
                if (hasConvertedPrompt == false)
                {
                    return false;
                }
                return string.IsNullOrWhiteSpace(convertedPrompt) == false;
            }
            return true;
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
            if (reference == _editingReference)
            {
                _editingMaskOpen = true;
            }
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
            Raise(nameof(GenerationInputCaption));
            Raise(nameof(ReferenceChangeStrength));
            Raise(nameof(ReferenceStrengthCaption));
            Raise(nameof(ReferenceImageHint));
            Raise(nameof(AreReferenceOptionsValid));
            Raise(nameof(IsPartialEditing));
            Raise(nameof(ShowEditingMaskEditor));
            Raise(nameof(EditingImageData));
            Raise(nameof(EditingMaskData));
            Raise(nameof(HasEditingMask));
            Raise(nameof(EditingInputCaption));
            Raise(nameof(EditingPromptHint));
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
            string convertedPrompt = string.Empty;
            if (ShowPromptConversion == true)
            {
                if (CanGeneratePrompt(editing) == false)
                {
                    throw new StudioException(StudioMessageCode.PromptRequired);
                }
                convertedPrompt = _convertedPrompt;
                if (editing == true)
                {
                    convertedPrompt = _editingConvertedPrompt;
                }
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
                    ImageData = draft.ImageData.ToArray(),
                    MaskData = draft.MaskData.ToArray(),
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
            EnqueueGeneration(settings, prompt, model, loras, removeBackground, seed, reference, titleKey, false, convertedPrompt);
            if (editing == true)
            {
                _editingMaskOpen = false;
                Raise(nameof(ShowEditingMaskEditor));
            }
            return Task.CompletedTask;
        }

        internal void EnqueueGeneration(DesktopSettings settings, string prompt, ModelAsset model, List<ModelAsset> loras, bool removeBackground, long seed, GenerationReferenceInput reference, string titleKey)
        {
            EnqueueGeneration(settings, prompt, model, loras, removeBackground, seed, reference, titleKey, true, string.Empty);
        }

        private void EnqueueGeneration(DesktopSettings settings, string prompt, ModelAsset model, List<ModelAsset> loras, bool removeBackground, long seed, GenerationReferenceInput reference, string titleKey, bool allowAutomaticPromptConversion, string convertedPrompt)
        {
            if (Session.CanQueue == false)
            {
                throw new InvalidOperationException("The application is not ready to queue an image request.");
            }
            bool usePromptConversion = false;
            if (allowAutomaticPromptConversion == true)
            {
                if (settings.GenerationBackend == GenerationBackend.ComfyUI)
                {
                    usePromptConversion = _runtimeInstaller.IsPromptConverterInstalled(settings.InstallDirectory);
                }
            }
            _work.Enqueue(_strings.GetString(titleKey) + " · " + prompt, cancellationToken => GenerateAsync(settings, prompt, model, loras, removeBackground, seed, reference, usePromptConversion, convertedPrompt, cancellationToken));
            _lastQueuedGenerationSeed = string.Empty;
            if (settings.GenerationBackend == GenerationBackend.ComfyUI)
            {
                _lastQueuedGenerationSeed = seed.ToString(CultureInfo.InvariantCulture);
            }
            Raise(nameof(HasQueuedGenerationSeed));
            Raise(nameof(QueuedGenerationSeedCaption));
        }

        private async Task GenerateAsync(DesktopSettings settings, string prompt, ModelAsset model, List<ModelAsset> loras, bool removeBackground, long seed, GenerationReferenceInput reference, bool usePromptConversion, string convertedPrompt, CancellationToken cancellationToken)
        {
            if (usePromptConversion == true)
            {
                string modelGuidance = Assets.GetModelPromptGuidance(model);
                convertedPrompt = await _promptConverter.ConvertAsync(settings, prompt, modelGuidance, _work.Progress, cancellationToken);
                if (string.IsNullOrWhiteSpace(convertedPrompt) == true)
                {
                    throw new StudioException(StudioMessageCode.PromptConversionFailed);
                }
            }
            JobRecord job = await _studio.GenerateAsync(settings, prompt, model, loras, removeBackground, seed, reference, _work.Progress, cancellationToken, convertedPrompt);
            if (Navigation.IsResultsView == false)
            {
                string path = _jobs.ImagePath(job, job.ImageFiles.Last());
                GenerationImage image = await Task.Run(() => _jobs.FindGenerationImage(job.OutputDirectory, path, cancellationToken), cancellationToken);
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
            bool editing = job.ReferenceMode == GenerationReferenceMode.Restyle;
            if (job.ReferenceMode == GenerationReferenceMode.Inpaint)
            {
                editing = true;
            }
            if (editing == true)
            {
                reference = _editingReference;
                targetTab = 1;
                EditingPrompt = job.UserPrompt;
                _editingReference.SetMode(job.ReferenceMode);
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
                byte[] restoredImageData = Array.Empty<byte>();
                try
                {
                    if (await LoadReferenceImageAsync(reference, job.ReferenceImagePath, job.ReferenceImageName) == true)
                    {
                        restoredImageData = reference.ImageData;
                        if (job.ReferenceMode == GenerationReferenceMode.Inpaint)
                        {
                            int maskRequest = reference.BeginLoad();
                            byte[] maskData = await Task.Run(() =>
                            {
                                Session.Lifetime.ThrowIfCancellationRequested();
                                using FileStream maskStream = File.OpenRead(job.ReferenceMaskPath);
                                if (maskStream.Length < 1)
                                {
                                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                                }
                                if (maskStream.Length > GenerationReferenceInput.MaximumImageBytes)
                                {
                                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                                }
                                byte[] data = new byte[(int)maskStream.Length];
                                maskStream.ReadExactly(data);
                                using SKMemoryStream encodedMask = new(data);
                                using SKCodec maskCodec = SKCodec.Create(encodedMask);
                                if (maskCodec == null)
                                {
                                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                                }
                                if (maskCodec.Info.Width < 1)
                                {
                                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                                }
                                if (maskCodec.Info.Height < 1)
                                {
                                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                                }
                                if ((long)maskCodec.Info.Width * maskCodec.Info.Height > GenerationReferenceInput.MaximumImagePixels)
                                {
                                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                                }
                                using SKBitmap decodedMask = SKBitmap.Decode(maskCodec);
                                if (decodedMask == null)
                                {
                                    throw new StudioException(StudioMessageCode.InvalidReferenceOptions);
                                }
                                return data;
                            }, Session.Lifetime);
                            if (Session.IsClosing == true)
                            {
                                return;
                            }
                            if (reference.IsCurrentLoad(maskRequest) == false)
                            {
                                return;
                            }
                            reference.MaskData = maskData;
                            RefreshReferenceInputs();
                        }
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    if (restoredImageData.Length > 0)
                    {
                        if (ReferenceEquals(reference.ImageData, restoredImageData) == false)
                        {
                            return;
                        }
                        reference.MaskData = Array.Empty<byte>();
                        Session.Error = _strings.GetString("EidolonText726");
                    }
                    else
                    {
                        reference.SetMissingImage(job.ReferenceImageName);
                        Session.Error = _strings.GetString("EidolonText454");
                    }
                    RefreshReferenceInputs();
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
            UseRandomGenerationSeed = false;
            GenerationSeed = job.Seed.ToString(CultureInfo.InvariantCulture);
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

        public GenerationViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs, AssetsViewModel assets, JobStore jobs, StudioService studio, ISeedProvider seeds, PromptConverter promptConverter, RuntimeInstaller runtimeInstaller) : base(session, navigation, work, strings, dialogs)
        {
            Assets = assets;
            _jobs = jobs;
            _studio = studio;
            _seeds = seeds;
            _promptConverter = promptConverter;
            _runtimeInstaller = runtimeInstaller;
            _promptConversionSettings = Session.Settings.Copy();
            if (Assets.SelectedModel != null)
            {
                _promptConversionModel = Assets.SelectedModel.Asset.Copy();
            }
            Assets.PropertyChanged += OnAssetsChanged;
            ConvertPromptCommand = Command(ConvertPromptAsync, () =>
            {
                if (Session.CanQueue == false)
                {
                    return false;
                }
                if (ShowPromptConversion == false)
                {
                    return false;
                }
                if (IsGenerationBackendReady == false)
                {
                    return false;
                }
                if (IsConvertingPrompt == true)
                {
                    return false;
                }
                if (Navigation.IsEditingView == true)
                {
                    return string.IsNullOrWhiteSpace(EditingPrompt) == false;
                }
                return string.IsNullOrWhiteSpace(Prompt) == false;
            });
            GenerateCommand = Command(QueueGenerationAsync, () =>
            {
                if (Session.CanQueue == false)
                {
                    return false;
                }
                if (IsGenerationBackendReady == false)
                {
                    return false;
                }
                if (IsGenerationSeedValid == false)
                {
                    return false;
                }
                if (_generationReference.AreOptionsValid == false)
                {
                    return false;
                }
                return CanGeneratePrompt(false);
            });
            EditImageCommand = Command(QueueEditingAsync, () =>
            {
                if (Session.CanQueue == false)
                {
                    return false;
                }
                if (IsGenerationBackendReady == false)
                {
                    return false;
                }
                if (IsGenerationSeedValid == false)
                {
                    return false;
                }
                if (_editingReference.HasImage == false)
                {
                    return false;
                }
                if (_editingReference.AreOptionsValid == false)
                {
                    return false;
                }
                return CanGeneratePrompt(true);
            });
            PickReferenceImageCommand = Command(PickReferenceImageAsync, () => Session.CanEditGenerationInputs == true);
            ClearReferenceImageCommand = Command(ClearReferenceImageAsync, () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                return HasReferenceImage;
            });
            ClearEditingMaskCommand = Command(() =>
            {
                EditingMaskData = Array.Empty<byte>();
                return Task.CompletedTask;
            }, () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                return HasEditingMask;
            });
            OpenEditingMaskCommand = Command(() =>
            {
                _editingMaskOpen = true;
                Raise(nameof(ShowEditingMaskEditor));
                return Task.CompletedTask;
            }, () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                if (IsPartialEditing == false)
                {
                    return false;
                }
                return _editingReference.HasImage;
            });
            CloseEditingMaskCommand = Command(() =>
            {
                _editingMaskOpen = false;
                Raise(nameof(ShowEditingMaskEditor));
                return Task.CompletedTask;
            }, () => true);
        }

        private void OnAssetsChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Assets.SelectedModel))
            {
                ModelAsset previousModel = _promptConversionModel;
                _promptConversionModel = null;
                if (Assets.SelectedModel != null)
                {
                    _promptConversionModel = Assets.SelectedModel.Asset.Copy();
                }
                if (HasPromptConversionModelChanged(previousModel, _promptConversionModel) == true)
                {
                    InvalidatePromptConversion(false);
                    InvalidatePromptConversion(true);
                }
                RefreshCommands();
            }
        }

        protected override void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Session.Settings))
            {
                DesktopSettings previousSettings = _promptConversionSettings;
                _promptConversionSettings = Session.Settings.Copy();
                if (HasPromptConversionSettingsChanged(previousSettings, Session.Settings) == true)
                {
                    InvalidatePromptConversion(false);
                    InvalidatePromptConversion(true);
                }
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
                RefreshPromptConversion();
                RefreshReferenceInputs();
            }
        }

        protected override void RefreshCommands()
        {
            Raise(nameof(ShowPromptConversion));
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
            _promptConversionRequest++;
            _editingPromptConversionRequest++;
            Assets.PropertyChanged -= OnAssetsChanged;
            _generationReference.Dispose();
            _editingReference.Dispose();
            base.Dispose();
        }

        private bool HasPromptConversionSettingsChanged(DesktopSettings previousSettings, DesktopSettings currentSettings)
        {
            if (previousSettings.GenerationBackend != currentSettings.GenerationBackend)
            {
                return true;
            }
            if (previousSettings.InstallDirectory != currentSettings.InstallDirectory)
            {
                return true;
            }
            if (previousSettings.CodexExecutablePath != currentSettings.CodexExecutablePath)
            {
                return true;
            }
            return previousSettings.CodexModel != currentSettings.CodexModel;
        }

        private bool HasPromptConversionModelChanged(ModelAsset previousModel, ModelAsset currentModel)
        {
            if (previousModel == null)
            {
                return currentModel != null;
            }
            if (currentModel == null)
            {
                return true;
            }
            if (previousModel.Id != currentModel.Id)
            {
                return true;
            }
            if (previousModel.Family != currentModel.Family)
            {
                return true;
            }
            if (previousModel.Name != currentModel.Name)
            {
                return true;
            }
            if (previousModel.EngineName != currentModel.EngineName)
            {
                return true;
            }
            if (previousModel.RuntimeRoot != currentModel.RuntimeRoot)
            {
                return true;
            }
            return previousModel.TriggerWord != currentModel.TriggerWord;
        }
    }
}

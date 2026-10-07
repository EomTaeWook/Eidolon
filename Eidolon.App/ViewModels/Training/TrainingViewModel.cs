using System.ComponentModel;
using Avalonia.Media;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class TrainingViewModel : StudioPanelViewModel
    {
        public AssetsViewModel Assets { get; private set; }

        private readonly StudioService _studio;
        private string _trainingName = string.Empty;
        private string _trainingTrigger = string.Empty;
        private string _trainingDescription = string.Empty;
        private bool _continueTraining;
        private decimal _trainingSteps = TrainingPreset.MaxSteps;
        private bool _trainingActive;
        private bool _trainingCancellationRequested;
        private bool _trainingRegistering;
        private bool _hasTrainingRemainingTime;
        private TimeSpan _trainingRemainingTime;
        private string _captionImagePath = string.Empty;
        private string _automaticDescription = string.Empty;
        private bool _loadingDescription;
        private int _descriptionRequest;
        private Task _descriptionLoad = Task.CompletedTask;
        public List<TrainingImageGroup> TrainingImageGroups { get; private set; } = new List<TrainingImageGroup>();
        public TrainingWorkflowViewModel Workflow { get; private set; }
        public AsyncCommand TrainCommand { get; private set; }
        public AsyncCommand QuickTrainingCommand { get; private set; }
        public AsyncCommand StandardTrainingCommand { get; private set; }
        public AsyncCommand CancelTrainingCommand { get; private set; }

        public decimal TrainingSteps
        {
            get
            {
                return _trainingSteps;
            }
            set
            {
                Set(ref _trainingSteps, value);
                RefreshTrainingSetup();
            }
        }

        public bool CanQueueTraining
        {
            get
            {
                if (Session.CanQueue == false)
                {
                    return false;
                }
                if (AreTrainingImagesLoading == true)
                {
                    return false;
                }
                return TrainingInputIssue() == StudioMessageCode.None;
            }
        }

        public string TrainingSetupHint
        {
            get
            {
                if (AreTrainingImagesLoading == true)
                {
                    return _strings.GetString("EidolonText475");
                }
                StudioMessageCode issue = TrainingInputIssue();
                if (issue == StudioMessageCode.None)
                {
                    return _strings.GetString("EidolonText248");
                }
                if (issue == StudioMessageCode.InvalidTrainingSteps)
                {
                    return _strings.Format(issue, TrainingPreset.MinimumSteps, TrainingPreset.MaximumSteps);
                }
                if (issue == StudioMessageCode.LoraRequired)
                {
                    return _strings.GetString("EidolonText260");
                }
                if (issue == StudioMessageCode.DatasetRequired)
                {
                    return _strings.GetString("EidolonText470");
                }
                return _strings.Format(issue);
            }
        }

        public decimal MinimumTrainingSteps
        {
            get
            {
                return TrainingPreset.MinimumSteps;
            }
        }

        public decimal MaximumTrainingSteps
        {
            get
            {
                return TrainingPreset.MaximumSteps;
            }
        }

        public bool IsTrainingActive
        {
            get
            {
                return _trainingActive;
            }
        }

        public string TrainingSpeedDescription
        {
            get
            {
                return _strings.Format("EidolonText331", TrainingPreset.QuickMaxSteps, TrainingPreset.MaxSteps);
            }
        }

        public string TrainingSpeedCaption
        {
            get
            {
                return _strings.Format("EidolonText481", TrainingPreset.QuickMaxSteps, TrainingPreset.MaxSteps);
            }
        }

        public bool CanCancelTraining
        {
            get
            {
                return Session.IsClosing == false && _trainingActive == true && _trainingCancellationRequested == false &&
                    _trainingRegistering == false;
            }
        }

        public bool ShowTrainingCancel
        {
            get
            {
                return _trainingActive == true && _trainingRegistering == false;
            }
        }

        public bool ShowTrainingRemainingTime
        {
            get
            {
                return _trainingActive == true && _trainingCancellationRequested == false && _trainingRegistering == false;
            }
        }

        public string TrainingRemainingText
        {
            get
            {
                if (_hasTrainingRemainingTime == false)
                {
                    return _strings.GetString("EidolonText330");
                }
                long seconds = (long)Math.Ceiling(_trainingRemainingTime.TotalSeconds);
                string duration = (seconds / 3600).ToString("D2") + ":" +
                    (seconds / 60 % 60).ToString("D2") + ":" + (seconds % 60).ToString("D2");
                return _strings.Format("EidolonText329", duration);
            }
        }

        public string CancelTrainingCaption
        {
            get
            {
                if (_trainingCancellationRequested == true)
                {
                    return _strings.GetString("EidolonText328");
                }
                return _strings.GetString("EidolonText327");
            }
        }

        public bool ContinueTraining
        {
            get
            {
                return _continueTraining;
            }
            set
            {
                Set(ref _continueTraining, value);
                RefreshTrainingSetup();
            }
        }

        public string TrainingImageSummary
        {
            get
            {
                return _strings.Format("EidolonText466", TrainingImageGroups.Sum(group => group.Images.Count));
            }
        }

        public bool HasTrainingImages
        {
            get
            {
                return TrainingImageGroups.Any(group => group.HasImages == true);
            }
        }

        public bool AreTrainingImagesLoading
        {
            get
            {
                if (_loadingDescription == true)
                {
                    return true;
                }
                return TrainingImageGroups.Any(group => group.IsLoading == true);
            }
        }

        public bool CanEditTrainingInputs
        {
            get
            {
                return Session.CanQueue;
            }
        }

        public string TrainingName
        {
            get
            {
                return _trainingName;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Set(ref _trainingName, value) == true)
                {
                    RefreshTrainingSetup();
                }
            }
        }

        public string TrainingTrigger
        {
            get
            {
                return _trainingTrigger;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Set(ref _trainingTrigger, value) == true)
                {
                    RefreshTrainingSetup();
                }
            }
        }

        public string TrainingDescription
        {
            get
            {
                return _trainingDescription;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Set(ref _trainingDescription, value) == true)
                {
                    RefreshTrainingSetup();
                }
            }
        }

        private void RefreshTrainingState()
        {
            Raise(nameof(IsTrainingActive));
            Raise(nameof(CanCancelTraining));
            Raise(nameof(ShowTrainingCancel));
            Raise(nameof(ShowTrainingRemainingTime));
            Raise(nameof(TrainingRemainingText));
            Raise(nameof(CancelTrainingCaption));
            RefreshCommands();
        }

        private async Task CancelTrainingAsync()
        {
            if (CanCancelTraining == false)
            {
                return;
            }
            _trainingCancellationRequested = true;
            Session.Status = _strings.GetString("EidolonText328");
            RefreshTrainingState();
            await _work.CancelCurrentAsync();
        }

        private StudioMessageCode TrainingInputIssue()
        {
            if (ContinueTraining == true && Assets.SelectedResumeLora == null)
            {
                return StudioMessageCode.LoraRequired;
            }
            if (Assets.SelectedModel == null)
            {
                return StudioMessageCode.ModelRequired;
            }
            if (HasTrainingImages == false)
            {
                return StudioMessageCode.DatasetRequired;
            }
            StudioMessageCode identityIssue = TrainingIdentityIssue();
            if (identityIssue != StudioMessageCode.None)
            {
                return identityIssue;
            }
            if (TrainingSteps != decimal.Truncate(TrainingSteps) || TrainingSteps < TrainingPreset.MinimumSteps ||
                TrainingSteps > TrainingPreset.MaximumSteps)
            {
                return StudioMessageCode.InvalidTrainingSteps;
            }
            return StudioMessageCode.None;
        }

        private StudioMessageCode TrainingIdentityIssue()
        {
            if (string.IsNullOrWhiteSpace(TrainingName) == true)
            {
                return StudioMessageCode.LoraNameRequired;
            }
            if (string.IsNullOrWhiteSpace(TrainingTrigger) == true)
            {
                return StudioMessageCode.TriggerRequired;
            }
            if (TrainingTrigger.Any(character => char.IsWhiteSpace(character) || character == ',') == true)
            {
                return StudioMessageCode.InvalidTrigger;
            }
            return StudioMessageCode.None;
        }

        private bool CanCompleteStep(TrainingStep step)
        {
            switch (step)
            {
                case TrainingStep.Images:
                    return HasTrainingImages == true && AreTrainingImagesLoading == false;
                case TrainingStep.Identity:
                    return TrainingIdentityIssue() == StudioMessageCode.None;
                case TrainingStep.Training:
                    return CanQueueTraining;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        private string StepHint(TrainingStep step)
        {
            switch (step)
            {
                case TrainingStep.Images:
                    if (AreTrainingImagesLoading == true)
                    {
                        return _strings.GetString("EidolonText475");
                    }
                    if (HasTrainingImages == false)
                    {
                        return _strings.GetString("EidolonText470");
                    }
                    return TrainingImageSummary;
                case TrainingStep.Identity:
                    StudioMessageCode issue = TrainingIdentityIssue();
                    if (issue != StudioMessageCode.None)
                    {
                        return _strings.Format(issue);
                    }
                    return _strings.GetString("EidolonText495");
                case TrainingStep.Training:
                    return TrainingSetupHint;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        private void RefreshTrainingSetup()
        {
            Raise(nameof(CanQueueTraining));
            Raise(nameof(TrainingSetupHint));
            RefreshCommands();
        }

        private Task SetTrainingStepsAsync(int steps)
        {
            TrainingSteps = steps;
            return Task.CompletedTask;
        }

        private Task QueueTrainingAsync()
        {
            if (CanQueueTraining == false)
            {
                return Task.CompletedTask;
            }
            if (TrainingSteps != decimal.Truncate(TrainingSteps) || TrainingSteps < TrainingPreset.MinimumSteps ||
                TrainingSteps > TrainingPreset.MaximumSteps)
            {
                throw new StudioException(StudioMessageCode.InvalidTrainingSteps,
                    TrainingPreset.MinimumSteps, TrainingPreset.MaximumSteps);
            }
            if (ContinueTraining == true && Assets.SelectedResumeLora == null)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText260"));
            }
            TrainingInput input = CreateTrainingInput();
            DesktopSettings settings = Session.CreateActiveSettings(Session.Settings);
            _work.Enqueue(_strings.GetString("EidolonText108") + " · " + input.Name, token => TrainAsync(settings, input, token));
            return Task.CompletedTask;
        }

        private TrainingInput CreateTrainingInput()
        {
            ModelAsset model = null;
            if (Assets.SelectedModel != null)
            {
                model = Assets.SelectedModel.Asset.Copy();
            }
            TrainingInput input = new TrainingInput
            {
                Model = model,
                Images = TrainingImageGroups.SelectMany(group => group.Snapshot()).ToList(),
                Name = TrainingName,
                TriggerWord = TrainingTrigger,
                Description = TrainingDescription,
                Steps = decimal.ToInt32(TrainingSteps)
            };
            if (ContinueTraining == true && Assets.SelectedResumeLora != null)
            {
                input.ResumeLora = Assets.SelectedResumeLora.Asset.Copy();
            }
            return input;
        }

        private async Task TrainAsync(DesktopSettings settings, TrainingInput input, CancellationToken token)
        {
            _trainingActive = true;
            _trainingCancellationRequested = false;
            _trainingRegistering = false;
            _hasTrainingRemainingTime = false;
            RefreshTrainingState();
            IProgress<Exception> engineRecoveryErrors = new Progress<Exception>(error =>
            {
                if (Session.IsClosing == false)
                {
                    Session.Error = _strings.GetExceptionMessage(error);
                }
            });
            try
            {
                ModelAsset asset = await _studio.TrainAsync(settings, input, _work.Progress,
                    engineRecoveryErrors, token, Session.Lifetime);
                await Assets.RefreshAssetsAsync();
                AssetItem lora = Assets.Loras.FirstOrDefault(item => item.Asset.Id == asset.Id);
                if (lora != null)
                {
                    lora.IsSelected = true;
                }
                Session.Status = _strings.GetString("EidolonText012");
                Session.Percent = 100;
            }
            finally
            {
                _trainingActive = false;
                _hasTrainingRemainingTime = false;
                RefreshTrainingState();
            }
        }

        internal async Task UseResultForTrainingAsync(GenerationItem result, string path)
        {
            if (CanEditTrainingInputs == false)
            {
                Navigation.SelectedTab = 2;
                return;
            }
            bool hasImages = HasTrainingImages;
            await TrainingImageGroups[0].AddPathsAsync(new[] { path });
            Workflow.ShowImages();
            if (hasImages == true)
            {
                Navigation.SelectedTab = 2;
                Session.Status = _strings.GetString("EidolonText302");
                return;
            }
            GenerationMetadata metadata = result.Metadata;
            AssetItem model = Assets.SelectedModel;
            TrainingDescription = string.Empty;
            if (metadata != null)
            {
                AssetItem recordedModel = Assets.FindReusableAsset(Assets.Models, metadata.Model);
                if (recordedModel != null)
                {
                    model = recordedModel;
                }
                TrainingDescription = metadata.PositivePrompt;
            }
            _automaticDescription = TrainingDescription;
            Assets.SelectedModel = model;
            ContinueTraining = false;
            Assets.SelectedResumeLora = null;
            Navigation.SelectedTab = 2;
            Session.Status = _strings.GetString("EidolonText302");
            if (model == null)
            {
                Session.Status = _strings.GetString("EidolonText301");
            }
        }

        private void RefreshTrainingSource()
        {
            Raise(nameof(HasTrainingImages));
            Raise(nameof(AreTrainingImagesLoading));
            Raise(nameof(TrainingImageSummary));
            RefreshTrainingSetup();
        }

        private void OnTrainingImagesChanged()
        {
            string path = string.Empty;
            TrainingImageItem first = TrainingImageGroups.SelectMany(group => group.Images).FirstOrDefault();
            if (first != null)
            {
                path = first.FilePath;
            }
            if (path != _captionImagePath)
            {
                _captionImagePath = path;
                int request = ++_descriptionRequest;
                if (string.IsNullOrEmpty(path) == false)
                {
                    string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                    if (string.IsNullOrWhiteSpace(TrainingName) == true)
                    {
                        TrainingName = "LoRA-" + suffix;
                    }
                    if (string.IsNullOrWhiteSpace(TrainingTrigger) == true)
                    {
                        TrainingTrigger = "eidolon_" + suffix;
                    }
                    _descriptionLoad = Task.WhenAll(_descriptionLoad, LoadTrainingDescriptionAsync(path, request));
                }
                else
                {
                    _loadingDescription = false;
                }
            }
            RefreshTrainingSource();
        }

        private async Task LoadTrainingDescriptionAsync(string path, int request)
        {
            _loadingDescription = true;
            RefreshTrainingSource();
            try
            {
                string caption = await _studio.ReadTrainingCaptionAsync(path, Session.Lifetime);
                if (Session.IsClosing == true)
                {
                    return;
                }
                if (request != _descriptionRequest)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(TrainingDescription) == true)
                {
                    TrainingDescription = caption;
                    _automaticDescription = caption;
                }
                else if (TrainingDescription == _automaticDescription)
                {
                    TrainingDescription = caption;
                    _automaticDescription = caption;
                }
            }
            catch (OperationCanceledException) when (Session.IsClosing == true)
            {
            }
            catch (Exception error)
            {
                if (Session.IsClosing == false)
                {
                    if (request == _descriptionRequest)
                    {
                        OnCommandError(error);
                    }
                }
            }
            finally
            {
                if (request == _descriptionRequest)
                {
                    _loadingDescription = false;
                    RefreshTrainingSource();
                }
            }
        }

        internal Task WaitForDescriptionAsync()
        {
            return _descriptionLoad;
        }

        public TrainingViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs,
            AssetsViewModel assets, StudioService studio) : base(session, navigation, work, strings, dialogs)
        {
            Assets = assets;
            _studio = studio;
            TrainingImageGroups = new List<TrainingImageGroup>
            {
                new TrainingImageGroup(TrainingBackground.White, "EidolonText461", Brushes.White,
                    _strings, _dialogs, () => CanEditTrainingInputs, RefreshTrainingSource, OnCommandError, Session.Lifetime),
                new TrainingImageGroup(TrainingBackground.Black, "EidolonText462", Brushes.Black,
                    _strings, _dialogs, () => CanEditTrainingInputs, RefreshTrainingSource, OnCommandError, Session.Lifetime)
            };
            foreach (TrainingImageGroup group in TrainingImageGroups)
            {
                _commands.AddRange(group.Commands);
                group.ImagesChanged += OnTrainingImagesChanged;
            }
            Workflow = new TrainingWorkflowViewModel(_strings, CanCompleteStep, StepHint,
                () => Session.CanEditGenerationInputs, OnCommandError);
            Assets.PropertyChanged += OnAssetsChanged;
            _work.ProgressChanged += OnTrainingProgress;
            TrainCommand = Command(QueueTrainingAsync, () => CanQueueTraining == true);
            QuickTrainingCommand = Command(() => SetTrainingStepsAsync(TrainingPreset.QuickMaxSteps), () => Session.CanQueue == true);
            StandardTrainingCommand = Command(() => SetTrainingStepsAsync(TrainingPreset.MaxSteps), () => Session.CanQueue == true);
            CancelTrainingCommand = Command(CancelTrainingAsync, () => CanCancelTraining == true);
        }

        public AssetItem SelectedResumeLora
        {
            get
            {
                return Assets.SelectedResumeLora;
            }
            set
            {
                Assets.SelectedResumeLora = value;
            }
        }

        private void OnAssetsChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Assets.SelectedResumeLora))
            {
                Raise(nameof(SelectedResumeLora));
                AssetItem lora = SelectedResumeLora;
                if (lora != null)
                {
                    if (string.IsNullOrWhiteSpace(TrainingName) == true)
                    {
                        TrainingName = lora.Asset.Name;
                    }
                    if (string.IsNullOrWhiteSpace(TrainingTrigger) == true)
                    {
                        TrainingTrigger = lora.Asset.TriggerWord;
                    }
                }
            }
            if (args.PropertyName == nameof(Assets.SelectedModel)
                || args.PropertyName == nameof(Assets.SelectedResumeLora))
            {
                RefreshTrainingSetup();
            }
        }

        private void OnTrainingProgress(WorkProgress progress)
        {
            if (_trainingActive == false)
            {
                return;
            }
            if (_trainingCancellationRequested == true)
            {
                Session.Status = _strings.GetString("EidolonText328");
            }
            if (progress.HasEstimatedRemainingTime == true)
            {
                _hasTrainingRemainingTime = true;
                _trainingRemainingTime = progress.EstimatedRemainingTime;
                Raise(nameof(TrainingRemainingText));
            }
            if (progress.Code == StudioMessageCode.RegisteringTrainedLora || progress.Code == StudioMessageCode.TrainingCompleted)
            {
                _trainingRegistering = true;
                RefreshTrainingState();
            }
        }

        protected override void RefreshCommands()
        {
            Raise(nameof(CanEditTrainingInputs));
            Raise(nameof(CanQueueTraining));
            Raise(nameof(TrainingSetupHint));
            base.RefreshCommands();
            if (Workflow != null)
            {
                Workflow.Refresh();
            }
        }

        public override void Localize()
        {
            foreach (TrainingImageGroup group in TrainingImageGroups)
            {
                group.Localize();
            }
            Raise(nameof(TrainingImageSummary));
            Raise(nameof(TrainingSpeedDescription));
            Raise(nameof(TrainingSpeedCaption));
            Workflow.Localize();
            RefreshTrainingState();
            base.Localize();
        }

        public override void Dispose()
        {
            Assets.PropertyChanged -= OnAssetsChanged;
            _work.ProgressChanged -= OnTrainingProgress;
            foreach (TrainingImageGroup group in TrainingImageGroups)
            {
                group.ImagesChanged -= OnTrainingImagesChanged;
                group.Dispose();
            }
            base.Dispose();
        }
    }
}

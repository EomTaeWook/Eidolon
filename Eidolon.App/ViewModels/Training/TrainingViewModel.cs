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
        public List<TrainingImageGroup> TrainingImageGroups { get; private set; } = new List<TrainingImageGroup>();
        public TrainingWorkflowViewModel Workflow { get; private set; }
        public TrainingDatasetViewModel Dataset { get; private set; }
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
                return Session.CanQueue == true && Dataset.IsPreparing == false
                    && AreTrainingImagesLoading == false && Dataset.IsCurrent == true
                    && TrainingInputIssue() == StudioMessageCode.None;
            }
        }

        public string TrainingSetupHint
        {
            get
            {
                if (Dataset.IsCurrent == false)
                {
                    return _strings.GetString("EidolonText497");
                }
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
                return TrainingImageGroups.Any(group => group.IsLoading == true);
            }
        }

        public bool CanEditTrainingInputs
        {
            get
            {
                return Session.CanQueue == true && Dataset.IsPreparing == false;
            }
        }

        public string CaptionExample
        {
            get
            {
                string description = TrainingDescription.Trim();
                if (string.IsNullOrWhiteSpace(description) == true)
                {
                    description = _strings.GetString("EidolonText502");
                }
                return TrainingTrigger.Trim() + ", " + description;
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
                    InvalidatePreparedDataset();
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
                    InvalidatePreparedDataset();
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
                    InvalidatePreparedDataset();
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
            if (Dataset.IsCurrent == false)
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
                case TrainingStep.Dataset:
                case TrainingStep.Captions:
                    return Dataset.IsCurrent == true && Dataset.IsPreparing == false;
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
                case TrainingStep.Dataset:
                    if (Dataset.IsPreparing == true)
                    {
                        return _strings.GetString("EidolonText499");
                    }
                    if (Dataset.IsCurrent == true)
                    {
                        return _strings.GetString("EidolonText498");
                    }
                    if (Dataset.HasPrepared == true)
                    {
                        return _strings.GetString("EidolonText500");
                    }
                    return _strings.GetString("EidolonText496");
                case TrainingStep.Captions:
                    return _strings.GetString("EidolonText501");
                case TrainingStep.Training:
                    return TrainingSetupHint;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        private void InvalidatePreparedDataset()
        {
            Dataset.Invalidate();
            Raise(nameof(CaptionExample));
            RefreshTrainingSetup();
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

        private TrainingInput CreateTrainingInput(bool usePreparedDataset = true)
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
            if (usePreparedDataset == true)
            {
                input.Images.Clear();
                input.ImageDirectory = Dataset.Path;
                input.Background = TrainingBackground.Original;
            }
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
            AssetItem model = null;
            TrainingDescription = string.Empty;
            if (metadata != null)
            {
                model = Assets.FindReusableAsset(Assets.Models, metadata.Model);
                TrainingDescription = metadata.PositivePrompt;
            }
            Assets.SelectedModel = model;
            ContinueTraining = false;
            Assets.SelectedResumeLora = null;
            TrainingName = string.Empty;
            TrainingTrigger = string.Empty;
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
            Dataset.RefreshInputs();
            RefreshTrainingSetup();
        }

        private void OnTrainingImagesChanged()
        {
            InvalidatePreparedDataset();
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
            Dataset = new TrainingDatasetViewModel(session, navigation, work, strings, dialogs, studio,
                () => CreateTrainingInput(false), () => CanCompleteStep(TrainingStep.Images) == true
                    && CanCompleteStep(TrainingStep.Identity) == true);
            Dataset.PropertyChanged += OnDatasetChanged;
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
            Raise(nameof(CaptionExample));
            Workflow.Localize();
            RefreshTrainingState();
            base.Localize();
        }

        private void OnDatasetChanged(object sender, PropertyChangedEventArgs args)
        {
            RefreshTrainingSetup();
        }

        public override void Dispose()
        {
            Dataset.PropertyChanged -= OnDatasetChanged;
            Dataset.Dispose();
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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using SkiaSharp;

namespace Eidolon.App.ViewModels
{
    public class StudioViewModel : ObservableObject, IAsyncDisposable
    {
        private const int GalleryPageSize = 12;
        private const int GalleryThumbnailWidth = 384;
        private readonly SettingsStore _settingsStore;
        private readonly AssetLibrary _assets;
        private readonly JobStore _jobs;
        private readonly RuntimeInstaller _installer;
        private readonly ComfyEngine _engine;
        private readonly StudioService _studio;
        private readonly ISeedProvider _seeds;
        private readonly DesktopDialogs _dialogs;
        private readonly ThemeService _themes;
        private readonly LanguageService _languages;
        private readonly StringHelper _strings;
        private readonly StudioWorkPresenter _work;
        private readonly string _dataDirectory;
        private readonly List<AsyncCommand> _commands = new List<AsyncCommand>();
        private readonly CancellationTokenSource _appLifetime = new CancellationTokenSource();
        private DesktopSettings _settings = new DesktopSettings();
        private bool _maintenanceWork;
        private bool _isBusy;
        private bool _initialized;
        private bool _closing;
        private string _prompt = string.Empty;
        private string _editingPrompt = string.Empty;
        private bool _useRandomGenerationSeed = true;
        private string _generationSeed = "0";
        private string _lastQueuedGenerationSeed = string.Empty;
        private string _loraSearch = string.Empty;
        private readonly GenerationReferenceDraft _generationReference = new GenerationReferenceDraft(GenerationReferenceMode.Reimagine, 0.65);
        private readonly GenerationReferenceDraft _editingReference = new GenerationReferenceDraft(GenerationReferenceMode.Restyle, 0.35);
        private bool _removeBackground;
        private string _status = string.Empty;
        private string _error = string.Empty;
        private double _percent;
        private bool _indeterminate;
        private AssetItem _selectedModel;
        private AssetItem _selectedAsset;
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
        private string _preparedTrainingDatasetPath = string.Empty;
        private string _trainingName = string.Empty;
        private string _trainingTrigger = string.Empty;
        private string _trainingDescription = string.Empty;
        private string _assetTrigger = string.Empty;
        private int _selectedTab;
        private int _selectedEngineTab;
        private FamilyChoice _selectedFamily;
        private ThemeChoice _selectedTheme;
        private LanguageChoice _selectedLanguage;
        private bool _continueTraining;
        private decimal _trainingSteps = TrainingPreset.MaxSteps;
        private bool _trainingActive;
        private bool _trainingCancellationRequested;
        private bool _trainingRegistering;
        private bool _hasTrainingRemainingTime;
        private TimeSpan _trainingRemainingTime;
        private AssetItem _selectedResumeLora;
        private int _runtimeModulesRequest;
        private Exception _runtimeModulesFailure;

        public ObservableCollection<AssetItem> Models { get; private set; } = new ObservableCollection<AssetItem>();
        public bool HasAvailableModels
        {
            get
            {
                return Models.Count > 0;
            }
        }
        public bool NeedsModelSetup
        {
            get
            {
                return _initialized == true && HasAvailableModels == false;
            }
        }
        public string EngineSetupCaption
        {
            get
            {
                if (_initialized == false)
                {
                    return _strings.GetString("EidolonText402");
                }
                if (IsEngineConnected == true)
                {
                    return _strings.GetString("EidolonText400");
                }
                if (HasLocalModelRuntime == true)
                {
                    return _strings.GetString("EidolonText406");
                }
                return _strings.GetString("EidolonText401");
            }
        }
        public string ModelSetupCaption
        {
            get
            {
                if (_initialized == false)
                {
                    return _strings.GetString("EidolonText402");
                }
                if (InstallingModel != null)
                {
                    return _strings.GetString("EidolonText394");
                }
                if (HasAvailableModels == true)
                {
                    return _strings.GetString("EidolonText396");
                }
                return _strings.GetString("EidolonText391");
            }
        }
        public string ModelSetupHint
        {
            get
            {
                if (_initialized == false)
                {
                    return _strings.GetString("EidolonText402");
                }
                ModelDownloadItem installing = InstallingModel;
                if (installing != null)
                {
                    return _strings.Format("EidolonText395", installing.Name);
                }
                if (HasAvailableModels == true)
                {
                    return _strings.Format("EidolonText397", Models.Count);
                }
                if (ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == true)
                {
                    return _strings.GetString("EidolonText403");
                }
                if (HasLocalModelRuntime == false)
                {
                    return _strings.GetString("EidolonText405");
                }
                return _strings.GetString("EidolonText392");
            }
        }
        public string ModelSetupActionCaption
        {
            get
            {
                if (InstallingModel != null)
                {
                    return _strings.GetString("EidolonText399");
                }
                if (HasAvailableModels == true)
                {
                    return _strings.GetString("EidolonText398");
                }
                if (HasLocalModelRuntime == false && IsEngineConnected == false)
                {
                    return _strings.GetString("EidolonText407");
                }
                if (ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == true)
                {
                    return _strings.GetString("EidolonText404");
                }
                return _strings.GetString("EidolonText393");
            }
        }
        private ModelDownloadItem InstallingModel
        {
            get
            {
                return DownloadableModels.FirstOrDefault(item => item.IsInstalling == true);
            }
        }
        public ObservableCollection<AssetItem> Library { get; private set; } = new ObservableCollection<AssetItem>();
        public ObservableCollection<AssetItem> Loras { get; private set; } = new ObservableCollection<AssetItem>();
        public string LoraSearch
        {
            get
            {
                return _loraSearch;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Set(ref _loraSearch, value) == true)
                {
                    RefreshLoraList();
                }
            }
        }
        public IEnumerable<AssetItem> FilteredLoras
        {
            get
            {
                string search = LoraSearch.Trim();
                return Loras.Where(item => item.Label.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                    || item.Asset.EngineName.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                    || item.Asset.TriggerWord.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
            }
        }
        public bool HasMatchingLoras
        {
            get
            {
                return FilteredLoras.Any();
            }
        }
        public bool ShowLoraSearch
        {
            get
            {
                return HasAvailableLoras == true && (Loras.Count > 4 || string.IsNullOrWhiteSpace(LoraSearch) == false);
            }
        }
        public string SelectedLorasCaption
        {
            get
            {
                return _strings.Format("EidolonText434", Loras.Count(item => item.IsSelected == true));
            }
        }
        public string SelectedLoraNames
        {
            get
            {
                return string.Join(", ", Loras.Where(item => item.IsSelected == true).Select(item => item.Label));
            }
        }
        public bool HasSelectedLoras
        {
            get
            {
                return Loras.Any(item => item.IsSelected == true);
            }
        }
        public bool HasAvailableLoras
        {
            get
            {
                return Loras.Count > 0;
            }
        }
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
        public ObservableCollection<QueuedWorkItem> PendingRequests { get; private set; } = new ObservableCollection<QueuedWorkItem>();
        public ObservableCollection<RuntimeModuleItem> RuntimeModules { get; private set; } = new ObservableCollection<RuntimeModuleItem>();
        public ObservableCollection<RuntimeModuleItem> CustomNodes { get; private set; } = new ObservableCollection<RuntimeModuleItem>();
        public List<FamilyChoice> Families { get; private set; }
        public ObservableCollection<FamilyChoice> AvailableFamilies { get; private set; } = new ObservableCollection<FamilyChoice>();
        public List<ThemeChoice> Themes { get; private set; }
        public List<LanguageChoice> Languages { get; private set; }
        public List<TrainingImageGroup> TrainingImageGroups { get; private set; } = new List<TrainingImageGroup>();
        public List<ModelDownloadItem> DownloadableModels { get; private set; }

        public AsyncCommand GenerateCommand { get; private set; }
        public AsyncCommand EditImageCommand { get; private set; }
        public AsyncCommand ShowEditingCommand { get; private set; }
        public AsyncCommand PickReferenceImageCommand { get; private set; }
        public AsyncCommand ClearReferenceImageCommand { get; private set; }
        public AsyncCommand UseResultAsReferenceCommand { get; private set; }
        public AsyncCommand TrainCommand { get; private set; }
        public AsyncCommand QuickTrainingCommand { get; private set; }
        public AsyncCommand StandardTrainingCommand { get; private set; }
        public AsyncCommand CancelTrainingCommand { get; private set; }
        public AsyncCommand InstallCommand { get; private set; }
        public AsyncCommand PickInstallCommand { get; private set; }
        public AsyncCommand CreateTrainingDatasetCommand { get; private set; }
        public AsyncCommand OpenTrainingDatasetCommand { get; private set; }
        public AsyncCommand PickGenerationDirectoryCommand { get; private set; }
        public AsyncCommand OpenGenerationDirectoryCommand { get; private set; }
        public AsyncCommand SaveSettingsCommand { get; private set; }
        public AsyncCommand StartEngineCommand { get; private set; }
        public AsyncCommand StopEngineCommand { get; private set; }
        public AsyncCommand OpenLogsCommand { get; private set; }
        public AsyncCommand ShowGenerationCommand { get; private set; }
        public AsyncCommand ShowResultsCommand { get; private set; }
        public AsyncCommand SelectGenerationCommand { get; private set; }
        public AsyncCommand RefreshGalleryCommand { get; private set; }
        public AsyncCommand PreviousGalleryPageCommand { get; private set; }
        public AsyncCommand NextGalleryPageCommand { get; private set; }
        public AsyncCommand ClearGallerySelectionCommand { get; private set; }
        public AsyncCommand ShowTrainingCommand { get; private set; }
        public AsyncCommand ShowEngineCommand { get; private set; }
        public AsyncCommand PrepareModelsCommand { get; private set; }
        public AsyncCommand ShowSettingsCommand { get; private set; }
        public AsyncCommand ShowHelpCommand { get; private set; }
        public AsyncCommand ImportCommand { get; private set; }
        public AsyncCommand ScanCommand { get; private set; }
        public AsyncCommand UpdateAssetCommand { get; private set; }
        public AsyncCommand RemoveAssetCommand { get; private set; }
        public AsyncCommand ExportCommand { get; private set; }
        public AsyncCommand DeleteGenerationCommand { get; private set; }
        public AsyncCommand DeleteAllGenerationsCommand { get; private set; }
        public AsyncCommand ReusePromptCommand { get; private set; }
        public AsyncCommand UseResultForTrainingCommand { get; private set; }
        public AsyncCommand OpenImageFolderCommand { get; private set; }
        public AsyncCommand OpenDataCommand { get; private set; }
        public AsyncCommand OpenRuntimeCommand { get; private set; }
        public AsyncCommand ClearPendingCommand { get; private set; }
        public AsyncCommand RefreshRuntimeModulesCommand { get; private set; }

        public bool HasRuntimeModules
        {
            get
            {
                return RuntimeModules.Count > 0;
            }
        }
        public bool HasCustomNodes
        {
            get
            {
                return CustomNodes.Count > 0;
            }
        }
        public bool HasRuntimeModulesError
        {
            get
            {
                return _runtimeModulesFailure != null;
            }
        }
        public bool ShowRuntimeModulesEmpty
        {
            get
            {
                return HasRuntimeModules == false && HasRuntimeModulesError == false;
            }
        }
        public bool ShowCustomNodesEmpty
        {
            get
            {
                return HasRuntimeModules == true && HasCustomNodes == false && HasRuntimeModulesError == false;
            }
        }
        public string RuntimeModulesError
        {
            get
            {
                if (_runtimeModulesFailure == null)
                {
                    return string.Empty;
                }
                return _strings.Format("EidolonText286", _strings.GetExceptionMessage(_runtimeModulesFailure));
            }
        }

        public DesktopSettings SettingsDraft { get; private set; } = new DesktopSettings();
        public string InstallDirectory
        {
            get
            {
                return SettingsDraft.InstallDirectory;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (string.Equals(SettingsDraft.InstallDirectory, value, StringComparison.Ordinal) == true)
                {
                    return;
                }
                SettingsDraft.InstallDirectory = value;
                Raise();
                RefreshCommands();
                if (_initialized == true && _closing == false)
                {
                    _ = RefreshRuntimeModulesAsync(_appLifetime.Token);
                }
            }
        }
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
                return CanQueue == true && AreTrainingImagesLoading == false && TrainingInputIssue() == StudioMessageCode.None;
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
                return _closing == false && _trainingActive == true && _trainingCancellationRequested == false &&
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

        public string GenerationDirectory
        {
            get
            {
                return SettingsDraft.GenerationDirectory;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (SettingsDraft.GenerationDirectory != value)
                {
                    SettingsDraft.GenerationDirectory = value;
                    Raise();
                }
            }
        }
        public bool IsBusy
        {
            get
            {
                return _isBusy;
            }
            private set
            {
                if (Set(ref _isBusy, value) == true)
                {
                    Raise(nameof(IsIdle));
                    RefreshCommands();
                }
            }
        }
        public string ServerAddress
        {
            get
            {
                return SettingsDraft.ServerAddress;
            }
            set
            {
                if (string.Equals(SettingsDraft.ServerAddress, value, StringComparison.Ordinal) == true)
                {
                    return;
                }
                SettingsDraft.ServerAddress = value;
                Raise();
                Raise(nameof(StartEngineCaption));
                Raise(nameof(CanInstallEngine));
                RefreshCommands();
                ScheduleEngineSettings();
            }
        }
        public string EngineStatus
        {
            get
            {
                if (_engine.Connection == null)
                {
                    return _strings.GetString("EidolonText269");
                }
                return _strings.Format("EidolonText268", _engine.Connection.ServerAddress);
            }
        }
        public string StopEngineCaption
        {
            get
            {
                if (_engine.Connection != null && _engine.Connection.OwnsProcess == false)
                {
                    return _strings.GetString("EidolonText267");
                }
                return _strings.GetString("EidolonText266");
            }
        }
        public bool IsEngineConnected
        {
            get
            {
                return _engine.Connection != null;
            }
        }
        public string StartEngineCaption
        {
            get
            {
                if (ComfyServerAddress.CanStartLocally(SettingsDraft) == true)
                {
                    return _strings.GetString("EidolonText265");
                }
                return _strings.GetString("EidolonText271");
            }
        }
        public bool CanInstallEngine
        {
            get
            {
                return IsIdle == true && ComfyServerAddress.CanStartLocally(SettingsDraft) == true;
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
        public AssetItem SelectedResumeLora
        {
            get
            {
                return _selectedResumeLora;
            }
            set
            {
                if (Set(ref _selectedResumeLora, value) == false)
                {
                    return;
                }
                RefreshTrainingSetup();
                if (value == null)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(TrainingName) == true)
                {
                    TrainingName = value.Asset.Name;
                }
                if (string.IsNullOrWhiteSpace(TrainingTrigger) == true)
                {
                    TrainingTrigger = value.Asset.TriggerWord;
                }
            }
        }
        public bool IsIdle
        {
            get
            {
                return _isBusy == false && _work.IsIdle == true && _initialized == true && _closing == false;
            }
        }
        public bool CanQueue
        {
            get
            {
                return _initialized == true && _closing == false && _maintenanceWork == false && _work.HasScheduledWork == false;
            }
        }
        public bool CanEditGenerationInputs
        {
            get
            {
                return _initialized == true && _closing == false;
            }
        }
        private GenerationReferenceDraft ActiveReference
        {
            get
            {
                if (IsEditingView == true)
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
                if (IsEditingView == true)
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
                if (IsEditingView == true)
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
                if (IsEditingView == true)
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
                if (IsEditingView == true)
                {
                    return EditImageCommand;
                }
                return GenerateCommand;
            }
        }
        public bool HasPendingRequests
        {
            get
            {
                return PendingRequests.Count > 0;
            }
        }
        public string QueueSummary
        {
            get
            {
                return _strings.Format("EidolonText245", PendingRequests.Count);
            }
        }
        public string ActiveRequestTitle
        {
            get
            {
                return _work.CurrentTitle;
            }
        }
        public string GenerationPositivePrompt
        {
            get
            {
                return _settings.PositivePrompt;
            }
        }
        public string GenerationNegativePrompt
        {
            get
            {
                return _settings.NegativePrompt;
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
                return string.IsNullOrEmpty(_lastQueuedGenerationSeed) == false;
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
        public string Status
        {
            get
            {
                return _status;
            }
            private set
            {
                if (Set(ref _status, value) == true)
                {
                    LogHelper.Info(value);
                }
            }
        }
        public string Error
        {
            get
            {
                return _error;
            }
            private set
            {
                Set(ref _error, value);
                Raise(nameof(HasError));
            }
        }
        public bool HasError
        {
            get
            {
                return string.IsNullOrEmpty(_error) == false;
            }
        }
        public double Percent
        {
            get
            {
                return _percent;
            }
            private set
            {
                Set(ref _percent, value);
            }
        }
        public bool Indeterminate
        {
            get
            {
                return _indeterminate;
            }
            private set
            {
                Set(ref _indeterminate, value);
            }
        }
        public int SelectedTab
        {
            get
            {
                return _selectedTab;
            }
            set
            {
                if (Set(ref _selectedTab, value) == true)
                {
                    Raise(nameof(IsGenerationView));
                    Raise(nameof(IsEditingView));
                    Raise(nameof(IsGenerationWorkspace));
                    Raise(nameof(GenerationInputCaption));
                    Raise(nameof(GenerationActionCaption));
                    Raise(nameof(GenerationSubmitCommand));
                    Raise(nameof(IsResultsView));
                    Raise(nameof(IsTrainingView));
                    Raise(nameof(IsEngineView));
                    Raise(nameof(IsSettingsView));
                    Raise(nameof(IsHelpView));
                    RefreshReferenceInputs();
                    if (value == 0 || value == 1 || value == 6)
                    {
                        SelectedResult = SelectedGeneration;
                    }
                }
            }
        }
        public bool IsGenerationView
        {
            get
            {
                return SelectedTab == 0;
            }
        }
        public bool IsResultsView
        {
            get
            {
                return SelectedTab == 6;
            }
        }
        public bool IsEditingView
        {
            get
            {
                return SelectedTab == 1;
            }
        }
        public bool IsGenerationWorkspace
        {
            get
            {
                return IsGenerationView == true || IsEditingView == true;
            }
        }
        public int SelectedEngineTab
        {
            get
            {
                return _selectedEngineTab;
            }
            set
            {
                Set(ref _selectedEngineTab, value);
            }
        }
        public bool IsTrainingView
        {
            get
            {
                return SelectedTab == 2;
            }
        }
        public bool IsEngineView
        {
            get
            {
                return SelectedTab == 3;
            }
        }
        public bool IsSettingsView
        {
            get
            {
                return SelectedTab == 4;
            }
        }
        public bool IsHelpView
        {
            get
            {
                return SelectedTab == 5;
            }
        }
        public ThemeChoice SelectedTheme
        {
            get
            {
                return _selectedTheme;
            }
            set
            {
                if (Set(ref _selectedTheme, value) == true && value != null)
                {
                    SettingsDraft.Theme = value.Value;
                    _themes.Apply(value.Value);
                }
            }
        }
        public LanguageChoice SelectedLanguage
        {
            get
            {
                return _selectedLanguage;
            }
            set
            {
                if (Set(ref _selectedLanguage, value) == true && value != null)
                {
                    SettingsDraft.Language = value.Value;
                    _languages.Apply(value.Value);
                    foreach (ThemeChoice theme in Themes)
                    {
                        theme.Localize(_strings);
                    }
                    foreach (FamilyChoice family in Families)
                    {
                        family.Localize(_strings);
                    }
                    foreach (TrainingImageGroup group in TrainingImageGroups)
                    {
                        group.Localize();
                    }
                    Raise(nameof(TrainingImageSummary));
                    Raise(nameof(ReferenceImageHint));
                    Raise(nameof(GenerationActionCaption));
                    Raise(nameof(GenerationInputCaption));
                    Raise(nameof(SelectedLorasCaption));
                    foreach (AssetItem item in Library.Concat(Loras))
                    {
                        item.Localize();
                    }
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
                    foreach (RuntimeModuleItem item in RuntimeModules.Concat(CustomNodes))
                    {
                        item.Localize();
                    }
                    foreach (ModelDownloadItem item in DownloadableModels)
                    {
                        item.Localize();
                    }
                    Raise(nameof(RuntimeModulesError));
                    Raise(nameof(ModelDownloadHint));
                    Status = _strings.TranslateMessage(Status);
                    Raise(nameof(QueueSummary));
                    Raise(nameof(EngineStatus));
                    Raise(nameof(StopEngineCaption));
                    Raise(nameof(StartEngineCaption));
                    Raise(nameof(TrainingSpeedDescription));
                    Raise(nameof(TrainingSpeedCaption));
                    Raise(nameof(TrainingRemainingText));
                    Raise(nameof(CancelTrainingCaption));
                    Raise(nameof(TrainingSetupHint));
                }
            }
        }
        public bool CanManageDownloadedModels
        {
            get
            {
                return IsIdle == true && ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == false
                    && HasLocalModelRuntime == true;
            }
        }
        private bool HasLocalModelRuntime
        {
            get
            {
                if (Path.IsPathFullyQualified(_settings.InstallDirectory) == false)
                {
                    return false;
                }
                RuntimeLayout layout = new RuntimeLayout(_settings.InstallDirectory);
                return File.Exists(layout.ComfyPython) == true && File.Exists(Path.Combine(layout.ComfyDirectory, "main.py")) == true;
            }
        }
        public string ModelDownloadHint
        {
            get
            {
                if (ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == true)
                {
                    return _strings.GetString("EidolonText352");
                }
                if (HasLocalModelRuntime == false)
                {
                    return _strings.GetString("EidolonText353");
                }
                return _strings.GetString("EidolonText343");
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
        public string PreparedTrainingDatasetPath
        {
            get
            {
                return _preparedTrainingDatasetPath;
            }
        }
        public bool HasPreparedTrainingDataset
        {
            get
            {
                return string.IsNullOrEmpty(_preparedTrainingDatasetPath) == false;
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
                Set(ref _trainingName, value);
                RefreshTrainingSetup();
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
                Set(ref _trainingTrigger, value);
                RefreshTrainingSetup();
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
                Set(ref _trainingDescription, value);
            }
        }
        public string AssetTrigger
        {
            get
            {
                return _assetTrigger;
            }
            set
            {
                Set(ref _assetTrigger, value);
            }
        }
        public FamilyChoice SelectedFamily
        {
            get
            {
                return _selectedFamily;
            }
            set
            {
                Set(ref _selectedFamily, value);
            }
        }
        public AssetItem SelectedModel
        {
            get
            {
                return _selectedModel;
            }
            set
            {
                if (Set(ref _selectedModel, value) == true)
                {
                    RebuildLoras();
                    RefreshTrainingSetup();
                }
            }
        }
        public AssetItem SelectedAsset
        {
            get
            {
                return _selectedAsset;
            }
            set
            {
                if (Set(ref _selectedAsset, value) == true)
                {
                    RefreshAvailableFamilies();
                    if (value != null)
                    {
                        SelectedFamily = Families.First(family => family.Value == value.Asset.Family);
                        AssetTrigger = value.Asset.TriggerWord;
                    }
                    RefreshCommands();
                }
            }
        }
        public bool HasSelectedAsset
        {
            get
            {
                return SelectedAsset != null;
            }
        }
        public bool IsSelectedAssetLora
        {
            get
            {
                return SelectedAsset != null && SelectedAsset.Asset.Kind == AssetKind.Lora;
            }
        }
        public bool NeedsAssetClassification
        {
            get
            {
                return SelectedAsset != null && SelectedAsset.Asset.Family == ModelFamily.Unknown;
            }
        }
        public bool HasLibrary
        {
            get
            {
                return Library.Count > 0;
            }
        }
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
                if (Set(ref _selectedGeneration, value) == true && (IsGenerationView == true || IsResultsView == true))
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

        public StudioViewModel(SettingsStore settingsStore, AssetLibrary assets, JobStore jobs,
            RuntimeInstaller installer, ComfyEngine engine, StudioService studio, DesktopDialogs dialogs,
            ThemeService themes, LanguageService languages, StringHelper strings, StudioWorkPresenter work,
            ISeedProvider seeds, string dataDirectory)
        {
            _settingsStore = settingsStore;
            _assets = assets;
            _jobs = jobs;
            _installer = installer;
            _engine = engine;
            _studio = studio;
            _seeds = seeds;
            _dialogs = dialogs;
            _themes = themes;
            _languages = languages;
            _strings = strings;
            _work = work;
            _work.QueueChanged += RefreshQueue;
            _work.Started += OnWorkStarted;
            _work.ProgressChanged += OnWorkProgress;
            _work.Finished += OnWorkFinished;
            _dataDirectory = dataDirectory;
            GallerySelection.CollectionChanged += OnGallerySelectionChanged;
            Status = _strings.GetString("EidolonText214");
            Families = new List<FamilyChoice>
            {
                new FamilyChoice(ModelFamily.Unknown, _strings.GetString("EidolonText219")),
                new FamilyChoice(ModelFamily.StableDiffusion15, "Stable Diffusion 1.5"),
                new FamilyChoice(ModelFamily.Sdxl, "SDXL")
            };
            _selectedFamily = Families[0];
            AvailableFamilies.Add(Families[0]);
            Themes = new List<ThemeChoice>
            {
                new ThemeChoice(AppTheme.Light, _strings.GetString("EidolonText220")),
                new ThemeChoice(AppTheme.Black, _strings.GetString("EidolonText221"))
            };
            Languages = new List<LanguageChoice>
            {
                new LanguageChoice(AppLanguage.Korean, _strings.GetString("EidolonText222")),
                new LanguageChoice(AppLanguage.English, "English")
            };
            TrainingImageGroups = new List<TrainingImageGroup>
            {
                new TrainingImageGroup(TrainingBackground.White, "EidolonText461", Brushes.White,
                    _strings, _dialogs, () => CanQueue, RefreshTrainingSource, OnCommandError, _appLifetime.Token),
                new TrainingImageGroup(TrainingBackground.Black, "EidolonText462", Brushes.Black,
                    _strings, _dialogs, () => CanQueue, RefreshTrainingSource, OnCommandError, _appLifetime.Token)
            };
            foreach (TrainingImageGroup group in TrainingImageGroups)
            {
                _commands.AddRange(group.Commands);
            }
            DownloadableModels = new List<ModelDownloadItem>();
            foreach (ModelDownload model in _assets.Downloads)
            {
                AsyncCommand install = Command(() => WorkAsync(token => DownloadModelAsync(model, token)),
                    () => CanManageDownloadedModels == true && FindDownloadedModel(model) == null);
                AsyncCommand delete = Command(() => DeleteDownloadedModelAsync(model),
                    () => CanManageDownloadedModels == true && FindDownloadedModel(model) != null);
                DownloadableModels.Add(new ModelDownloadItem(model, _strings, install, delete));
            }
            GenerateCommand = Command(QueueGenerationAsync,
                () => CanQueue == true && SelectedModel != null && IsGenerationSeedValid == true
                    && _generationReference.AreOptionsValid == true && string.IsNullOrWhiteSpace(Prompt) == false);
            EditImageCommand = Command(QueueEditingAsync,
                () => CanQueue == true && SelectedModel != null && IsGenerationSeedValid == true
                    && _editingReference.HasImage == true && _editingReference.AreOptionsValid == true
                    && string.IsNullOrWhiteSpace(EditingPrompt) == false);
            PickReferenceImageCommand = Command(PickReferenceImageAsync, () => CanEditGenerationInputs == true);
            ClearReferenceImageCommand = Command(ClearReferenceImageAsync, () => CanEditGenerationInputs == true && HasReferenceImage == true);
            UseResultAsReferenceCommand = Command(UseResultAsReferenceAsync,
                () => CanEditGenerationInputs == true && HasSelectedResult == true && HasPreview == true);
            TrainCommand = Command(QueueTrainingAsync, () => CanQueueTraining == true);
            QuickTrainingCommand = Command(() => SetTrainingStepsAsync(TrainingPreset.QuickMaxSteps), () => CanQueue == true);
            StandardTrainingCommand = Command(() => SetTrainingStepsAsync(TrainingPreset.MaxSteps), () => CanQueue == true);
            CancelTrainingCommand = Command(CancelTrainingAsync, () => CanCancelTraining == true);
            InstallCommand = Command(() => WorkAsync(InstallAsync), () => CanInstallEngine == true);
            PickInstallCommand = Command(PickInstallAsync);
            CreateTrainingDatasetCommand = Command(CreateTrainingDatasetAsync,
                () => CanQueue == true && HasTrainingImages == true && AreTrainingImagesLoading == false);
            OpenTrainingDatasetCommand = Command(() => _dialogs.OpenFolderAsync(_preparedTrainingDatasetPath),
                () => _closing == false && HasPreparedTrainingDataset == true);
            PickGenerationDirectoryCommand = Command(PickGenerationDirectoryAsync, () => _closing == false);
            OpenGenerationDirectoryCommand = Command(OpenGenerationDirectoryAsync, () => _closing == false);
            SaveSettingsCommand = Command(() => WorkAsync(SaveSettingsAsync));
            StartEngineCommand = Command(() =>
            {
                _work.CancelScheduled();
                return WorkAsync(SaveEngineSettingsAsync);
            });
            StopEngineCommand = Command(() =>
            {
                _work.CancelScheduled();
                return WorkAsync(StopEngineAsync);
            }, () => IsIdle == true && _engine.Connection != null);
            OpenLogsCommand = Command(() => _dialogs.OpenFolderAsync(Path.Combine(_dataDirectory, "Logs")), () => true);
            ShowGenerationCommand = Command(() => NavigateAsync(0), () => true);
            ShowEditingCommand = Command(() => NavigateAsync(1), () => true);
            ShowResultsCommand = Command(() => NavigateAsync(6), () => true);
            SelectGenerationCommand = new AsyncCommand(SelectGenerationAsync,
                () => _closing == false && IsGalleryLoading == false, OnCommandError);
            _commands.Add(SelectGenerationCommand);
            RefreshGalleryCommand = Command(() => RefreshGalleryAsync(),
                () => _closing == false && IsGalleryLoading == false);
            PreviousGalleryPageCommand = Command(() => LoadGalleryPageAsync(_galleryPageNumber - 1),
                () => _closing == false && IsGalleryLoading == false && _galleryPageNumber > 1);
            NextGalleryPageCommand = Command(() => LoadGalleryPageAsync(_galleryPageNumber + 1),
                () => _closing == false && IsGalleryLoading == false && _galleryPageNumber < _galleryPageCount);
            ClearGallerySelectionCommand = Command(() =>
            {
                GallerySelection.Clear();
                return Task.CompletedTask;
            }, () => _closing == false && HasGallerySelection == true);
            ShowTrainingCommand = Command(() => NavigateAsync(2), () => true);
            ShowEngineCommand = Command(() => NavigateAsync(3), () => true);
            PrepareModelsCommand = Command(() =>
            {
                if (HasAvailableModels == false && HasLocalModelRuntime == false && IsEngineConnected == false)
                {
                    SelectedEngineTab = 0;
                }
                else
                {
                    SelectedEngineTab = 1;
                }
                return NavigateAsync(3);
            }, () => _initialized == true && _closing == false);
            ShowSettingsCommand = Command(() => NavigateAsync(4), () => true);
            ShowHelpCommand = Command(() => NavigateAsync(5), () => true);
            ImportCommand = Command(() => WorkAsync(ImportAsync), () => IsIdle == true && ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == false);
            ScanCommand = Command(() => WorkAsync(ScanAssetsAsync));
            UpdateAssetCommand = Command(() => WorkAsync(UpdateAssetAsync), () => IsIdle == true && SelectedAsset != null);
            RemoveAssetCommand = Command(() => WorkAsync(RemoveAssetAsync), () => IsIdle == true && SelectedAsset != null);
            ExportCommand = Command(() => _dialogs.ExportImageAsync(_previewPath), () => _closing == false && HasPreview == true);
            DeleteGenerationCommand = Command(() => DeleteGenerationsAsync(false),
                () => IsIdle == true && IsGalleryLoading == false && HasGallerySelection == true);
            DeleteAllGenerationsCommand = Command(() => DeleteGenerationsAsync(true),
                () => IsIdle == true && IsGalleryLoading == false && HasGenerations == true);
            ReusePromptCommand = Command(ReusePromptAsync,
                () => _closing == false && SelectedResult != null && SelectedResult.HasMetadata == true);
            UseResultForTrainingCommand = Command(UseResultForTrainingAsync,
                () => CanQueue == true && HasPreview == true && SelectedResult != null);
            OpenImageFolderCommand = Command(OpenImageFolderAsync, () => _closing == false);
            OpenDataCommand = Command(() => _dialogs.OpenFolderAsync(_dataDirectory), () => true);
            OpenRuntimeCommand = Command(() => _dialogs.OpenFolderAsync(new RuntimeLayout(InstallDirectory).Root),
                () => IsIdle == true && string.IsNullOrWhiteSpace(InstallDirectory) == false);
            ClearPendingCommand = Command(ClearPendingAsync, () => CanQueue == true && HasPendingRequests == true);
            RefreshRuntimeModulesCommand = Command(() => WorkAsync(RefreshRuntimeModulesAsync));
        }

        private AsyncCommand Command(Func<Task> action, Func<bool> canExecute = null)
        {
            if (canExecute == null)
            {
                canExecute = () => IsIdle;
            }
            AsyncCommand command = new AsyncCommand(action, canExecute, OnCommandError);
            _commands.Add(command);
            return command;
        }

        private void OnCommandError(Exception error)
        {
            LogHelper.Error(error);
            Error = _strings.GetExceptionMessage(error);
        }

        public async Task InitializeAsync()
        {
            if (_closing == true)
            {
                return;
            }
            _initialized = false;
            _work.CancelScheduled();
            Raise(nameof(IsIdle));
            Raise(nameof(CanQueue));
            Raise(nameof(CanInstallEngine));
            RefreshCommands();
            try
            {
                _settings = _settingsStore.Load();
                RefreshGenerationInstructions();
                SettingsDraft = _settings.Copy();
                Raise(nameof(SettingsDraft));
                Raise(nameof(InstallDirectory));
                Raise(nameof(GenerationDirectory));
                Raise(nameof(ServerAddress));
                Raise(nameof(StartEngineCaption));
                SelectedTheme = Themes.First(theme => theme.Value == _settings.Theme);
                SelectedLanguage = Languages.First(language => language.Value == _settings.Language);
                await RefreshRuntimeModulesAsync(_appLifetime.Token);
                await RefreshAssetsAsync();
                if (_closing == true)
                {
                    return;
                }
                await RefreshGalleryAsync(true);
                if (_closing == true)
                {
                    return;
                }
                _initialized = true;
                Error = string.Empty;
                Status = _strings.GetString("EidolonText224");
                DesktopSettings startupSettings = _settings.Copy();
                _maintenanceWork = true;
                _work.Enqueue(_strings.GetString("EidolonText255"), token => ExecuteMaintenanceAsync(
                    startupToken => StartEngineOnStartupAsync(startupSettings, startupToken), token));
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
                Error = _strings.GetString("EidolonText226") + _strings.GetExceptionMessage(error);
            }
            Raise(nameof(IsIdle));
            Raise(nameof(CanQueue));
            Raise(nameof(CanInstallEngine));
            RefreshCommands();
        }

        private Task NavigateAsync(int tab)
        {
            SelectedTab = tab;
            if (tab == 6)
            {
                return RefreshGalleryAsync();
            }
            if ((tab == 0 || tab == 1) && IsIdle == true && (HasLocalModelRuntime == true || _engine.Connection != null))
            {
                return WorkAsync(ScanAssetsAsync);
            }
            return Task.CompletedTask;
        }

        private Task WorkAsync(Func<CancellationToken, Task> action)
        {
            if (IsIdle == false)
            {
                return Task.CompletedTask;
            }
            _maintenanceWork = true;
            Raise(nameof(CanQueue));
            _work.Enqueue(_strings.GetString("EidolonText227"), token => ExecuteMaintenanceAsync(action, token));
            return Task.CompletedTask;
        }

        private async Task ExecuteMaintenanceAsync(Func<CancellationToken, Task> action, CancellationToken token)
        {
            _maintenanceWork = true;
            Raise(nameof(CanQueue));
            RefreshCommands();
            try
            {
                await action(token);
            }
            finally
            {
                _maintenanceWork = false;
                Raise(nameof(CanQueue));
                RefreshCommands();
            }
        }

        private void RefreshQueue()
        {
            if (_closing == true)
            {
                return;
            }
            PendingRequests.Clear();
            foreach (string title in _work.GetPendingTitles())
            {
                PendingRequests.Add(new QueuedWorkItem(title));
            }
            Raise(nameof(HasPendingRequests));
            Raise(nameof(QueueSummary));
            Raise(nameof(ActiveRequestTitle));
            Raise(nameof(IsIdle));
            Raise(nameof(CanQueue));
            Raise(nameof(CanInstallEngine));
            RefreshCommands();
        }

        private Task ClearPendingAsync()
        {
            _work.ClearPending();
            return Task.CompletedTask;
        }

        private void OnWorkStarted()
        {
            IsBusy = true;
            Error = string.Empty;
            Percent = 0;
            Indeterminate = true;
            Status = _strings.GetString("EidolonText227");
            Raise(nameof(CanInstallEngine));
        }

        private void OnWorkProgress(WorkProgress progress)
        {
            Status = _strings.Format(progress.Code, progress.Arguments);
            Percent = progress.Percent;
            Indeterminate = progress.IsIndeterminate;
            if (_trainingActive == true)
            {
                if (_trainingCancellationRequested == true)
                {
                    Status = _strings.GetString("EidolonText328");
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
            Status = _strings.GetString("EidolonText328");
            RefreshTrainingState();
            await _work.CancelCurrentAsync();
        }

        private void OnWorkFinished(StudioWorkState state, Exception failure)
        {
            if (state == StudioWorkState.Cancelled)
            {
                Status = _strings.GetString("EidolonText228");
            }
            if (state == StudioWorkState.Failed)
            {
                Status = _strings.GetString("EidolonText229");
                Error = _strings.GetExceptionMessage(failure);
            }
            try
            {
                _ = RefreshGalleryAsync();
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
                Error = _strings.GetExceptionMessage(error);
            }
            IsBusy = false;
            Indeterminate = false;
            Raise(nameof(CanInstallEngine));
            Raise(nameof(EngineStatus));
            Raise(nameof(StopEngineCaption));
            Raise(nameof(IsEngineConnected));
            Raise(nameof(StartEngineCaption));
        }

        private async Task PickReferenceImageAsync()
        {
            GenerationReferenceDraft reference = ActiveReference;
            string path = await _dialogs.PickReferenceImageAsync();
            if (string.IsNullOrWhiteSpace(path) == false && _closing == false)
            {
                await LoadReferenceImageAsync(reference, path);
            }
        }

        private async Task<bool> LoadReferenceImageAsync(GenerationReferenceDraft reference, string path, string imageName = null)
        {
            int request = reference.BeginLoad();
            CancellationToken token = _appLifetime.Token;
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
            if (_closing == true || reference.IsCurrentLoad(request) == false)
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
            Error = string.Empty;
            return true;
        }

        private Task ClearReferenceImageAsync()
        {
            ActiveReference.Clear();
            RefreshReferenceInputs();
            Error = string.Empty;
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

        private async Task UseResultAsReferenceAsync()
        {
            GenerationItem result = SelectedResult;
            if (await LoadReferenceImageAsync(_editingReference, result.Image.FilePath) == false)
            {
                return;
            }
            _editingReference.ChangeStrength = 0.35;
            if (result.HasMetadata == true)
            {
                EditingPrompt = result.Metadata.UserPrompt;
                AssetItem model = FindReusableAsset(Models, result.Metadata.Model);
                if (model != null)
                {
                    SelectedModel = model;
                }
            }
            else
            {
                EditingPrompt = string.Empty;
            }
            SelectedTab = 1;
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
            if (UseRandomGenerationSeed == true)
            {
                seed = _seeds.Next();
            }
            else if (long.TryParse(GenerationSeed, NumberStyles.None, CultureInfo.InvariantCulture, out seed) == false || seed < 0)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationSeed, 0, long.MaxValue);
            }
            ModelAsset model = null;
            if (SelectedModel != null)
            {
                model = SelectedModel.Asset.Copy();
            }
            DesktopSettings settings = CreateActiveSettings(_settings);
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
            List<ModelAsset> loras = Loras.Where(lora => lora.IsSelected == true).Select(lora => lora.Asset.Copy()).ToList();
            _work.Enqueue(_strings.GetString(titleKey) + " · " + prompt,
                token => GenerateAsync(settings, prompt, model, loras, removeBackground, seed, reference, token));
            _lastQueuedGenerationSeed = seed.ToString(CultureInfo.InvariantCulture);
            Raise(nameof(HasQueuedGenerationSeed));
            Raise(nameof(QueuedGenerationSeedCaption));
            return Task.CompletedTask;
        }

        private async Task GenerateAsync(DesktopSettings settings, string prompt, ModelAsset model,
            List<ModelAsset> loras, bool removeBackground, long seed, GenerationReferenceInput reference, CancellationToken token)
        {
            JobRecord job = await _studio.GenerateAsync(settings, prompt, model, loras, removeBackground, seed, reference, _work.Progress, token);
            if (IsResultsView == false)
            {
                string path = _jobs.ImagePath(job, job.ImageFiles.Last());
                GenerationImage image = await Task.Run(() => _jobs.FindGenerationImage(job.OutputDirectory, path, token), token);
                if (image != null)
                {
                    SelectedGeneration = new GenerationItem(image, _strings);
                }
            }
            Status = _strings.GetString("EidolonText004");
            Percent = 100;
        }

        private StudioMessageCode TrainingInputIssue()
        {
            if (ContinueTraining == true && SelectedResumeLora == null)
            {
                return StudioMessageCode.LoraRequired;
            }
            if (SelectedModel == null)
            {
                return StudioMessageCode.ModelRequired;
            }
            if (HasTrainingImages == false)
            {
                return StudioMessageCode.DatasetRequired;
            }
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
            if (TrainingSteps != decimal.Truncate(TrainingSteps) || TrainingSteps < TrainingPreset.MinimumSteps ||
                TrainingSteps > TrainingPreset.MaximumSteps)
            {
                return StudioMessageCode.InvalidTrainingSteps;
            }
            return StudioMessageCode.None;
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
            if (TrainingSteps != decimal.Truncate(TrainingSteps) || TrainingSteps < TrainingPreset.MinimumSteps ||
                TrainingSteps > TrainingPreset.MaximumSteps)
            {
                throw new StudioException(StudioMessageCode.InvalidTrainingSteps,
                    TrainingPreset.MinimumSteps, TrainingPreset.MaximumSteps);
            }
            if (ContinueTraining == true && SelectedResumeLora == null)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText260"));
            }
            TrainingInput input = CreateTrainingInput();
            DesktopSettings settings = CreateActiveSettings(_settings);
            _work.Enqueue(_strings.GetString("EidolonText108") + " · " + input.Name, token => TrainAsync(settings, input, token));
            return Task.CompletedTask;
        }

        private TrainingInput CreateTrainingInput()
        {
            ModelAsset model = null;
            if (SelectedModel != null)
            {
                model = SelectedModel.Asset.Copy();
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
            if (ContinueTraining == true && SelectedResumeLora != null)
            {
                input.ResumeLora = SelectedResumeLora.Asset.Copy();
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
            try
            {
                ModelAsset asset = await _studio.TrainAsync(settings, input, _work.Progress, token);
                await RefreshAssetsAsync();
                AssetItem lora = Loras.FirstOrDefault(item => item.Asset.Id == asset.Id);
                if (lora != null)
                {
                    lora.IsSelected = true;
                }
                Status = _strings.GetString("EidolonText012");
                Percent = 100;
            }
            finally
            {
                _trainingActive = false;
                _hasTrainingRemainingTime = false;
                RefreshTrainingState();
                if (_closing == false && ComfyServerAddress.UsesServerAssets(settings) == false)
                {
                    try
                    {
                        await _engine.EnsureReadyAsync(settings, _work.Progress, _appLifetime.Token);
                    }
                    catch (OperationCanceledException) when (_closing == true)
                    {
                    }
                    catch (Exception error)
                    {
                        LogHelper.Error(error);
                        Error = _strings.GetExceptionMessage(error);
                    }
                }
            }
        }

        private async Task StartEngineOnStartupAsync(DesktopSettings settings, CancellationToken token)
        {
            try
            {
                await StartEngineAsync(settings, token);
            }
            catch (StudioException error) when (error.Code == StudioMessageCode.InstallDirectoryRequired ||
                error.Code == StudioMessageCode.RuntimeNotInstalled || error.Code == StudioMessageCode.RuntimeIncomplete ||
                error.Code == StudioMessageCode.GenerationRuntimeMissing)
            {
                SelectedTab = 3;
                Status = _strings.GetString("EidolonText225");
            }
        }

        private async Task StartEngineAsync(DesktopSettings settings, CancellationToken token)
        {
            await _engine.EnsureReadyAsync(settings, _work.Progress, token);
            DesktopSettings activeSettings = CreateActiveSettings(settings);
            if (ComfyServerAddress.UsesServerAssets(activeSettings) == true)
            {
                await _engine.RefreshExternalModelsAsync(activeSettings, _assets, token);
                await RefreshAssetsAsync();
                Status = _strings.GetString("EidolonText256");
            }
            else
            {
                await Task.Run(() => _assets.ScanAsync(settings, _work.Progress, token), token);
                await RefreshAssetsAsync();
                Status = EngineStatus;
            }
            Percent = 100;
            Raise(nameof(EngineStatus));
            Raise(nameof(StopEngineCaption));
            Raise(nameof(IsEngineConnected));
            Raise(nameof(StartEngineCaption));
            RefreshCommands();
        }

        private DesktopSettings CreateActiveSettings(DesktopSettings settings)
        {
            DesktopSettings active = settings.Copy();
            EngineConnection connection = _engine.Connection;
            if (connection != null && connection.UsesLocalAssets == false &&
                connection.ServerAddress == ComfyServerAddress.Root(settings))
            {
                active.UseServerAssets = true;
                active.ServerAddress = connection.ServerAddress;
            }
            return active;
        }

        private void ScheduleEngineSettings()
        {
            if (_initialized == false || _closing == true)
            {
                return;
            }
            _work.CancelScheduled();
            DesktopSettings settings = CreateEngineSettings();
            try
            {
                settings.Validate();
            }
            catch (StudioException)
            {
                return;
            }
            _work.ScheduleWhenIdle(_strings.GetString("EidolonText255"),
                token => ExecuteMaintenanceAsync(SaveEngineSettingsAsync, token));
        }

        private async Task StopEngineAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _engine.StopAsync();
            await RefreshAssetsAsync();
            Status = _strings.GetString("EidolonText269");
            Percent = 0;
            Raise(nameof(EngineStatus));
            Raise(nameof(StopEngineCaption));
            Raise(nameof(IsEngineConnected));
            Raise(nameof(StartEngineCaption));
        }

        private async Task InstallAsync(CancellationToken token)
        {
            DesktopSettings settings = CreateEngineSettings();
            settings.Validate();
            await _engine.StopAsync();
            _settingsStore.Save(settings);
            _settings = settings;
            try
            {
                await _installer.InstallAsync(settings, _work.Progress, token);
                await StartEngineAsync(settings, token);
                await RefreshAssetsAsync();
                Status = _strings.GetString("EidolonText230");
                if (Models.Count > 0)
                {
                    Status = _strings.GetString("EidolonText231");
                    SelectedTab = 0;
                }
                else
                {
                    SelectedTab = 3;
                    SelectedEngineTab = 1;
                }
            }
            finally
            {
                await RefreshRuntimeModulesAsync(_appLifetime.Token);
            }
        }

        private async Task RefreshRuntimeModulesAsync(CancellationToken token)
        {
            int request = ++_runtimeModulesRequest;
            string directory = InstallDirectory.Trim();
            try
            {
                IReadOnlyList<RuntimeModule> modules = Array.Empty<RuntimeModule>();
                if (Path.IsPathFullyQualified(directory) == true)
                {
                    modules = await Task.Run(() => _installer.ReadModules(directory, token), token);
                }
                if (_closing == true || request != _runtimeModulesRequest)
                {
                    return;
                }
                RuntimeModules.Clear();
                CustomNodes.Clear();
                foreach (RuntimeModule module in modules)
                {
                    RuntimeModuleItem item = new RuntimeModuleItem(module, _strings);
                    if (module.Kind == RuntimeModuleKind.CustomNode)
                    {
                        CustomNodes.Add(item);
                    }
                    else
                    {
                        RuntimeModules.Add(item);
                    }
                }
                _runtimeModulesFailure = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested == true)
            {
                return;
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
                if (_closing == true || request != _runtimeModulesRequest)
                {
                    return;
                }
                RuntimeModules.Clear();
                CustomNodes.Clear();
                _runtimeModulesFailure = error;
            }
            Raise(nameof(HasRuntimeModules));
            Raise(nameof(HasCustomNodes));
            Raise(nameof(HasRuntimeModulesError));
            Raise(nameof(ShowRuntimeModulesEmpty));
            Raise(nameof(ShowCustomNodesEmpty));
            Raise(nameof(RuntimeModulesError));
        }

        private ModelAsset FindDownloadedModel(ModelDownload download)
        {
            AssetItem item = Library.FirstOrDefault(asset => asset.Asset.Kind == AssetKind.Checkpoint
                && asset.Asset.EngineName.Equals(download.FileName, StringComparison.OrdinalIgnoreCase) == true);
            if (item == null)
            {
                return null;
            }
            return item.Asset;
        }

        private async Task DownloadModelAsync(ModelDownload download, CancellationToken token)
        {
            ModelDownloadItem item = DownloadableModels.Single(entry => entry.Model == download);
            item.IsInstalling = true;
            RefreshCommands();
            try
            {
                ModelAsset model = await _assets.DownloadAsync(CreateActiveSettings(_settings), download.FileName, _work.Progress, token);
                if (string.IsNullOrEmpty(_settings.DefaultModelId) == true)
                {
                    _settings.DefaultModelId = model.Id;
                    _settingsStore.Save(_settings);
                    SettingsDraft.DefaultModelId = model.Id;
                }
                await RefreshAssetsAsync();
                Status = _strings.Format("EidolonText349", download.Name);
            }
            finally
            {
                item.IsInstalling = false;
                RefreshCommands();
            }
        }

        private async Task DeleteDownloadedModelAsync(ModelDownload download)
        {
            ModelAsset model = FindDownloadedModel(download);
            if (model == null)
            {
                return;
            }
            DesktopSettings settings = CreateActiveSettings(_settings);
            string path = new RuntimeLayout(settings.InstallDirectory).AssetPath(model);
            string id = model.Id;
            if (await _dialogs.ConfirmDeleteAsync(_strings.GetString("EidolonText356"),
                _strings.Format("EidolonText357", download.Name, path)) == false)
            {
                return;
            }
            await WorkAsync(async token =>
            {
                try
                {
                    await _assets.DeleteDownloadedAsync(settings, id, token);
                    if (_settings.DefaultModelId == id)
                    {
                        _settings.DefaultModelId = string.Empty;
                        SettingsDraft.DefaultModelId = string.Empty;
                        _settingsStore.Save(_settings);
                    }
                    Status = _strings.Format("EidolonText358", download.Name);
                }
                finally
                {
                    await RefreshAssetsAsync();
                }
            });
        }

        private async Task RemoveAssetAsync(string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _assets.RemoveAsync(id);
            if (_settings.DefaultModelId == id)
            {
                _settings.DefaultModelId = string.Empty;
                SettingsDraft.DefaultModelId = string.Empty;
                _settingsStore.Save(_settings);
            }
            await RefreshAssetsAsync();
            Status = _strings.GetString("EidolonText240");
        }

        private async Task PickInstallAsync()
        {
            string path = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText233"));
            if (string.IsNullOrEmpty(path) == false)
            {
                InstallDirectory = path;
            }
        }

        private async Task CreateTrainingDatasetAsync()
        {
            TrainingInput input = CreateTrainingInput();
            string parentDirectory = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText472"));
            if (string.IsNullOrEmpty(parentDirectory) == true || _closing == true)
            {
                return;
            }
            _work.Enqueue(_strings.GetString("EidolonText471"), async token =>
            {
                string path = await _studio.PrepareTrainingDatasetAsync(input, parentDirectory, _work.Progress, token);
                _preparedTrainingDatasetPath = path;
                Raise(nameof(PreparedTrainingDatasetPath));
                Raise(nameof(HasPreparedTrainingDataset));
                Status = _strings.Format("EidolonText473", input.Images.Count);
                RefreshCommands();
            });
        }

        private async Task PickGenerationDirectoryAsync()
        {
            string path = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText299"));
            if (string.IsNullOrEmpty(path) == false)
            {
                GenerationDirectory = path;
            }
        }

        private async Task SaveSettingsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DesktopSettings settings = _settings.Copy();
            settings.Theme = SettingsDraft.Theme;
            settings.Language = SettingsDraft.Language;
            settings.PositivePrompt = SettingsDraft.PositivePrompt;
            settings.NegativePrompt = SettingsDraft.NegativePrompt;
            settings.GenerationDirectory = GenerationDirectory.Trim();
            _settingsStore.Save(settings);
            _settings = settings;
            GenerationDirectory = settings.GenerationDirectory;
            RefreshGenerationInstructions();
            await RefreshAssetsAsync();
            Status = _strings.GetString("EidolonText235");
        }

        private DesktopSettings CreateEngineSettings()
        {
            DesktopSettings settings = _settings.Copy();
            settings.InstallDirectory = SettingsDraft.InstallDirectory;
            settings.UseCpu = SettingsDraft.UseCpu;
            settings.ServerAddress = string.Empty;
            if (SettingsDraft.ServerAddress != null)
            {
                settings.ServerAddress = SettingsDraft.ServerAddress.Trim();
            }
            return settings;
        }

        private async Task SaveEngineSettingsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _work.CancelScheduled();
            DesktopSettings settings = CreateEngineSettings();
            settings.Validate();
            bool connectionChanged = ComfyServerAddress.Root(settings) != ComfyServerAddress.Root(_settings) ||
                settings.InstallDirectory != _settings.InstallDirectory || settings.UseCpu != _settings.UseCpu;
            if (connectionChanged == true)
            {
                await _engine.StopAsync();
            }
            _settingsStore.Save(settings);
            _settings = settings;
            await StartEngineAsync(settings, token);
            await RefreshAssetsAsync();
            if (_engine.Connection != null)
            {
                Status = EngineStatus;
            }
        }

        private async Task ImportAsync(CancellationToken token)
        {
            string path = await _dialogs.PickModelAsync();
            if (string.IsNullOrEmpty(path) == true)
            {
                return;
            }
            Status = _strings.GetString("EidolonText237");
            ModelAsset asset = await _assets.ImportAsync(_settings, path, ModelFamily.Unknown, string.Empty, token);
            if (asset.Kind == AssetKind.Checkpoint && string.IsNullOrEmpty(_settings.DefaultModelId) == true)
            {
                _settings.DefaultModelId = asset.Id;
                SettingsDraft.DefaultModelId = asset.Id;
                _settingsStore.Save(_settings);
            }
            await RefreshAssetsAsync();
            SelectedAsset = Library.First(item => item.Asset.Id == asset.Id);
            Status = _strings.GetString("EidolonText238");
        }

        private async Task UpdateAssetAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _assets.UpdateAsync(SelectedAsset.Asset, SelectedFamily.Value, AssetTrigger);
            await RefreshAssetsAsync();
            Status = _strings.GetString("EidolonText239");
        }

        private async Task RemoveAssetAsync(CancellationToken token)
        {
            await RemoveAssetAsync(SelectedAsset.Asset.Id, token);
        }

        private async Task ReusePromptAsync()
        {
            GenerationMetadata job = SelectedResult.Metadata;
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
                reference.ChangeStrength = job.Denoise;
                try
                {
                    await LoadReferenceImageAsync(reference, job.ReferenceImagePath, job.ReferenceImageName);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    reference.SetMissingImage(job.ReferenceImageName);
                    RefreshReferenceInputs();
                    Error = _strings.GetString("EidolonText454");
                }
            }
            RemoveBackground = job.RemoveBackground;
            GenerationSeed = job.Seed.ToString(CultureInfo.InvariantCulture);
            UseRandomGenerationSeed = false;
            SelectedModel = FindReusableAsset(Models, job.Model);
            bool missingAsset = SelectedModel == null;
            foreach (AssetItem lora in Loras)
            {
                lora.IsSelected = false;
            }
            foreach (ModelAsset recordedLora in job.Loras)
            {
                AssetItem lora = FindReusableAsset(Loras, recordedLora);
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
                Error = _strings.GetString("EidolonText370");
            }
            SelectedTab = targetTab;
            RefreshReferenceInputs();
        }

        private AssetItem FindReusableAsset(IEnumerable<AssetItem> assets, ModelAsset recorded)
        {
            AssetItem result = assets.FirstOrDefault(item => item.Asset.Id == recorded.Id);
            if (result == null)
            {
                result = assets.FirstOrDefault(item => item.Asset.Kind == recorded.Kind && item.Asset.Family == recorded.Family
                    && string.Equals(item.Asset.RuntimeRoot, recorded.RuntimeRoot, StringComparison.OrdinalIgnoreCase) == true
                    && string.Equals(item.Asset.EngineName, recorded.EngineName, StringComparison.Ordinal) == true);
            }
            return result;
        }

        private void RefreshGenerationInstructions()
        {
            Raise(nameof(GenerationPositivePrompt));
            Raise(nameof(GenerationNegativePrompt));
        }

        private async Task DeleteGenerationsAsync(bool all)
        {
            string directory = _jobs.OutputDirectory(_settings.GenerationDirectory);
            List<string> paths = new List<string>();
            if (all == true)
            {
                CancellationToken token = _appLifetime.Token;
                paths.AddRange(await Task.Run(() => _jobs.LoadGenerationPaths(directory, token), token));
                if (_closing == true)
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
            await WorkAsync(async token =>
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
                Status = _strings.Format("EidolonText323", paths.Count);
            });
        }

        private async Task UseResultForTrainingAsync()
        {
            GenerationItem result = SelectedResult;
            string path = _previewPath;
            bool hasImages = HasTrainingImages;
            await TrainingImageGroups[0].AddPathsAsync(new[] { path });
            if (hasImages == true)
            {
                SelectedTab = 2;
                Status = _strings.GetString("EidolonText302");
                return;
            }
            GenerationMetadata metadata = result.Metadata;
            AssetItem model = null;
            TrainingDescription = string.Empty;
            if (metadata != null)
            {
                model = FindReusableAsset(Models, metadata.Model);
                TrainingDescription = metadata.PositivePrompt;
            }
            SelectedModel = model;
            ContinueTraining = false;
            SelectedResumeLora = null;
            TrainingName = string.Empty;
            TrainingTrigger = string.Empty;
            SelectedTab = 2;
            Status = _strings.GetString("EidolonText302");
            if (model == null)
            {
                Status = _strings.GetString("EidolonText301");
            }
        }

        private void RefreshTrainingSource()
        {
            Raise(nameof(HasTrainingImages));
            Raise(nameof(AreTrainingImagesLoading));
            Raise(nameof(TrainingImageSummary));
            RefreshTrainingSetup();
        }

        private Task OpenGenerationDirectoryAsync()
        {
            string directory = GenerationDirectory.Trim();
            if (string.IsNullOrWhiteSpace(directory) == true)
            {
                directory = DesktopSettings.DefaultGenerationDirectory;
            }
            return _dialogs.OpenFolderAsync(directory);
        }

        private Task OpenImageFolderAsync()
        {
            if (HasPreview == true && IsResultsView == false)
            {
                return _dialogs.OpenFolderAsync(Path.GetDirectoryName(_previewPath));
            }
            if (string.IsNullOrWhiteSpace(_settings.GenerationDirectory) == false)
            {
                return _dialogs.OpenFolderAsync(_settings.GenerationDirectory);
            }
            return _dialogs.OpenFolderAsync(Path.Combine(_dataDirectory, "Images"));
        }

        private async Task ScanAssetsAsync(CancellationToken token)
        {
            DesktopSettings settings = CreateActiveSettings(_settings);
            if (ComfyServerAddress.UsesServerAssets(settings) == true)
            {
                await _engine.EnsureReadyAsync(settings, _work.Progress, token);
                await _engine.RefreshExternalModelsAsync(settings, _assets, token);
            }
            else
            {
                await Task.Run(() => _assets.ScanAsync(settings, _work.Progress, token), token);
            }
            await RefreshAssetsAsync();
            Status = _strings.GetString("EidolonText223");
        }

        private async Task RefreshAssetsAsync()
        {
            List<ModelAsset> assets = await _assets.ListAsync(CreateActiveSettings(_settings));
            string selectedId = string.Empty;
            if (SelectedModel != null)
            {
                selectedId = SelectedModel.Asset.Id;
            }
            HashSet<string> selectedLoras = Loras.Where(item => item.IsSelected == true).Select(item => item.Asset.Id).ToHashSet();
            string resumeId = string.Empty;
            if (SelectedResumeLora != null)
            {
                resumeId = SelectedResumeLora.Asset.Id;
            }
            Library.Clear();
            Models.Clear();
            foreach (ModelAsset asset in assets)
            {
                AssetItem item = new AssetItem(asset, _strings);
                Library.Add(item);
                if (asset.Kind == AssetKind.Checkpoint)
                {
                    Models.Add(item);
                }
            }
            bool localAssets = ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == false;
            foreach (ModelDownloadItem item in DownloadableModels)
            {
                item.IsInstalled = localAssets == true && assets.Any(asset => asset.Kind == AssetKind.Checkpoint
                    && asset.EngineName.Equals(item.Model.FileName, StringComparison.OrdinalIgnoreCase) == true);
            }
            Raise(nameof(HasAvailableModels));
            SelectedAsset = null;
            RefreshAvailableFamilies();
            SelectedModel = Models.FirstOrDefault(item => item.Asset.Id == selectedId);
            if (SelectedModel == null)
            {
                SelectedModel = Models.FirstOrDefault(item => item.Asset.Id == _settings.DefaultModelId);
            }
            if (SelectedModel == null)
            {
                SelectedModel = Models.FirstOrDefault(item => item.Asset.Family == ModelFamily.Sdxl);
            }
            if (SelectedModel == null)
            {
                SelectedModel = Models.FirstOrDefault(item => item.Asset.Family != ModelFamily.Unknown);
            }
            RebuildLoras();
            foreach (AssetItem item in Loras)
            {
                item.IsSelected = selectedLoras.Contains(item.Asset.Id);
            }
            SelectedResumeLora = Loras.FirstOrDefault(item => item.Asset.Id == resumeId);
            RefreshCommands();
        }

        private void RebuildLoras()
        {
            string resumeId = string.Empty;
            if (SelectedResumeLora != null)
            {
                resumeId = SelectedResumeLora.Asset.Id;
            }
            HashSet<string> selected = Loras.Where(item => item.IsSelected == true).Select(item => item.Asset.Id).ToHashSet();
            foreach (AssetItem item in Loras)
            {
                item.PropertyChanged -= OnLoraPropertyChanged;
            }
            Loras.Clear();
            if (SelectedModel == null)
            {
                SelectedResumeLora = null;
                Raise(nameof(HasAvailableLoras));
                RefreshLoraList();
                return;
            }
            foreach (AssetItem item in Library)
            {
                if (item.Asset.Kind == AssetKind.Lora && item.Asset.Family == SelectedModel.Asset.Family && item.Asset.Family != ModelFamily.Unknown)
                {
                    AssetItem lora = new AssetItem(item.Asset, _strings) { IsSelected = selected.Contains(item.Asset.Id) };
                    lora.PropertyChanged += OnLoraPropertyChanged;
                    Loras.Add(lora);
                }
            }
            SelectedResumeLora = Loras.FirstOrDefault(item => item.Asset.Id == resumeId);
            Raise(nameof(HasAvailableLoras));
            RefreshLoraList();
        }

        private void OnLoraPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(AssetItem.IsSelected))
            {
                Raise(nameof(SelectedLorasCaption));
                Raise(nameof(SelectedLoraNames));
                Raise(nameof(HasSelectedLoras));
            }
        }

        private void RefreshLoraList()
        {
            Raise(nameof(ShowLoraSearch));
            Raise(nameof(FilteredLoras));
            Raise(nameof(HasMatchingLoras));
            Raise(nameof(SelectedLorasCaption));
            Raise(nameof(SelectedLoraNames));
            Raise(nameof(HasSelectedLoras));
        }

        private void RefreshAvailableFamilies()
        {
            ModelFamily selected = ModelFamily.Unknown;
            if (SelectedFamily != null)
            {
                selected = SelectedFamily.Value;
            }
            HashSet<ModelFamily> installed = Library.Select(item => item.Asset.Family).ToHashSet();
            bool needsClassification = SelectedAsset != null && SelectedAsset.Asset.Family == ModelFamily.Unknown;
            AvailableFamilies.Clear();
            foreach (FamilyChoice family in Families)
            {
                if (family.Value == ModelFamily.Unknown || installed.Contains(family.Value) == true || needsClassification == true)
                {
                    AvailableFamilies.Add(family);
                }
            }
            SelectedFamily = AvailableFamilies.FirstOrDefault(family => family.Value == selected);
            if (SelectedFamily == null)
            {
                SelectedFamily = AvailableFamilies[0];
            }
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

        private Task RefreshGalleryAsync(bool markInterrupted = false)
        {
            return LoadGalleryPageAsync(_requestedGalleryPageNumber, true, markInterrupted);
        }

        private Task LoadGalleryPageAsync(int number, bool refreshSelection = false, bool markInterrupted = false)
        {
            if (_closing == true)
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
            if (_closing == true || request != _galleryLoadRequest)
            {
                return;
            }
            using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime.Token);
            _galleryLoadCancellation = lifetime;
            CancellationToken token = lifetime.Token;
            List<Bitmap> thumbnails = new List<Bitmap>();
            IsGalleryLoading = true;
            GalleryError = string.Empty;
            string directory = _settings.GenerationDirectory;
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
                if (IsResultsView == true)
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
                if (_closing == false && request == _galleryLoadRequest)
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
                Error = _strings.GetString("EidolonText244") + _strings.GetExceptionMessage(error);
            }
        }

        private void RefreshCommands()
        {
            Raise(nameof(CanEditGenerationInputs));
            Raise(nameof(IsGenerationSeedValid));
            Raise(nameof(GenerationSeedValidationHint));
            Raise(nameof(QueuedGenerationSeedCaption));
            Raise(nameof(NeedsModelSetup));
            Raise(nameof(HasSelectedAsset));
            Raise(nameof(IsSelectedAssetLora));
            Raise(nameof(NeedsAssetClassification));
            Raise(nameof(HasLibrary));
            Raise(nameof(EngineSetupCaption));
            Raise(nameof(ModelSetupCaption));
            Raise(nameof(ModelSetupHint));
            Raise(nameof(ModelSetupActionCaption));
            Raise(nameof(CanManageDownloadedModels));
            Raise(nameof(ModelDownloadHint));
            foreach (AsyncCommand command in _commands)
            {
                command.Refresh();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _closing = true;
            _appLifetime.Cancel();
            Raise(nameof(IsIdle));
            Raise(nameof(CanQueue));
            RefreshCommands();
            try
            {
                await Task.WhenAll(_galleryLoadTask, _work.DisposeAsync().AsTask());
            }
            finally
            {
                GallerySelection.CollectionChanged -= OnGallerySelectionChanged;
                foreach (AssetItem item in Loras)
                {
                    item.PropertyChanged -= OnLoraPropertyChanged;
                }
                _work.QueueChanged -= RefreshQueue;
                _work.Started -= OnWorkStarted;
                _work.ProgressChanged -= OnWorkProgress;
                _work.Finished -= OnWorkFinished;
                await _engine.DisposeAsync();
                _appLifetime.Dispose();
                Preview = null;
                _generationReference.Dispose();
                _editingReference.Dispose();
                foreach (TrainingImageGroup group in TrainingImageGroups)
                {
                    group.Dispose();
                }
                foreach (GenerationItem item in Generations)
                {
                    item.Dispose();
                }
                Generations.Clear();
            }
        }
    }
}

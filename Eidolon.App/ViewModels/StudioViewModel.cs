using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.App.ViewModels
{
    public class StudioViewModel : ObservableObject, IAsyncDisposable
    {
        private readonly SettingsStore _settingsStore;
        private readonly AssetLibrary _assets;
        private readonly JobStore _jobs;
        private readonly RuntimeInstaller _installer;
        private readonly ComfyEngine _engine;
        private readonly StudioService _studio;
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
        private bool _removeBackground;
        private string _status = string.Empty;
        private string _error = string.Empty;
        private double _percent;
        private bool _indeterminate;
        private AssetItem _selectedModel;
        private AssetItem _selectedAsset;
        private JobItem _selectedJob;
        private JobItem _selectedGeneration;
        private Bitmap _preview;
        private string _previewPath = string.Empty;
        private string _selectedImageFile = string.Empty;
        private string _datasetDirectory = string.Empty;
        private readonly List<string> _trainingImages = new List<string>();
        private string _trainingName = string.Empty;
        private string _trainingTrigger = string.Empty;
        private string _trainingDescription = string.Empty;
        private string _assetTrigger = string.Empty;
        private int _selectedTab;
        private int _selectedEngineTab;
        private FamilyChoice _selectedFamily;
        private ThemeChoice _selectedTheme;
        private LanguageChoice _selectedLanguage;
        private bool _downloadStarter = true;
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
        public ObservableCollection<AssetItem> Library { get; private set; } = new ObservableCollection<AssetItem>();
        public ObservableCollection<AssetItem> Loras { get; private set; } = new ObservableCollection<AssetItem>();
        public bool HasAvailableLoras
        {
            get
            {
                return Loras.Count > 0;
            }
        }
        public ObservableCollection<JobItem> Generations { get; private set; } = new ObservableCollection<JobItem>();
        public ObservableCollection<string> ResultImageFiles { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<QueuedWorkItem> PendingRequests { get; private set; } = new ObservableCollection<QueuedWorkItem>();
        public ObservableCollection<RuntimeModuleItem> RuntimeModules { get; private set; } = new ObservableCollection<RuntimeModuleItem>();
        public ObservableCollection<RuntimeModuleItem> CustomNodes { get; private set; } = new ObservableCollection<RuntimeModuleItem>();
        public List<FamilyChoice> Families { get; private set; }
        public ObservableCollection<FamilyChoice> AvailableFamilies { get; private set; } = new ObservableCollection<FamilyChoice>();
        public List<ThemeChoice> Themes { get; private set; }
        public List<LanguageChoice> Languages { get; private set; }

        public AsyncCommand GenerateCommand { get; private set; }
        public AsyncCommand TrainCommand { get; private set; }
        public AsyncCommand QuickTrainingCommand { get; private set; }
        public AsyncCommand StandardTrainingCommand { get; private set; }
        public AsyncCommand CancelTrainingCommand { get; private set; }
        public AsyncCommand InstallCommand { get; private set; }
        public AsyncCommand PickInstallCommand { get; private set; }
        public AsyncCommand PickDatasetCommand { get; private set; }
        public AsyncCommand OpenDatasetFolderCommand { get; private set; }
        public AsyncCommand PickGenerationDirectoryCommand { get; private set; }
        public AsyncCommand OpenGenerationDirectoryCommand { get; private set; }
        public AsyncCommand SaveSettingsCommand { get; private set; }
        public AsyncCommand StartEngineCommand { get; private set; }
        public AsyncCommand StopEngineCommand { get; private set; }
        public AsyncCommand OpenLogsCommand { get; private set; }
        public AsyncCommand ShowGenerationCommand { get; private set; }
        public AsyncCommand ShowResultsCommand { get; private set; }
        public AsyncCommand ShowTrainingCommand { get; private set; }
        public AsyncCommand ShowEngineCommand { get; private set; }
        public AsyncCommand ShowSettingsCommand { get; private set; }
        public AsyncCommand ShowHelpCommand { get; private set; }
        public AsyncCommand ImportCommand { get; private set; }
        public AsyncCommand ScanCommand { get; private set; }
        public AsyncCommand DownloadModelCommand { get; private set; }
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
                return CanQueue == true && TrainingInputIssue() == StudioMessageCode.None;
            }
        }

        public string TrainingSetupHint
        {
            get
            {
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
        public string CommonPositivePrompt
        {
            get
            {
                return _settings.PositivePrompt;
            }
        }
        public string CommonNegativePrompt
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
        public string Prompt
        {
            get
            {
                return _prompt;
            }
            set
            {
                Set(ref _prompt, value);
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
                    Raise(nameof(IsResultsView));
                    Raise(nameof(IsTrainingView));
                    Raise(nameof(IsEngineView));
                    Raise(nameof(IsSettingsView));
                    Raise(nameof(IsHelpView));
                    if (value == 0 || value == 6)
                    {
                        SelectedJob = SelectedGeneration;
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
                    foreach (AssetItem item in Library.Concat(Loras))
                    {
                        item.Localize();
                    }
                    foreach (JobItem item in Generations)
                    {
                        item.Localize();
                    }
                    foreach (RuntimeModuleItem item in RuntimeModules.Concat(CustomNodes))
                    {
                        item.Localize();
                    }
                    Raise(nameof(RuntimeModulesError));
                    Status = _strings.TranslateMessage(Status);
                    Raise(nameof(QueueSummary));
                    Raise(nameof(EngineStatus));
                    Raise(nameof(StopEngineCaption));
                    Raise(nameof(StartEngineCaption));
                    Raise(nameof(TrainingSpeedDescription));
                    Raise(nameof(TrainingRemainingText));
                    Raise(nameof(CancelTrainingCaption));
                    Raise(nameof(PickDatasetCaption));
                    Raise(nameof(TrainingSetupHint));
                }
            }
        }
        public bool DownloadStarter
        {
            get
            {
                return _downloadStarter;
            }
            set
            {
                Set(ref _downloadStarter, value);
            }
        }
        public string DatasetDirectory
        {
            get
            {
                return _datasetDirectory;
            }
            set
            {
                _trainingImages.Clear();
                Set(ref _datasetDirectory, value);
                RefreshTrainingSource();
                RefreshTrainingSetup();
            }
        }
        public bool HasSelectedTrainingImage
        {
            get
            {
                return _trainingImages.Count > 0;
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
        public JobItem SelectedJob
        {
            get
            {
                return _selectedJob;
            }
            set
            {
                if (Set(ref _selectedJob, value) == true)
                {
                    Raise(nameof(HasSelectedJob));
                    string previousImageFile = _selectedImageFile;
                    ResultImageFiles.Clear();
                    string selectedImageFile = string.Empty;
                    if (value != null)
                    {
                        if (value.Job.Kind == JobKind.Generation)
                        {
                            Set(ref _selectedGeneration, value, nameof(SelectedGeneration));
                            foreach (string imageFile in value.Job.ImageFiles)
                            {
                                ResultImageFiles.Add(imageFile);
                            }
                            if (value.Job.ImageFiles.Contains(previousImageFile) == true)
                            {
                                selectedImageFile = previousImageFile;
                            }
                            else if (value.Job.ImageFiles.Count > 0)
                            {
                                selectedImageFile = value.Job.ImageFiles[0];
                            }
                        }
                    }
                    Set(ref _selectedImageFile, selectedImageFile, nameof(SelectedImageFile));
                    Raise(nameof(HasMultipleResultImages));
                    ShowPreview(value);
                    RefreshCommands();
                }
            }
        }
        public bool HasSelectedJob
        {
            get
            {
                return SelectedJob != null;
            }
        }
        public JobItem SelectedGeneration
        {
            get
            {
                return _selectedGeneration;
            }
            set
            {
                if (Set(ref _selectedGeneration, value) == true && (IsGenerationView == true || IsResultsView == true))
                {
                    SelectedJob = value;
                }
            }
        }
        public string SelectedImageFile
        {
            get
            {
                return _selectedImageFile;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Set(ref _selectedImageFile, value) == true)
                {
                    ShowPreview(SelectedJob);
                    RefreshCommands();
                }
            }
        }
        public bool HasMultipleResultImages
        {
            get
            {
                return ResultImageFiles.Count > 1;
            }
        }
        public bool HasGenerations
        {
            get
            {
                return Generations.Count > 0;
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
            ThemeService themes, LanguageService languages, StringHelper strings, StudioWorkPresenter work, string dataDirectory)
        {
            _settingsStore = settingsStore;
            _assets = assets;
            _jobs = jobs;
            _installer = installer;
            _engine = engine;
            _studio = studio;
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
            GenerateCommand = Command(QueueGenerationAsync, () => CanQueue == true);
            TrainCommand = Command(QueueTrainingAsync, () => CanQueueTraining == true);
            QuickTrainingCommand = Command(() => SetTrainingStepsAsync(TrainingPreset.QuickMaxSteps), () => CanQueue == true);
            StandardTrainingCommand = Command(() => SetTrainingStepsAsync(TrainingPreset.MaxSteps), () => CanQueue == true);
            CancelTrainingCommand = Command(CancelTrainingAsync, () => CanCancelTraining == true);
            InstallCommand = Command(() => WorkAsync(InstallAsync), () => CanInstallEngine == true);
            PickInstallCommand = Command(PickInstallAsync);
            PickDatasetCommand = Command(PickDatasetAsync, () => CanQueue == true);
            OpenDatasetFolderCommand = Command(() => _dialogs.OpenFolderAsync(DatasetDirectory.Trim()),
                () => _closing == false && string.IsNullOrWhiteSpace(DatasetDirectory) == false);
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
            ShowResultsCommand = Command(() => NavigateAsync(6), () => true);
            ShowTrainingCommand = Command(() => NavigateAsync(2), () => true);
            ShowEngineCommand = Command(() => NavigateAsync(3), () => true);
            ShowSettingsCommand = Command(() => NavigateAsync(4), () => true);
            ShowHelpCommand = Command(() => NavigateAsync(5), () => true);
            ImportCommand = Command(() => WorkAsync(ImportAsync), () => IsIdle == true && ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == false);
            ScanCommand = Command(() => WorkAsync(async token =>
            {
                IProgress<WorkProgress> progress = _work.Progress;
                DesktopSettings activeSettings = CreateActiveSettings(_settings);
                if (ComfyServerAddress.UsesServerAssets(activeSettings) == true)
                {
                    await _engine.EnsureReadyAsync(activeSettings, progress, token);
                    await _engine.RefreshExternalModelsAsync(activeSettings, _assets, token);
                }
                else
                {
                    await Task.Run(() => _assets.ScanAsync(_settings, progress, token), token);
                }
                await RefreshAssetsAsync();
                Status = _strings.GetString("EidolonText223");
            }));
            DownloadModelCommand = Command(() => WorkAsync(DownloadModelAsync), () => IsIdle == true && ComfyServerAddress.UsesServerAssets(CreateActiveSettings(_settings)) == false);
            UpdateAssetCommand = Command(() => WorkAsync(UpdateAssetAsync), () => IsIdle == true && SelectedAsset != null);
            RemoveAssetCommand = Command(() => WorkAsync(RemoveAssetAsync), () => IsIdle == true && SelectedAsset != null);
            ExportCommand = Command(() => _dialogs.ExportImageAsync(_previewPath), () => _closing == false && HasPreview == true);
            DeleteGenerationCommand = Command(() => DeleteGenerationsAsync(false),
                () => IsIdle == true && SelectedGeneration != null);
            DeleteAllGenerationsCommand = Command(() => DeleteGenerationsAsync(true),
                () => IsIdle == true && HasGenerations == true);
            ReusePromptCommand = Command(ReusePromptAsync,
                () => _closing == false && SelectedJob != null && SelectedJob.Job.Kind == JobKind.Generation);
            UseResultForTrainingCommand = Command(UseResultForTrainingAsync,
                () => CanQueue == true && HasPreview == true && SelectedJob != null && SelectedJob.Job.Kind == JobKind.Generation);
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
            AsyncCommand command = new AsyncCommand(action, canExecute, error =>
            {
                LogHelper.Error(error);
                Error = _strings.GetExceptionMessage(error);
            });
            _commands.Add(command);
            return command;
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
                Raise(nameof(CommonPositivePrompt));
                Raise(nameof(CommonNegativePrompt));
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
                RefreshJobs(true);
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

        public string SelectedTrainingImagePath
        {
            get
            {
                if (_trainingImages.Count == 0)
                {
                    return string.Empty;
                }
                return _trainingImages[0];
            }
        }

        public string PickDatasetCaption
        {
            get
            {
                if (HasSelectedTrainingImage == true)
                {
                    return _strings.GetString("EidolonText333");
                }
                return _strings.GetString("EidolonText142");
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
                RefreshJobs();
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

        private Task QueueGenerationAsync()
        {
            if (string.IsNullOrWhiteSpace(Prompt) == true)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText001"));
            }
            ModelAsset model = null;
            if (SelectedModel != null)
            {
                model = SelectedModel.Asset.Copy();
            }
            DesktopSettings settings = CreateActiveSettings(_settings);
            string prompt = Prompt;
            bool removeBackground = RemoveBackground;
            List<ModelAsset> loras = Loras.Where(lora => lora.IsSelected == true).Select(lora => lora.Asset.Copy()).ToList();
            _work.Enqueue(_strings.GetString("EidolonText106") + " · " + prompt,
                token => GenerateAsync(settings, prompt, model, loras, removeBackground, token));
            return Task.CompletedTask;
        }

        private async Task GenerateAsync(DesktopSettings settings, string prompt, ModelAsset model,
            List<ModelAsset> loras, bool removeBackground, CancellationToken token)
        {
            JobRecord job = await _studio.GenerateAsync(settings, prompt, model, loras, removeBackground, _work.Progress, token);
            RefreshJobs();
            if (IsResultsView == false)
            {
                SelectedGeneration = Generations.First(item => item.Job.Id == job.Id);
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
            if (string.IsNullOrWhiteSpace(DatasetDirectory) == true)
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
            ModelAsset model = null;
            if (SelectedModel != null)
            {
                model = SelectedModel.Asset.Copy();
            }
            TrainingInput input = new TrainingInput
            {
                Model = model,
                ImageDirectory = DatasetDirectory,
                ImageFiles = _trainingImages.ToList(),
                Name = TrainingName,
                TriggerWord = TrainingTrigger,
                Description = TrainingDescription,
                Steps = decimal.ToInt32(TrainingSteps)
            };
            if (ContinueTraining == true)
            {
                input.ResumeLora = SelectedResumeLora.Asset.Copy();
            }
            DesktopSettings settings = CreateActiveSettings(_settings);
            _work.Enqueue(_strings.GetString("EidolonText108") + " · " + input.Name, token => TrainAsync(settings, input, token));
            return Task.CompletedTask;
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
                if (DownloadStarter == true && ComfyServerAddress.UsesServerAssets(CreateActiveSettings(settings)) == false)
                {
                    await DownloadModelAsync(token);
                }
                await RefreshAssetsAsync();
                Status = _strings.GetString("EidolonText230");
                if (Models.Count > 0)
                {
                    Status = _strings.GetString("EidolonText231");
                    SelectedTab = 0;
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

        private async Task DownloadModelAsync(CancellationToken token)
        {
            ModelAsset model = await _assets.DownloadStarterAsync(_settings, _work.Progress, token);
            _settings.DefaultModelId = model.Id;
            _settingsStore.Save(_settings);
            SettingsDraft.DefaultModelId = model.Id;
            await RefreshAssetsAsync();
            Status = _strings.GetString("EidolonText232");
        }

        private async Task PickInstallAsync()
        {
            string path = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText233"));
            if (string.IsNullOrEmpty(path) == false)
            {
                InstallDirectory = path;
            }
        }

        private async Task PickDatasetAsync()
        {
            string path = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText234"));
            if (string.IsNullOrEmpty(path) == false)
            {
                DatasetDirectory = path;
            }
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
            Raise(nameof(CommonPositivePrompt));
            Raise(nameof(CommonNegativePrompt));
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
            ModelAsset asset = await _assets.ImportAsync(_settings, path, SelectedFamily.Value, AssetTrigger, token);
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
            token.ThrowIfCancellationRequested();
            string id = SelectedAsset.Asset.Id;
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

        private Task ReusePromptAsync()
        {
            Prompt = SelectedJob.Job.UserPrompt;
            RemoveBackground = SelectedJob.Job.RemoveBackground;
            SelectedModel = Models.FirstOrDefault(item => item.Asset.Id == SelectedJob.Job.Model.Id);
            foreach (AssetItem lora in Loras)
            {
                lora.IsSelected = SelectedJob.Job.Loras.Any(item => item.Id == lora.Asset.Id);
            }
            SelectedTab = 0;
            return Task.CompletedTask;
        }

        private async Task DeleteGenerationsAsync(bool all)
        {
            List<string> ids = new List<string>();
            string message = _strings.GetString("EidolonText319");
            if (all == true)
            {
                ids.AddRange(Generations.Select(item => item.Job.Id));
                message = _strings.Format("EidolonText320", ids.Count);
            }
            else
            {
                ids.Add(SelectedGeneration.Job.Id);
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
                    await Task.Run(() => _jobs.DeleteGenerations(ids), token);
                }
                finally
                {
                    RefreshJobs();
                }
                Status = _strings.Format("EidolonText323", ids.Count);
            });
        }

        private Task UseResultForTrainingAsync()
        {
            JobRecord job = SelectedJob.Job;
            AssetItem model = Models.FirstOrDefault(item => item.Asset.Id == job.Model.Id);
            if (model == null)
            {
                model = Models.FirstOrDefault(item => item.Asset.EngineName == job.Model.EngineName &&
                    string.Equals(item.Asset.RuntimeRoot, job.Model.RuntimeRoot, StringComparison.OrdinalIgnoreCase) == true);
            }
            SelectedModel = model;
            DatasetDirectory = Path.GetDirectoryName(_previewPath);
            _trainingImages.Add(_previewPath);
            RefreshTrainingSource();
            TrainingDescription = job.PositivePrompt;
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
            return Task.CompletedTask;
        }

        private void RefreshTrainingSource()
        {
            Raise(nameof(HasSelectedTrainingImage));
            Raise(nameof(SelectedTrainingImagePath));
            Raise(nameof(PickDatasetCaption));
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
            if (HasPreview == true)
            {
                return _dialogs.OpenFolderAsync(Path.GetDirectoryName(_previewPath));
            }
            if (string.IsNullOrWhiteSpace(_settings.GenerationDirectory) == false)
            {
                return _dialogs.OpenFolderAsync(_settings.GenerationDirectory);
            }
            return _dialogs.OpenFolderAsync(Path.Combine(_dataDirectory, "Images"));
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
            Loras.Clear();
            if (SelectedModel == null)
            {
                SelectedResumeLora = null;
                Raise(nameof(HasAvailableLoras));
                return;
            }
            foreach (AssetItem item in Library)
            {
                if (item.Asset.Kind == AssetKind.Lora && item.Asset.Family == SelectedModel.Asset.Family && item.Asset.Family != ModelFamily.Unknown)
                {
                    Loras.Add(new AssetItem(item.Asset, _strings) { IsSelected = selected.Contains(item.Asset.Id) });
                }
            }
            SelectedResumeLora = Loras.FirstOrDefault(item => item.Asset.Id == resumeId);
            Raise(nameof(HasAvailableLoras));
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

        private void RefreshJobs(bool markInterrupted = false)
        {
            string selectedImageFile = SelectedImageFile;
            string generationId = string.Empty;
            if (SelectedGeneration != null)
            {
                generationId = SelectedGeneration.Job.Id;
            }
            Generations.Clear();
            foreach (JobRecord job in _jobs.LoadAll(markInterrupted))
            {
                if (job.Kind == JobKind.Generation)
                {
                    Generations.Add(new JobItem(job, _strings));
                }
            }
            JobItem generation = Generations.FirstOrDefault(item => item.Job.Id == generationId);
            if (generation == null)
            {
                generation = Generations.FirstOrDefault(item => item.Job.ImageFiles.Count > 0);
            }
            SelectedGeneration = generation;
            Raise(nameof(HasGenerations));
            SelectedJob = generation;
            if (ResultImageFiles.Contains(selectedImageFile) == true)
            {
                SelectedImageFile = selectedImageFile;
            }
        }

        private void ShowPreview(JobItem item)
        {
            Preview = null;
            _previewPath = string.Empty;
            if (item == null || item.Job.ImageFiles.Count == 0)
            {
                return;
            }
            try
            {
                string imageFile = item.Job.ImageFiles[0];
                if (item.Job.ImageFiles.Contains(SelectedImageFile) == true)
                {
                    imageFile = SelectedImageFile;
                }
                _previewPath = _jobs.ImagePath(item.Job, imageFile);
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
                await _work.DisposeAsync();
            }
            finally
            {
                _work.QueueChanged -= RefreshQueue;
                _work.Started -= OnWorkStarted;
                _work.ProgressChanged -= OnWorkProgress;
                _work.Finished -= OnWorkFinished;
                await _engine.DisposeAsync();
                _appLifetime.Dispose();
                Preview = null;
            }
        }
    }
}

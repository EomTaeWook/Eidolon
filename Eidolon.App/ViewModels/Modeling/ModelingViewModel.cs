using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Eidolon.App.Models.Modeling;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.Services;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using Eidolon.Modeling;

namespace Eidolon.App.ViewModels
{
    public class ModelingViewModel : StudioPanelViewModel
    {
        private readonly BlenderModelingService _blenderModelingService;
        private readonly BlenderInstaller _blenderInstaller;
        private readonly CodexExecutableLocator _codexExecutableLocator;
        private readonly JobStore _jobStore;
        private readonly AssetImageLoader _assetImageLoader;
        private readonly ModelingAgentStatusChecker _agentStatusChecker;
        private ModelingAgentStatus _agentStatus = ModelingAgentStatus.Unknown;
        private string _checkedAgentExecutablePath = string.Empty;
        private string _agentConfiguredPath = string.Empty;
        private bool _hasCheckedAgent;
        private CancellationTokenSource _agentStatusCancellationTokenSource;
        private Task _agentStatusTask = Task.CompletedTask;
        private ModelingAgentStatus _gptWslSearchStatus = ModelingAgentStatus.Unknown;
        private string _detectedGptWslDistribution = string.Empty;
        private ModelingAgentStatus _detectedGptWslAgentStatus = ModelingAgentStatus.Unknown;
        private ModelingAgentStatus _wslAgentStatus = ModelingAgentStatus.Unknown;
        private string _selectedWslDistribution;
        private bool _hasWslDistributionList;
        private bool _isListingWslDistributions;
        private CancellationTokenSource _wslStatusCancellationTokenSource;
        private Task _wslStatusTask = Task.CompletedTask;
        private string _sourceBlendPath = string.Empty;
        private string _resultSourceBlendPath = string.Empty;
        private string _instructions = string.Empty;
        private decimal? _maximumTriangleCount = 0m;
        private bool _showBlenderWindow;
        private ModelingAgent _selectedModelingAgent = ModelingAgent.Codex;
        private string _outputDirectory = string.Empty;
        private string _statusKey = "EidolonText774";
        private AssetCollection _selectedCollection;
        private int _selectedInputTab;
        private ModelingInput _activeInput;
        private string _activeRequestTitle = string.Empty;
        private ModelingResult _result;
        private bool _isModeling;
        private bool _closed;
        private Task _collectionsLoadTask = Task.CompletedTask;
        private const int CollectionPageSize = 6;
        private int _collectionPageNumber = 1;
        private bool _isCollectionPickerOpen;
        private CancellationTokenSource _collectionThumbnailCancellationTokenSource;
        private Task _collectionThumbnailLoadTask = Task.CompletedTask;

        public ObservableCollection<AssetCollection> Collections
        {
            get;
            private set;
        } = new ObservableCollection<AssetCollection>();

        public ObservableCollection<ModelingCollectionItem> CollectionChoices
        {
            get;
            private set;
        } = new ObservableCollection<ModelingCollectionItem>();

        public bool IsCollectionPickerOpen
        {
            get
            {
                return _isCollectionPickerOpen;
            }
            set
            {
                if (_closed == true)
                {
                    value = false;
                }
                if (Set(ref _isCollectionPickerOpen, value) == false)
                {
                    return;
                }
                ClearCollectionChoices();
                if (value == true)
                {
                    _collectionPageNumber = 1;
                    _ = RefreshCollectionsAsync();
                }
                RefreshCommands();
            }
        }

        public bool HasCollectionChoices
        {
            get
            {
                return CollectionChoices.Count > 0;
            }
        }

        public string CollectionPageCaption
        {
            get
            {
                int pageCount = Math.Max(1, (Collections.Count + CollectionPageSize - 1) / CollectionPageSize);
                return _collectionPageNumber + " / " + pageCount;
            }
        }

        public AsyncCommand PreviousCollectionPageCommand
        {
            get;
            private set;
        }

        public AsyncCommand NextCollectionPageCommand
        {
            get;
            private set;
        }

        public AsyncCommand SelectCollectionCommand
        {
            get;
            private set;
        }

        public ModelingImageItem SingleImage
        {
            get;
            private set;
        }

        public IReadOnlyList<string> ModelingAgents
        {
            get;
        } = new string[] { "GPT", "Claude" };

        public int SelectedModelingAgentIndex
        {
            get
            {
                if (SelectedModelingAgent == ModelingAgent.Claude)
                {
                    return 1;
                }
                return 0;
            }
            set
            {
                if (value == 0)
                {
                    SelectedModelingAgent = ModelingAgent.Codex;
                }
                if (value == 1)
                {
                    SelectedModelingAgent = ModelingAgent.Claude;
                }
            }
        }

        public ModelingAgent SelectedModelingAgent
        {
            get
            {
                return _selectedModelingAgent;
            }
            set
            {
                if (Set(ref _selectedModelingAgent, value) == true)
                {
                    Raise(nameof(IsCodexAgent));
                    Raise(nameof(SelectedModelingAgentIndex));
                    CancelWslStatusCheck();
                    _wslAgentStatus = ModelingAgentStatus.Unknown;
                    _isListingWslDistributions = false;
                    CancelAgentStatusCheck();
                    _agentStatus = ModelingAgentStatus.Unknown;
                    _checkedAgentExecutablePath = string.Empty;
                    _hasCheckedAgent = false;
                    if (Navigation.IsModelingView == true)
                    {
                        _ = RefreshAgentStatusAsync();
                    }
                    RefreshCommands();
                }
            }
        }

        public bool IsCodexAgent
        {
            get
            {
                return SelectedModelingAgent == ModelingAgent.Codex;
            }
        }

        public string AgentStatusCaption
        {
            get
            {
                switch (GetDisplayedAgentStatus())
                {
                    case ModelingAgentStatus.Checking:
                        return _strings.GetString("EidolonText812");
                    case ModelingAgentStatus.Missing:
                        if (IsCodexAgent == true)
                        {
                            return _strings.GetString("EidolonText808");
                        }
                        return _strings.GetString("EidolonText810");
                    case ModelingAgentStatus.LoginRequired:
                        if (IsCodexAgent == true)
                        {
                            return _strings.GetString("EidolonText809");
                        }
                        return _strings.GetString("EidolonText811");
                    case ModelingAgentStatus.Ready:
                        if (IsCodexAgent == true)
                        {
                            return _strings.GetString("EidolonText824");
                        }
                        return _strings.GetString("EidolonText806");
                    default:
                        return _strings.GetString("EidolonText813");
                }
            }
        }

        public bool HasAgentStatusCaption
        {
            get
            {
                return string.IsNullOrEmpty(AgentStatusCaption) == false;
            }
        }

        public bool HasAgentGuidance
        {
            get
            {
                if (GetDisplayedAgentStatus() == ModelingAgentStatus.Checking)
                {
                    return false;
                }
                return GetDisplayedAgentStatus() != ModelingAgentStatus.Ready;
            }
        }

        public string AgentGuidanceLabel
        {
            get
            {
                if (IsCodexAgent == true)
                {
                    return "Codex CLI";
                }
                return "Claude Code";
            }
        }

        public string AgentGuidanceUrl
        {
            get
            {
                if (IsCodexAgent == true)
                {
                    if (GetDisplayedAgentStatus() == ModelingAgentStatus.Missing)
                    {
                        return "https://developers.openai.com/codex/quickstart";
                    }
                    return "https://learn.chatgpt.com/docs/developer-commands?surface=cli#codex-login";
                }
                if (_agentStatus == ModelingAgentStatus.Missing)
                {
                    return "https://code.claude.com/docs/en/setup";
                }
                return "https://code.claude.com/docs/en/authentication";
            }
        }

        public AsyncCommand RefreshAgentStatusCommand
        {
            get;
            private set;
        }

        public AsyncCommand OpenAgentGuidanceCommand
        {
            get;
            private set;
        }

        public bool IsWslSupported
        {
            get
            {
                return OperatingSystem.IsWindows();
            }
        }

        public bool ShowWslDetails
        {
            get
            {
                if (IsWslSupported == false)
                {
                    return false;
                }
                return IsCodexAgent == false;
            }
        }

        public bool ShowGptWslReadOnly
        {
            get
            {
                if (IsCodexAgent == false)
                {
                    return false;
                }
                if (_agentStatus == ModelingAgentStatus.Ready)
                {
                    return false;
                }
                if (_agentStatus == ModelingAgentStatus.Unknown)
                {
                    return false;
                }
                if (_agentStatus == ModelingAgentStatus.Checking)
                {
                    return false;
                }
                return string.IsNullOrEmpty(_detectedGptWslDistribution) == false;
            }
        }

        public string AgentLocationCaption
        {
            get
            {
                if (ShowGptWslReadOnly == true)
                {
                    return "WSL · " + _detectedGptWslDistribution;
                }
                if (OperatingSystem.IsWindows() == true)
                {
                    if (IsCodexAgent == true)
                    {
                        if (_agentStatus != ModelingAgentStatus.Ready)
                        {
                            if (_agentStatus == ModelingAgentStatus.LoginRequired)
                            {
                                return "Windows";
                            }
                            return "Windows / WSL";
                        }
                    }
                    return "Windows";
                }
                if (OperatingSystem.IsMacOS() == true)
                {
                    return "macOS";
                }
                return "CLI";
            }
        }

        public ObservableCollection<string> WslDistributions
        {
            get;
        } = new ObservableCollection<string>();

        public string SelectedWslDistribution
        {
            get
            {
                return _selectedWslDistribution;
            }
            set
            {
                if (Set(ref _selectedWslDistribution, value) == true)
                {
                    CancelWslStatusCheck();
                    _wslAgentStatus = ModelingAgentStatus.Unknown;
                    _isListingWslDistributions = false;
                    RefreshCommands();
                }
            }
        }

        public bool HasWslDistributionSelection
        {
            get
            {
                if (IsWslSupported == false)
                {
                    return false;
                }
                if (_closed == true)
                {
                    return false;
                }
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return WslDistributions.Contains(SelectedWslDistribution);
            }
        }

        public string WslStatusCaption
        {
            get
            {
                switch (_wslAgentStatus)
                {
                    case ModelingAgentStatus.Unknown:
                        if (_hasWslDistributionList == true)
                        {
                            if (WslDistributions.Count == 0)
                            {
                                return _strings.GetString("EidolonText825");
                            }
                        }
                        return string.Empty;
                    case ModelingAgentStatus.Checking:
                        if (_isListingWslDistributions == true)
                        {
                            return _strings.GetString("EidolonText378");
                        }
                        return _strings.GetString("EidolonText812");
                    case ModelingAgentStatus.Missing:
                        if (IsCodexAgent == true)
                        {
                            return _strings.GetString("EidolonText808");
                        }
                        return _strings.GetString("EidolonText810");
                    case ModelingAgentStatus.LoginRequired:
                        if (IsCodexAgent == true)
                        {
                            return _strings.GetString("EidolonText809");
                        }
                        return _strings.GetString("EidolonText811");
                    case ModelingAgentStatus.Ready:
                        return _strings.GetString("EidolonText824");
                    default:
                        return _strings.GetString("EidolonText813");
                }
            }
        }

        public bool HasWslStatusCaption
        {
            get
            {
                return string.IsNullOrEmpty(WslStatusCaption) == false;
            }
        }

        public AsyncCommand RefreshWslDistributionsCommand
        {
            get;
            private set;
        }

        public AsyncCommand RefreshWslAgentStatusCommand
        {
            get;
            private set;
        }

        public IReadOnlyList<ModelingImageItem> DirectionImages
        {
            get;
            private set;
        }

        public IReadOnlyList<ModelingImageItem> AdditionalDirectionImages
        {
            get;
            private set;
        }

        public AsyncCommand PickDirectionImageCommand
        {
            get;
            private set;
        }

        public AsyncCommand RemoveDirectionImageCommand
        {
            get;
            private set;
        }

        public AsyncCommand LoadCollectionCommand
        {
            get;
            private set;
        }

        public AsyncCommand PickReferenceCommand
        {
            get;
            private set;
        }

        public AsyncCommand ClearReferenceCommand
        {
            get;
            private set;
        }

        public AsyncCommand RefreshCollectionsCommand
        {
            get;
            private set;
        }

        public AsyncCommand PickBlendCommand
        {
            get;
            private set;
        }

        public AsyncCommand ClearBlendCommand
        {
            get;
            private set;
        }

        public AsyncCommand ModelCommand
        {
            get;
            private set;
        }

        public AsyncCommand CancelModelingCommand
        {
            get;
            private set;
        }

        public AsyncCommand ContinueResultCommand
        {
            get;
            private set;
        }

        public AsyncCommand OpenResultCommand
        {
            get;
            private set;
        }

        public AsyncCommand SaveBlendCommand
        {
            get;
            private set;
        }

        public AsyncCommand SaveGlbCommand
        {
            get;
            private set;
        }

        public ModelingViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs, BlenderModelingService blenderModelingService, BlenderInstaller blenderInstaller, CodexExecutableLocator codexExecutableLocator, JobStore jobStore, AssetImageLoader assetImageLoader, ModelingAgentStatusChecker agentStatusChecker) : base(session, navigation, work, strings, dialogs)
        {
            _blenderModelingService = blenderModelingService;
            _blenderInstaller = blenderInstaller;
            _codexExecutableLocator = codexExecutableLocator;
            _jobStore = jobStore;
            _assetImageLoader = assetImageLoader;
            _agentStatusChecker = agentStatusChecker;
            RefreshAgentStatusCommand = Command(RefreshAgentStatusAsync, () => Session.IsClosing == false);
            OpenAgentGuidanceCommand = Command(() => _dialogs.OpenLinkAsync(AgentGuidanceUrl), () => Session.IsClosing == false);
            RefreshWslDistributionsCommand = Command(RefreshWslDistributionsAsync, () => IsWslSupported);
            RefreshWslAgentStatusCommand = Command(RefreshWslAgentStatusAsync, () => HasWslDistributionSelection);
            SingleImage = new ModelingImageItem("reference", "EidolonText746", strings);
            DirectionImages = new ModelingImageItem[]
            {
                new ModelingImageItem("front", "EidolonText637", strings),
                new ModelingImageItem("left", "EidolonText703", strings),
                new ModelingImageItem("right", "EidolonText702", strings),
                new ModelingImageItem("back", "EidolonText639", strings)
            };
            AdditionalDirectionImages = new ModelingImageItem[]
            {
                new ModelingImageItem("top", "EidolonText640", strings),
                new ModelingImageItem("side", "EidolonText638", strings)
            };
            PickDirectionImageCommand = new AsyncCommand(parameter => PickDirectionImageAsync(parameter as ModelingImageItem), () => Session.CanEditGenerationInputs, OnCommandError);
            _commands.Add(PickDirectionImageCommand);
            RemoveDirectionImageCommand = new AsyncCommand(parameter =>
            {
                ModelingImageItem imageItem = parameter as ModelingImageItem;
                if (imageItem != null)
                {
                    imageItem.Clear();
                    RefreshCommands();
                }
                return Task.CompletedTask;
            }, () => Session.CanEditGenerationInputs, OnCommandError);
            _commands.Add(RemoveDirectionImageCommand);
            LoadCollectionCommand = Command(LoadCollectionAsync, () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                return SelectedCollection != null;
            });
            PickReferenceCommand = Command(PickReferenceAsync, () => Session.CanEditGenerationInputs);
            ClearReferenceCommand = Command(() =>
            {
                if (SelectedInputTab == 0)
                {
                    SingleImage.Clear();
                    RefreshCommands();
                }
                if (SelectedInputTab == 1)
                {
                    foreach (ModelingImageItem imageItem in DirectionImages.Concat(AdditionalDirectionImages))
                    {
                        imageItem.Clear();
                    }
                    SelectedCollection = null;
                    RefreshCommands();
                }
                return Task.CompletedTask;
            }, () => Session.CanEditGenerationInputs);
            RefreshCollectionsCommand = Command(RefreshCollectionsAsync, () => Session.CanEditGenerationInputs);
            PreviousCollectionPageCommand = Command(() =>
            {
                _collectionPageNumber--;
                return LoadCollectionChoicesAsync();
            }, () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                if (IsCollectionPickerOpen == false)
                {
                    return false;
                }
                return _collectionPageNumber > 1;
            });
            NextCollectionPageCommand = Command(() =>
            {
                _collectionPageNumber++;
                return LoadCollectionChoicesAsync();
            }, () =>
            {
                if (Session.CanEditGenerationInputs == false)
                {
                    return false;
                }
                if (IsCollectionPickerOpen == false)
                {
                    return false;
                }
                return _collectionPageNumber * CollectionPageSize < Collections.Count;
            });
            SelectCollectionCommand = new AsyncCommand(async parameter =>
            {
                ModelingCollectionItem collectionItem = parameter as ModelingCollectionItem;
                if (collectionItem == null)
                {
                    return;
                }
                if (CollectionChoices.Contains(collectionItem) == false)
                {
                    return;
                }
                SelectedCollection = collectionItem.Collection;
                IsCollectionPickerOpen = false;
                await LoadCollectionAsync();
            }, () => Session.CanEditGenerationInputs, OnCommandError);
            _commands.Add(SelectCollectionCommand);
            PickBlendCommand = Command(PickBlendAsync, () => Session.CanEditGenerationInputs);
            ClearBlendCommand = Command(() =>
            {
                SourceBlendPath = string.Empty;
                return Task.CompletedTask;
            }, () => Session.CanEditGenerationInputs);
            ModelCommand = Command(QueueModelingAsync, () => CanModel);
            CancelModelingCommand = Command(() => _work.CancelCurrentAsync(_activeRequestTitle), () => IsModeling);
            ContinueResultCommand = Command(() =>
            {
                SourceBlendPath = BlendPath;
                return Task.CompletedTask;
            }, () => CanContinue);
            OpenResultCommand = Command(() => _dialogs.OpenFolderAsync(OutputDirectory), () => CanOpenResult);
            SaveBlendCommand = Command(() => _dialogs.ExportModelingFileAsync(BlendPath, _resultSourceBlendPath), () => CanContinue);
            SaveGlbCommand = Command(() => _dialogs.ExportModelingFileAsync(GlbPath), () => CanSaveGlb);
        }

        public string ReferencePath
        {
            get
            {
                return SingleImage.ImagePath;
            }
        }

        public int SelectedInputTab
        {
            get
            {
                return _selectedInputTab;
            }
            set
            {
                if (value < 0)
                {
                    return;
                }
                if (value > 1)
                {
                    return;
                }
                if (Set(ref _selectedInputTab, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public AssetCollection SelectedCollection
        {
            get
            {
                return _selectedCollection;
            }
            set
            {
                if (Set(ref _selectedCollection, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public string SourceBlendPath
        {
            get
            {
                return _sourceBlendPath;
            }
            private set
            {
                if (Set(ref _sourceBlendPath, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public string SourceBlendFileName
        {
            get
            {
                return Path.GetFileName(SourceBlendPath);
            }
        }

        public bool HasSourceBlendPath
        {
            get
            {
                return string.IsNullOrWhiteSpace(SourceBlendPath) == false;
            }
        }

        public string SourceBlendTargetCaption
        {
            get
            {
                if (HasSourceBlendPath == false)
                {
                    return string.Empty;
                }
                return _strings.Format("EidolonText826", SourceBlendFileName);
            }
        }

        public string ModelingSubmitCaption
        {
            get
            {
                if (HasSourceBlendPath == true)
                {
                    return _strings.GetString("EidolonText827");
                }
                return _strings.GetString("EidolonText752");
            }
        }

        public bool HasBlenderExecutablePath
        {
            get
            {
                return string.IsNullOrWhiteSpace(BlenderExecutablePath) == false;
            }
        }

        public bool ShowBlenderWindow
        {
            get
            {
                return _showBlenderWindow;
            }
            set
            {
                Set(ref _showBlenderWindow, value);
            }
        }

        public decimal? MaximumTriangleCount
        {
            get
            {
                return _maximumTriangleCount;
            }
            set
            {
                if (Set(ref _maximumTriangleCount, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public string TriangleCountCaption
        {
            get
            {
                if (_result == null)
                {
                    return string.Empty;
                }
                if (_result.TriangleCount > 0)
                {
                    return _strings.Format("EidolonText789", _result.TriangleCount);
                }
                return _strings.GetString("EidolonText790");
            }
        }

        public bool HasTriangleCountCaption
        {
            get
            {
                return _result != null;
            }
        }

        public string Instructions
        {
            get
            {
                return _instructions;
            }
            set
            {
                if (Set(ref _instructions, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public string OutputDirectory
        {
            get
            {
                return _outputDirectory;
            }
            private set
            {
                Set(ref _outputDirectory, value);
                RefreshCommands();
            }
        }

        public string BlendPath
        {
            get
            {
                if (_result == null)
                {
                    return string.Empty;
                }
                return _result.BlendPath;
            }
        }

        public string GlbPath
        {
            get
            {
                if (_result == null)
                {
                    return string.Empty;
                }
                return _result.GlbPath;
            }
        }

        public string StageCaption
        {
            get
            {
                return _strings.GetString(_statusKey);
            }
        }

        public bool IsModeling
        {
            get
            {
                return _isModeling;
            }
            private set
            {
                Set(ref _isModeling, value);
                RefreshCommands();
            }
        }

        public bool CanContinue
        {
            get
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return File.Exists(BlendPath);
            }
        }

        public bool CanSaveGlb
        {
            get
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return File.Exists(GlbPath);
            }
        }

        public bool CanOpenResult
        {
            get
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return Directory.Exists(OutputDirectory);
            }
        }

        public string BlenderExecutablePath
        {
            get
            {
                string installDirectory = Session.Settings.InstallDirectory;
                if (string.IsNullOrWhiteSpace(installDirectory) == true)
                {
                    return string.Empty;
                }
                installDirectory = installDirectory.Trim();
                if (Path.IsPathFullyQualified(installDirectory) == false)
                {
                    return string.Empty;
                }
                try
                {
                    return _blenderInstaller.GetInstalledExecutable(installDirectory);
                }
                catch (ArgumentException)
                {
                    return string.Empty;
                }
                catch (IOException)
                {
                    return string.Empty;
                }
                catch (NotSupportedException)
                {
                    return string.Empty;
                }
            }
        }

        public bool NeedsBlenderInstallation
        {
            get
            {
                if (BlenderInstaller.IsPlatformSupported == false)
                {
                    return false;
                }
                return string.IsNullOrEmpty(BlenderExecutablePath);
            }
        }

        public string ReferenceCaption
        {
            get
            {
                int referenceCount = 0;
                if (SelectedInputTab == 0)
                {
                    if (File.Exists(ReferencePath) == true)
                    {
                        referenceCount = 1;
                    }
                }
                if (SelectedInputTab == 1)
                {
                    foreach (ModelingImageItem imageItem in DirectionImages.Concat(AdditionalDirectionImages))
                    {
                        if (File.Exists(imageItem.ImagePath) == true)
                        {
                            referenceCount++;
                        }
                    }
                }
                return _strings.Format("EidolonText766", referenceCount);
            }
        }

        public string InputIssue
        {
            get
            {
                if (_agentStatus != ModelingAgentStatus.Ready)
                {
                    return AgentStatusCaption;
                }
                if (SelectedModelingAgent == ModelingAgent.Claude)
                {
                    return _strings.GetString("EidolonText806");
                }
                if (BlenderInstaller.IsPlatformSupported == false)
                {
                    return _strings.GetString("EidolonText769");
                }
                if (NeedsBlenderInstallation == true)
                {
                    return _strings.GetString("EidolonText745");
                }
                if (File.Exists(_checkedAgentExecutablePath) == false)
                {
                    return _strings.GetString("EidolonText813");
                }
                if (HasSourceBlendPath == true)
                {
                    if (string.IsNullOrWhiteSpace(Instructions) == true)
                    {
                        return _strings.GetString("EidolonText765");
                    }
                }
                decimal maximumTriangleCount = MaximumTriangleCount.GetValueOrDefault();
                if (maximumTriangleCount < 0m)
                {
                    return _strings.GetString("EidolonText788");
                }
                if (maximumTriangleCount > 1000000m)
                {
                    return _strings.GetString("EidolonText788");
                }
                if (decimal.Truncate(maximumTriangleCount) != maximumTriangleCount)
                {
                    return _strings.GetString("EidolonText788");
                }
                if (string.IsNullOrWhiteSpace(SourceBlendPath) == false)
                {
                    return string.Empty;
                }
                if (SelectedInputTab == 0)
                {
                    if (string.IsNullOrWhiteSpace(ReferencePath) == false)
                    {
                        if (File.Exists(ReferencePath) == false)
                        {
                            return _strings.Format(StudioMessageCode.InvalidReferenceImage);
                        }
                        return string.Empty;
                    }
                }
                if (SelectedInputTab == 1)
                {
                    foreach (ModelingImageItem imageItem in DirectionImages.Concat(AdditionalDirectionImages))
                    {
                        if (File.Exists(imageItem.ImagePath) == true)
                        {
                            return string.Empty;
                        }
                    }
                }
                return _strings.GetString("EidolonText764");
            }
        }

        public bool HasInputIssue
        {
            get
            {
                return string.IsNullOrEmpty(InputIssue) == false;
            }
        }

        public bool CanModel
        {
            get
            {
                if (Session.CanQueue == false)
                {
                    return false;
                }
                if (_closed == true)
                {
                    return false;
                }
                return HasInputIssue == false;
            }
        }

        private Task PickReferenceAsync()
        {
            return PickDirectionImageAsync(SingleImage);
        }

        private async Task PickDirectionImageAsync(ModelingImageItem imageItem)
        {
            if (imageItem == null)
            {
                return;
            }
            if (_closed == true)
            {
                return;
            }
            string imagePath = await _dialogs.PickReferenceImageAsync();
            if (string.IsNullOrWhiteSpace(imagePath) == true)
            {
                return;
            }
            if (_closed == true)
            {
                return;
            }
            if (Session.IsClosing == true)
            {
                return;
            }
            await StartDirectionImageLoadAsync(imageItem, imagePath);
        }

        private Task<bool> StartDirectionImageLoadAsync(ModelingImageItem imageItem, string imagePath)
        {
            imageItem.ImageLoadCancellationTokenSource?.Cancel();
            CancellationTokenSource imageLoadCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            imageItem.ImageLoadCancellationTokenSource = imageLoadCancellationTokenSource;
            Task<bool> imageLoadTask = LoadDirectionImageAsync(imageItem, imageItem.ImageLoadTask, imagePath, imageLoadCancellationTokenSource);
            imageItem.ImageLoadTask = imageLoadTask;
            return imageLoadTask;
        }

        private async Task<bool> LoadDirectionImageAsync(ModelingImageItem imageItem, Task previousLoadTask, string imagePath, CancellationTokenSource imageLoadCancellationTokenSource)
        {
            Bitmap thumbnail = null;
            try
            {
                await previousLoadTask;
                imageLoadCancellationTokenSource.Token.ThrowIfCancellationRequested();
                thumbnail = await Task.Run(() => _assetImageLoader.Load(_assetImageLoader.Read(imagePath), 192, false), imageLoadCancellationTokenSource.Token);
                imageLoadCancellationTokenSource.Token.ThrowIfCancellationRequested();
                if (_closed == true)
                {
                    return false;
                }
                if (Session.IsClosing == true)
                {
                    return false;
                }
                if (imageItem.ImageLoadCancellationTokenSource != imageLoadCancellationTokenSource)
                {
                    return false;
                }
                imageItem.SetImage(imagePath, thumbnail);
                thumbnail = null;
                RefreshCommands();
                return true;
            }
            catch (OperationCanceledException) when (imageLoadCancellationTokenSource.IsCancellationRequested == true)
            {
            }
            catch (Exception error)
            {
                if (_closed == false)
                {
                    if (Session.IsClosing == false)
                    {
                        if (imageItem.ImageLoadCancellationTokenSource == imageLoadCancellationTokenSource)
                        {
                            OnCommandError(new StudioException(StudioMessageCode.InvalidReferenceImage, error));
                        }
                    }
                }
            }
            finally
            {
                thumbnail?.Dispose();
                if (imageItem.ImageLoadCancellationTokenSource == imageLoadCancellationTokenSource)
                {
                    imageItem.ImageLoadCancellationTokenSource = null;
                }
                imageLoadCancellationTokenSource.Dispose();
            }
            return false;
        }

        private async Task LoadCollectionAsync()
        {
            AssetCollection collection = SelectedCollection;
            if (collection == null)
            {
                return;
            }
            foreach (AssetFrame frame in collection.Frames.OrderBy(frame => frame.Number))
            {
                if (_closed == true)
                {
                    return;
                }
                if (Session.IsClosing == true)
                {
                    return;
                }
                if (frame.State != JobState.Completed)
                {
                    continue;
                }
                if (File.Exists(frame.ImagePath) == false)
                {
                    continue;
                }
                ModelingImageItem imageItem = DirectionImages.Concat(AdditionalDirectionImages).FirstOrDefault(candidate => candidate.Direction == frame.Label);
                if (imageItem == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(imageItem.ImagePath) == false)
                {
                    continue;
                }
                if (imageItem.ImageLoadCancellationTokenSource != null)
                {
                    continue;
                }
                if (await StartDirectionImageLoadAsync(imageItem, frame.ImagePath) == false)
                {
                    return;
                }
            }
        }

        private async Task PickBlendAsync()
        {
            string scenePath = await _dialogs.PickBlendFileAsync();
            if (string.IsNullOrWhiteSpace(scenePath) == true)
            {
                return;
            }
            if (Session.IsClosing == true)
            {
                return;
            }
            SourceBlendPath = scenePath;
        }

        public Task RefreshCollectionsAsync()
        {
            if (_closed == true)
            {
                return Task.CompletedTask;
            }
            ClearCollectionChoices();
            RefreshCommands();
            if (_collectionsLoadTask.IsCompleted == false)
            {
                return _collectionsLoadTask;
            }
            _collectionsLoadTask = LoadCollectionsAsync();
            return _collectionsLoadTask;
        }

        private async Task LoadCollectionsAsync()
        {
            if (_closed == true)
            {
                return;
            }
            try
            {
                List<AssetCollection> collections = await Task.Run(() => ReadCollections(Session.Lifetime), Session.Lifetime);
                if (_closed == true)
                {
                    return;
                }
                if (Session.IsClosing == true)
                {
                    return;
                }
                string selectedId = string.Empty;
                if (SelectedCollection != null)
                {
                    selectedId = SelectedCollection.Id;
                }
                Collections.Clear();
                foreach (AssetCollection collection in collections)
                {
                    Collections.Add(collection);
                }
                SelectedCollection = Collections.FirstOrDefault(collection => collection.Id == selectedId);
                if (IsCollectionPickerOpen == true)
                {
                    _ = LoadCollectionChoicesAsync();
                }
            }
            catch (OperationCanceledException) when (Session.Lifetime.IsCancellationRequested == true)
            {
            }
            catch (Exception error)
            {
                if (_closed == false)
                {
                    if (Session.IsClosing == false)
                    {
                        OnCommandError(error);
                    }
                }
            }
        }

        private List<AssetCollection> ReadCollections(CancellationToken cancellationToken)
        {
            List<AssetCollection> completedCollections = new List<AssetCollection>();
            foreach (AssetCollection collection in _jobStore.LoadAssetCollections(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (AssetFrame frame in collection.Frames)
                {
                    if (frame.State != JobState.Completed)
                    {
                        continue;
                    }
                    if (File.Exists(frame.ImagePath) == false)
                    {
                        continue;
                    }
                    completedCollections.Add(collection);
                    break;
                }
            }
            return completedCollections;
        }

        private void ClearCollectionChoices()
        {
            CancellationTokenSource thumbnailCancellationTokenSource = _collectionThumbnailCancellationTokenSource;
            _collectionThumbnailCancellationTokenSource = null;
            thumbnailCancellationTokenSource?.Cancel();
            foreach (ModelingCollectionItem collectionItem in CollectionChoices)
            {
                collectionItem.Dispose();
            }
            CollectionChoices.Clear();
            Raise(nameof(HasCollectionChoices));
        }

        private Task LoadCollectionChoicesAsync()
        {
            ClearCollectionChoices();
            if (_closed == true)
            {
                return _collectionThumbnailLoadTask;
            }
            if (IsCollectionPickerOpen == false)
            {
                return _collectionThumbnailLoadTask;
            }
            int pageCount = Math.Max(1, (Collections.Count + CollectionPageSize - 1) / CollectionPageSize);
            _collectionPageNumber = Math.Clamp(_collectionPageNumber, 1, pageCount);
            List<ModelingCollectionItem> pageItems = Collections.Skip((_collectionPageNumber - 1) * CollectionPageSize).Take(CollectionPageSize).Select(collection => new ModelingCollectionItem(collection, _strings)).ToList();
            foreach (ModelingCollectionItem collectionItem in pageItems)
            {
                CollectionChoices.Add(collectionItem);
            }
            Raise(nameof(HasCollectionChoices));
            RefreshCommands();
            CancellationTokenSource thumbnailCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            _collectionThumbnailCancellationTokenSource = thumbnailCancellationTokenSource;
            _collectionThumbnailLoadTask = LoadCollectionThumbnailsAsync(_collectionThumbnailLoadTask, pageItems, thumbnailCancellationTokenSource);
            return _collectionThumbnailLoadTask;
        }

        private async Task LoadCollectionThumbnailsAsync(Task previousLoadTask, IReadOnlyList<ModelingCollectionItem> pageItems, CancellationTokenSource thumbnailCancellationTokenSource)
        {
            try
            {
                await previousLoadTask;
                foreach (ModelingCollectionItem collectionItem in pageItems)
                {
                    Bitmap thumbnail = null;
                    try
                    {
                        thumbnailCancellationTokenSource.Token.ThrowIfCancellationRequested();
                        if (string.IsNullOrEmpty(collectionItem.ImagePath) == true)
                        {
                            continue;
                        }
                        thumbnail = await Task.Run(() => _assetImageLoader.Load(_assetImageLoader.Read(collectionItem.ImagePath), 192, false), thumbnailCancellationTokenSource.Token);
                        thumbnailCancellationTokenSource.Token.ThrowIfCancellationRequested();
                        if (_closed == true)
                        {
                            return;
                        }
                        if (IsCollectionPickerOpen == false)
                        {
                            return;
                        }
                        if (_collectionThumbnailCancellationTokenSource != thumbnailCancellationTokenSource)
                        {
                            return;
                        }
                        collectionItem.SetThumbnail(thumbnail);
                        thumbnail = null;
                    }
                    catch (OperationCanceledException) when (thumbnailCancellationTokenSource.IsCancellationRequested == true)
                    {
                        return;
                    }
                    catch (Exception error)
                    {
                        if (_closed == false)
                        {
                            if (_collectionThumbnailCancellationTokenSource == thumbnailCancellationTokenSource)
                            {
                                OnCommandError(new StudioException(StudioMessageCode.InvalidReferenceImage, error));
                            }
                        }
                    }
                    finally
                    {
                        thumbnail?.Dispose();
                    }
                }
            }
            finally
            {
                if (_collectionThumbnailCancellationTokenSource == thumbnailCancellationTokenSource)
                {
                    _collectionThumbnailCancellationTokenSource = null;
                }
                thumbnailCancellationTokenSource.Dispose();
            }
        }

        private Task QueueModelingAsync()
        {
            if (SelectedModelingAgent == ModelingAgent.Claude)
            {
                return Task.CompletedTask;
            }
            if (CanModel == false)
            {
                return Task.CompletedTask;
            }
            List<ModelingReference> references = new List<ModelingReference>();
            if (SelectedInputTab == 0)
            {
                if (File.Exists(ReferencePath) == true)
                {
                    references.Add(new ModelingReference("reference", ReferencePath));
                }
            }
            if (SelectedInputTab == 1)
            {
                foreach (ModelingImageItem imageItem in DirectionImages.Concat(AdditionalDirectionImages))
                {
                    if (File.Exists(imageItem.ImagePath) == false)
                    {
                        continue;
                    }
                    references.Add(new ModelingReference(imageItem.Direction, imageItem.ImagePath));
                }
            }
            string outputDirectory = Path.Combine(Session.DataDirectory, "Modeling", Guid.NewGuid().ToString("N"));
            int maximumTriangleCount = decimal.ToInt32(MaximumTriangleCount.GetValueOrDefault());
            bool showBlenderWindow = ShowBlenderWindow;
            ModelingAgent agent = SelectedModelingAgent;
            string agentExecutablePath = _checkedAgentExecutablePath;
            string agentModel = string.Empty;
            if (agent == ModelingAgent.Codex)
            {
                agentModel = Session.Settings.CodexModel;
            }
            string instructions = string.Empty;
            if (string.IsNullOrWhiteSpace(Instructions) == false)
            {
                instructions = Instructions.Trim();
            }
            ModelingInput input = new ModelingInput(agentExecutablePath, agentModel, BlenderExecutablePath, outputDirectory, SourceBlendPath, instructions, references, maximumTriangleCount, showBlenderWindow, agent);
            EnqueueModelingInput(input, false);
            return Task.CompletedTask;
        }

        internal async Task<string> EnqueueAsync(string instructions, IReadOnlyList<ModelingReference> references, string sourceBlendPath, int maximumTriangleCount, CancellationToken cancellationToken)
        {
            EnsureModelingCanQueue(cancellationToken);
            DesktopSettings settings = Session.Settings.Copy();
            string blenderExecutablePath = BlenderExecutablePath;
            if (BlenderInstaller.IsPlatformSupported == false)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText769"));
            }
            if (File.Exists(blenderExecutablePath) == false)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText745"));
            }
            string capturedInstructions = instructions ?? string.Empty;
            capturedInstructions = capturedInstructions.Trim();
            string capturedSourceBlendPath = sourceBlendPath ?? string.Empty;
            ModelingReference[] capturedReferences = references.ToArray();
            ArgumentOutOfRangeException.ThrowIfNegative(maximumTriangleCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTriangleCount, 1000000);
            if (capturedInstructions.Length > 16000)
            {
                throw new ArgumentException("instructions must not exceed 16000 characters.");
            }
            if (string.IsNullOrWhiteSpace(capturedSourceBlendPath) == false)
            {
                if (string.IsNullOrWhiteSpace(capturedInstructions) == true)
                {
                    throw new ArgumentException("Editing a source_blend_path requires instructions.");
                }
            }
            else if (capturedReferences.Length == 0)
            {
                throw new ArgumentException("A reference image or source_blend_path is required.");
            }
            List<ModelingReference> validatedReferences = await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(capturedSourceBlendPath) == false)
                {
                    capturedSourceBlendPath = ValidateModelingInputPath(capturedSourceBlendPath, ".blend");
                }
                List<ModelingReference> validated = new List<ModelingReference>();
                foreach (ModelingReference reference in capturedReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string imagePath = ValidateModelingInputPath(reference.ImagePath, string.Empty);
                    using Bitmap image = _assetImageLoader.Load(_assetImageLoader.Read(imagePath), 192, false);
                    cancellationToken.ThrowIfCancellationRequested();
                    validated.Add(new ModelingReference(reference.Direction, imagePath));
                }
                return validated;
            }, cancellationToken);
            using CancellationTokenSource readinessCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Session.Lifetime);
            readinessCancellation.CancelAfter(TimeSpan.FromSeconds(12));
            string executablePath = await Task.Run(() => FindAgentExecutable(ModelingAgent.Codex, settings.CodexExecutablePath), readinessCancellation.Token).WaitAsync(readinessCancellation.Token);
            if (string.IsNullOrEmpty(executablePath) == true)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText808"));
            }
            ModelingAgentStatus readiness = await _agentStatusChecker.CheckAsync(ModelingAgent.Codex, executablePath, readinessCancellation.Token);
            if (readiness == ModelingAgentStatus.LoginRequired)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText809"));
            }
            if (readiness != ModelingAgentStatus.Ready)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText813"));
            }
            EnsureModelingCanQueue(cancellationToken);
            string outputDirectory = Path.Combine(Session.DataDirectory, "Modeling", Guid.NewGuid().ToString("N"));
            ModelingInput input = new ModelingInput(executablePath, settings.CodexModel, blenderExecutablePath, outputDirectory, capturedSourceBlendPath, capturedInstructions, validatedReferences, maximumTriangleCount, false, ModelingAgent.Codex);
            return EnqueueModelingInput(input, true);
        }

        private void EnsureModelingCanQueue(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed == true)
            {
                throw new InvalidOperationException("The modeling screen is closing.");
            }
            if (Session.CanQueue == false)
            {
                throw new InvalidOperationException("Modeling cannot be queued during initialization, shutdown, scheduled maintenance or maintenance work.");
            }
        }

        private string ValidateModelingInputPath(string path, string requiredExtension)
        {
            if (OperatingSystem.IsWindows() == true)
            {
                string windowsPath = path.Replace('/', '\\');
                foreach (string prefix in new string[] { @"\\", @"\??\", @"\Device\" })
                {
                    if (windowsPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        throw new ArgumentException("UNC and device paths are not accepted as local modeling inputs.");
                    }
                }
            }
            if (Path.IsPathFullyQualified(path) == false)
            {
                throw new ArgumentException("Modeling input paths must be absolute local file paths.");
            }
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath) == false)
            {
                throw new ArgumentException("A modeling input file does not exist.");
            }
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (requiredExtension.Length > 0)
            {
                if (extension != requiredExtension)
                {
                    throw new ArgumentException("source_blend_path must identify a .blend file.");
                }
            }
            else
            {
                if (new string[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(extension) == false)
                {
                    throw new ArgumentException("Modeling reference images must be PNG, JPEG or WebP.");
                }
            }
            return fullPath;
        }

        private string EnqueueModelingInput(ModelingInput input, bool includeRequestId)
        {
            EnsureModelingCanQueue(Session.Lifetime);
            string requestId = Path.GetFileName(input.OutputDirectory);
            string requestTitle = _strings.GetString("EidolonText739");
            if (string.IsNullOrWhiteSpace(input.Instructions) == false)
            {
                requestTitle += " · " + input.Instructions;
            }
            if (includeRequestId == true)
            {
                requestTitle = "3D [" + requestId + "]";
            }
            _work.Enqueue(requestTitle, cancellationToken => ModelAsync(input, requestTitle, cancellationToken));
            return requestId;
        }

        internal string GetRequestState(string requestId)
        {
            string requestTitle = "3D [" + requestId + "]";
            if (_work.CurrentTitle == requestTitle)
            {
                return "running";
            }
            if (_activeInput != null)
            {
                if (Path.GetFileName(_activeInput.OutputDirectory) == requestId)
                {
                    return "running";
                }
            }
            if (_work.GetPendingTitles().Contains(requestTitle) == true)
            {
                return "queued";
            }
            if (Path.GetFileName(OutputDirectory) == requestId)
            {
                if (_statusKey == "EidolonText228")
                {
                    return "cancelled";
                }
                if (_statusKey == "EidolonText229")
                {
                    return "failed";
                }
            }
            return "unknown";
        }

        private async Task ModelAsync(ModelingInput input, string requestTitle, CancellationToken cancellationToken)
        {
            _activeInput = input;
            _activeRequestTitle = requestTitle;
            IsModeling = true;
            _result = null;
            _resultSourceBlendPath = string.Empty;
            OutputDirectory = input.OutputDirectory;
            SetStage("EidolonText757");
            RefreshResult();
            IProgress<ModelingStage> progress = new Progress<ModelingStage>(stage =>
            {
                if (Session.IsClosing == true)
                {
                    return;
                }
                if (_activeInput != input)
                {
                    return;
                }
                switch (stage)
                {
                    case ModelingStage.Opening:
                        SetStage("EidolonText757");
                        break;
                    case ModelingStage.Planning:
                        SetStage("EidolonText758");
                        break;
                    case ModelingStage.Rendering:
                        SetStage("EidolonText759");
                        break;
                }
            });
            try
            {
                ModelingResult result = await _blenderModelingService.ModelAsync(input, progress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (Session.IsClosing == true)
                {
                    return;
                }
                _result = result;
                _resultSourceBlendPath = input.SourceBlendPath;
                OutputDirectory = result.DirectoryPath;
                SetStage("EidolonText761");
                Session.Indeterminate = false;
                Session.Percent = 100;
                RefreshResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == true)
            {
                _statusKey = "EidolonText228";
                Raise(nameof(StageCaption));
                throw;
            }
            catch (ModelingException error)
            {
                OutputDirectory = error.DirectoryPath;
                _statusKey = "EidolonText229";
                Raise(nameof(StageCaption));
                throw new InvalidOperationException(_strings.Format("EidolonText762", error.DirectoryPath), error);
            }
            catch (Exception error)
            {
                _statusKey = "EidolonText229";
                Raise(nameof(StageCaption));
                throw new InvalidOperationException(_strings.Format("EidolonText762", input.OutputDirectory), error);
            }
            finally
            {
                _activeInput = null;
                _activeRequestTitle = string.Empty;
                IsModeling = false;
            }
        }

        private void SetStage(string statusKey)
        {
            _statusKey = statusKey;
            Raise(nameof(StageCaption));
            Session.Status = StageCaption;
        }

        private void RefreshResult()
        {
            Raise(nameof(TriangleCountCaption));
            Raise(nameof(HasTriangleCountCaption));
            Raise(nameof(BlendPath));
            Raise(nameof(GlbPath));
            RefreshCommands();
        }

        private Task RefreshWslDistributionsAsync()
        {
            return StartWslStatusCheck(true);
        }

        private Task RefreshWslAgentStatusAsync()
        {
            if (HasWslDistributionSelection == false)
            {
                return Task.CompletedTask;
            }
            return StartWslStatusCheck(false);
        }

        private Task StartWslStatusCheck(bool listDistributions)
        {
            if (IsWslSupported == false)
            {
                return Task.CompletedTask;
            }
            if (_closed == true)
            {
                return Task.CompletedTask;
            }
            if (Session.IsClosing == true)
            {
                return Task.CompletedTask;
            }
            Task previousTask = _wslStatusTask;
            CancelWslStatusCheck();
            CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            _wslStatusCancellationTokenSource = cancellationTokenSource;
            _isListingWslDistributions = listDistributions;
            _wslAgentStatus = ModelingAgentStatus.Checking;
            _wslStatusTask = CheckWslStatusAsync(previousTask, listDistributions, SelectedModelingAgent, SelectedWslDistribution, cancellationTokenSource);
            RefreshCommands();
            return _wslStatusTask;
        }

        private async Task CheckWslStatusAsync(Task previousTask, bool listDistributions, ModelingAgent agent, string distribution, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                await previousTask;
                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                if (listDistributions == true)
                {
                    List<string> distributions = await _agentStatusChecker.ListWslDistributionsAsync(cancellationTokenSource.Token);
                    cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    if (IsCurrentWslStatusCheck(agent, cancellationTokenSource) == false)
                    {
                        return;
                    }
                    foreach (string existingDistribution in WslDistributions.ToArray())
                    {
                        if (distributions.Contains(existingDistribution, StringComparer.Ordinal) == false)
                        {
                            WslDistributions.Remove(existingDistribution);
                        }
                    }
                    foreach (string registeredDistribution in distributions)
                    {
                        if (WslDistributions.Contains(registeredDistribution) == false)
                        {
                            WslDistributions.Add(registeredDistribution);
                        }
                    }
                    if (WslDistributions.Contains(_selectedWslDistribution) == false)
                    {
                        _selectedWslDistribution = null;
                        Raise(nameof(SelectedWslDistribution));
                    }
                    _hasWslDistributionList = true;
                    _wslAgentStatus = ModelingAgentStatus.Unknown;
                }
                else
                {
                    ModelingAgentStatus status = await _agentStatusChecker.CheckWslAsync(agent, distribution, cancellationTokenSource.Token);
                    cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    if (IsCurrentWslStatusCheck(agent, cancellationTokenSource) == false)
                    {
                        return;
                    }
                    if (SelectedWslDistribution != distribution)
                    {
                        return;
                    }
                    _wslAgentStatus = status;
                }
                RefreshCommands();
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested == true)
            {
            }
            catch (TimeoutException)
            {
                if (IsCurrentWslStatusCheck(agent, cancellationTokenSource) == true)
                {
                    _wslAgentStatus = ModelingAgentStatus.TimedOut;
                    RefreshCommands();
                }
            }
            catch (Exception)
            {
                if (IsCurrentWslStatusCheck(agent, cancellationTokenSource) == true)
                {
                    _wslAgentStatus = ModelingAgentStatus.Failed;
                    RefreshCommands();
                }
            }
            finally
            {
                if (_wslStatusCancellationTokenSource == cancellationTokenSource)
                {
                    _wslStatusCancellationTokenSource = null;
                    _isListingWslDistributions = false;
                }
                cancellationTokenSource.Dispose();
            }
        }

        private bool IsCurrentWslStatusCheck(ModelingAgent agent, CancellationTokenSource cancellationTokenSource)
        {
            if (_closed == true)
            {
                return false;
            }
            if (Session.IsClosing == true)
            {
                return false;
            }
            if (_wslStatusCancellationTokenSource != cancellationTokenSource)
            {
                return false;
            }
            return SelectedModelingAgent == agent;
        }

        private void CancelWslStatusCheck()
        {
            CancellationTokenSource cancellationTokenSource = _wslStatusCancellationTokenSource;
            _wslStatusCancellationTokenSource = null;
            cancellationTokenSource?.Cancel();
        }

        private Task RefreshAgentStatusAsync()
        {
            if (_closed == true)
            {
                return Task.CompletedTask;
            }
            if (Session.IsClosing == true)
            {
                return Task.CompletedTask;
            }
            Task previousTask = _agentStatusTask;
            CancelAgentStatusCheck();
            CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(Session.Lifetime);
            _agentStatusCancellationTokenSource = cancellationTokenSource;
            _agentConfiguredPath = Session.Settings.CodexExecutablePath;
            _agentStatus = ModelingAgentStatus.Checking;
            _gptWslSearchStatus = ModelingAgentStatus.Unknown;
            _detectedGptWslDistribution = string.Empty;
            _detectedGptWslAgentStatus = ModelingAgentStatus.Unknown;
            _hasCheckedAgent = true;
            _agentStatusTask = CheckAgentStatusAsync(previousTask, SelectedModelingAgent, _agentConfiguredPath, cancellationTokenSource);
            RefreshCommands();
            return _agentStatusTask;
        }

        private async Task CheckAgentStatusAsync(Task previousTask, ModelingAgent agent, string configuredPath, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                await previousTask;
                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                using CancellationTokenSource searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);
                searchCancellation.CancelAfter(TimeSpan.FromSeconds(12));
                string executablePath = await Task.Run(() => FindAgentExecutable(agent, configuredPath), searchCancellation.Token).WaitAsync(searchCancellation.Token);
                ModelingAgentStatus status = ModelingAgentStatus.Missing;
                if (string.IsNullOrEmpty(executablePath) == false)
                {
                    try
                    {
                        status = await _agentStatusChecker.CheckAsync(agent, executablePath, searchCancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested == false)
                    {
                        status = ModelingAgentStatus.TimedOut;
                    }
                }
                (ModelingAgentStatus SearchStatus, string Distribution, ModelingAgentStatus AgentStatus) wslResult = (ModelingAgentStatus.Unknown, string.Empty, ModelingAgentStatus.Unknown);
                if (agent == ModelingAgent.Codex)
                {
                    if (OperatingSystem.IsWindows() == true)
                    {
                        try
                        {
                            wslResult = await FindGptWslAsync(status != ModelingAgentStatus.Ready, searchCancellation.Token);
                        }
                        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested == false)
                        {
                            wslResult = (ModelingAgentStatus.TimedOut, string.Empty, ModelingAgentStatus.Unknown);
                        }
                    }
                }
                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                if (IsCurrentAgentStatusCheck(agent, configuredPath, cancellationTokenSource) == false)
                {
                    return;
                }
                _checkedAgentExecutablePath = executablePath;
                _agentStatus = status;
                _gptWslSearchStatus = wslResult.SearchStatus;
                _detectedGptWslDistribution = wslResult.Distribution;
                _detectedGptWslAgentStatus = wslResult.AgentStatus;
                RefreshCommands();
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested == true)
            {
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentAgentStatusCheck(agent, configuredPath, cancellationTokenSource) == true)
                {
                    _agentStatus = ModelingAgentStatus.TimedOut;
                    _gptWslSearchStatus = ModelingAgentStatus.TimedOut;
                    RefreshCommands();
                }
            }
            catch (Exception)
            {
                if (IsCurrentAgentStatusCheck(agent, configuredPath, cancellationTokenSource) == true)
                {
                    _agentStatus = ModelingAgentStatus.Failed;
                    RefreshCommands();
                }
            }
            finally
            {
                if (_agentStatusCancellationTokenSource == cancellationTokenSource)
                {
                    _agentStatusCancellationTokenSource = null;
                }
                cancellationTokenSource.Dispose();
            }
        }

        private async Task<(ModelingAgentStatus SearchStatus, string Distribution, ModelingAgentStatus AgentStatus)> FindGptWslAsync(bool inspectAgents, CancellationToken cancellationToken)
        {
            string detectedDistribution = string.Empty;
            ModelingAgentStatus searchStatus = ModelingAgentStatus.Missing;
            try
            {
                List<string> distributions = await _agentStatusChecker.ListWslDistributionsAsync(cancellationToken);
                if (inspectAgents == false)
                {
                    return (ModelingAgentStatus.Unknown, string.Empty, ModelingAgentStatus.Unknown);
                }
                foreach (string distribution in distributions.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (distribution.Equals("docker-desktop", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        continue;
                    }
                    if (distribution.Equals("docker-desktop-data", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        continue;
                    }
                    ModelingAgentStatus status = await _agentStatusChecker.CheckWslAsync(ModelingAgent.Codex, distribution, cancellationToken, distributions);
                    if (status == ModelingAgentStatus.Ready)
                    {
                        return (ModelingAgentStatus.Ready, distribution, status);
                    }
                    if (status == ModelingAgentStatus.LoginRequired)
                    {
                        if (string.IsNullOrEmpty(detectedDistribution) == true)
                        {
                            detectedDistribution = distribution;
                        }
                        continue;
                    }
                    if (status != ModelingAgentStatus.Missing)
                    {
                        searchStatus = status;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (string.IsNullOrEmpty(detectedDistribution) == true)
                {
                    throw;
                }
            }
            catch (TimeoutException)
            {
                searchStatus = ModelingAgentStatus.TimedOut;
            }
            catch (Exception)
            {
                searchStatus = ModelingAgentStatus.Failed;
            }
            if (string.IsNullOrEmpty(detectedDistribution) == false)
            {
                return (ModelingAgentStatus.Ready, detectedDistribution, ModelingAgentStatus.LoginRequired);
            }
            return (searchStatus, string.Empty, ModelingAgentStatus.Unknown);
        }

        private ModelingAgentStatus GetDisplayedAgentStatus()
        {
            if (IsCodexAgent == false)
            {
                return _agentStatus;
            }
            if (_agentStatus == ModelingAgentStatus.Ready)
            {
                return _agentStatus;
            }
            if (_agentStatus == ModelingAgentStatus.Checking)
            {
                return _agentStatus;
            }
            if (_agentStatus == ModelingAgentStatus.Unknown)
            {
                return _agentStatus;
            }
            if (string.IsNullOrEmpty(_detectedGptWslDistribution) == false)
            {
                return _detectedGptWslAgentStatus;
            }
            if (OperatingSystem.IsWindows() == true)
            {
                if (_agentStatus == ModelingAgentStatus.Missing)
                {
                    return _gptWslSearchStatus;
                }
            }
            return _agentStatus;
        }

        private string FindAgentExecutable(ModelingAgent agent, string configuredPath)
        {
            if (agent == ModelingAgent.Claude)
            {
                return BlenderModelingService.FindClaudeExecutable();
            }
            return _codexExecutableLocator.Find(configuredPath);
        }

        private bool IsCurrentAgentStatusCheck(ModelingAgent agent, string configuredPath, CancellationTokenSource cancellationTokenSource)
        {
            if (_closed == true)
            {
                return false;
            }
            if (Session.IsClosing == true)
            {
                return false;
            }
            if (_agentStatusCancellationTokenSource != cancellationTokenSource)
            {
                return false;
            }
            if (SelectedModelingAgent != agent)
            {
                return false;
            }
            if (agent == ModelingAgent.Codex)
            {
                if (Session.Settings.CodexExecutablePath != configuredPath)
                {
                    return false;
                }
            }
            return true;
        }

        private void CancelAgentStatusCheck()
        {
            CancellationTokenSource cancellationTokenSource = _agentStatusCancellationTokenSource;
            _agentStatusCancellationTokenSource = null;
            cancellationTokenSource?.Cancel();
        }

        protected override void OnNavigationPropertyChanged(object sender, PropertyChangedEventArgs arguments)
        {
            if (arguments.PropertyName != nameof(Navigation.IsModelingView))
            {
                return;
            }
            if (Navigation.IsModelingView == false)
            {
                if (_wslStatusCancellationTokenSource != null)
                {
                    CancelWslStatusCheck();
                    _wslAgentStatus = ModelingAgentStatus.Unknown;
                    _isListingWslDistributions = false;
                    RefreshCommands();
                }
                if (_agentStatusCancellationTokenSource != null)
                {
                    CancelAgentStatusCheck();
                    _agentStatus = ModelingAgentStatus.Unknown;
                    _hasCheckedAgent = false;
                    RefreshCommands();
                }
                return;
            }
            if (_hasCheckedAgent == false)
            {
                _ = RefreshAgentStatusAsync();
            }
        }

        protected override void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs arguments)
        {
            if (arguments.PropertyName != nameof(Session.Settings))
            {
                return;
            }
            if (IsCodexAgent == false)
            {
                return;
            }
            if (_agentConfiguredPath == Session.Settings.CodexExecutablePath)
            {
                return;
            }
            CancelAgentStatusCheck();
            _agentStatus = ModelingAgentStatus.Unknown;
            _checkedAgentExecutablePath = string.Empty;
            _hasCheckedAgent = false;
            RefreshCommands();
        }

        protected override void RefreshCommands()
        {
            Raise(nameof(AgentLocationCaption));
            Raise(nameof(ShowGptWslReadOnly));
            Raise(nameof(ShowWslDetails));
            Raise(nameof(WslStatusCaption));
            Raise(nameof(HasWslStatusCaption));
            Raise(nameof(HasWslDistributionSelection));
            Raise(nameof(AgentStatusCaption));
            Raise(nameof(HasAgentStatusCaption));
            Raise(nameof(HasAgentGuidance));
            Raise(nameof(AgentGuidanceLabel));
            Raise(nameof(AgentGuidanceUrl));
            Raise(nameof(CollectionPageCaption));
            Raise(nameof(SourceBlendFileName));
            Raise(nameof(SourceBlendTargetCaption));
            Raise(nameof(ModelingSubmitCaption));
            Raise(nameof(HasSourceBlendPath));
            Raise(nameof(HasBlenderExecutablePath));
            Raise(nameof(ReferencePath));
            Raise(nameof(ReferenceCaption));
            Raise(nameof(InputIssue));
            Raise(nameof(HasInputIssue));
            Raise(nameof(CanModel));
            Raise(nameof(CanContinue));
            Raise(nameof(CanSaveGlb));
            Raise(nameof(CanOpenResult));
            Raise(nameof(BlenderExecutablePath));
            Raise(nameof(NeedsBlenderInstallation));
            base.RefreshCommands();
        }

        public override void Localize()
        {
            foreach (ModelingCollectionItem collectionItem in CollectionChoices)
            {
                collectionItem.Localize(_strings);
            }
            Raise(nameof(TriangleCountCaption));
            Raise(nameof(HasTriangleCountCaption));
            SingleImage.Localize(_strings);
            foreach (ModelingImageItem imageItem in DirectionImages.Concat(AdditionalDirectionImages))
            {
                imageItem.Localize(_strings);
            }
            Raise(nameof(StageCaption));
            base.Localize();
        }

        public async Task CloseAsync()
        {
            _closed = true;
            CancelWslStatusCheck();
            CancelAgentStatusCheck();
            IsCollectionPickerOpen = false;
            ClearCollectionChoices();
            foreach (ModelingImageItem imageItem in DirectionImages.Concat(AdditionalDirectionImages).Prepend(SingleImage))
            {
                imageItem.Dispose();
            }
            try
            {
                await Task.WhenAll(DirectionImages.Concat(AdditionalDirectionImages).Prepend(SingleImage).Select(imageItem => imageItem.ImageLoadTask).Prepend(_collectionsLoadTask).Prepend(_collectionThumbnailLoadTask).Prepend(_agentStatusTask).Prepend(_wslStatusTask));
            }
            catch (OperationCanceledException) when (Session.IsClosing == true)
            {
            }
        }

        public override void Dispose()
        {
            _closed = true;
            CancelWslStatusCheck();
            CancelAgentStatusCheck();
            IsCollectionPickerOpen = false;
            ClearCollectionChoices();
            foreach (ModelingImageItem imageItem in DirectionImages.Concat(AdditionalDirectionImages).Prepend(SingleImage))
            {
                imageItem.Dispose();
            }
            base.Dispose();
        }
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.App.ViewModels
{
    public class EngineViewModel : StudioPanelViewModel
    {
        public AssetsViewModel Assets { get; private set; }
        public SettingsViewModel Settings { get; private set; }

        private readonly AssetLibrary _assets;
        private readonly RuntimeInstaller _installer;
        private readonly ComfyEngine _engine;
        private readonly JobStore _jobs;
        private int _runtimeModulesRequest;
        private Exception _runtimeModulesFailure;
        public ObservableCollection<RuntimeModuleItem> RuntimeModules { get; private set; } = new ObservableCollection<RuntimeModuleItem>();
        public ObservableCollection<RuntimeModuleItem> CustomNodes { get; private set; } = new ObservableCollection<RuntimeModuleItem>();
        public AsyncCommand InstallCommand { get; private set; }
        public AsyncCommand InstallPromptConverterCommand { get; private set; }
        public AsyncCommand DeletePromptConverterCommand { get; private set; }
        public AsyncCommand StartEngineCommand { get; private set; }
        public AsyncCommand StopEngineCommand { get; private set; }
        public AsyncCommand OpenRuntimeCommand { get; private set; }
        public AsyncCommand RefreshRuntimeModulesCommand { get; private set; }
        public AsyncCommand OpenTrainingDataCommand { get; private set; }
        public AsyncCommand ClearTrainingDataCommand { get; private set; }

        public string PromptConverterInstallDirectory
        {
            get
            {
                string installDirectory = Session.Settings.InstallDirectory;
                if (string.IsNullOrWhiteSpace(installDirectory) == false)
                {
                    return installDirectory.Trim();
                }
                installDirectory = Settings.InstallDirectory;
                if (string.IsNullOrWhiteSpace(installDirectory) == true)
                {
                    return string.Empty;
                }
                return installDirectory.Trim();
            }
        }

        public bool CanInstallPromptConverter
        {
            get
            {
                if (Session.IsIdle == false)
                {
                    return false;
                }
                return Path.IsPathFullyQualified(PromptConverterInstallDirectory);
            }
        }

        public bool CanDeletePromptConverter
        {
            get
            {
                if (Session.IsIdle == false)
                {
                    return false;
                }
                string installDirectory = PromptConverterInstallDirectory;
                if (Path.IsPathFullyQualified(installDirectory) == false)
                {
                    return false;
                }
                return _installer.CanDeletePromptConverter(installDirectory);
            }
        }

        public string PromptConverterStatus
        {
            get
            {
                string installDirectory = PromptConverterInstallDirectory;
                if (Path.IsPathFullyQualified(installDirectory) == true)
                {
                    if (_installer.IsPromptConverterInstalled(installDirectory) == true)
                    {
                        return _strings.GetString("EidolonText276");
                    }
                }
                return _strings.GetString("EidolonText277");
            }
        }

        private async Task InstallPromptConverterAsync(DesktopSettings requestedSettings, CancellationToken cancellationToken)
        {
            try
            {
                await _installer.InstallPromptConverterAsync(requestedSettings, _work.Progress, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(Session.Settings.InstallDirectory) == true)
                {
                    DesktopSettings settings = Session.Settings.Copy();
                    settings.InstallDirectory = requestedSettings.InstallDirectory;
                    Session.SaveSettings(settings);
                }
            }
            finally
            {
                RefreshCommands();
            }
        }

        private async Task DeletePromptConverterAsync()
        {
            DesktopSettings requestedSettings = Session.Settings.Copy();
            requestedSettings.InstallDirectory = PromptConverterInstallDirectory;
            RuntimeLayout runtimeLayout = new RuntimeLayout(requestedSettings.InstallDirectory);
            if (await _dialogs.ConfirmDeleteAsync(_strings.GetString("EidolonText718"), _strings.Format("EidolonText719", Path.GetDirectoryName(runtimeLayout.PromptConverterModelPath), Path.GetDirectoryName(runtimeLayout.PromptConverterExecutable))) == false)
            {
                return;
            }
            await Session.WorkAsync(async cancellationToken =>
            {
                try
                {
                    await Task.Run(() => _installer.DeletePromptConverter(requestedSettings, cancellationToken), cancellationToken);
                    Session.Status = _strings.GetString("EidolonText720");
                }
                finally
                {
                    RefreshCommands();
                }
            });
        }

        public bool CanOpenTrainingData
        {
            get
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                return Session.HasLocalModelRuntime;
            }
        }

        public bool CanClearTrainingData
        {
            get
            {
                if (CanOpenTrainingData == false)
                {
                    return false;
                }
                return Session.IsIdle;
            }
        }

        public string TrainingDataDirectory
        {
            get
            {
                if (CanOpenTrainingData == false)
                {
                    return string.Empty;
                }
                return new RuntimeLayout(Session.Settings.InstallDirectory).TrainingDataDirectory;
            }
        }

        private async Task ClearTrainingDataAsync()
        {
            DesktopSettings settings = Session.Settings.Copy();
            string path = new RuntimeLayout(settings.InstallDirectory).TrainingDataDirectory;
            if (await _dialogs.ConfirmDeleteAsync(_strings.GetString("EidolonText578"),
                _strings.Format("EidolonText579", path)) == false)
            {
                return;
            }
            await Session.WorkAsync(async token =>
            {
                int count = await Task.Run(() => _jobs.ClearTrainingDatasets(settings, token), token);
                Session.Status = _strings.Format("EidolonText580", count);
            });
        }

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
                if (ComfyServerAddress.CanStartLocally(Session.SettingsDraft) == true)
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
                return Session.IsIdle == true && ComfyServerAddress.CanStartLocally(Session.SettingsDraft) == true;
            }
        }

        internal async Task StartEngineOnStartupAsync(DesktopSettings settings, CancellationToken token)
        {
            try
            {
                await StartEngineAsync(settings, token);
            }
            catch (StudioException error) when (error.Code == StudioMessageCode.InstallDirectoryRequired ||
                error.Code == StudioMessageCode.RuntimeNotInstalled || error.Code == StudioMessageCode.RuntimeIncomplete ||
                error.Code == StudioMessageCode.GenerationRuntimeMissing)
            {
                Navigation.SelectedSettingsTab = 1;
                Navigation.SelectedTab = 4;
                Session.Status = _strings.GetString("EidolonText225");
            }
        }

        private async Task StartEngineAsync(DesktopSettings settings, CancellationToken token)
        {
            await _engine.EnsureReadyAsync(settings, _work.Progress, token);
            DesktopSettings activeSettings = Session.CreateActiveSettings(settings);
            if (ComfyServerAddress.UsesServerAssets(activeSettings) == true)
            {
                await _engine.RefreshExternalModelsAsync(activeSettings, _assets, token);
                await Assets.RefreshAssetsAsync();
                Session.Status = _strings.GetString("EidolonText256");
            }
            else
            {
                await Task.Run(() => _assets.ScanAsync(settings, _work.Progress, token), token);
                await Assets.RefreshAssetsAsync();
                Session.Status = EngineStatus;
            }
            Session.Percent = 100;
            Raise(nameof(EngineStatus));
            Raise(nameof(StopEngineCaption));
            Raise(nameof(IsEngineConnected));
            Raise(nameof(StartEngineCaption));
            RefreshCommands();
        }

        private void ScheduleEngineSettings()
        {
            if (Session.IsInitialized == false)
            {
                return;
            }
            if (Session.IsClosing == true)
            {
                return;
            }
            _work.CancelScheduled();
            DesktopSettings settings = Settings.CreateEngineSettings();
            try
            {
                settings.Validate();
            }
            catch (StudioException)
            {
                return;
            }
            _work.ScheduleWhenIdle(_strings.GetString("EidolonText255"), token => Session.ExecuteMaintenanceAsync(maintenanceToken => SaveEngineSettingsAsync(settings, maintenanceToken), token));
        }

        private async Task StopEngineAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _engine.StopAsync();
            await Assets.RefreshAssetsAsync();
            Session.Status = _strings.GetString("EidolonText269");
            Session.Percent = 0;
            Raise(nameof(EngineStatus));
            Raise(nameof(StopEngineCaption));
            Raise(nameof(IsEngineConnected));
            Raise(nameof(StartEngineCaption));
        }

        private async Task InstallAsync(DesktopSettings requestedSettings, CancellationToken token)
        {
            DesktopSettings settings = Session.Settings.Copy();
            settings.InstallDirectory = requestedSettings.InstallDirectory;
            settings.UseCpu = requestedSettings.UseCpu;
            settings.ServerAddress = requestedSettings.ServerAddress;
            settings.Validate();
            await _engine.StopAsync();
            Session.SaveSettings(settings);
            try
            {
                await _installer.InstallAsync(settings, _work.Progress, token);
                await StartEngineAsync(settings, token);
                await Assets.RefreshAssetsAsync();
                Session.Status = _strings.GetString("EidolonText230");
                if (Assets.Models.Count > 0)
                {
                    Session.Status = _strings.GetString("EidolonText231");
                    Navigation.SelectedTab = 0;
                }
                else
                {
                    Navigation.SelectedEnvironmentTab = 2;
                    Navigation.SelectedTab = 3;
                }
            }
            finally
            {
                await RefreshRuntimeModulesAsync(Session.Lifetime);
            }
        }

        internal async Task RefreshRuntimeModulesAsync(CancellationToken token)
        {
            int request = ++_runtimeModulesRequest;
            string directory = Settings.InstallDirectory.Trim();
            try
            {
                IReadOnlyList<RuntimeModule> modules = Array.Empty<RuntimeModule>();
                if (Path.IsPathFullyQualified(directory) == true)
                {
                    modules = await Task.Run(() => _installer.ReadModules(directory, token), token);
                }
                if (Session.IsClosing == true || request != _runtimeModulesRequest)
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
                if (Session.IsClosing == true || request != _runtimeModulesRequest)
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

        private async Task SaveEngineSettingsAsync(DesktopSettings requestedSettings, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DesktopSettings settings = Session.Settings.Copy();
            settings.InstallDirectory = requestedSettings.InstallDirectory;
            settings.UseCpu = requestedSettings.UseCpu;
            settings.ServerAddress = requestedSettings.ServerAddress;
            settings.Validate();
            bool connectionChanged = false;
            if (ComfyServerAddress.Root(settings) != ComfyServerAddress.Root(Session.Settings))
            {
                connectionChanged = true;
            }
            if (settings.InstallDirectory != Session.Settings.InstallDirectory)
            {
                connectionChanged = true;
            }
            if (settings.UseCpu != Session.Settings.UseCpu)
            {
                connectionChanged = true;
            }
            if (connectionChanged == true)
            {
                await _engine.StopAsync();
            }
            Session.SaveSettings(settings);
            await StartEngineAsync(settings, token);
            await Assets.RefreshAssetsAsync();
            if (_engine.Connection != null)
            {
                Session.Status = EngineStatus;
            }
        }

        public EngineViewModel(StudioSession session, StudioNavigationViewModel navigation, StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs, AssetsViewModel assets, SettingsViewModel settings, AssetLibrary library, RuntimeInstaller installer, ComfyEngine engine, JobStore jobs) : base(session, navigation, work, strings, dialogs)
        {
            Assets = assets;
            Settings = settings;
            _assets = library;
            _installer = installer;
            _engine = engine;
            _jobs = jobs;
            Settings.PropertyChanged += OnSettingsChanged;
            InstallCommand = Command(() =>
            {
                DesktopSettings requestedSettings = Settings.CreateEngineSettings();
                return Session.WorkAsync(token => InstallAsync(requestedSettings, token));
            }, () => CanInstallEngine == true);
            InstallPromptConverterCommand = Command(() =>
            {
                DesktopSettings requestedSettings = Session.Settings.Copy();
                requestedSettings.InstallDirectory = PromptConverterInstallDirectory;
                return Session.WorkAsync(cancellationToken => InstallPromptConverterAsync(requestedSettings, cancellationToken));
            }, () => CanInstallPromptConverter);
            DeletePromptConverterCommand = Command(DeletePromptConverterAsync, () => CanDeletePromptConverter);
            StartEngineCommand = Command(() =>
            {
                _work.CancelScheduled();
                DesktopSettings requestedSettings = Settings.CreateEngineSettings();
                return Session.WorkAsync(token => SaveEngineSettingsAsync(requestedSettings, token));
            });
            StopEngineCommand = Command(() =>
            {
                _work.CancelScheduled();
                return Session.WorkAsync(StopEngineAsync);
            }, () =>
            {
                if (Session.IsIdle == false)
                {
                    return false;
                }
                return _engine.Connection != null;
            });
            OpenRuntimeCommand = Command(() => _dialogs.OpenFolderAsync(new RuntimeLayout(Settings.InstallDirectory).Root), () =>
            {
                if (Session.IsIdle == false)
                {
                    return false;
                }
                return string.IsNullOrWhiteSpace(Settings.InstallDirectory) == false;
            });
            RefreshRuntimeModulesCommand = Command(() => Session.WorkAsync(RefreshRuntimeModulesAsync));
            OpenTrainingDataCommand = Command(() => _dialogs.OpenFolderAsync(TrainingDataDirectory), () => CanOpenTrainingData);
            ClearTrainingDataCommand = Command(ClearTrainingDataAsync, () => CanClearTrainingData);
        }

        private async void OnSettingsChanged(object sender, PropertyChangedEventArgs args)
        {
            RefreshCommands();
            if (args.PropertyName == nameof(Settings.ServerAddress))
            {
                ScheduleEngineSettings();
            }
            if (args.PropertyName == nameof(Settings.InstallDirectory)
                && Session.IsInitialized == true && Session.IsClosing == false)
            {
                try
                {
                    await RefreshRuntimeModulesAsync(Session.Lifetime);
                }
                catch (Exception error)
                {
                    OnCommandError(error);
                }
            }
        }

        protected override void RefreshCommands()
        {
            Raise(nameof(EngineStatus));
            Raise(nameof(StopEngineCaption));
            Raise(nameof(IsEngineConnected));
            Raise(nameof(StartEngineCaption));
            Raise(nameof(CanInstallEngine));
            Raise(nameof(PromptConverterInstallDirectory));
            Raise(nameof(CanInstallPromptConverter));
            Raise(nameof(CanDeletePromptConverter));
            Raise(nameof(PromptConverterStatus));
            Raise(nameof(CanOpenTrainingData));
            Raise(nameof(CanClearTrainingData));
            Raise(nameof(TrainingDataDirectory));
            base.RefreshCommands();
        }

        public override void Localize()
        {
            foreach (RuntimeModuleItem item in RuntimeModules.Concat(CustomNodes))
            {
                item.Localize();
            }
            Raise(nameof(RuntimeModulesError));
            base.Localize();
        }

        public override void Dispose()
        {
            Settings.PropertyChanged -= OnSettingsChanged;
            base.Dispose();
        }
    }
}

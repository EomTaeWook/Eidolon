using System.Collections.ObjectModel;
using System.ComponentModel;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.App.ViewModels
{
    public class AssetsViewModel : StudioPanelViewModel
    {
        private readonly AssetLibrary _assets;
        private readonly ComfyEngine _engine;
        private string _loraSearch = string.Empty;
        private AssetItem _selectedModel;
        private AssetItem _selectedAsset;
        private string _assetTrigger = string.Empty;
        private FamilyChoice _selectedFamily;
        private AssetItem _selectedResumeLora;
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
                if (Session.Settings.GenerationBackend == GenerationBackend.Codex)
                {
                    return false;
                }
                return Session.IsInitialized == true && HasAvailableModels == false;
            }
        }

        public string EngineSetupCaption
        {
            get
            {
                if (Session.IsInitialized == false)
                {
                    return _strings.GetString("EidolonText402");
                }
                if (Session.IsEngineConnected == true)
                {
                    return _strings.GetString("EidolonText400");
                }
                if (Session.HasLocalModelRuntime == true)
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
                if (Session.IsInitialized == false)
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
                if (Session.IsInitialized == false)
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
                if (ComfyServerAddress.UsesServerAssets(Session.CreateActiveSettings(Session.Settings)) == true)
                {
                    return _strings.GetString("EidolonText403");
                }
                if (Session.HasLocalModelRuntime == false)
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
                if (Session.HasLocalModelRuntime == false && Session.IsEngineConnected == false)
                {
                    return _strings.GetString("EidolonText407");
                }
                if (ComfyServerAddress.UsesServerAssets(Session.CreateActiveSettings(Session.Settings)) == true)
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

        public List<FamilyChoice> Families { get; private set; }
        public ObservableCollection<FamilyChoice> AvailableFamilies { get; private set; } = new ObservableCollection<FamilyChoice>();
        public List<ModelDownloadItem> DownloadableModels { get; private set; }
        public AsyncCommand PrepareModelsCommand { get; private set; }
        public AsyncCommand OpenModelDirectoryCommand { get; private set; }
        public AsyncCommand OpenLoraDirectoryCommand { get; private set; }
        public AsyncCommand ScanCommand { get; private set; }
        public AsyncCommand UpdateAssetCommand { get; private set; }
        public AsyncCommand DeleteAssetCommand { get; private set; }

        public AssetItem SelectedResumeLora
        {
            get
            {
                return _selectedResumeLora;
            }
            set
            {
                Set(ref _selectedResumeLora, value);
            }
        }

        public bool CanManageDownloadedModels
        {
            get
            {
                return Session.IsIdle == true && ComfyServerAddress.UsesServerAssets(Session.CreateActiveSettings(Session.Settings)) == false
                    && Session.HasLocalModelRuntime == true;
            }
        }

        public bool CanOpenModelDirectories
        {
            get
            {
                if (Session.IsClosing == true)
                {
                    return false;
                }
                if (Session.HasLocalModelRuntime == false)
                {
                    return false;
                }
                return ComfyServerAddress.UsesServerAssets(Session.CreateActiveSettings(Session.Settings)) == false;
            }
        }

        public string ModelDirectory
        {
            get
            {
                if (CanOpenModelDirectories == false)
                {
                    return string.Empty;
                }
                return new RuntimeLayout(Session.Settings.InstallDirectory).ModelsDirectory;
            }
        }

        public string LoraDirectory
        {
            get
            {
                if (CanOpenModelDirectories == false)
                {
                    return string.Empty;
                }
                return new RuntimeLayout(Session.Settings.InstallDirectory).LorasDirectory;
            }
        }

        public string ModelFoldersHint
        {
            get
            {
                if (ComfyServerAddress.UsesServerAssets(Session.CreateActiveSettings(Session.Settings)) == true)
                {
                    return _strings.GetString("EidolonText258");
                }
                return _strings.GetString("EidolonText573");
            }
        }

        public string ModelDownloadHint
        {
            get
            {
                if (ComfyServerAddress.UsesServerAssets(Session.CreateActiveSettings(Session.Settings)) == true)
                {
                    return _strings.GetString("EidolonText352");
                }
                if (Session.HasLocalModelRuntime == false)
                {
                    return _strings.GetString("EidolonText353");
                }
                return _strings.GetString("EidolonText343");
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
                ModelAsset model = await _assets.DownloadAsync(Session.CreateActiveSettings(Session.Settings), download.FileName, _work.Progress, token);
                if (string.IsNullOrEmpty(Session.Settings.DefaultModelId) == true)
                {
                    Session.Settings.DefaultModelId = model.Id;
                    Session.SaveSettings(Session.Settings);
                    Session.SettingsDraft.DefaultModelId = model.Id;
                }
                await RefreshAssetsAsync();
                Session.Status = _strings.Format("EidolonText349", download.Name);
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
            await DeleteAssetAsync(model, true);
        }

        private Task DeleteSelectedAssetAsync()
        {
            return DeleteAssetAsync(SelectedAsset.Asset, false);
        }

        private async Task DeleteAssetAsync(ModelAsset model, bool downloadedOnly)
        {
            DesktopSettings settings = Session.CreateActiveSettings(Session.Settings);
            string path = new RuntimeLayout(settings.InstallDirectory).AssetPath(model);
            string id = model.Id;
            string name = model.Name;
            if (await _dialogs.ConfirmDeleteAsync(_strings.GetString("EidolonText556"),
                _strings.Format("EidolonText557", name, path)) == false)
            {
                return;
            }
            await Session.WorkAsync(async token =>
            {
                try
                {
                    if (downloadedOnly == true)
                    {
                        await _assets.DeleteDownloadedAsync(settings, id, token);
                    }
                    else
                    {
                        await _assets.DeleteAsync(settings, id, token);
                    }
                    if (Session.Settings.DefaultModelId == id)
                    {
                        Session.Settings.DefaultModelId = string.Empty;
                        Session.SettingsDraft.DefaultModelId = string.Empty;
                        Session.SaveSettings(Session.Settings);
                    }
                    Session.Status = _strings.Format("EidolonText558", name);
                }
                finally
                {
                    await RefreshAssetsAsync();
                }
            });
        }

        private async Task UpdateAssetAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _assets.UpdateAsync(SelectedAsset.Asset, SelectedFamily.Value, AssetTrigger);
            await RefreshAssetsAsync();
            Session.Status = _strings.GetString("EidolonText239");
        }

        public bool CanDeleteSelectedAsset
        {
            get
            {
                if (CanManageDownloadedModels == false)
                {
                    return false;
                }
                return SelectedAsset != null;
            }
        }

        internal AssetItem FindReusableAsset(IEnumerable<AssetItem> assets, ModelAsset recorded)
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

        internal async Task ScanAssetsAsync(CancellationToken token)
        {
            DesktopSettings settings = Session.CreateActiveSettings(Session.Settings);
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
            Session.Status = _strings.GetString("EidolonText223");
        }

        internal async Task RefreshAssetsAsync()
        {
            List<ModelAsset> assets = await _assets.ListAsync(Session.CreateActiveSettings(Session.Settings));
            string selectedAssetId = string.Empty;
            if (SelectedAsset != null)
            {
                selectedAssetId = SelectedAsset.Asset.Id;
            }
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
            bool localAssets = ComfyServerAddress.UsesServerAssets(Session.CreateActiveSettings(Session.Settings)) == false;
            foreach (ModelDownloadItem item in DownloadableModels)
            {
                item.IsInstalled = localAssets == true && assets.Any(asset => asset.Kind == AssetKind.Checkpoint
                    && asset.EngineName.Equals(item.Model.FileName, StringComparison.OrdinalIgnoreCase) == true);
            }
            Raise(nameof(HasAvailableModels));
            SelectedAsset = Library.FirstOrDefault(item => item.Asset.Id == selectedAssetId);
            RefreshAvailableFamilies();
            SelectedModel = Models.FirstOrDefault(item => item.Asset.Id == selectedId);
            if (SelectedModel == null)
            {
                SelectedModel = Models.FirstOrDefault(item => item.Asset.Id == Session.Settings.DefaultModelId);
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

        public AssetsViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs,
            AssetLibrary assets, ComfyEngine engine) : base(session, navigation, work, strings, dialogs)
        {
            _assets = assets;
            _engine = engine;
            Families = new List<FamilyChoice>
            {
                new FamilyChoice(ModelFamily.Unknown, _strings.GetString("EidolonText219")),
                new FamilyChoice(ModelFamily.StableDiffusion15, "Stable Diffusion 1.5"),
                new FamilyChoice(ModelFamily.Sdxl, "SDXL")
            };
            _selectedFamily = Families[0];
            AvailableFamilies.Add(Families[0]);
            DownloadableModels = new List<ModelDownloadItem>();
            foreach (ModelDownload model in _assets.Downloads)
            {
                AsyncCommand install = Command(() => Session.WorkAsync(token => DownloadModelAsync(model, token)),
                    () => CanManageDownloadedModels == true && FindDownloadedModel(model) == null);
                AsyncCommand delete = Command(() => DeleteDownloadedModelAsync(model),
                    () => CanManageDownloadedModels == true && FindDownloadedModel(model) != null);
                DownloadableModels.Add(new ModelDownloadItem(model, _strings, install, delete));
            }
            PrepareModelsCommand = Command(() =>
            {
                if (HasAvailableModels == false && Session.HasLocalModelRuntime == false && Session.IsEngineConnected == false)
                {
                    Navigation.SelectedEngineTab = StudioNavigationViewModel.EngineInstallationTab;
                }
                else
                {
                    Navigation.SelectedEngineTab = StudioNavigationViewModel.ModelsTab;
                }
                return Navigation.NavigateAsync(3);
            }, () => Session.IsInitialized == true && Session.IsClosing == false);
            OpenModelDirectoryCommand = Command(() => _dialogs.OpenFolderAsync(ModelDirectory), () => CanOpenModelDirectories);
            OpenLoraDirectoryCommand = Command(() => _dialogs.OpenFolderAsync(LoraDirectory), () => CanOpenModelDirectories);
            ScanCommand = Command(() => Session.WorkAsync(ScanAssetsAsync));
            UpdateAssetCommand = Command(() => Session.WorkAsync(UpdateAssetAsync), () => Session.IsIdle == true && SelectedAsset != null);
            DeleteAssetCommand = Command(DeleteSelectedAssetAsync, () => CanDeleteSelectedAsset);
        }

        protected override void RefreshCommands()
        {
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
            Raise(nameof(CanOpenModelDirectories));
            Raise(nameof(ModelDirectory));
            Raise(nameof(LoraDirectory));
            Raise(nameof(ModelFoldersHint));
            Raise(nameof(CanDeleteSelectedAsset));
            Raise(nameof(ModelDownloadHint));
            base.RefreshCommands();
        }

        public override void Localize()
        {
            foreach (FamilyChoice family in Families)
            {
                family.Localize(_strings);
            }
            foreach (AssetItem item in Library.Concat(Loras))
            {
                item.Localize();
            }
            foreach (ModelDownloadItem item in DownloadableModels)
            {
                item.Localize();
            }
            RefreshLoraList();
            base.Localize();
        }

        public override void Dispose()
        {
            foreach (AssetItem item in Loras)
            {
                item.PropertyChanged -= OnLoraPropertyChanged;
            }
            base.Dispose();
        }
    }
}

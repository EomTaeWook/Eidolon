using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Mcp;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Application;
using Eidolon.Core.Infrastructure;
using Eidolon.Core.Domain;

namespace Eidolon.App.ViewModels
{
    public class StudioViewModel : ObservableObject, IAsyncDisposable
    {
        public McpViewModel Mcp { get; private set; }
        public AssetCreationViewModel AssetCreation { get; private set; }
        private readonly ComfyEngine _engine;
        private readonly StudioWorkPresenter _work;
        private readonly StringHelper _strings;
        public StudioSession Session { get; private set; }
        public StudioNavigationViewModel Navigation { get; private set; }
        public AssetsViewModel Assets { get; private set; }
        public GenerationViewModel Generation { get; private set; }
        public GalleryViewModel Gallery { get; private set; }
        public TrainingViewModel Training { get; private set; }
        public EngineViewModel Engine { get; private set; }
        public SettingsViewModel Settings { get; private set; }

        public StudioViewModel(SettingsStore settingsStore, AssetLibrary assets, JobStore jobs,
            RuntimeInstaller installer, ComfyEngine engine, StudioService studio, DesktopDialogs dialogs,
            ThemeService themes, LanguageService languages, StringHelper strings, StudioWorkPresenter work,
            ISeedProvider seeds, CodexModelCatalog codexModels, string dataDirectory,
            AssetCreationService assetCreation, AssetImageLoader assetImages)
        {
            _engine = engine;
            _work = work;
            _strings = strings;
            Session = new StudioSession(settingsStore, engine, work, strings, dataDirectory);
            Navigation = new StudioNavigationViewModel(NavigateAsync, Session.ReportError);
            Assets = new AssetsViewModel(Session, Navigation, work, strings, dialogs, assets, engine);
            Settings = new SettingsViewModel(Session, Navigation, work, strings, dialogs, Assets, themes, languages, codexModels);
            Generation = new GenerationViewModel(Session, Navigation, work, strings, dialogs, Assets, jobs, studio, seeds);
            Training = new TrainingViewModel(Session, Navigation, work, strings, dialogs, Assets, studio);
            AssetCreation = new AssetCreationViewModel(Session, Navigation, work, strings, dialogs, Assets, assetCreation, jobs, seeds, assetImages);
            Gallery = new GalleryViewModel(Session, Navigation, work, strings, dialogs, Generation, Training, jobs, AssetCreation);
            Engine = new EngineViewModel(Session, Navigation, work, strings, dialogs, Assets, Settings, assets, installer, engine, jobs);
            Mcp = new McpViewModel(Session, Navigation, work, strings, dialogs,
                new McpService(Session, Assets, Generation, jobs, seeds, strings, AssetCreation, assetCreation));
            Generation.ImageGenerated += Gallery.ShowGeneratedImage;
            Settings.LanguageChanged += Localize;
            _work.Finished += OnWorkFinished;
        }

        public async Task InitializeAsync()
        {
            if (Session.IsClosing == true)
            {
                return;
            }
            Session.IsInitialized = false;
            _work.CancelScheduled();
            try
            {
                Session.LoadSettings();
                Settings.LoadChoices();
                await Engine.RefreshRuntimeModulesAsync(Session.Lifetime);
                await Assets.RefreshAssetsAsync();
                if (Session.IsClosing == true)
                {
                    return;
                }
                await Gallery.RefreshGalleryAsync(true);
                await AssetCreation.RefreshCollectionsAsync();
                if (Session.IsClosing == true)
                {
                    return;
                }
                Session.IsInitialized = true;
                Session.Error = string.Empty;
                Session.Status = _strings.GetString("EidolonText224");
                DesktopSettings settings = Session.Settings.Copy();
                if (settings.GenerationBackend == GenerationBackend.ComfyUI)
                {
                    Session.MaintenanceWork = true;
                    _work.Enqueue(_strings.GetString("EidolonText255"), token => Session.ExecuteMaintenanceAsync(
                        startupToken => Engine.StartEngineOnStartupAsync(settings, startupToken), token));
                }
            }
            catch (Exception error)
            {
                LogHelper.Error(error);
                Session.Error = _strings.GetString("EidolonText226") + _strings.GetExceptionMessage(error);
            }
            Session.RefreshAvailability();
        }

        private Task NavigateAsync(int tab)
        {
            Navigation.SelectedTab = tab;
            if (tab == 3)
            {
                if (Navigation.SelectedEnvironmentTab == 0)
                {
                    return Settings.RefreshCodexModelsAsync();
                }
            }
            if (tab == 6)
            {
                return Gallery.RefreshGalleryAsync();
            }
            if (tab == 7)
            {
                return AssetCreation.RefreshCollectionsAsync();
            }
            if ((tab == 0 || tab == 1) && Session.IsIdle == true
                && (Session.HasLocalModelRuntime == true || Session.IsEngineConnected == true))
            {
                return Session.WorkAsync(Assets.ScanAssetsAsync);
            }
            return Task.CompletedTask;
        }

        private async void OnWorkFinished(StudioWorkState state, Exception failure)
        {
            try
            {
                await Gallery.RefreshGalleryAsync();
                await AssetCreation.RefreshCollectionsAsync();
            }
            catch (Exception error)
            {
                Session.ReportError(error);
            }
        }

        private void Localize()
        {
            Session.Localize();
            Assets.Localize();
            Generation.Localize();
            Gallery.Localize();
            Mcp.Localize();
            AssetCreation.Localize();
            Training.Localize();
            Engine.Localize();
            Settings.Localize();
        }

        public async ValueTask DisposeAsync()
        {
            Session.BeginClosing();
            try
            {
                await Task.WhenAll(Gallery.WaitForLoadAsync(), Settings.WaitForModelsAsync(), Training.WaitForDescriptionAsync(),
                    _work.DisposeAsync().AsTask(), Mcp.CloseAsync(), AssetCreation.Preview.CloseAsync());
            }
            finally
            {
                Generation.ImageGenerated -= Gallery.ShowGeneratedImage;
                Settings.LanguageChanged -= Localize;
                _work.Finished -= OnWorkFinished;
                Gallery.Dispose();
                Mcp.Dispose();
                AssetCreation.Dispose();
                Generation.Dispose();
                Training.Dispose();
                Engine.Dispose();
                Settings.Dispose();
                Assets.Dispose();
                await _engine.DisposeAsync();
                Session.Dispose();
            }
        }
    }
}

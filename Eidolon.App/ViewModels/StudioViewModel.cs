using Dignus.Log;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Application;
using Eidolon.Core.Infrastructure;

namespace Eidolon.App.ViewModels
{
    public class StudioViewModel : ObservableObject, IAsyncDisposable
    {
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
            ISeedProvider seeds, string dataDirectory)
        {
            _engine = engine;
            _work = work;
            _strings = strings;
            Session = new StudioSession(settingsStore, engine, work, strings, dataDirectory);
            Navigation = new StudioNavigationViewModel(NavigateAsync, Session.ReportError);
            Assets = new AssetsViewModel(Session, Navigation, work, strings, dialogs, assets, engine);
            Settings = new SettingsViewModel(Session, Navigation, work, strings, dialogs, Assets, themes, languages);
            Generation = new GenerationViewModel(Session, Navigation, work, strings, dialogs, Assets, jobs, studio, seeds);
            Training = new TrainingViewModel(Session, Navigation, work, strings, dialogs, Assets, studio);
            Gallery = new GalleryViewModel(Session, Navigation, work, strings, dialogs, Generation, Training, jobs);
            Engine = new EngineViewModel(Session, Navigation, work, strings, dialogs, Assets, Settings, assets, installer, engine);
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
                if (Session.IsClosing == true)
                {
                    return;
                }
                Session.IsInitialized = true;
                Session.Error = string.Empty;
                Session.Status = _strings.GetString("EidolonText224");
                DesktopSettings settings = Session.Settings.Copy();
                Session.MaintenanceWork = true;
                _work.Enqueue(_strings.GetString("EidolonText255"), token => Session.ExecuteMaintenanceAsync(
                    startupToken => Engine.StartEngineOnStartupAsync(settings, startupToken), token));
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
            if (tab == 6)
            {
                return Gallery.RefreshGalleryAsync();
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
            Training.Localize();
            Engine.Localize();
            Settings.Localize();
        }

        public async ValueTask DisposeAsync()
        {
            Session.BeginClosing();
            try
            {
                await Task.WhenAll(Gallery.WaitForLoadAsync(), _work.DisposeAsync().AsTask());
            }
            finally
            {
                Generation.ImageGenerated -= Gallery.ShowGeneratedImage;
                Settings.LanguageChanged -= Localize;
                _work.Finished -= OnWorkFinished;
                Gallery.Dispose();
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

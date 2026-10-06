using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Dignus.DependencyInjection;
using Dignus.DependencyInjection.Extensions;
using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.ViewModels;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.App
{
    public partial class App : Application
    {
        public App()
        {
        }

        private ServiceContainer _serviceContainer;
        private HttpClient _downloadsHttp;
        private HttpClient _engineHttp;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    IServiceProvider services = CreateServices(desktop);
                    StringHelper strings = services.GetService<StringHelper>();
                    services.GetService<LanguageService>().Apply(strings.Language);
                    desktop.MainWindow = services.GetService<MainWindow>();
                    desktop.Exit += OnExit;
                }
                catch
                {
                    DisposeClients();
                    throw;
                }
            }
            base.OnFrameworkInitializationCompleted();
        }

        private IServiceProvider CreateServices(IClassicDesktopStyleApplicationLifetime desktop)
        {
            string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eidolon");
            _downloadsHttp = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(30) })
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            _downloadsHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Eidolon/0.1.0");
            _engineHttp = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5), UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _serviceContainer = new ServiceContainer();
            _serviceContainer.RegisterType<Application>(this);
            _serviceContainer.RegisterType(desktop);
            _serviceContainer.RegisterType(TimeProvider.System);
            _serviceContainer.RegisterType(new StringLanguageSelection(new DesktopSettings().Language));
            _serviceContainer.RegisterType<AtomicJsonFile, AtomicJsonFile>(LifeScope.Singleton);
            _serviceContainer.RegisterType<ProcessRunner, ProcessRunner>(LifeScope.Singleton);
            _serviceContainer.RegisterType<RuntimeModuleReader, RuntimeModuleReader>(LifeScope.Singleton);
            _serviceContainer.RegisterType<RuntimeInstaller, RuntimeInstaller>(LifeScope.Singleton);
            _serviceContainer.RegisterType<SafetensorsInspector, SafetensorsInspector>(LifeScope.Singleton);
            _serviceContainer.RegisterType<ComfyWorkflowBuilder, ComfyWorkflowBuilder>(LifeScope.Singleton);
            _serviceContainer.RegisterType<ISeedProvider, CryptoSeedProvider>(LifeScope.Singleton);
            _serviceContainer.RegisterType<LoraTrainer, LoraTrainer>(LifeScope.Singleton);
            _serviceContainer.RegisterType<StudioService, StudioService>(LifeScope.Singleton);
            _serviceContainer.RegisterType<StudioWorkQueue, StudioWorkQueue>(LifeScope.Singleton);
            _serviceContainer.RegisterType<ThemeService, ThemeService>(LifeScope.Singleton);
            _serviceContainer.RegisterType<LanguageService, LanguageService>(LifeScope.Singleton);
            _serviceContainer.RegisterType<MainWindow, MainWindow>(LifeScope.Singleton);
            _serviceContainer.RegisterType<FileDownloader>(provider => new FileDownloader(
                _downloadsHttp, provider.GetService<TimeProvider>()), LifeScope.Singleton);
            _serviceContainer.RegisterType<BackgroundRemovalService>(provider => new BackgroundRemovalService(
                provider.GetService<FileDownloader>(), Path.Combine(dataDirectory, "Models", "BackgroundRemoval")), LifeScope.Singleton);
            _serviceContainer.RegisterType<SettingsStore>(provider => new SettingsStore(
                dataDirectory, provider.GetService<AtomicJsonFile>(), provider.GetService<StringHelper>()), LifeScope.Singleton);
            _serviceContainer.RegisterType<JobStore>(provider => new JobStore(
                dataDirectory, provider.GetService<AtomicJsonFile>()), LifeScope.Singleton);
            _serviceContainer.RegisterType<AssetLibrary>(provider => new AssetLibrary(
                dataDirectory, provider.GetService<AtomicJsonFile>(), provider.GetService<SafetensorsInspector>(),
                provider.GetService<FileDownloader>(), provider.GetService<TimeProvider>()), LifeScope.Singleton);
            _serviceContainer.RegisterType<ComfyEngine>(provider => new ComfyEngine(
                _engineHttp, provider.GetService<ProcessRunner>(), provider.GetService<RuntimeInstaller>(),
                provider.GetService<ComfyWorkflowBuilder>(), provider.GetService<FileDownloader>(),
                provider.GetService<TimeProvider>()), LifeScope.Singleton);
            _serviceContainer.RegisterType<StudioViewModel>(provider => new StudioViewModel(
                provider.GetService<SettingsStore>(), provider.GetService<AssetLibrary>(), provider.GetService<JobStore>(),
                provider.GetService<RuntimeInstaller>(), provider.GetService<ComfyEngine>(), provider.GetService<StudioService>(),
                provider.GetService<DesktopDialogs>(), provider.GetService<ThemeService>(), provider.GetService<LanguageService>(),
                provider.GetService<StringHelper>(), provider.GetService<StudioWorkPresenter>(),
                provider.GetService<ISeedProvider>(), dataDirectory), LifeScope.Singleton);
            _serviceContainer.RegisterDependencies(typeof(StringHelper).Assembly);
            return _serviceContainer.Build();
        }

        private void OnExit(object sender, ControlledApplicationLifetimeExitEventArgs arguments)
        {
            DisposeClients();
        }

        private void DisposeClients()
        {
            if (_downloadsHttp != null)
            {
                _downloadsHttp.Dispose();
                _downloadsHttp = null;
            }
            if (_engineHttp != null)
            {
                _engineHttp.Dispose();
                _engineHttp = null;
            }
        }
    }
}

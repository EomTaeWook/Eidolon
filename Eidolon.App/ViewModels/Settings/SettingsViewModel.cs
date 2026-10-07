using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;

namespace Eidolon.App.ViewModels
{
    public class SettingsViewModel : StudioPanelViewModel
    {
        private AssetsViewModel Assets { get; set; }

        private readonly ThemeService _themes;
        private readonly LanguageService _languages;
        private ThemeChoice _selectedTheme;
        private LanguageChoice _selectedLanguage;
        public List<ThemeChoice> Themes { get; private set; }
        public List<LanguageChoice> Languages { get; private set; }
        public AsyncCommand PickInstallCommand { get; private set; }
        public AsyncCommand PickGenerationDirectoryCommand { get; private set; }
        public AsyncCommand OpenGenerationDirectoryCommand { get; private set; }
        public AsyncCommand SaveSettingsCommand { get; private set; }
        public AsyncCommand OpenLogsCommand { get; private set; }
        public AsyncCommand OpenDataCommand { get; private set; }

        public string InstallDirectory
        {
            get
            {
                return Session.SettingsDraft.InstallDirectory;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (string.Equals(Session.SettingsDraft.InstallDirectory, value, StringComparison.Ordinal) == true)
                {
                    return;
                }
                Session.SettingsDraft.InstallDirectory = value;
                Raise();
                RefreshCommands();
            }
        }

        public string GenerationDirectory
        {
            get
            {
                return Session.SettingsDraft.GenerationDirectory;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Session.SettingsDraft.GenerationDirectory != value)
                {
                    Session.SettingsDraft.GenerationDirectory = value;
                    Raise();
                }
            }
        }

        public string ServerAddress
        {
            get
            {
                return Session.SettingsDraft.ServerAddress;
            }
            set
            {
                if (string.Equals(Session.SettingsDraft.ServerAddress, value, StringComparison.Ordinal) == true)
                {
                    return;
                }
                Session.SettingsDraft.ServerAddress = value;
                Raise();

                RefreshCommands();
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
                    Session.SettingsDraft.Theme = value.Value;
                    _themes.Apply(value.Value);
                }
            }
        }

        public event Action LanguageChanged;

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
                    Session.SettingsDraft.Language = value.Value;
                    _languages.Apply(value.Value);
                    LanguageChanged?.Invoke();
                }
            }
        }

        private async Task PickInstallAsync()
        {
            string path = await _dialogs.PickFolderAsync(_strings.GetString("EidolonText233"));
            if (string.IsNullOrEmpty(path) == false)
            {
                InstallDirectory = path;
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
            DesktopSettings settings = Session.Settings.Copy();
            settings.Theme = Session.SettingsDraft.Theme;
            settings.Language = Session.SettingsDraft.Language;
            settings.PositivePrompt = Session.SettingsDraft.PositivePrompt;
            settings.NegativePrompt = Session.SettingsDraft.NegativePrompt;
            settings.GenerationDirectory = GenerationDirectory.Trim();
            Session.SaveSettings(settings);
            GenerationDirectory = settings.GenerationDirectory;
            await Assets.RefreshAssetsAsync();
            Session.Status = _strings.GetString("EidolonText235");
        }

        internal DesktopSettings CreateEngineSettings()
        {
            DesktopSettings settings = Session.Settings.Copy();
            settings.InstallDirectory = Session.SettingsDraft.InstallDirectory;
            settings.UseCpu = Session.SettingsDraft.UseCpu;
            settings.ServerAddress = string.Empty;
            if (Session.SettingsDraft.ServerAddress != null)
            {
                settings.ServerAddress = Session.SettingsDraft.ServerAddress.Trim();
            }
            return settings;
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

        public SettingsViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs,
            AssetsViewModel assets, ThemeService themes, LanguageService languages) : base(session, navigation, work, strings, dialogs)
        {
            Assets = assets;
            _themes = themes;
            _languages = languages;
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
            PickInstallCommand = Command(PickInstallAsync);
            PickGenerationDirectoryCommand = Command(PickGenerationDirectoryAsync, () => Session.IsClosing == false);
            OpenGenerationDirectoryCommand = Command(OpenGenerationDirectoryAsync, () => Session.IsClosing == false);
            SaveSettingsCommand = Command(() => Session.WorkAsync(SaveSettingsAsync));
            OpenLogsCommand = Command(() => _dialogs.OpenFolderAsync(Path.Combine(Session.DataDirectory, "Logs")), () => true);
            OpenDataCommand = Command(() => _dialogs.OpenFolderAsync(Session.DataDirectory), () => true);
        }

        public DesktopSettings SettingsDraft
        {
            get
            {
                return Session.SettingsDraft;
            }
        }

        internal void LoadChoices()
        {
            Raise(nameof(SettingsDraft));
            Raise(nameof(InstallDirectory));
            Raise(nameof(GenerationDirectory));
            Raise(nameof(ServerAddress));
            SelectedTheme = Themes.First(theme => theme.Value == Session.Settings.Theme);
            SelectedLanguage = Languages.First(language => language.Value == Session.Settings.Language);
        }

        public override void Localize()
        {
            foreach (ThemeChoice theme in Themes)
            {
                theme.Localize(_strings);
            }
            base.Localize();
        }
    }
}

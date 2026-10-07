using Eidolon.App.Services;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Eidolon.App.ViewModels
{
    public class SettingsViewModel : StudioPanelViewModel
    {
        private AssetsViewModel Assets { get; set; }

        private readonly ThemeService _themes;
        private readonly LanguageService _languages;
        private readonly CodexModelCatalog _codexModels;
        private CodexModelChoice _selectedCodexModel;
        private Task _modelLoad = Task.CompletedTask;
        private bool _loadingCodexModels;
        private string _codexModelError = string.Empty;
        public ObservableCollection<CodexModelChoice> CodexModels { get; private set; } = new ObservableCollection<CodexModelChoice>();
        public AsyncCommand RefreshCodexModelsCommand { get; private set; }
        private ThemeChoice _selectedTheme;
        private LanguageChoice _selectedLanguage;
        private GenerationBackendChoice _selectedGenerationBackend;
        public List<GenerationBackendChoice> GenerationBackends { get; private set; }
        public AsyncCommand PickCodexExecutableCommand { get; private set; }
        public List<ThemeChoice> Themes { get; private set; }
        public List<LanguageChoice> Languages { get; private set; }
        public PromptSettingsViewModel Prompts { get; private set; }
        public AsyncCommand PickInstallCommand { get; private set; }
        public AsyncCommand PickGenerationDirectoryCommand { get; private set; }
        public AsyncCommand OpenGenerationDirectoryCommand { get; private set; }
        public AsyncCommand SaveSettingsCommand { get; private set; }
        public AsyncCommand SaveGenerationEnvironmentCommand { get; private set; }
        public AsyncCommand OpenLogsCommand { get; private set; }
        public AsyncCommand OpenDataCommand { get; private set; }

        public GenerationBackendChoice SelectedGenerationBackend
        {
            get
            {
                return _selectedGenerationBackend;
            }
            set
            {
                if (Set(ref _selectedGenerationBackend, value) == false)
                {
                    return;
                }
                if (value == null)
                {
                    return;
                }
                Session.SettingsDraft.GenerationBackend = value.Value;
                Raise(nameof(IsCodexSelected));
                Raise(nameof(HasUnappliedGenerationBackend));
                if (IsCodexSelected == true)
                {
                    RefreshCodexModelsCommand.Execute(null);
                }
            }
        }

        public bool IsCodexSelected
        {
            get
            {
                return Session.SettingsDraft.GenerationBackend == GenerationBackend.Codex;
            }
        }

        public bool HasUnappliedGenerationBackend
        {
            get
            {
                if (Session.SettingsDraft.GenerationBackend != Session.Settings.GenerationBackend)
                {
                    return true;
                }
                if (IsCodexSelected == false)
                {
                    return false;
                }
                if (string.Equals(CodexModel.Trim(), Session.Settings.CodexModel, StringComparison.Ordinal) == false)
                {
                    return true;
                }
                return string.Equals(CodexExecutablePath.Trim(), Session.Settings.CodexExecutablePath,
                    StringComparison.OrdinalIgnoreCase) == false;
            }
        }

        public string CodexExecutablePath
        {
            get
            {
                return Session.SettingsDraft.CodexExecutablePath;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Session.SettingsDraft.CodexExecutablePath != value)
                {
                    Session.SettingsDraft.CodexExecutablePath = value;
                    Raise();
                    Raise(nameof(HasUnappliedGenerationBackend));
                }
            }
        }

        public string CodexModel
        {
            get
            {
                return Session.SettingsDraft.CodexModel;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Session.SettingsDraft.CodexModel == value)
                {
                    return;
                }
                Session.SettingsDraft.CodexModel = value;
                Raise();
                Raise(nameof(HasUnappliedGenerationBackend));
            }
        }

        public CodexModelChoice SelectedCodexModel
        {
            get
            {
                return _selectedCodexModel;
            }
            set
            {
                if (Set(ref _selectedCodexModel, value) == false)
                {
                    return;
                }
                if (value == null)
                {
                    return;
                }
                CodexModel = value.Model;
            }
        }

        public bool IsLoadingCodexModels
        {
            get
            {
                return _loadingCodexModels;
            }
            private set
            {
                if (Set(ref _loadingCodexModels, value) == true)
                {
                    Raise(nameof(CodexModelsHint));
                    RefreshCommands();
                }
            }
        }

        public string CodexModelsHint
        {
            get
            {
                if (IsLoadingCodexModels == true)
                {
                    return _strings.GetString("EidolonText553");
                }
                if (string.IsNullOrEmpty(_codexModelError) == false)
                {
                    return _strings.Format("EidolonText554", _codexModelError);
                }
                return _strings.GetString("EidolonText552");
            }
        }

        internal Task RefreshCodexModelsAsync()
        {
            if (IsLoadingCodexModels == true)
            {
                return _modelLoad;
            }
            if (IsCodexSelected == false)
            {
                return Task.CompletedTask;
            }
            if (Session.IsClosing == true)
            {
                return Task.CompletedTask;
            }
            _modelLoad = LoadCodexModelsAsync();
            return _modelLoad;
        }

        private async Task LoadCodexModelsAsync()
        {
            string path = CodexExecutablePath.Trim();
            IsLoadingCodexModels = true;
            _codexModelError = string.Empty;
            try
            {
                List<CodexModelInfo> models = await _codexModels.ReadAsync(path, Session.DataDirectory, Session.Lifetime);
                if (Session.IsClosing == true)
                {
                    return;
                }
                if (path != CodexExecutablePath.Trim())
                {
                    return;
                }
                string selected = CodexModel.Trim();
                CodexModels.Clear();
                CodexModels.Add(new CodexModelChoice(string.Empty, string.Empty, _strings));
                foreach (CodexModelInfo model in models)
                {
                    CodexModels.Add(new CodexModelChoice(model.Model, model.DisplayName, _strings));
                }
                SelectCodexModel(selected);
            }
            catch (OperationCanceledException) when (Session.IsClosing == true)
            {
            }
            catch (Exception error)
            {
                if (Session.IsClosing == false)
                {
                    _codexModelError = _strings.GetExceptionMessage(error);
                }
            }
            finally
            {
                IsLoadingCodexModels = false;
                Raise(nameof(CodexModelsHint));
            }
        }

        private void SelectCodexModel(string model)
        {
            CodexModelChoice choice = CodexModels.FirstOrDefault(item => item.Model == model);
            if (choice == null)
            {
                choice = new CodexModelChoice(model, model, _strings);
                CodexModels.Add(choice);
            }
            SelectedCodexModel = choice;
        }

        internal Task WaitForModelsAsync()
        {
            return _modelLoad;
        }

        private async Task PickCodexExecutableAsync()
        {
            string path = await _dialogs.PickCodexExecutableAsync();
            if (string.IsNullOrEmpty(path) == false)
            {
                CodexExecutablePath = path;
                await RefreshCodexModelsAsync();
            }
        }

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
                if (Set(ref _selectedTheme, value) == false)
                {
                    return;
                }
                if (value == null)
                {
                    return;
                }
                Session.SettingsDraft.Theme = value.Value;
                _themes.Apply(value.Value);
                Raise(nameof(IsLightTheme));
                Raise(nameof(IsBlackTheme));
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
                if (Set(ref _selectedLanguage, value) == false)
                {
                    return;
                }
                if (value == null)
                {
                    return;
                }
                Session.SettingsDraft.Language = value.Value;
                _languages.Apply(value.Value);
                Raise(nameof(IsKoreanLanguage));
                Raise(nameof(IsEnglishLanguage));
                LanguageChanged?.Invoke();
            }
        }

        public bool IsLightTheme
        {
            get
            {
                return Session.SettingsDraft.Theme == AppTheme.Light;
            }
            set
            {
                if (value == true)
                {
                    SelectedTheme = Themes.First(theme => theme.Value == AppTheme.Light);
                }
            }
        }

        public bool IsBlackTheme
        {
            get
            {
                return Session.SettingsDraft.Theme == AppTheme.Black;
            }
            set
            {
                if (value == true)
                {
                    SelectedTheme = Themes.First(theme => theme.Value == AppTheme.Black);
                }
            }
        }

        public bool IsKoreanLanguage
        {
            get
            {
                return Session.SettingsDraft.Language == AppLanguage.Korean;
            }
            set
            {
                if (value == true)
                {
                    SelectedLanguage = Languages.First(language => language.Value == AppLanguage.Korean);
                }
            }
        }

        public bool IsEnglishLanguage
        {
            get
            {
                return Session.SettingsDraft.Language == AppLanguage.English;
            }
            set
            {
                if (value == true)
                {
                    SelectedLanguage = Languages.First(language => language.Value == AppLanguage.English);
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

        private Task SaveSettingsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DesktopSettings settings = Session.Settings.Copy();
            settings.Theme = Session.SettingsDraft.Theme;
            settings.Language = Session.SettingsDraft.Language;
            settings.GenerationDirectory = GenerationDirectory.Trim();
            Session.SaveSettings(settings);
            GenerationDirectory = settings.GenerationDirectory;
            Session.Status = _strings.GetString("EidolonText235");
            return Task.CompletedTask;
        }

        private async Task SaveGenerationEnvironmentAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DesktopSettings settings = Session.Settings.Copy();
            settings.GenerationBackend = Session.SettingsDraft.GenerationBackend;
            settings.CodexExecutablePath = CodexExecutablePath.Trim();
            settings.CodexModel = CodexModel.Trim();
            settings.PositivePrompt = Session.SettingsDraft.PositivePrompt;
            settings.NegativePrompt = Session.SettingsDraft.NegativePrompt;
            settings.PromptPresets = new ObservableCollection<PromptPreset>(Session.SettingsDraft.PromptPresets.Select(preset => preset.Copy()));
            settings.ActivePromptPresetId = Session.SettingsDraft.ActivePromptPresetId;
            Session.SaveSettings(settings);
            CodexExecutablePath = settings.CodexExecutablePath;
            CodexModel = settings.CodexModel;
            if (settings.GenerationBackend == GenerationBackend.ComfyUI)
            {
                await Assets.RefreshAssetsAsync();
            }
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
            AssetsViewModel assets, ThemeService themes, LanguageService languages, CodexModelCatalog codexModels) : base(session, navigation, work, strings, dialogs)
        {
            Assets = assets;
            _themes = themes;
            _languages = languages;
            _codexModels = codexModels;
            Prompts = new PromptSettingsViewModel(session, navigation, work, strings, dialogs);
            GenerationBackends = new List<GenerationBackendChoice>
            {
                new GenerationBackendChoice(GenerationBackend.ComfyUI, strings),
                new GenerationBackendChoice(GenerationBackend.Codex, strings)
            };
            PickCodexExecutableCommand = Command(PickCodexExecutableAsync, () => Session.IsClosing == false);
            RefreshCodexModelsCommand = Command(RefreshCodexModelsAsync,
                () => Session.IsClosing == false && IsLoadingCodexModels == false);
            CodexModels.Add(new CodexModelChoice(string.Empty, string.Empty, _strings));
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
            SaveGenerationEnvironmentCommand = Command(() => Session.WorkAsync(SaveGenerationEnvironmentAsync));
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
            Prompts.Load();
            SelectCodexModel(Session.Settings.CodexModel);
            SelectedGenerationBackend = GenerationBackends.First(choice => choice.Value == Session.Settings.GenerationBackend);
            Raise(nameof(CodexExecutablePath));
            Raise(nameof(CodexModel));
            Raise(nameof(IsCodexSelected));
            Raise(nameof(HasUnappliedGenerationBackend));
            Raise(nameof(SettingsDraft));
            Raise(nameof(InstallDirectory));
            Raise(nameof(GenerationDirectory));
            Raise(nameof(ServerAddress));
            SelectedTheme = Themes.First(theme => theme.Value == Session.Settings.Theme);
            SelectedLanguage = Languages.First(language => language.Value == Session.Settings.Language);
        }

        protected override void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Session.Settings))
            {
                Raise(nameof(HasUnappliedGenerationBackend));
            }
        }

        public override void Localize()
        {
            foreach (ThemeChoice theme in Themes)
            {
                theme.Localize(_strings);
            }
            foreach (GenerationBackendChoice backend in GenerationBackends)
            {
                backend.Localize(_strings);
            }
            Prompts.Localize();
            foreach (CodexModelChoice model in CodexModels)
            {
                model.Localize(_strings);
            }
            Raise(nameof(CodexModelsHint));
            base.Localize();
        }

        public override void Dispose()
        {
            Prompts.Dispose();
            base.Dispose();
        }
    }
}

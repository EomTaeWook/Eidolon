using System.Collections.ObjectModel;
using Eidolon.App.Localization;
using Eidolon.App.Presenters;
using Eidolon.App.Services;

namespace Eidolon.App.ViewModels
{
    public class PromptSettingsViewModel : StudioPanelViewModel
    {
        private PromptPreset _selectedPreset;
        private string _presetName = string.Empty;
        private bool _changingPresets;

        public PromptSettingsViewModel(StudioSession session, StudioNavigationViewModel navigation,
            StudioWorkPresenter work, StringHelper strings, DesktopDialogs dialogs) : base(session, navigation, work, strings, dialogs)
        {
            AddCommand = Command(AddAsync, () => CanEdit == true && IsNameValid == true
                && Presets.Any(preset => NameMatches(preset) == true) == false);
            UpdateCommand = Command(UpdateAsync, () => CanEdit == true && _selectedPreset != null
                && IsNameValid == true && IsModified == true);
            DeleteCommand = Command(DeleteAsync, () => CanEdit == true && _selectedPreset != null);
        }

        public AsyncCommand AddCommand { get; private set; }
        public AsyncCommand UpdateCommand { get; private set; }
        public AsyncCommand DeleteCommand { get; private set; }

        public ObservableCollection<PromptPreset> Presets
        {
            get
            {
                return Session.SettingsDraft.PromptPresets;
            }
        }

        public bool CanEdit
        {
            get
            {
                return Session.CanEditGenerationInputs;
            }
        }

        public PromptPreset SelectedPreset
        {
            get
            {
                return _selectedPreset;
            }
            set
            {
                if (_changingPresets == true || Set(ref _selectedPreset, value) == false)
                {
                    return;
                }
                if (value == null)
                {
                    Session.SettingsDraft.ActivePromptPresetId = string.Empty;
                    PresetName = string.Empty;
                }
                else
                {
                    Session.SettingsDraft.ActivePromptPresetId = value.Id;
                    PresetName = value.Name;
                    PositivePrompt = value.PositivePrompt;
                    NegativePrompt = value.NegativePrompt;
                }
                RefreshCommands();
            }
        }

        public string PresetName
        {
            get
            {
                return _presetName;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Set(ref _presetName, value) == true)
                {
                    RefreshCommands();
                }
            }
        }

        public string PositivePrompt
        {
            get
            {
                return Session.SettingsDraft.PositivePrompt;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Session.SettingsDraft.PositivePrompt == value)
                {
                    return;
                }
                Session.SettingsDraft.PositivePrompt = value;
                Raise();
                RefreshCommands();
            }
        }

        public string NegativePrompt
        {
            get
            {
                return Session.SettingsDraft.NegativePrompt;
            }
            set
            {
                if (value == null)
                {
                    value = string.Empty;
                }
                if (Session.SettingsDraft.NegativePrompt == value)
                {
                    return;
                }
                Session.SettingsDraft.NegativePrompt = value;
                Raise();
                RefreshCommands();
            }
        }

        private bool NameMatches(PromptPreset preset)
        {
            return string.Equals(preset.Name.Trim(), PresetName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private bool IsNameValid
        {
            get
            {
                return string.IsNullOrWhiteSpace(PresetName) == false
                    && Presets.Any(preset => preset != _selectedPreset && NameMatches(preset) == true) == false;
            }
        }

        private bool IsModified
        {
            get
            {
                return _selectedPreset != null && (PresetName.Trim() != _selectedPreset.Name
                    || PositivePrompt != _selectedPreset.PositivePrompt || NegativePrompt != _selectedPreset.NegativePrompt);
            }
        }

        public string Hint
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PresetName) == true)
                {
                    return _strings.GetString("EidolonText519");
                }
                if (IsNameValid == false)
                {
                    return _strings.GetString("EidolonText520");
                }
                if (_selectedPreset != null && IsModified == true)
                {
                    return _strings.GetString("EidolonText521");
                }
                return _strings.GetString("EidolonText522");
            }
        }

        private PromptPreset CapturePreset(string id)
        {
            return new PromptPreset
            {
                Id = id,
                Name = PresetName.Trim(),
                PositivePrompt = PositivePrompt,
                NegativePrompt = NegativePrompt
            };
        }

        private Task AddAsync()
        {
            PromptPreset preset = CapturePreset(Guid.NewGuid().ToString("N"));
            _changingPresets = true;
            try
            {
                Presets.Add(preset);
                _selectedPreset = preset;
                Session.SettingsDraft.ActivePromptPresetId = preset.Id;
                PresetName = preset.Name;
                Raise(nameof(SelectedPreset));
            }
            finally
            {
                _changingPresets = false;
            }
            RefreshCommands();
            return Task.CompletedTask;
        }

        private Task UpdateAsync()
        {
            int index = Presets.IndexOf(_selectedPreset);
            PromptPreset preset = CapturePreset(_selectedPreset.Id);
            _changingPresets = true;
            try
            {
                Presets[index] = preset;
                _selectedPreset = preset;
                PresetName = preset.Name;
                Raise(nameof(SelectedPreset));
            }
            finally
            {
                _changingPresets = false;
            }
            RefreshCommands();
            return Task.CompletedTask;
        }

        private Task DeleteAsync()
        {
            _changingPresets = true;
            try
            {
                Presets.Remove(_selectedPreset);
                _selectedPreset = null;
                Session.SettingsDraft.ActivePromptPresetId = string.Empty;
                PresetName = string.Empty;
                Raise(nameof(SelectedPreset));
            }
            finally
            {
                _changingPresets = false;
            }
            RefreshCommands();
            return Task.CompletedTask;
        }

        internal void Load()
        {
            _changingPresets = true;
            try
            {
                _selectedPreset = Presets.FirstOrDefault(preset => preset.Id == Session.SettingsDraft.ActivePromptPresetId);
                _presetName = string.Empty;
                if (_selectedPreset != null)
                {
                    _presetName = _selectedPreset.Name;
                }
                Raise(nameof(Presets));
                Raise(nameof(SelectedPreset));
                Raise(nameof(PresetName));
                Raise(nameof(PositivePrompt));
                Raise(nameof(NegativePrompt));
            }
            finally
            {
                _changingPresets = false;
            }
            RefreshCommands();
        }

        protected override void RefreshCommands()
        {
            Raise(nameof(CanEdit));
            Raise(nameof(Hint));
            base.RefreshCommands();
        }
    }
}

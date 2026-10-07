using Eidolon.App.Localization;
using Eidolon.Core.Domain;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eidolon.App.Services
{
    public class DesktopSettings : StudioSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.Light;
        public AppLanguage Language { get; set; }
        public ObservableCollection<PromptPreset> PromptPresets { get; set; } = new ObservableCollection<PromptPreset>();
        public string ActivePromptPresetId { get; set; } = string.Empty;
        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalSettings { get; set; } = new Dictionary<string, JsonElement>();

        public DesktopSettings()
        {
            GenerationDirectory = DefaultGenerationDirectory;
            Language = AppLanguage.English;
            if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko")
            {
                Language = AppLanguage.Korean;
            }
        }

        public static string DefaultGenerationDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eidolon", "Images");
            }
        }

        public new DesktopSettings Copy()
        {
            return new DesktopSettings
            {
                SchemaVersion = SchemaVersion,
                Theme = Theme,
                Language = Language,
                InstallDirectory = InstallDirectory,
                GenerationDirectory = GenerationDirectory,
                UseCpu = UseCpu,
                ServerAddress = ServerAddress,
                UseServerAssets = UseServerAssets,
                AdditionalSettings = new Dictionary<string, JsonElement>(AdditionalSettings),
                PositivePrompt = PositivePrompt,
                NegativePrompt = NegativePrompt,
                PromptPresets = new ObservableCollection<PromptPreset>(PromptPresets.Select(preset => preset.Copy())),
                ActivePromptPresetId = ActivePromptPresetId,
                DefaultModelId = DefaultModelId,
                GenerationBackend = GenerationBackend,
                CodexExecutablePath = CodexExecutablePath,
                CodexModel = CodexModel
            };
        }

        public new void Validate()
        {
            base.Validate();
            if (Enum.IsDefined(Language) == false)
            {
                throw new InvalidOperationException("EidolonText019");
            }
            if (Enum.IsDefined(Theme) == false)
            {
                throw new InvalidOperationException("EidolonText020");
            }
            if (PromptPresets == null || ActivePromptPresetId == null)
            {
                throw new InvalidOperationException("EidolonText525");
            }
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PromptPreset preset in PromptPresets)
            {
                if (preset == null)
                {
                    throw new InvalidOperationException("EidolonText525");
                }
                if (string.IsNullOrWhiteSpace(preset.Id) == true || string.IsNullOrWhiteSpace(preset.Name) == true)
                {
                    throw new InvalidOperationException("EidolonText525");
                }
                if (preset.PositivePrompt == null || preset.NegativePrompt == null)
                {
                    throw new InvalidOperationException("EidolonText525");
                }
                if (ids.Add(preset.Id) == false || names.Add(preset.Name.Trim()) == false)
                {
                    throw new InvalidOperationException("EidolonText525");
                }
            }
            if (string.IsNullOrEmpty(ActivePromptPresetId) == false && ids.Contains(ActivePromptPresetId) == false)
            {
                throw new InvalidOperationException("EidolonText525");
            }
        }
    }
}

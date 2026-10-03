using Eidolon.App.Localization;
using Eidolon.Core.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eidolon.App.Services
{
    public class DesktopSettings : StudioSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.Black;
        public AppLanguage Language { get; set; }
        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalSettings { get; set; } = new Dictionary<string, JsonElement>();

        public DesktopSettings()
        {
            Language = AppLanguage.English;
            if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko")
            {
                Language = AppLanguage.Korean;
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
                UseCpu = UseCpu,
                ServerAddress = ServerAddress,
                UseServerAssets = UseServerAssets,
                AdditionalSettings = new Dictionary<string, JsonElement>(AdditionalSettings),
                PositivePrompt = PositivePrompt,
                NegativePrompt = NegativePrompt,
                DefaultModelId = DefaultModelId
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
        }
    }
}

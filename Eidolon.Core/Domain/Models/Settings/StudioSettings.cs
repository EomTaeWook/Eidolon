using System.Text.Json.Serialization;

namespace Eidolon.Core.Domain
{
    public class StudioSettings
    {
        public const string DefaultServerAddress = "http://127.0.0.1:8189";
        public int SchemaVersion { get; set; } = 1;
        public string InstallDirectory { get; set; } = string.Empty;
        public string GenerationDirectory { get; set; } = string.Empty;
        public bool UseCpu { get; set; }
        public string ServerAddress { get; set; } = DefaultServerAddress;
        [JsonIgnore]
        public bool UseServerAssets { get; set; }
        public string PositivePrompt { get; set; } = string.Empty;
        public string NegativePrompt { get; set; } = string.Empty;
        public string DefaultModelId { get; set; } = string.Empty;
        public GenerationBackend GenerationBackend { get; set; } = GenerationBackend.ComfyUI;
        public string CodexExecutablePath { get; set; } = string.Empty;
        public string CodexModel { get; set; } = string.Empty;

        public StudioSettings()
        {
        }

        public StudioSettings Copy()
        {
            return new StudioSettings
            {
                InstallDirectory = InstallDirectory,
                GenerationDirectory = GenerationDirectory,
                UseCpu = UseCpu,
                ServerAddress = ServerAddress,
                UseServerAssets = UseServerAssets,
                PositivePrompt = PositivePrompt,
                NegativePrompt = NegativePrompt,
                DefaultModelId = DefaultModelId,
                GenerationBackend = GenerationBackend,
                CodexExecutablePath = CodexExecutablePath,
                CodexModel = CodexModel
            };
        }

        public void Validate()
        {
            if (Enum.IsDefined(GenerationBackend) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationBackend);
            }
            if (CodexExecutablePath == null)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationSettings);
            }
            if (CodexModel == null)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationSettings);
            }
            if (Uri.TryCreate(ServerAddress, UriKind.Absolute, out Uri server) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidServerAddress);
            }
            if (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps)
            {
                throw new StudioException(StudioMessageCode.InvalidServerAddress);
            }
            if (string.IsNullOrEmpty(server.UserInfo) == false || string.IsNullOrEmpty(server.Query) == false || string.IsNullOrEmpty(server.Fragment) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidServerAddress);
            }
            if (string.IsNullOrWhiteSpace(InstallDirectory) == false)
            {
                if (Path.IsPathFullyQualified(InstallDirectory) == false)
                {
                    throw new StudioException(StudioMessageCode.AbsoluteInstallPathRequired);
                }
            }
            if (GenerationDirectory == null)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
            }
            if (string.IsNullOrWhiteSpace(GenerationDirectory) == false && Path.IsPathFullyQualified(GenerationDirectory) == false)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationDirectory);
            }
            if (PositivePrompt == null || NegativePrompt == null || DefaultModelId == null)
            {
                throw new StudioException(StudioMessageCode.InvalidGenerationSettings);
            }
        }
    }
}

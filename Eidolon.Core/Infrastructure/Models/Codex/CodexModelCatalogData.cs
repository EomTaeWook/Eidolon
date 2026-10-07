using System.Text.Json.Serialization;

namespace Eidolon.Core.Infrastructure
{
    public class CodexModelCatalogData
    {
        public CodexModelCatalogData()
        {
        }

        [JsonPropertyName("models")]
        [JsonRequired]
        public List<CodexModelInfo> Models { get; set; } = new List<CodexModelInfo>();
    }
}

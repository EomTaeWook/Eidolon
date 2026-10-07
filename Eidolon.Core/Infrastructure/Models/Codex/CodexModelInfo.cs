using System.Text.Json.Serialization;

namespace Eidolon.Core.Infrastructure
{
    public class CodexModelInfo
    {
        public CodexModelInfo()
        {
        }

        [JsonPropertyName("slug")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("visibility")]
        public string Visibility { get; set; } = string.Empty;

        [JsonPropertyName("input_modalities")]
        public List<string> InputModalities { get; set; } = new List<string>();
    }
}

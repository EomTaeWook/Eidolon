using System.Text.Json.Serialization;

namespace Eidolon.Core.Infrastructure
{
    public class CodexImageResult
    {
        public CodexImageResult()
        {
        }

        [JsonPropertyName("image_path")]
        public string ImagePath { get; set; } = string.Empty;

        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }
}

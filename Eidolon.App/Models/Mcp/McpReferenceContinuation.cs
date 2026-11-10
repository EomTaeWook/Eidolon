using System.Text.Json.Nodes;

namespace Eidolon.App.Mcp
{
    public class McpReferenceContinuation
    {
        public McpReferenceContinuation()
        {
        }

        public JsonObject Arguments { get; set; }
        public JsonNode OriginalRequestId { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public McpGenerationOptions Options { get; set; }
        public bool UseServerAssets { get; set; }
        public string ReferenceSha256 { get; set; } = string.Empty;
    }
}

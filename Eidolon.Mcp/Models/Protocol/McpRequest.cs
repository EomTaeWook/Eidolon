using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class McpRequest
    {
        public JsonNode Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public JsonObject Parameters { get; set; } = new JsonObject();
        public bool IsNotification { get; set; }
        public bool IsModern { get; set; }

        public McpRequest()
        {
        }
    }
}

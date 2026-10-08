using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class ServerDiscoverController : IMcpController
    {
        private readonly McpServerOptions _options;

        internal ServerDiscoverController(McpServerOptions options)
        {
            _options = options;
        }

        public string Method
        {
            get
            {
                return "server/discover";
            }
        }

        public Task<JsonObject> ExecuteAsync(McpRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.IsModern == false)
            {
                throw new McpProtocolException(-32601, "server/discover requires the current MCP protocol.", 200);
            }
            return Task.FromResult(new JsonObject
            {
                ["supportedVersions"] = new JsonArray(McpProtocol.CurrentVersion),
                ["capabilities"] = McpProtocol.ToolCapabilities(),
                ["instructions"] = _options.Instructions,
                ["ttlMs"] = 0,
                ["cacheScope"] = "private"
            });
        }
    }
}

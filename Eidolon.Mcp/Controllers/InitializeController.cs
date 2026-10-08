using System.Text.Json;
using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class InitializeController : IMcpController
    {
        private readonly McpServerOptions _options;

        internal InitializeController(McpServerOptions options)
        {
            _options = options;
        }

        public string Method
        {
            get
            {
                return "initialize";
            }
        }

        public Task<JsonObject> ExecuteAsync(McpRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            JsonObject parameters = request.Parameters;
            if (parameters["protocolVersion"]?.GetValueKind() != JsonValueKind.String)
            {
                throw new McpProtocolException(-32602, "initialize requires protocolVersion as a string.");
            }
            if (parameters["clientInfo"] is not JsonObject client)
            {
                throw new McpProtocolException(-32602, "initialize requires clientInfo as an object.");
            }
            if (client["name"]?.GetValueKind() != JsonValueKind.String
                || string.IsNullOrWhiteSpace(client["name"].GetValue<string>()) == true)
            {
                throw new McpProtocolException(-32602, "clientInfo requires a nonempty name.");
            }
            if (client["version"]?.GetValueKind() != JsonValueKind.String
                || string.IsNullOrWhiteSpace(client["version"].GetValue<string>()) == true)
            {
                throw new McpProtocolException(-32602, "clientInfo requires a nonempty version.");
            }
            if (parameters["capabilities"] is not JsonObject)
            {
                throw new McpProtocolException(-32602, "initialize requires capabilities as an object.");
            }
            string version = parameters["protocolVersion"].GetValue<string>();
            if (McpProtocol.IsLegacyVersion(version) == false)
            {
                version = McpProtocol.LegacyVersion;
            }
            return Task.FromResult(new JsonObject
            {
                ["protocolVersion"] = version,
                ["capabilities"] = McpProtocol.ToolCapabilities(),
                ["serverInfo"] = McpProtocol.ServerInfo(_options),
                ["instructions"] = _options.Instructions
            });
        }
    }
}

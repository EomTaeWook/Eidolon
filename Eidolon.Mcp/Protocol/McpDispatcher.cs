using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class McpDispatcher
    {
        private readonly McpServerOptions _options;
        private readonly Dictionary<string, IMcpController> _controllers;

        internal McpDispatcher(McpServerOptions options, IReadOnlyList<McpTool> tools,
            Action<Exception> reportError, Func<JsonNode, Task> cancelRequest)
        {
            _options = options;
            Dictionary<string, McpTool> registeredTools = new Dictionary<string, McpTool>(StringComparer.Ordinal);
            foreach (McpTool tool in tools)
            {
                ArgumentNullException.ThrowIfNull(tool);
                if (registeredTools.TryAdd(tool.Name, tool) == false)
                {
                    throw new ArgumentException("An MCP tool name is registered more than once: " + tool.Name, nameof(tools));
                }
            }
            _controllers = new Dictionary<string, IMcpController>(StringComparer.Ordinal);
            Register(new InitializeController(options));
            Register(new PingController());
            Register(new ServerDiscoverController(options));
            Register(new ToolsListController(registeredTools));
            Register(new ToolsCallController(registeredTools, reportError));
            Register(new NotificationsCancelledController(cancelRequest));
        }

        private void Register(IMcpController controller)
        {
            _controllers.Add(controller.Method, controller);
        }

        internal async Task<JsonObject> DispatchAsync(McpRequest request, CancellationToken token)
        {
            if (request.IsNotification == true)
            {
                if (_controllers.TryGetValue(request.Method, out IMcpController notification) == true)
                {
                    await notification.ExecuteAsync(request, token).ConfigureAwait(false);
                }
                return null;
            }
            if (_controllers.TryGetValue(request.Method, out IMcpController controller) == false
                || request.Method.StartsWith("notifications/", StringComparison.Ordinal) == true)
            {
                int status = 200;
                if (request.IsModern == true)
                {
                    status = 404;
                }
                throw new McpProtocolException(-32601, "Method not found: " + request.Method, status);
            }
            JsonObject result = await controller.ExecuteAsync(request, token).ConfigureAwait(false);
            if (request.IsModern == true)
            {
                result["resultType"] = "complete";
                result["_meta"] = new JsonObject
                {
                    ["io.modelcontextprotocol/serverInfo"] = McpProtocol.ServerInfo(_options)
                };
            }
            return McpProtocol.Response(request.Id, result);
        }
    }
}

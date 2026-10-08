using Dignus.DependencyInjection;
using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class McpDispatcher
    {
        private readonly McpServerOptions _options;
        private readonly Dictionary<string, Type> _controllerTypes;
        private readonly IServiceProvider _services;

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
            ServiceContainer services = new ServiceContainer();
            services.RegisterType(options);
            services.RegisterType<IReadOnlyDictionary<string, McpTool>>(registeredTools);
            services.RegisterType<Action<Exception>>(reportError);
            services.RegisterType<Func<JsonNode, Task>>(cancelRequest);
            _controllerTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            Register<InitializeController>(services, InitializeController.MethodName);
            Register<PingController>(services, PingController.MethodName);
            Register<ServerDiscoverController>(services, ServerDiscoverController.MethodName);
            Register<ToolsListController>(services, ToolsListController.MethodName);
            Register<ToolsCallController>(services, ToolsCallController.MethodName);
            Register<NotificationsCancelledController>(services, NotificationsCancelledController.MethodName);
            _services = services.Build();
        }

        private void Register<TController>(ServiceContainer services, string method) where TController : class, IMcpController
        {
            services.RegisterType<TController, TController>(LifeScope.Transient);
            _controllerTypes.Add(method, typeof(TController));
        }

        internal async Task<JsonObject> DispatchAsync(McpRequest request, CancellationToken token)
        {
            if (request.IsNotification == true)
            {
                if (_controllerTypes.TryGetValue(request.Method, out Type notificationType) == true)
                {
                    token.ThrowIfCancellationRequested();
                    IMcpController notification = (IMcpController)_services.GetService(notificationType);
                    await notification.ExecuteAsync(request, token).ConfigureAwait(false);
                }
                return null;
            }
            if (request.Method.StartsWith("notifications/", StringComparison.Ordinal) == true)
            {
                throw MethodNotFound(request);
            }
            if (_controllerTypes.TryGetValue(request.Method, out Type controllerType) == false)
            {
                throw MethodNotFound(request);
            }
            token.ThrowIfCancellationRequested();
            IMcpController controller = (IMcpController)_services.GetService(controllerType);
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

        private McpProtocolException MethodNotFound(McpRequest request)
        {
            int status = 200;
            if (request.IsModern == true)
            {
                status = 404;
            }
            return new McpProtocolException(-32601, "Method not found: " + request.Method, status);
        }
    }
}

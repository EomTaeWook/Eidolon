using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class ToolsListController : IMcpController
    {
        internal const string MethodName = "tools/list";
        private readonly IReadOnlyDictionary<string, McpTool> _tools;

        public ToolsListController(IReadOnlyDictionary<string, McpTool> tools)
        {
            _tools = tools;
        }

        public string Method
        {
            get
            {
                return MethodName;
            }
        }

        public Task<JsonObject> ExecuteAsync(McpRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.Parameters["cursor"] != null)
            {
                throw new McpProtocolException(-32602, "This server returns its complete tool list without pagination.");
            }
            JsonArray descriptions = new JsonArray();
            foreach (McpTool tool in _tools.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal))
            {
                descriptions.Add(tool.Describe());
            }
            JsonObject result = new JsonObject { ["tools"] = descriptions };
            if (request.IsModern == true)
            {
                result["ttlMs"] = 0;
                result["cacheScope"] = "private";
            }
            return Task.FromResult(result);
        }
    }
}

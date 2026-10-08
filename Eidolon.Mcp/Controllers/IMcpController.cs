using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal interface IMcpController
    {
        string Method { get; }
        Task<JsonObject> ExecuteAsync(McpRequest request, CancellationToken token);
    }
}

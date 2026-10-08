using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class PingController : IMcpController
    {
        internal PingController()
        {
        }

        public string Method
        {
            get
            {
                return "ping";
            }
        }

        public Task<JsonObject> ExecuteAsync(McpRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new JsonObject());
        }
    }
}

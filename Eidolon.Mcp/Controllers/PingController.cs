using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class PingController : IMcpController
    {
        internal const string MethodName = "ping";

        public PingController()
        {
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
            return Task.FromResult(new JsonObject());
        }
    }
}

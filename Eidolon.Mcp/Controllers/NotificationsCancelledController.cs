using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class NotificationsCancelledController : IMcpController
    {
        internal const string MethodName = "notifications/cancelled";
        private readonly Func<JsonNode, Task> _cancelRequest;

        public NotificationsCancelledController(Func<JsonNode, Task> cancelRequest)
        {
            _cancelRequest = cancelRequest;
        }

        public string Method
        {
            get
            {
                return MethodName;
            }
        }

        public async Task<JsonObject> ExecuteAsync(McpRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.IsModern == true)
            {
                return null;
            }
            JsonNode id = request.Parameters["requestId"];
            if (id == null)
            {
                return null;
            }
            if (id.GetValueKind() != JsonValueKind.String)
            {
                if (id.GetValueKind() != JsonValueKind.Number)
                {
                    return null;
                }
                if (long.TryParse(id.ToJsonString(), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out _) == false)
                {
                    return null;
                }
            }
            await _cancelRequest(id).ConfigureAwait(false);
            return null;
        }
    }
}

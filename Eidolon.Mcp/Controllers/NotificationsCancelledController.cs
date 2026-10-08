using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Eidolon.Mcp
{
    internal class NotificationsCancelledController : IMcpController
    {
        private readonly Func<JsonNode, Task> _cancelRequest;

        internal NotificationsCancelledController(Func<JsonNode, Task> cancelRequest)
        {
            _cancelRequest = cancelRequest;
        }

        public string Method
        {
            get
            {
                return "notifications/cancelled";
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
            if (id == null || (id.GetValueKind() != JsonValueKind.String
                && (id.GetValueKind() != JsonValueKind.Number || long.TryParse(id.ToJsonString(), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out _) == false)))
            {
                return null;
            }
            await _cancelRequest(id).ConfigureAwait(false);
            return null;
        }
    }
}

using System.Text.Json;

namespace Eidolon.Core.Infrastructure
{
    public class CodexOutputReader
    {
        public CodexOutputReader()
        {
        }

        public string ThreadId { get; private set; } = string.Empty;

        public void ReadLine(string line)
        {
            if (line.Contains("thread.started", StringComparison.Ordinal) == false)
            {
                return;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                if (root.TryGetProperty("type", out JsonElement type) == false)
                {
                    return;
                }
                if (type.ValueKind != JsonValueKind.String)
                {
                    return;
                }
                if (type.GetString() != "thread.started")
                {
                    return;
                }
                if (root.TryGetProperty("thread_id", out JsonElement thread) == false)
                {
                    return;
                }
                if (thread.ValueKind != JsonValueKind.String)
                {
                    return;
                }
                if (Guid.TryParse(thread.GetString(), out Guid id) == false)
                {
                    return;
                }
                ThreadId = id.ToString("D");
            }
            catch (JsonException)
            {
            }
        }
    }
}

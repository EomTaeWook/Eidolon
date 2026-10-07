using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    internal class JobHeader
    {
        public JobHeader()
        {
        }

        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public JobKind Kind { get; set; }
        public JobState State { get; set; }
        public bool HasImageMetadata { get; set; }
    }
}
